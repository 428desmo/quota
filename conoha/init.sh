#!/usr/bin/env bash
# Run from your Mac: ./conoha/init.sh
set -euo pipefail
source "$(dirname "$0")/common.sh"
echo "Connecting to $SSH_TARGET (SSH port $SSH_PORT)..."
echo 'No VPS changes are made until the SSH connection succeeds.'
remote "$(root_command -s -- "$REMOTE_ROOT" "$APP_PORT" "$SERVER_NAME" "$SSH_PORT")" < "$CONOHA_DIR/setup-server.sh"
echo 'Initial setup completed. Run ./conoha/update.sh to deploy the app.'
