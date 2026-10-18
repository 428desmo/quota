# Copy to config.sh, then edit. Do not put passwords here; use SSH keys/agent.
VPS_HOST=""                   # IP, DNS name or an alias in ~/.ssh/config
SSH_USER="root"               # Other users require passwordless sudo for deployment
SSH_PORT=22
SSH_KEY=""                    # Optional absolute path to a private key
REMOTE_ROOT="/srv/quota"
APP_PORT=8080
SERVER_NAME="_"               # Domain name, or _ for access by IP (HTTP)
PUBLIC_DOMAIN="app1.gunsnstoneroses.com" # HTTPS host for the index and /quota/
WEB_DIR=""                    # Default: unity/Builds/WebGL
