#!/usr/bin/env bash
set -euo pipefail
CONOHA_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
PROJECT_ROOT=$(cd "$CONOHA_DIR/.." && pwd)
CONFIG=${QUOTA_DEPLOY_CONFIG:-"$CONOHA_DIR/config.sh"}
[ -f "$CONFIG" ] || { echo "Copy conoha/config.example.sh to conoha/config.sh and edit it." >&2; exit 1; }
# This is a trusted local shell configuration, not a downloaded file.
source "$CONFIG"
: "${VPS_HOST:?Set VPS_HOST in config.sh}"
SSH_USER=${SSH_USER:-root}; SSH_PORT=${SSH_PORT:-22}; SSH_KEY=${SSH_KEY:-}
REMOTE_ROOT=${REMOTE_ROOT:-/srv/quota}; APP_PORT=${APP_PORT:-8080}; SERVER_NAME=${SERVER_NAME:-_}
PUBLIC_DOMAIN=${PUBLIC_DOMAIN:-app1.gunsnstoneroses.com}
[[ "$VPS_HOST" =~ ^[A-Za-z0-9._-]+$ ]] || { echo 'Use an IPv4 address, DNS name or SSH alias.' >&2; exit 1; }
[[ "$SSH_USER" =~ ^[A-Za-z0-9_-]+$ ]] || exit 1
[[ "$REMOTE_ROOT" =~ ^/srv/[A-Za-z0-9_-]+$ ]] || { echo 'REMOTE_ROOT must be /srv/<simple-name>.' >&2; exit 1; }
[[ "$SERVER_NAME" =~ ^[A-Za-z0-9._-]+$ ]] || exit 1
[[ "$PUBLIC_DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]] && [[ "$PUBLIC_DOMAIN" == *.* ]] || exit 1
for port in "$SSH_PORT" "$APP_PORT"; do
  [[ "$port" =~ ^[1-9][0-9]*$ ]] && [ "$port" -le 65535 ] || { echo 'Invalid port.' >&2; exit 1; }
done
[ "$APP_PORT" -ge 1024 ] && [ "$APP_PORT" != "$SSH_PORT" ] || { echo 'APP_PORT must be >=1024 and differ from SSH_PORT.' >&2; exit 1; }
SSH_TARGET="$SSH_USER@$VPS_HOST"
SSH_ARGS=(-o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=4 -p "$SSH_PORT")
if [ "${QUOTA_SSH_DEBUG:-0}" = 1 ]; then SSH_ARGS+=(-v); fi
if [ -n "$SSH_KEY" ]; then SSH_ARGS+=(-i "$SSH_KEY"); fi
# rsync's -e value is a shell command; quote every argument rather than interpolate paths.
printf -v RSYNC_SSH '%q ' ssh "${SSH_ARGS[@]}"
remote() { ssh "${SSH_ARGS[@]}" "$SSH_TARGET" "$@"; }
root_command() {
  if [ "$SSH_USER" = root ]; then printf 'bash '; else printf 'sudo -n bash '; fi
  printf '%q ' "$@"
}
