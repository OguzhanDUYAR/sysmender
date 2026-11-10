"""Install the verified application in the current user's local directories."""
from __future__ import annotations

import hashlib
import json
import os
import re
import shlex
import stat
import subprocess
import sys
import tempfile
from contextlib import contextmanager
from pathlib import Path

APP_ID = "org.oguzhanduyar.LinuxBakim"
BUNDLE_NAMES = {"linuxbakim.pyz", f"{APP_ID}.svg", "setup.py"}
LIMIT = 4 * 1024 * 1024


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_regular(path, owned=False):
    try:
        attributes = path.lstat()
    except FileNotFoundError:
        return None
    if not stat.S_ISREG(attributes.st_mode) or attributes.st_size > LIMIT:
        raise ValueError(f"Normal bir dosya bekleniyor: {path}")
    if owned and hasattr(os, "geteuid") and attributes.st_uid != os.geteuid():
        raise PermissionError(f"Dosya başka bir kullanıcıya ait: {path}")
    return path.read_bytes(), stat.S_IMODE(attributes.st_mode)


def read_bundle(folder):
    manifest = read_regular(folder / "bundle.sha256")
    if manifest is None:
        raise ValueError("Kurulum bütünlük listesi eksik.")
    hashes = {}
    for line in manifest[0].decode("ascii").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  ([A-Za-z0-9._-]+)", line)
        if not match or match[2] not in BUNDLE_NAMES or match[2] in hashes:
            raise ValueError("Kurulum bütünlük listesi geçersiz.")
        hashes[match[2]] = match[1]
    if hashes.keys() != BUNDLE_NAMES:
        raise ValueError("Kurulum bütünlük listesi eksik.")
    result = {}
    for name, expected in hashes.items():
        entry = read_regular(folder / name)
        if entry is None or digest(entry[0]) != expected:
            raise ValueError(f"Kurulum dosyası doğrulanamadı: {name}")
        result[name] = entry[0]
    return result


def layout(home, data_home=None):
    local = home / ".local"
    share = data_home or local / "share"
    base = share / "linuxbakim"
    return {
        "application": base / "linuxbakim.pyz",
        "launcher": local / "bin/linuxbakim",
        "desktop": share / "applications" / f"{APP_ID}.desktop",
        "icon": share / "icons/hicolor/scalable/apps" / f"{APP_ID}.svg",
        "state": base / "install-state.json",
    }


def require_directory(path):
    if path.parent != path and not path.parent.is_dir():
        require_directory(path.parent)
    if not path.exists() and not path.is_symlink():
        path.mkdir(mode=0o700, exist_ok=True)
    attrs = path.lstat()
    if not stat.S_ISDIR(attrs.st_mode):
        raise PermissionError(f"Güvenli bir kurulum klasörü değil: {path}")
    if hasattr(os, "geteuid") and (attrs.st_uid != os.geteuid() or attrs.st_mode & 0o022):
        raise PermissionError(f"Kurulum klasörünün sahibi veya izinleri uygun değil: {path}")


def check_ancestors(path):
    for parent in path.parents:
        try:
            attrs = parent.lstat()
        except FileNotFoundError:
            continue
        sticky_root = attrs.st_uid == 0 and attrs.st_mode & stat.S_ISVTX
        if not stat.S_ISDIR(attrs.st_mode) or (hasattr(os, "geteuid") and attrs.st_mode & 0o022 and not sticky_root):
            raise PermissionError(f"Kurulum yolu güvenli değil: {parent}")


@contextmanager
def install_lock(base):
    import fcntl

    require_directory(base)
    path = base / ".install.lock"
    descriptor = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    try:
        attrs = os.fstat(descriptor)
        if not stat.S_ISREG(attrs.st_mode) or attrs.st_uid != os.geteuid():
            raise PermissionError(f"Kurulum kilidi güvenli değil: {path}")
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError("Başka bir Linux Bakım Merkezi kurulumu çalışıyor.") from error
        yield
    finally:
        os.close(descriptor)


def desktop_entry(launcher):
    value = str(launcher)
    for char in ('\\', '"', '$', chr(96)):
        value = value.replace(char, "\\" + char)
    value = value.replace("\\", "\\\\").replace("%", "%%")
    return (
        "[Desktop Entry]\nType=Application\nName=Linux Bakım Merkezi\n"
        "Name[en]=Linux Maintenance Center\n"
        "Comment=Yerel sistem tanılama ve bakım araçları\n"
        f'Exec=/bin/sh "{value}" gui\nIcon={APP_ID}\nTerminal=false\nCategories=System;Utility;\n'
    ).encode("utf-8")


