#!/usr/bin/env python3
"""Serve both Quota clients while keeping /quota/ as the lightweight alias."""

import argparse
from pathlib import Path


def portal_config(contents: str, root: str) -> str:
    old_root = f"root {root}/web/current;"
    new_root = f"root {root}/web/current/portal;"
    if old_root not in contents and new_root not in contents:
        raise ValueError("Quota Nginx document root was not found")
    contents = contents.replace(old_root, new_root)
    anchor = "    location /api/ {"
    if anchor not in contents:
        raise ValueError("Quota API location was not found")
    old_locations = f"""    location = /quota {{
        return 308 /quota/;
    }}
    location ^~ /quota/Build/ {{
        alias {root}/web/current/Build/;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }}
    location ^~ /quota/ {{
        alias {root}/web/current/;
        add_header Cache-Control "no-cache";
    }}
"""
    locations = f"""    location = /quota1 {{ return 308 /quota1/$is_args$args; }}
    location ^~ /quota1/Build/ {{
        alias {root}/web/current/Build/;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }}
    location ^~ /quota1/ {{
        alias {root}/web/current/;
        index index.html;
        add_header Cache-Control "no-cache";
    }}
    location = /quota2 {{ return 308 /quota2/$is_args$args; }}
    location ^~ /quota2/ {{
        alias {root}/web/current/quota2/;
        index index.html;
        default_type text/javascript;
        add_header Cache-Control "no-cache";
    }}
    location = /quota {{ return 308 /quota/$is_args$args; }}
    location ^~ /quota/ {{
        alias {root}/web/current/quota2/;
        index index.html;
        default_type text/javascript;
        add_header Cache-Control "no-cache";
    }}
"""
    if "location ^~ /quota1/" in contents:
        if locations not in contents:
            raise ValueError("Quota routes differ from the managed configuration")
    elif old_locations in contents:
        contents = contents.replace(old_locations, locations)
    elif "location ^~ /quota/" in contents:
        raise ValueError("Unrecognized existing Quota routes")
    else:
        contents = contents.replace(anchor, locations + anchor, 1)
    return contents


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root")
    parser.add_argument("configs", nargs="+", type=Path)
    args = parser.parse_args()
    for path in args.configs:
        path.write_text(portal_config(path.read_text(), args.root))
