from pathlib import Path

import pytest

from tools.prepare_web_lite import stamp_release


def release(root: Path, payload: str):
    root.mkdir()
    (root / "sw.js").write_text("const CACHE = '__QUOTA_RELEASE__';")
    (root / "app.mjs").write_text(payload)
    return root


def test_offline_cache_changes_with_the_shipped_assets(tmp_path):
    a = stamp_release(release(tmp_path / "a", "version 1"))
    b = stamp_release(release(tmp_path / "b", "version 1"))
    c = stamp_release(release(tmp_path / "c", "version 2"))
    assert a == b
    assert a != c
    assert "__QUOTA_RELEASE__" not in (tmp_path / "a/sw.js").read_text()
    assert a in (tmp_path / "a/sw.js").read_text()


def test_prepared_worker_is_not_stamped_a_second_time(tmp_path):
    root = release(tmp_path / "a", "version 1")
    stamp_release(root)
    with pytest.raises(ValueError):
        stamp_release(root)
