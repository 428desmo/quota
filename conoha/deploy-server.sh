#!/usr/bin/env bash
# Invoked remotely by update.sh. Keeps old releases for rollback.
set -Eeuo pipefail
step='startup'
progress() { step=$1; printf '\n[%s] %s\n' "$(date -Is)" "$step"; }
trap 'status=$?; printf "Deployment failed during: %s (line %s, exit %s)\n" "$step" "$LINENO" "$status" >&2; exit "$status"' ERR
[ "$EUID" -eq 0 ] || { echo 'Root privileges required.' >&2; exit 1; }
root=$1; port=$2
stage=$(cd "$(dirname "$0")" && pwd)
[ -f /etc/systemd/system/quota.service ] && [ -d "$root/web/releases" ] || { echo 'Run init.sh first.' >&2; exit 1; }
[ -f "$stage/web/index.html" ] && [ -f "$stage/backend/MANIFEST" ] || exit 1
# Check Python syntax, imports and configured data before changing public links.
progress 'Checking Python imports and data'
PYTHONDONTWRITEBYTECODE=1 python3 - "$stage/backend" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1]); sys.path.insert(0, str(root))
for path in root.rglob('*.py'): compile(path.read_text(), str(path), 'exec')
from tools.serve_unity_web import UnityHall
from quota.ranking import load
from quota.items import goods_pool
load(); goods_pool(); UnityHall()
PY
progress 'Checking Nginx configuration'
nginx -t
release=$(date -u +%Y%m%dT%H%M%SZ)-${stage##*.}
old_web=$(readlink "$root/web/current" || true)
old_backend=$(readlink "$root/backend/current" || true)
backend_changed=true
if [ -n "$old_backend" ] && cmp -s "$root/backend/current/MANIFEST" "$stage/backend/MANIFEST"; then backend_changed=false; fi
replace_link() {
  ln -s "$2" "$1.next.$release"
  mv -Tf "$1.next.$release" "$1"
}
rollback() {
  trap - ERR
  echo "Deployment failed during: $step; restoring previous release links." >&2
  if [ -n "$old_web" ]; then replace_link "$root/web/current" "$old_web"; else rm -f "$root/web/current"; fi
  if [ -n "$old_backend" ]; then
    replace_link "$root/backend/current" "$old_backend"
    if [ "$backend_changed" = true ]; then systemctl restart quota || true; fi
  else
    rm -f "$root/backend/current"; systemctl stop quota || true
  fi
}
# Server-side staging has become root-owned; update.sh removes its own remaining files.
progress 'Installing Web files, permissions and SELinux labels'
new_web="$root/web/releases/$release"
mv "$stage/web" "$new_web"
chown -R root:root "$new_web"
find "$new_web" -type d -exec chmod 0755 {} +
find "$new_web" -type f -exec chmod 0644 {} +
if command -v selinuxenabled >/dev/null && selinuxenabled; then restorecon -RF "$root/web"; fi
new_backend="$old_backend"
if [ "$backend_changed" = true ]; then
  new_backend="$root/backend/releases/$release"
  mv "$stage/backend" "$new_backend"
  chown -R root:root "$new_backend"
  find "$new_backend" -type d -exec chmod 0755 {} +
  find "$new_backend" -type f -exec chmod 0644 {} +
fi
progress 'Switching public release and starting/restarting API if needed'
trap rollback ERR
replace_link "$root/web/current" "$new_web"
if [ "$backend_changed" = true ]; then
  replace_link "$root/backend/current" "$new_backend"
  echo 'Backend changed: restarting quota (active tables will be cleared).'
  systemctl restart quota
elif ! systemctl is-active --quiet quota; then
  systemctl start quota
else
  echo 'Web-only update: quota keeps running and active tables are retained.'
fi
progress 'Waiting for API health check (up to 20 attempts)'
ready=false
for attempt in $(seq 1 20); do
  if curl -fsS --max-time 2 "http://127.0.0.1:$port/api/state" >/dev/null; then ready=true; break; fi
  echo "API not ready: attempt $attempt/20" >&2
  sleep 1
done
[ "$ready" = true ]
progress 'Checking HTTP through Nginx'
curl -fsS --max-time 5 -H 'Host: _' http://127.0.0.1/index.html >/dev/null
trap - ERR
progress 'Removing obsolete Build files from retained Web releases'
for previous in "$root"/web/releases/*; do
  if [ -f "$previous/index.html" ] && [ -d "$previous/Build" ]; then
    python3 "$stage/web_files.py" prune "$previous" || echo "Cleanup skipped for $previous; release retained." >&2
  fi
done
echo "Published $release. Previous releases are retained under $root for rollback."
