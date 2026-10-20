#!/usr/bin/env bash
# Run from your Mac after exporting WebGL: ./conoha/update.sh
set -Eeuo pipefail
step='startup'
progress() { step=$1; printf '\n[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$step" >&2; }
trap 'status=$?; printf "Update failed during: %s (line %s, exit %s)\n" "$step" "$LINENO" "$status" >&2; exit "$status"' ERR
source "$(dirname "$0")/common.sh"
WEB_DIR=${WEB_DIR:-"$PROJECT_ROOT/unity/Builds/WebGL"}
[ -f "$WEB_DIR/index.html" ] && [ -d "$WEB_DIR/Build" ] || { echo "No WebGL export at $WEB_DIR; build/export in Unity first." >&2; exit 1; }
progress "Checking SSH connection to $SSH_TARGET"
remote 'echo "SSH connection OK"'
progress 'Preparing Python backend and data locally'
local_stage=$(mktemp -d "${TMPDIR:-/tmp}/quota-upload.XXXXXX")
remote_stage=''
cleanup() {
  echo "Cleaning up temporary upload directories..." >&2
  rm -rf "$local_stage"
  if [ -n "$remote_stage" ]; then
    printf -v cleanup_cmd 'rm -rf -- %q' "$remote_stage"
    remote "$cleanup_cmd" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT
progress 'Selecting only the current WebGL build locally'
python3 "$CONOHA_DIR/web_files.py" prepare "$WEB_DIR" "$local_stage/web"
mkdir -p "$local_stage/web/portal"
rsync -a "$PROJECT_ROOT/portal/" "$local_stage/web/portal/"
cp "$PROJECT_ROOT/visual/horizontal_base.jpg" "$local_stage/web/portal/harbor-wide.jpg"
mkdir -p "$local_stage/web/quota2"
rsync -a --exclude='*.md' --exclude='*.test.mjs' --exclude=tests/ "$PROJECT_ROOT/web-lite/" "$local_stage/web/quota2/"
python3 "$PROJECT_ROOT/tools/prepare_web_lite.py" "$local_stage/web/quota2"
mkdir -p "$local_stage/backend/tools"
rsync -a --exclude=__pycache__ --exclude='*.pyc' "$PROJECT_ROOT/quota/" "$local_stage/backend/quota/"
cp "$PROJECT_ROOT/tools/serve_unity_web.py" "$local_stage/backend/tools/"
for file in quota_goods_v1.0.json quota_item_sets_v1.0.json cpu_ranking_v1.2.json; do
  cp "$PROJECT_ROOT/$file" "$local_stage/backend/"
done
python3 - "$local_stage/backend" <<'PY'
import hashlib, pathlib, sys
root = pathlib.Path(sys.argv[1]); digest = hashlib.sha256()
for path in sorted(root.rglob('*')):
    if path.is_file():
        digest.update(str(path.relative_to(root)).encode() + b'\0')
        digest.update(path.read_bytes() + b'\0')
(root / 'MANIFEST').write_text(digest.hexdigest() + '\n')
PY
progress 'Creating temporary upload directory on VPS'
remote_stage=$(remote 'mktemp -d /tmp/quota-deploy.XXXXXXXX')
[[ "$remote_stage" =~ ^/tmp/quota-deploy\.[A-Za-z0-9]+$ ]] || { echo 'Unexpected remote staging path.' >&2; exit 1; }
# Seed only the previous active build, never historical Build artifacts.
progress 'Uploading selection/cleanup helper'
rsync -av --progress --timeout=120 -e "$RSYNC_SSH" "$CONOHA_DIR/web_files.py" "$SSH_TARGET:$remote_stage/web_files.py"
printf -v seed_cmd 'if [ -f %q/web/current/index.html ]; then python3 %q/web_files.py prepare %q/web/current %q/web; else mkdir -p %q/web; fi' "$REMOTE_ROOT" "$remote_stage" "$REMOTE_ROOT" "$remote_stage" "$remote_stage"
progress 'Copying previous active Web build on VPS'
remote "$seed_cmd"
progress "Uploading WebGL export ($(du -sh "$local_stage/web" | awk '{print $1}')); first upload sends the full build"
rsync -av --progress --timeout=120 --delete -e "$RSYNC_SSH" "$local_stage/web/" "$SSH_TARGET:$remote_stage/web/"
progress 'Uploading Python backend and data'
rsync -av --progress --timeout=120 --delete -e "$RSYNC_SSH" "$local_stage/backend/" "$SSH_TARGET:$remote_stage/backend/"
progress 'Uploading deployment script'
rsync -av --progress --timeout=120 -e "$RSYNC_SSH" "$CONOHA_DIR/deploy-server.sh" "$SSH_TARGET:$remote_stage/deploy-server.sh"
rsync -a --timeout=120 -e "$RSYNC_SSH" "$CONOHA_DIR/configure-index-server.sh" "$PROJECT_ROOT/tools/nginx_portal.py" "$SSH_TARGET:$remote_stage/"
progress 'Publishing release and checking services on VPS'
remote "$(root_command "$remote_stage/deploy-server.sh" "$REMOTE_ROOT" "$APP_PORT")"
progress 'Configuring the app index and /quota1/, /quota2/, /quota/ routes'
remote "$(root_command "$remote_stage/configure-index-server.sh" "$REMOTE_ROOT" "$PUBLIC_DOMAIN")"
echo "Deployment completed: https://$PUBLIC_DOMAIN/ (index), https://$PUBLIC_DOMAIN/quota1/ (Unity), https://$PUBLIC_DOMAIN/quota2/ (lightweight), https://$PUBLIC_DOMAIN/quota/ (Unity alias)"
