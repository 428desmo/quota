from tools.load_test_multitable import Stats, percentile_bound
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
