#!/usr/bin/env bash
# Invoked remotely by init.sh. No application is uploaded by this script.
set -Eeuo pipefail
step='startup'
progress() { step=$1; printf '\n[%s] %s\n' "$(date -Is)" "$step"; }
trap 'status=$?; printf "Failed during: %s (line %s, exit %s)\n" "$step" "$LINENO" "$status" >&2; exit "$status"' ERR
if [ "${QUOTA_INIT_TRACE:-0}" = 1 ]; then set -x; fi
[ "$EUID" -eq 0 ] || { echo 'Root privileges required.' >&2; exit 1; }
root=$1; port=$2; server_name=$3; ssh_port=$4
. /etc/os-release
[ "$ID" = almalinux ] && [[ "$VERSION_ID" = 10* ]] || { echo 'This setup targets AlmaLinux 10.x.' >&2; exit 1; }
progress 'Installing packages with dnf (repository access / existing dnf lock)'
dnf install -y nginx python3 rsync curl firewalld policycoreutils-python-utils
progress 'Creating application user and directories'
id quota >/dev/null 2>&1 || useradd --system --home-dir "$root" --shell /sbin/nologin quota
install -d -m 0755 "$root" "$root/web" "$root/web/releases" "$root/backend" "$root/backend/releases"
progress 'Writing systemd and Nginx configuration'
cat > /etc/systemd/system/quota.service <<SERVICE
[Unit]
Description=Quota multiplayer API
After=network.target
ConditionPathExists=$root/backend/current/tools/serve_unity_web.py
[Service]
Type=simple
User=quota
Group=quota
WorkingDirectory=$root/backend/current
Environment=PYTHONUNBUFFERED=1
Environment=PYTHONDONTWRITEBYTECODE=1
ExecStart=/usr/bin/python3 $root/backend/current/tools/serve_unity_web.py --host 127.0.0.1 --port $port --dir $root/web/current
Restart=on-failure
RestartSec=3
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
MemoryAccounting=true
[Install]
WantedBy=multi-user.target
SERVICE
# Dedicated Quota VPS: remove the distribution welcome server, keeping a backup.
[ -e /etc/nginx/nginx.conf.quota-before ] || cp -p /etc/nginx/nginx.conf /etc/nginx/nginx.conf.quota-before
cat > /etc/nginx/nginx.conf <<'MAIN'
user nginx;
worker_processes 1;
error_log /var/log/nginx/error.log;
pid /run/nginx.pid;
events { worker_connections 256; }
http {
    include /etc/nginx/mime.types;
    default_type application/octet-stream;
    access_log /var/log/nginx/access.log;
    sendfile on;
    keepalive_timeout 15;
    include /etc/nginx/conf.d/*.conf;
}
MAIN
cat > /etc/nginx/conf.d/quota.conf <<NGINX
server {
    listen 80 default_server;
    server_name $server_name;
    root $root/web/current/portal;
    index index.html;
    location = /quota { return 308 /quota/; }
    location ^~ /quota/Build/ {
        alias $root/web/current/Build/;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }
    location ^~ /quota/ {
        alias $root/web/current/;
        add_header Cache-Control "no-cache";
    }
    location /api/ {
        proxy_pass http://127.0.0.1:$port;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_connect_timeout 3s;
        proxy_read_timeout 30s;
        add_header Cache-Control "no-store" always;
    }
    location ~ \.wasm\$ {
        default_type application/wasm;
        try_files \$uri =404;
    }
    location / {
        try_files \$uri \$uri/ =404;
        add_header Cache-Control "no-cache";
    }
}
NGINX
# Keep SELinux enforcing; label the static tree and allow reverse-proxy connections.
progress 'Applying SELinux configuration'
if command -v selinuxenabled >/dev/null && selinuxenabled; then
  semanage fcontext -a -t httpd_sys_content_t "$root/web(/.*)?" 2>/dev/null || semanage fcontext -m -t httpd_sys_content_t "$root/web(/.*)?"
  restorecon -RF "$root/web"
  setsebool -P httpd_can_network_connect on
fi
# Preserve SSH access, including a non-standard configured port.
progress 'Configuring firewalld and preserving SSH access'
if ! systemctl is-active --quiet firewalld; then
  firewall-offline-cmd --add-service=ssh
  firewall-offline-cmd --add-port="$ssh_port/tcp"
fi
systemctl enable --now firewalld
zone=$(firewall-cmd --get-default-zone)
firewall-cmd --permanent --zone="$zone" --add-service=ssh
firewall-cmd --permanent --zone="$zone" --add-port="$ssh_port/tcp"
firewall-cmd --permanent --zone="$zone" --add-service=http
firewall-cmd --reload
progress 'Checking Nginx and enabling services'
nginx -t
systemctl daemon-reload
systemctl enable quota nginx
systemctl enable --now nginx
systemctl reload nginx
printf '\nMemory and swap (512 MB operation should be measured):\n'
free -h
printf '\nAllow TCP 80 in the ConoHa security group as well. Port %s stays private.\n' "$port"
echo 'HTTP configured. HTTPS/certificates are a separate step once a domain is assigned.'
