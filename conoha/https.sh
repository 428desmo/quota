#!/usr/bin/env bash
# Configure a dedicated HTTPS virtual host without changing HTTP access by IP.
set -Eeuo pipefail
source "$(dirname "$0")/common.sh"
domain=${1:-app1.gunsnstoneroses.com}
email=${QUOTA_CERT_EMAIL:-}
[[ "$domain" =~ ^[A-Za-z0-9.-]+$ ]] && [[ "$domain" == *.* ]] || { echo 'Invalid domain.' >&2; exit 1; }
echo "Configuring HTTPS for $domain on $SSH_TARGET"
remote "$(root_command -s -- "$domain" "$email" "$APP_PORT" "$REMOTE_ROOT")" < "$CONOHA_DIR/setup-https-server.sh"
