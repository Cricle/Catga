#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent.parent
MARKDOWN_PATHS = [
    REPO_ROOT / "README.md",
    REPO_ROOT / "docs",
    REPO_ROOT / "examples" / "README.md",
]
TOC_PATHS = [
    REPO_ROOT / "toc.yml",
    REPO_ROOT / "docs" / "toc.yml",
]

MARKDOWN_LINK_RE = re.compile(r"\]\(([^)]+)\)")
TOC_HREF_RE = re.compile(r"^\s*href:\s*(.+?)\s*$")
IGNORED_PREFIXES = ("http://", "https://", "mailto:", "#")
IGNORED_GENERATED_TOC_TARGETS = ("api/",)


def should_skip(link: str) -> bool:
    return not link or link.startswith(IGNORED_PREFIXES)


def normalize_target(raw_link: str) -> tuple[str, str | None]:
    target, _, anchor = raw_link.partition("#")
    return target.strip(), anchor or None


def validate_markdown_file(path: Path) -> list[str]:
    errors: list[str] = []
    content = path.read_text(encoding="utf-8")

    for match in MARKDOWN_LINK_RE.finditer(content):
        raw_link = match.group(1).strip()
        if should_skip(raw_link):
            continue

        target, _anchor = normalize_target(raw_link)
        if not target:
            continue

        resolved = (path.parent / target).resolve()
        if not resolved.exists():
            errors.append(f"{path.relative_to(REPO_ROOT)} -> {raw_link} -> {resolved}")

    return errors


def validate_toc_file(path: Path) -> list[str]:
    errors: list[str] = []

    for line in path.read_text(encoding="utf-8").splitlines():
        match = TOC_HREF_RE.match(line)
        if not match:
            continue

        raw_href = match.group(1).strip().strip("'\"")
        if should_skip(raw_href):
            continue
        if raw_href in IGNORED_GENERATED_TOC_TARGETS:
            continue

        target, _anchor = normalize_target(raw_href)
        if not target:
            continue

        resolved = (path.parent / target).resolve()
        if not resolved.exists():
            errors.append(f"{path.relative_to(REPO_ROOT)} -> {raw_href} -> {resolved}")

    return errors


def iter_markdown_files() -> list[Path]:
    files: list[Path] = []

    for path in MARKDOWN_PATHS:
        if path.is_dir():
            files.extend(sorted(path.rglob("*.md")))
        elif path.is_file():
            files.append(path)

    return files


def main() -> int:
    errors: list[str] = []

    for markdown_file in iter_markdown_files():
        errors.extend(validate_markdown_file(markdown_file))

    for toc_file in TOC_PATHS:
        errors.extend(validate_toc_file(toc_file))

    if errors:
        print("Broken documentation links found:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("all markdown and toc links exist")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
