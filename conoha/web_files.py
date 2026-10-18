#!/usr/bin/env python3
"""Select the current Unity Build; never delete outside its Build directory."""
import argparse
import re
import shutil
from pathlib import Path


def build_files(root):
    html = (root / 'index.html').read_text(encoding='utf-8')
    directory = re.search(r'(?:var|let|const)\s+buildUrl\s*=\s*"([^"]+)"', html)
    if not directory or directory.group(1) != 'Build':
        raise ValueError('Unsupported buildUrl; expected Build')
    names = set(re.findall(r'buildUrl\s*\+\s*"/([^"\n]+)"', html))
    if not names or not any(n.endswith('.wasm') for n in names) or not any(n.endswith('.data') for n in names):
        raise ValueError('Cannot identify current Unity build; refusing cleanup')
    for name in names:
        if Path(name).name != name or not (root / 'Build' / name).is_file():
            raise ValueError(f'Missing or unsafe Build reference: {name}')
        if name.endswith(('.gz', '.br', '.unityweb')):
            raise ValueError('Compressed builds require a different Nginx configuration')
    return names


def prepare(root, destination):
    names = build_files(root)
    destination.mkdir()
    for source in root.iterdir():
        if source.name == 'Build':
            continue
        if source.is_symlink():
            raise ValueError(f'Unexpected symlink: {source}')
        if source.is_dir():
            shutil.copytree(source, destination / source.name)
        else:
            shutil.copy2(source, destination / source.name)
    (destination / 'Build').mkdir()
    for name in names:
        shutil.copy2(root / 'Build' / name, destination / 'Build' / name)
    total = sum(p.stat().st_size for p in destination.rglob('*') if p.is_file())
    print(f'Current export: {len(names)} Build files, {total / 1048576:.1f} MiB')


def prune(root):
    names = build_files(root)  # Validate all required files before deleting anything.
    deleted = 0
    for path in (root / 'Build').iterdir():
        if path.is_file() and path.name not in names:
            deleted += path.stat().st_size
            path.unlink()
    print(f'{root}: removed {deleted / 1048576:.1f} MiB of obsolete Build files')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['prepare', 'prune'])
    parser.add_argument('root', type=Path)
    parser.add_argument('destination', type=Path, nargs='?')
    args = parser.parse_args()
    if args.mode == 'prepare':
        if args.destination is None:
            parser.error('prepare requires destination')
        prepare(args.root, args.destination)
    else:
        prune(args.root)
