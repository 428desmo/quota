"""Exercise the multiplayer API without downloading the Unity WebGL build.

Runs against a disposable/local server by default. See README.md for examples.
"""

from __future__ import annotations

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from http import HTTPStatus
from http.client import HTTPConnection, HTTPException, HTTPSConnection
import json
from pathlib import Path
import threading
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from urllib.parse import urlsplit
from uuid import uuid4

BUCKETS_MS = (10, 25, 50, 100, 200, 400, 800, 1600, 3200, 6400)


class ApiSession:
    """Keep one HTTP connection per virtual client when the server supports it."""

    def __init__(self, base: str, timeout: float) -> None:
        address = urlsplit(base)
        if address.scheme not in ("http", "https") or not address.hostname or address.path not in ("", "/"):
            raise ValueError("base URL must be an http(s) origin without a path")
        connection_type = HTTPSConnection if address.scheme == "https" else HTTPConnection
        self.connection = connection_type(address.hostname, address.port, timeout=timeout)
        self.phase = "idle"

    def exchange(self, path: str, client: str, body: bytes | None) -> tuple[int, bytes]:
        headers = {"X-Quota-Client": client}
        if body is not None:
            headers["Content-Type"] = "application/json"
        self.phase = "connect"
        if self.connection.sock is None:
            self.connection.connect()
        self.phase = "send"
        self.connection.request("POST" if body is not None else "GET", path, body=body, headers=headers)
        self.phase = "response_headers"
        response = self.connection.getresponse()
        self.phase = "response_body"
        payload = response.read()
        self.phase = "idle"
        return response.status, payload

    def close(self) -> None:
        self.connection.close()


class Stats:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.routes: dict[str, dict] = {}
        self.errors: Counter[str] = Counter()

    def add(self, route: str, status: int, elapsed_ms: float, size: int, error: str = "") -> None:
        with self.lock:
            row = self.routes.setdefault(route, {"count": 0, "statuses": Counter(), "bytes": 0,
                                                 "buckets": [0] * (len(BUCKETS_MS) + 1), "max_ms": 0.0})
            row["count"] += 1
            row["statuses"][status] += 1
            row["bytes"] += size
            row["max_ms"] = max(row["max_ms"], elapsed_ms)
            index = next((i for i, bound in enumerate(BUCKETS_MS) if elapsed_ms <= bound), len(BUCKETS_MS))
            row["buckets"][index] += 1
            if error:
                self.errors[error] += 1

    def snapshot(self) -> dict:
        with self.lock:
            return {"routes": {route: {**row, "statuses": dict(row["statuses"]), "buckets": row["buckets"][:]}
                               for route, row in self.routes.items()}, "errors": dict(self.errors)}


def percentile_bound(row: dict, percentile: float) -> str:
    target = max(1, row["count"] * percentile)
    seen = 0
    for index, count in enumerate(row["buckets"]):
        seen += count
        if seen >= target:
            return f"≤{BUCKETS_MS[index]} ms" if index < len(BUCKETS_MS) else f">{BUCKETS_MS[-1]} ms"
    return "n/a"


def request(base: str, path: str, client: str, timeout: float, body: dict | None = None,
            stats: Stats | None = None, session: ApiSession | None = None) -> dict:
    data = json.dumps(body, ensure_ascii=False).encode() if body is not None else None
    headers = {"X-Quota-Client": client, "Content-Type": "application/json"}
    started = time.perf_counter()
    status, payload, error = 0, b"", ""
    try:
        if session is not None:
            status, payload = session.exchange(path, client, data)
        else:
            with urlopen(Request(base + path, data=data, headers=headers), timeout=timeout) as response:
                status, payload = response.status, response.read()
        if status >= 400:
            try:
                detail = json.loads(payload).get("error", "")
            except (ValueError, UnicodeDecodeError, AttributeError):
                detail = ""
            try:
                reason = HTTPStatus(status).phrase
            except ValueError:
                reason = "HTTP error"
            error = f"HTTP {status} {reason}"
            raise RuntimeError(f"{path}: {error}" + (f": {detail}" if detail else ""))
        result = json.loads(payload)
        if "error" in result:
            error = str(result["error"])
            raise RuntimeError(f"{path}: {error}")
        return result
    except HTTPError as exc:
        status, payload = exc.code, exc.read()
        try:
            detail = str(json.loads(payload).get("error", ""))
        except (ValueError, UnicodeDecodeError):
            detail = ""
        error = f"HTTP {status} {exc.reason}"
        raise RuntimeError(f"{path}: {error}" + (f": {detail}" if detail else "")) from exc
    except (URLError, TimeoutError, OSError, HTTPException) as exc:
        error = type(exc).__name__ + (f" during {session.phase}" if session is not None else "")
        if session is not None:
            session.close()
        raise RuntimeError(f"{path}: {exc}") from exc
    except (ValueError, UnicodeDecodeError) as exc:
        error = "invalid JSON response"
        raise RuntimeError(f"{path}: {error}") from exc
    finally:
        if stats is not None:
            stats.add(path, status, (time.perf_counter() - started) * 1000, len(payload), error)


