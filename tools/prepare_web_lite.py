"""Give each complete lightweight release its own offline cache identity."""
import argparse
import hashlib
from pathlib import Path


def stamp_release(root: Path) -> str:
    worker = root / "sw.js"
    source = worker.read_text()
    if source.count("__QUOTA_RELEASE__") != 1:
        raise ValueError("Expected one unstamped release token in sw.js")
    digest = hashlib.sha256()
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Unexpected symlink: {path}")
        if path.is_file():
            digest.update(path.relative_to(root).as_posix().encode() + b"\0")
            digest.update(path.read_bytes() + b"\0")
    revision = digest.hexdigest()[:16]
    worker.write_text(source.replace("__QUOTA_RELEASE__", revision))
    return revision


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    print("Lightweight offline release:", stamp_release(args.directory))
