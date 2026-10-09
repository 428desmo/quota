from tools import load_test_multitable
from tools.load_test_multitable import ApiSession, Stats, percentile_bound, request
from tools.serve_unity_web import ServerMetrics, diagnostic_signature


def test_server_metrics_bucket_and_snapshot_are_bounded_copies():
    metrics = ServerMetrics()
    metrics.observe("GET /api/state", 200, 2.5, 1.5, 0.4, 0.2, 512)
    metrics.observe("GET /api/state", 500, 750, 300, 250, 10, 0)
    snapshot = metrics.snapshot()
    row = snapshot["routes"]["GET /api/state"]
    assert row["requests"] == 2
    assert row["statuses"] == {"200": 1, "500": 1}
    assert row["response_bytes"] == 512
    assert sum(row["duration_ms_buckets"]) == 2
    assert sum(row["lock_wait_ms_buckets"]) == 2
    row["duration_ms_buckets"][0] = 999
    assert metrics.snapshot()["routes"]["GET /api/state"]["duration_ms_buckets"][0] != 999


def test_countdown_changes_do_not_create_diagnostic_state_changes():
    state = {"phase": "playing", "table_id": "a", "actions": ["pass"], "turn_remaining": 20.0}
    signature = diagnostic_signature(state)
    state["turn_remaining"] = 19.6
    assert diagnostic_signature(state) == signature
    state["actions"].append("pass")
    assert diagnostic_signature(state) != signature


def test_load_report_counts_errors_and_percentile_bound():
    stats = Stats()
    stats.add("/api/state", 200, 50, 200)
    stats.add("/api/state", 500, 450, 0, "failure")
    row = stats.snapshot()["routes"]["/api/state"]
    assert row["statuses"] == {200: 1, 500: 1}
    assert stats.snapshot()["errors"] == {"failure": 1}
    assert percentile_bound(row, .5) == "≤50 ms"
    assert percentile_bound(row, .95) == "≤800 ms"


def test_virtual_client_reuses_one_http_connection(monkeypatch):
    connections = []

    class FakeResponse:
        status = 200

        def read(self):
            return b'{"phase":"hall"}'

    class FakeConnection:
        def __init__(self, host, port, timeout):
            self.calls = []
            connections.append(self)

        def request(self, method, path, body, headers):
            self.calls.append((method, path, body, headers))

        def getresponse(self):
            return FakeResponse()

        def close(self):
            pass

    monkeypatch.setattr(load_test_multitable, "HTTPConnection", FakeConnection)
    session = ApiSession("http://127.0.0.1:18080", 5)
    assert request("http://127.0.0.1:18080", "/api/state", "virtual-a", 5, session=session)["phase"] == "hall"
    assert request("http://127.0.0.1:18080", "/api/state", "virtual-a", 5, session=session)["phase"] == "hall"
    assert len(connections) == 1
    assert len(connections[0].calls) == 2
    assert all(call[3]["X-Quota-Client"] == "virtual-a" for call in connections[0].calls)