def worker(base: str, client: str, leader: bool, mode: str, start_at: float, stop_at: float,
           session: ApiSession,
           interval: float, timeout: float, stats: Stats, finished: set[str], finished_lock: threading.Lock) -> None:
    delay = start_at - time.monotonic()
    if delay > 0:
        time.sleep(delay)
    next_poll = max(start_at, time.monotonic())
    while time.monotonic() < stop_at:
        try:
            state = request(base, "/api/state", client, timeout, stats=stats, session=session)
            if leader and mode == "cpu" and state.get("awaiting_next_round") and not state.get("finished"):
                request(base, "/api/action", client, timeout,
                        {"key": "next_round", "revision": len(state["actions"])}, stats, session)
            elif mode == "human" and state.get("phase") == "playing" and not state.get("finished") and not state.get("awaiting_next_round") and not state["you"]["observer"] and state["you"]["seat"] == state.get("current_seat"):
                request(base, "/api/action", client, timeout,
                        {"key": "pass", "revision": len(state["actions"])}, stats, session)
            elif leader and mode == "human" and state.get("awaiting_next_round") and not state.get("finished"):
                request(base, "/api/action", client, timeout,
                        {"key": "next_round", "revision": len(state["actions"])}, stats, session)
            if leader and state.get("finished"):
                with finished_lock:
                    finished.add(state.get("table_id", ""))
        except RuntimeError:
            # Errors are counted and printed in the summary; keep other clients running.
            pass
        next_poll += interval
        delay = next_poll - time.monotonic()
        if delay > 0:
            time.sleep(delay)
        else:
            next_poll = time.monotonic()


def print_report(elapsed: float, stats: Stats, finished: int) -> None:
    snapshot = stats.snapshot()
    print(f"elapsed={elapsed:.1f}s finished_tables={finished}")
    for route, row in sorted(snapshot["routes"].items()):
        print(f"{route}: requests={row['count']} rate={row['count'] / max(0.001, elapsed):.1f}/s "
              f"statuses={row['statuses']} p50={percentile_bound(row, .50)} "
              f"p95={percentile_bound(row, .95)} p99={percentile_bound(row, .99)} "
              f"max={row['max_ms']:.1f}ms received={row['bytes'] / 1024 / 1024:.2f}MiB")
    if snapshot["errors"]:
        print(f"errors={snapshot['errors']}")


def fetch_metrics(url: str, timeout: float) -> dict:
    with urlopen(url, timeout=timeout) as response:
        return json.load(response)


def fetch_metrics_optional(url: str | None, timeout: float) -> tuple[dict | None, str | None]:
    if not url:
        return None, None
    for attempt in range(3):
        try:
            return fetch_metrics(url, timeout), None
        except (HTTPError, URLError, TimeoutError, OSError, ValueError, UnicodeDecodeError) as exc:
            error = f"{type(exc).__name__}: {exc}"
            if attempt < 2:
                time.sleep(0.5 * (attempt + 1))
    return None, error


