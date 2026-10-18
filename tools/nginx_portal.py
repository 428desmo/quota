#!/usr/bin/env python3
"""Give the site root an app index and serve Quota from /quota/."""

import argparse
from pathlib import Path


def portal_config(contents: str, root: str) -> str:
    old_root = f"root {root}/web/current;"
    new_root = f"root {root}/web/current/portal;"
    if old_root not in contents and new_root not in contents:
        raise ValueError("Quota Nginx document root was not found")
    contents = contents.replace(old_root, new_root)
    if "location ^~ /quota/" not in contents:
        anchor = "    location /api/ {"
        if anchor not in contents:
            raise ValueError("Quota API location was not found")
        locations = f"""    location = /quota {{
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
        contents = contents.replace(anchor, locations + anchor, 1)
    return contents


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root")
    parser.add_argument("configs", nargs="+", type=Path)
    args = parser.parse_args()
    for path in args.configs:
        path.write_text(portal_config(path.read_text(), args.root))
