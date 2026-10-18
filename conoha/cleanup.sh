#!/usr/bin/env bash
# Run only after stopping any previous update. Cleans abandoned upload staging.
set -euo pipefail
source "$(dirname "$0")/common.sh"
remote "$(root_command -s --)" <<'REMOTE'
set -euo pipefail
python3 - <<'PY'
import pathlib, re, shutil
stages = [p for p in pathlib.Path('/tmp').glob('quota-deploy.*') if re.fullmatch(r'quota-deploy\.[A-Za-z0-9]+', p.name) and p.is_dir() and not p.is_symlink()]
for proc in pathlib.Path('/proc').glob('[0-9]*/cmdline'):
    try:
        cmd = proc.read_bytes().replace(b'\0', b' ').decode(errors='replace')
    except (OSError, PermissionError):
        continue
    if any(str(p) in cmd for p in stages):
        raise SystemExit('A deployment/transfer process is still using a staging directory; stop it before cleanup.')
for stage in stages:
    if not ((stage/'web').is_dir() or (stage/'backend/tools/serve_unity_web.py').is_file()):
        print(f'Skipping unrecognized directory: {stage}')
        continue
    print(f'Removing abandoned upload: {stage}', flush=True)
    shutil.rmtree(stage)
PY
REMOTE
