#!/usr/bin/env bash
# Run after a Web release containing portal/index.html has been published.
set -Eeuo pipefail
[ "$EUID" -eq 0 ] || { echo 'Root privileges required.' >&2; exit 1; }
root=$1; domain=$2; stage=$(cd "$(dirname "$0")" && pwd)
[[ "$root" =~ ^/srv/[A-Za-z0-9_-]+$ ]] && [[ "$domain" =~ ^[A-Za-z0-9.-]+$ ]] || exit 1
[ -f "$root/web/current/portal/index.html" ] || { echo 'Portal page is missing from current release.' >&2; exit 1; }
http_config=/etc/nginx/conf.d/quota.conf
https_config=/etc/nginx/conf.d/quota-https.conf
[ -f "$http_config" ] || { echo 'Quota HTTP config required.' >&2; exit 1; }
backup=$(mktemp -d /tmp/quota-portal-nginx.XXXXXXXX)
cp -p "$http_config" "$backup/http.conf"
configs=("$http_config")
if [ -f "$https_config" ]; then
  cp -p "$https_config" "$backup/https.conf"
  configs+=("$https_config")
fi
restore() {
  cp -p "$backup/http.conf" "$http_config"
  if [ -f "$backup/https.conf" ]; then cp -p "$backup/https.conf" "$https_config"; fi
  nginx -t && systemctl reload nginx
}
cleanup() { rm -rf "$backup"; }
trap cleanup EXIT
if ! python3 "$stage/nginx_portal.py" "$root" "${configs[@]}" || ! nginx -t || ! systemctl reload nginx; then
  restore
  echo 'Nginx portal switch failed; previous configuration restored.' >&2
  exit 1
fi
check_page() {
  label=$1; expected=$2; shift 2
  if ! curl -fsS --max-time 10 "$@" -o "$backup/$label"; then
    echo "$label: HTTP request failed" >&2
    return 1
  fi
  if ! grep -q "$expected" "$backup/$label"; then
    echo "$label: expected page marker missing" >&2
    return 1
  fi
  echo "$label: OK"
}
index_ready=false
for attempt in $(seq 1 10); do
  if check_page http-index '遊びの港' -H 'Host: _' http://127.0.0.1/; then
    index_ready=true
    break
  fi
  sleep 1
done
pages_ready=true
if [ "$index_ready" != true ] \
    || ! check_page http-quota createUnityInstance -H 'Host: _' http://127.0.0.1/quota/; then
  pages_ready=false
fi
if [ -f "$backup/https.conf" ] && [ "$pages_ready" = true ]; then
  if ! check_page https-index '遊びの港' --resolve "$domain:443:127.0.0.1" "https://$domain/" \
      || ! check_page https-quota createUnityInstance --resolve "$domain:443:127.0.0.1" "https://$domain/quota/"; then
    pages_ready=false
  fi
fi
if [ "$pages_ready" != true ]; then
  restore
  echo 'Portal health check failed; previous configuration restored.' >&2
  exit 1
fi
echo "Index and Quota ready at / and /quota/ on the configured virtual hosts."
