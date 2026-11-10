"""Versioned summaries and private local audit records."""
from __future__ import annotations

import json
import os
import secrets
import stat
from datetime import datetime, timezone
from pathlib import Path

from . import __version__


def timestamp() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def report(results, include_details=False) -> dict:
    rows = [result.as_dict() for result in results]
    if not include_details:
        for row in rows:
            row.pop("detail")
    return {"schema": 1, "version": __version__, "created": timestamp(),
            "details_included": include_details, "results": rows}


def encode(document) -> str:
    return json.dumps(document, indent=2, ensure_ascii=False) + "\n"


def save_new(path: Path, document: dict) -> None:
    data = encode(document).encode("utf-8")
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(str(path), flags, 0o600), "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


class Audit:
    """Keep repair records outside shared or externally configured directories."""
    def __init__(self):
        self.folder = Path.home() / ".local/state/linuxbakim"
        self.folder.mkdir(mode=0o700, parents=True, exist_ok=True)
        self.directory_fd = os.open(str(self.folder), os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        attrs = os.fstat(self.directory_fd)
        if attrs.st_uid != os.geteuid() or stat.S_IMODE(attrs.st_mode) & 0o077:
            self.close()
            raise PermissionError("Repair audit folder must be private (0700) and owned by the current user")
        self.id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + secrets.token_hex(8)

    def write(self, phase, **fields):
        data = encode({"schema": 1, "id": self.id, "time": timestamp(), "phase": phase, **fields}).encode("utf-8")
        name = self.id + "-" + phase + ".json"
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW
        with os.fdopen(os.open(name, flags, 0o600, dir_fd=self.directory_fd), "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.fsync(self.directory_fd)

    def close(self):
        os.close(self.directory_fd)
