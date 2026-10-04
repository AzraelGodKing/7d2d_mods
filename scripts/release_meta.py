#!/usr/bin/env python3
"""Read ModInfo.xml for a mod folder. Prints GitHub Actions outputs."""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

VERSION = re.compile(r'<Version\s+value="([^"]+)"', re.I)
DISPLAY = re.compile(r'<DisplayName\s+value="([^"]*)"', re.I)
DESCRIPTION = re.compile(r'<Description\s+value="([^"]*)"', re.I)


def parse_version(about: str) -> str:
    match = VERSION.search(about)
    return match.group(1).strip() if match else ""


def parse_name(about: str) -> str:
    match = DISPLAY.search(about)
    return match.group(1).strip() if match else ""


def parse_description(about: str) -> str:
    match = DESCRIPTION.search(about)
    return match.group(1).strip() if match else ""


def write_github_output(path: Path, values: dict[str, str]) -> None:
    with path.open("a", encoding="utf-8") as fh:
        for key, value in values.items():
            if "\n" in value:
                fh.write(f"{key}<<EOF\n{value}\nEOF\n")
            else:
                fh.write(f"{key}={value}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--mod-dir", required=True)
    parser.add_argument("--github-output", action="store_true")
    args = parser.parse_args()

    info_path = Path(args.mod_dir) / "mod" / "ModInfo.xml"
    if not info_path.is_file():
        print(f"Missing {info_path}", file=sys.stderr)
        return 1

    text = info_path.read_text(encoding="utf-8")
    version = parse_version(text)
    name = parse_name(text)
    description = parse_description(text)
    if not version:
        print(f"No Version in {info_path}", file=sys.stderr)
        return 1
    if not name:
        print(f"No DisplayName in {info_path}", file=sys.stderr)
        return 1

    values = {
        "version": version,
        "display_name": name,
        "changelog_md": description,
        "changelog_bbcode": description,
    }
    if args.github_output:
        import os

        out = os.environ.get("GITHUB_OUTPUT")
        if not out:
            print("GITHUB_OUTPUT is not set", file=sys.stderr)
            return 1
        write_github_output(Path(out), values)
    else:
        for key, value in values.items():
            print(f"{key}={value}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