def save_results(path: Path, args: argparse.Namespace, stats: Stats, finished: int,
                 before: dict | None, after: dict | None,
                 before_error: str | None, after_error: str | None) -> None:
    path.write_text(json.dumps({
        "config": {"tables": args.tables, "clients_per_table": args.clients_per_table,
                   "mode": args.mode, "duration": args.duration, "poll_interval": args.poll_interval},
        "client": stats.snapshot(), "finished_tables": finished,
        "server_before": before, "server_after": after,
        "server_before_error": before_error, "server_after_error": after_error,
    }, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def print_server_delta(before: dict, after: dict) -> None:
    previous = before["routes"].get("GET /api/state", {})
    current = after["routes"].get("GET /api/state", {})
    count = current.get("requests", 0) - previous.get("requests", 0)
    if count:
        wait = current["lock_wait_ms_total"] - previous.get("lock_wait_ms_total", 0)
        held = current["lock_held_ms_total"] - previous.get("lock_held_ms_total", 0)
        serial = current["serialization_ms_total"] - previous.get("serialization_ms_total", 0)
        sent = current["response_bytes"] - previous.get("response_bytes", 0)
        print(f"server GET /api/state: requests={count} avg_lock_wait={wait/count:.3f}ms "
              f"avg_lock_held={held/count:.3f}ms avg_json={serial/count:.3f}ms "
              f"sent={sent / 1024 / 1024:.2f}MiB")
    cpu = after["cpu"]["decisions"] - before["cpu"]["decisions"]
    cpu_ms = after["cpu"]["decision_ms_total"] - before["cpu"]["decision_ms_total"]
    user_cpu = after["process"]["cpu_user_seconds"] - before["process"]["cpu_user_seconds"]
    system_cpu = after["process"]["cpu_system_seconds"] - before["process"]["cpu_system_seconds"]
    print(f"server process: cpu_decisions={cpu} cpu_think_total={cpu_ms:.1f}ms "
          f"user_cpu={user_cpu:.2f}s system_cpu={system_cpu:.2f}s "
          f"rss={after['process']['rss_bytes']}B peak_rss={after['process']['peak_rss_bytes']}B "
          f"threads={after['process']['threads']} peak_inflight_since_start={after.get('peak_inflight_api', 'n/a')} "
          f"action_entries={after['hall']['action_history_entries']}")
    if before.get("kernel_tcp") and after.get("kernel_tcp"):
        overflow = after["kernel_tcp"]["listen_overflows"] - before["kernel_tcp"]["listen_overflows"]
        dropped = after["kernel_tcp"]["listen_drops"] - before["kernel_tcp"]["listen_drops"]
        print(f"server kernel TCP: listen_overflows={overflow} listen_drops={dropped} (host-wide)")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:8080")
    parser.add_argument("--tables", type=int, default=5)
    parser.add_argument("--clients-per-table", type=int, default=3, help="One leader plus participants or observers")
    parser.add_argument("--players", type=int, choices=(3, 4), default=3)
    parser.add_argument("--mode", choices=("cpu", "human", "lobby"), default="cpu")
    parser.add_argument("--round-mode", type=int, choices=(0, 1, 2), default=2)
    parser.add_argument("--duration", type=float, default=60.0)
    parser.add_argument("--poll-interval", type=float, default=0.4)
    parser.add_argument("--timeout", type=float, default=5.0)
    parser.add_argument("--ramp-seconds", type=float, default=0.0)
    parser.add_argument("--metrics-url", help="Local /api/metrics URL, for server-side before/after measurements")
    parser.add_argument("--json-out", type=Path, help="Write aggregate client/server measurements to JSON")
    args = parser.parse_args()
    if args.tables < 1 or args.clients_per_table < 1 or args.duration <= 0 or args.poll_interval <= 0 or args.timeout <= 0 or not 0 <= args.ramp_seconds < args.duration:
        parser.error("tables, clients, duration, poll interval and timeout must be positive; ramp must be shorter than duration")
    base = args.base_url.rstrip("/")
    run_id = uuid4().hex[:10]
    clients: list[tuple[str, bool, int]] = []
    sessions: dict[str, ApiSession] = {}
    stats = Stats()
    finished: set[str] = set()
    finished_lock = threading.Lock()
    metrics_before, metrics_before_error = fetch_metrics_optional(args.metrics_url, args.timeout)
    if metrics_before_error:
        print(f"server metrics before unavailable: {metrics_before_error}")
    try:
        # Setup is excluded from the steady-state latency report.
        for table_index in range(args.tables):
            leader = f"load-{run_id}-{table_index}-0"
            sessions[leader] = ApiSession(base, args.timeout)
            state = request(base, "/api/table", leader, args.timeout,
                            {"name": f"load-{table_index}", "players": args.players,
                             "sit_out": args.mode == "cpu", "round_mode": args.round_mode},
                            session=sessions[leader])
            clients.append((leader, True, table_index))
            table_id = state["table_id"]
            if args.mode == "cpu":
                request(base, "/api/start", leader, args.timeout, {}, session=sessions[leader])
            for client_index in range(1, args.clients_per_table):
                client = f"load-{run_id}-{table_index}-{client_index}"
                if args.mode == "human" and client_index == args.players:
                    # The remaining virtual clients join an ongoing game as observers.
                    request(base, "/api/start", leader, args.timeout, {}, session=sessions[leader])
                sessions[client] = ApiSession(base, args.timeout)
                request(base, "/api/join", client, args.timeout,
                        {"table": table_id, "name": f"load-{table_index}-{client_index}"},
                        session=sessions[client])
                clients.append((client, False, table_index))
            if args.mode == "human" and args.clients_per_table <= args.players:
                request(base, "/api/start", leader, args.timeout, {}, session=sessions[leader])
        # Setup connections can sit idle longer than Nginx's keepalive timeout
        # while later tables ramp up; start polling on fresh connections.
        for session in sessions.values():
            session.close()
        print(f"created {args.tables} tables / {len(clients)} clients; mode={args.mode}")
        started = time.monotonic()
        stop_at = started + args.duration
        with ThreadPoolExecutor(max_workers=len(clients)) as executor:
            futures = [executor.submit(worker, base, client, leader, args.mode,
                                       started + args.ramp_seconds * table_index / max(1, args.tables - 1), stop_at,
                                       sessions[client],
                                       args.poll_interval, args.timeout, stats, finished, finished_lock)
                       for client, leader, table_index in clients]
            for future in futures:
                future.result()
        print_report(time.monotonic() - started, stats, len(finished))
        metrics_after, metrics_after_error = fetch_metrics_optional(args.metrics_url, args.timeout)
        if metrics_after_error:
            print(f"server metrics after unavailable: {metrics_after_error}")
        if metrics_before and metrics_after:
            print_server_delta(metrics_before, metrics_after)
        if args.json_out:
            save_results(args.json_out, args, stats, len(finished), metrics_before, metrics_after,
                         metrics_before_error, metrics_after_error)
        return 1 if stats.snapshot()["errors"] else 0
    finally:
        for client, _, _ in reversed(clients):
            try:
                request(base, "/api/leave", client, args.timeout, {}, session=sessions[client])
            except RuntimeError:
                pass
        for session in sessions.values():
            session.close()


if __name__ == "__main__":
    raise SystemExit(main())
