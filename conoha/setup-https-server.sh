#!/usr/bin/env bash
set -Eeuo pipefail
[ "$EUID" -eq 0 ] || { echo 'Root privileges required.' >&2; exit 1; }
domain=$1; email=$2; app_port=$3; root=$4
[[ "$domain" =~ ^[A-Za-z0-9.-]+$ ]] && [[ "$domain" == *.* ]] || exit 1
[[ "$app_port" =~ ^[0-9]+$ ]] && [[ "$root" =~ ^/srv/[A-Za-z0-9_-]+$ ]] || exit 1
[ -f /etc/nginx/conf.d/quota.conf ] && [ -f "$root/web/current/index.html" ] || {
  echo 'Deploy the HTTP app before configuring HTTPS.' >&2; exit 1;
}

dnf install -y certbot python3-certbot-nginx

certificate="/etc/letsencrypt/live/$domain/fullchain.pem"
if [ ! -f "$certificate" ]; then
  # Give the Nginx authenticator a matching host while obtaining the first
  # certificate. Restore the IP-access virtual host even if issuance fails.
  original=$(mktemp /tmp/quota-nginx-http.XXXXXXXX)
  cp -p /etc/nginx/conf.d/quota.conf "$original"
  restore_http() {
    trap - ERR INT TERM
    cp -p "$original" /etc/nginx/conf.d/quota.conf
    rm -f "$original"
    nginx -t && systemctl reload nginx
  }
  trap 'restore_http' ERR INT TERM
  if grep -q 'server_name _;' /etc/nginx/conf.d/quota.conf; then
    sed -i "s/server_name _;/server_name $domain _;/" /etc/nginx/conf.d/quota.conf
  fi
  nginx -t
  systemctl reload nginx
  cert_args=(certonly --nginx --non-interactive --agree-tos --no-eff-email -d "$domain")
  if [ -n "$email" ]; then
    cert_args+=(--email "$email")
  else
    cert_args+=(--register-unsafely-without-email)
  fi
  certbot "${cert_args[@]}"
  restore_http
  trap - ERR INT TERM
fi

config=/etc/nginx/conf.d/quota-https.conf
temporary="$config.next"
cat > "$temporary" <<NGINX
server {
    listen 80;
    server_name $domain;
    return 301 https://$domain\$request_uri;
}
server {
    listen 443 ssl;
    server_name $domain;
    root $root/web/current/portal;
    index index.html;
    ssl_certificate /etc/letsencrypt/live/$domain/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$domain/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_session_cache shared:SSL:10m;
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
        proxy_pass http://127.0.0.1:$app_port;
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
if [ -f "$config" ]; then cp -p "$config" "$config.quota-before"; fi
mv "$temporary" "$config"
if ! nginx -t; then
  if [ -f "$config.quota-before" ]; then mv "$config.quota-before" "$config"; else rm -f "$config"; fi
  exit 1
fi
systemctl reload nginx
firewall-cmd --permanent --add-service=https
firewall-cmd --reload

install -d -m 0755 /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/quota-reload-nginx.sh <<'HOOK'
#!/usr/bin/env bash
set -euo pipefail
nginx -t
systemctl reload nginx
HOOK
chmod 0755 /etc/letsencrypt/renewal-hooks/deploy/quota-reload-nginx.sh
if systemctl list-unit-files --type=timer | grep -q '^certbot-renew.timer'; then
  systemctl enable --now certbot-renew.timer
elif systemctl list-unit-files --type=timer | grep -q '^certbot.timer'; then
  systemctl enable --now certbot.timer
else
  echo 'No Certbot renewal timer found; set up scheduled renewal before relying on this certificate.' >&2
  exit 1
fi
certbot renew --dry-run
curl -fsS --max-time 10 --resolve "$domain:443:127.0.0.1" "https://$domain/api/state" >/dev/null
echo "HTTPS ready: https://$domain/"
echo 'Also allow inbound TCP 443 in the ConoHa security group.'
