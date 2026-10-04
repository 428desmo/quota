"""Serve the Unity WebGL export. The Python table stays on port 8000."""

from __future__ import annotations

import argparse
import urllib.error
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ROOT / "unity" / "Builds" / "WebGL"
PORT = 8080
TABLE = "http://127.0.0.1:8000"

TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript",
    ".css": "text/css; charset=utf-8",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".unityweb": "application/octet-stream",
    ".json": "application/json",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".svg": "image/svg+xml",
    ".ico": "image/x-icon",
}


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, directory: str, **kwargs):
        super().__init__(*args, directory=directory, **kwargs)

    def do_GET(self) -> None:
        if self.path.startswith("/api/"):
            self.proxy()
            return
        super().do_GET()

    def do_POST(self) -> None:
        if self.path.startswith("/api/"):
            self.proxy()
            return
        self.send_error(404)

    def do_OPTIONS(self) -> None:
        if self.path.startswith("/api/"):
            self.proxy()
            return
        self.send_error(404)

    def proxy(self) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else None
        request = urllib.request.Request(TABLE + self.path, data=body, method=self.command)
        for name in ("Content-Type", "X-Quota-Client"):
            value = self.headers.get(name)
            if value:
                request.add_header(name, value)
        try:
            with urllib.request.urlopen(request, timeout=12) as response:
                payload = response.read()
                self.send_response(response.status)
                self.send_header("Content-Type", response.headers.get("Content-Type", "application/json"))
                self.send_header("Cache-Control", "no-store")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
        except urllib.error.HTTPError as error:
            payload = error.read()
            self.send_response(error.code)
            self.send_header("Content-Type", error.headers.get("Content-Type", "application/json"))
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        except OSError:
            self.send_error(502, "the table server on port 8000 is not running")

    def end_headers(self) -> None:
        path = Path(self.translate_path(self.path))
        if path.suffix == ".gz":
            self.send_header("Content-Encoding", "gzip")
        elif path.suffix == ".br":
            self.send_header("Content-Encoding", "br")
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def guess_type(self, path: str) -> str:
        name = Path(path)
        if name.suffix in (".gz", ".br"):
            name = Path(name.stem)
        return TYPES.get(name.suffix, "application/octet-stream")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=PORT)
    parser.add_argument("--dir", type=Path, default=DEFAULT_DIR)
    args = parser.parse_args()
    folder = args.dir.resolve()
    if not folder.is_dir():
        raise SystemExit(f"WebGL export is not built yet: {folder}")
    server = ThreadingHTTPServer(("0.0.0.0", args.port), lambda *a, **k: Handler(*a, directory=str(folder), **k))
    print(f"http://127.0.0.1:{args.port}  （Unity WebGL。Python版は 8000 のまま）")
    server.serve_forever()


if __name__ == "__main__":
    main()
