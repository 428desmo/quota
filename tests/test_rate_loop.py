import importlib.util
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def _loop():
    spec = importlib.util.spec_from_file_location("rate_loop", ROOT / "tools" / "rate_loop.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _repo(path):
    def git(*args):
        return subprocess.run(
            ("git",) + args, cwd=path, check=True, capture_output=True, text=True
        ).stdout

    (path / "unity" / "Assets" / "StreamingAssets").mkdir(parents=True)
    git("init", "-q", "-b", "work")
    git("config", "user.email", "loop@example.com")
    git("config", "user.name", "loop")
    (path / "README.md").write_text("start\n", encoding="utf-8")
    git("add", "README.md")
    git("commit", "-q", "-m", "first")
    return git


def test_the_commit_holds_the_ranking_files_alone(tmp_path, monkeypatch):
    loop = _loop()
    repo = tmp_path / "repo"
    repo.mkdir()
    git = _repo(repo)
    for name in loop.PATHS:
        (repo / name).write_text("{}\n", encoding="utf-8")
    (repo / "README.md").write_text("edited while the loop ran\n", encoding="utf-8")
    (repo / "stray.txt").write_text("untracked\n", encoding="utf-8")
    monkeypatch.setattr(loop, "ROOT", repo)

    assert loop.publish("Add 2000 CPU rating matches (2000 total).", "origin", False) == "committed"

    inside = git("show", "--name-only", "--format=", "HEAD").split()
    assert sorted(inside) == sorted(loop.PATHS)
    assert git("log", "-1", "--format=%s").strip() == "Add 2000 CPU rating matches (2000 total)."
    assert (repo / "README.md").read_text(encoding="utf-8") == "edited while the loop ran\n"
    waiting = git("status", "--porcelain")
    assert "README.md" in waiting
    assert "stray.txt" in waiting


def test_a_batch_that_changed_nothing_is_not_committed(tmp_path, monkeypatch):
    loop = _loop()
    repo = tmp_path / "repo"
    repo.mkdir()
    git = _repo(repo)
    monkeypatch.setattr(loop, "ROOT", repo)

    assert loop.publish("nothing yet", "origin", False) == "nothing to commit"

    for name in loop.PATHS:
        (repo / name).write_text("{}\n", encoding="utf-8")
    assert loop.publish("first batch", "origin", False) == "committed"
    assert loop.publish("same again", "origin", False) == "nothing to commit"
    assert len(git("log", "--format=%s").split("\n")) == 3
