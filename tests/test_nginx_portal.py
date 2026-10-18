from pathlib import Path

import pytest

from tools.nginx_portal import portal_config


ROOT = "/srv/quota"


def test_portal_routes_keep_api_and_https_redirect():
    before = """server {
    listen 80;
    server_name app1.gunsnstoneroses.com;
    return 301 https://app1.gunsnstoneroses.com$request_uri;
}
server {
    listen 443 ssl;
    root /srv/quota/web/current;
    location /api/ { proxy_pass http://127.0.0.1:8080; }
}
"""
    after = portal_config(before, ROOT)
    assert "return 301 https://app1.gunsnstoneroses.com$request_uri;" in after
    assert "root /srv/quota/web/current/portal;" in after
    assert "alias /srv/quota/web/current/Build/;" in after
    assert "alias /srv/quota/web/current/;" in after
    assert "location = /quota" in after
    assert "location /api/ { proxy_pass http://127.0.0.1:8080; }" in after
    assert portal_config(after, ROOT) == after


def test_unrecognized_nginx_config_is_rejected():
    with pytest.raises(ValueError):
        portal_config("server { root /var/www/html; }", ROOT)


def test_index_links_to_quota_and_retires_only_old_root_worker():
    page = (Path(__file__).resolve().parents[1] / "portal/index.html").read_text()
    for game in ("quota", "fragile", "loop"):
        assert f'href="/{game}/"' in page
    assert 'src="fragile-screen.jpg"' in page
    assert "気分転換にゲームをどうぞ。" in page
    assert 'name="app-index"' in page
    assert 'registration.scope === location.origin + "/"' in page