def desired(paths, python, bundle, desktop):
    if any(char in str(path) for path in (*paths.values(), python) for char in "\r\n\t"):
        raise ValueError("Kurulum yolları satır sonu veya sekme karakteri içeremez.")
    launcher = (
        f"#!/bin/sh\nexec {shlex.quote(str(python))} -I "
        f'{shlex.quote(str(paths["application"]))} "$@"\n'
    ).encode("utf-8")
    entries = {
        "application": (bundle["linuxbakim.pyz"], 0o644),
        "launcher": (launcher, 0o755),
    }
    if desktop:
        entries["icon"] = (bundle[f"{APP_ID}.svg"], 0o644)
        entries["desktop"] = (desktop_entry(paths["launcher"]), 0o644)
    state = {"schema": 1, "files": {key: digest(item[0]) for key, item in entries.items()}}
    entries["state"] = (json.dumps(state, sort_keys=True, indent=2).encode("utf-8") + b"\n", 0o600)
    return entries


def read_installed(paths, current):
    state = current["state"]
    if state is None:
        return set()
    saved = json.loads(state[0])
    if not isinstance(saved, dict) or saved.get("schema") != 1 or not isinstance(saved.get("files"), dict):
        raise ValueError("Kurulum kaydı geçersiz.")
    files = saved["files"]
    if not {"application", "launcher"} <= files.keys() or not files.keys() <= paths.keys() - {"state"}:
        raise ValueError("Kurulum kaydındaki dosya listesi geçersiz.")
    for key, expected in files.items():
        if not isinstance(expected, str) or not re.fullmatch(r"[0-9a-f]{64}", expected):
            raise ValueError("Kurulum kaydındaki bütünlük bilgisi geçersiz.")
        item = current[key]
        if item is not None and digest(item[0]) != expected:
            raise ValueError(f"Kurulumdan sonra düzenlenen dosya korunuyor: {paths[key]}")
    return set(files)


def replace_file(path, entry):
    if entry is None:
        path.unlink(missing_ok=True)
        return
    data, mode = entry
    require_directory(path.parent)
    descriptor, temp_name = tempfile.mkstemp(prefix=".linuxbakim-", dir=path.parent)
    temp = Path(temp_name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        temp.chmod(mode)
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def apply_files(paths, entries):
    for path in paths.values():
        check_ancestors(path)
    current = {key: read_regular(path, owned=True) for key, path in paths.items()}
    managed = read_installed(paths, current)
    if not entries and not managed:
        return False
    for key in entries.keys() - {"state"}:
        if current[key] is not None and key not in managed:
            raise FileExistsError(f"Başka bir dosya korunuyor: {paths[key]}")
    changes = [(key, entries.get(key)) for key in paths if
               (key in entries or key in managed or key == "state") and current[key] != entries.get(key)]
    written = []
    try:
        for key, entry in changes:
            written.append(key)
            replace_file(paths[key], entry)
    except (OSError, ValueError, KeyboardInterrupt) as error:
        failed = []
        for key in reversed(written):
            try:
                if read_regular(paths[key], owned=True) != current[key]:
                    replace_file(paths[key], current[key])
            except (OSError, ValueError):
                failed.append(str(paths[key]))
        if failed:
            raise RuntimeError(f"Geri alınamayan dosyalar: {', '.join(failed)}") from error
        raise
    return bool(changes)


def main(args):
    if not sys.platform.startswith("linux") or os.geteuid() == 0:
        raise PermissionError("Kurulum, Linux üzerinde normal kullanıcıyla çalışır.")
    if len(args) != 3 or args[0] not in {"install", "uninstall"} or args[1] not in {"desktop", "cli"}:
        raise ValueError("Kurulum için install.sh dosyasını kullanın.")
    mode, profile, interpreter = args
    home = Path.home().resolve(strict=True)
    data = os.environ.get("XDG_DATA_HOME")
    data_home = Path(data) if data and Path(data).is_absolute() else None
    paths = layout(home, data_home)
    base = paths["state"].parent
    check_ancestors(paths["state"])
    if mode == "uninstall":
        if not base.exists():
            print("Kullanıcı kurulumu bulunamadı.")
            return
        with install_lock(base):
            changed = apply_files(paths, {})
        print("Kullanıcı kurulumu kaldırıldı." if changed else "Kullanıcı kurulumu bulunamadı.")
        return
    folder = Path(__file__).resolve().parent
    bundle = read_bundle(folder)
    python = Path(interpreter)
    if not python.is_absolute():
        raise ValueError("Python yolu mutlak olmalı.")
    checked = subprocess.run((str(python), "-I", str(folder / "linuxbakim.pyz"), "--version"),
                             capture_output=True, timeout=15, check=False)
    if checked.returncode != 0 or b"Oguzhan DUYAR" not in checked.stdout:
        raise RuntimeError("Taşınabilir uygulama başlatma kontrolünden geçemedi.")
    entries = desired(paths, python, bundle, profile == "desktop")
    with install_lock(base):
        apply_files(paths, entries)
    print(f"Kurulum tamamlandı. Başlatma komutu: {shlex.quote(str(paths['launcher']))}" +
          (" gui" if profile == "desktop" else " --help"))


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except KeyboardInterrupt:
        raise SystemExit(130)
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        print(f"Kurulum tamamlanamadı. {error}", file=sys.stderr)
        raise SystemExit(1)
