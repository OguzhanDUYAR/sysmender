"""Build an architecture-neutral portable zipapp, Debian package, and source ZIP."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import tarfile
import zipfile
from pathlib import Path, PurePosixPath

from linuxbakim import __version__

BASE = Path(__file__).resolve().parent
APP = "org.oguzhanduyar.LinuxBakim"
STAMP = (2026, 1, 1, 0, 0, 0)
PORTABLE = f"LinuxBakimMerkezi-v{__version__}.pyz"
SOURCE = "LinuxBakimMerkezi-Source.zip"
DEB = f"linuxbakim_{__version__}-1_all.deb"
ARCH_RECIPE = "LinuxBakimMerkezi-PKGBUILD"
LAUNCHER = b"#!/usr/bin/env python3\n"


def zip_contents(entries, prefix=b""):
    output = io.BytesIO(prefix)
    output.seek(len(prefix))
    with zipfile.ZipFile(output, "w") as archive:
        for name, data in sorted(entries.items()):
            item = zipfile.ZipInfo(name, STAMP)
            item.external_attr = 0o100644 << 16
            archive.writestr(item, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    return output.getvalue()


def portable():
    files = {path.relative_to(BASE).as_posix(): path.read_bytes() for path in (BASE / "linuxbakim").glob("*.py")}
    files["__main__.py"] = b"from linuxbakim.cli import main\n\nraise SystemExit(main())\n"
    return zip_contents(files, LAUNCHER)


def tar_bytes(entries):
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        directories = {parent.as_posix() for name in entries for parent in PurePosixPath(name).parents}
        for directory in sorted(directories):
            info = tarfile.TarInfo("./" if directory == "." else "./" + directory)
            info.type, info.mode = tarfile.DIRTYPE, 0o755
            info.uname = info.gname = "root"
            archive.addfile(info)
        for name, (data, mode) in sorted(entries.items()):
            info = tarfile.TarInfo("./" + name)
            info.size = len(data)
            info.mode = mode
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            archive.addfile(info, io.BytesIO(data))
    return gzip.compress(output.getvalue(), compresslevel=9, mtime=0)


def ar_bytes(members):
    data = bytearray(b"!<arch>\n")
    for name, payload in members:
        if len(name) > 15:
            raise ValueError("Member name too long")
        data.extend(f"{name:<16}{0:<12}{0:<6}{0:<6}{100644:<8}{len(payload):<10}".encode("ascii"))
        data.extend(bytes((96, 10)))
        data.extend(payload)
        if len(payload) % 2:
            data.extend(b"\n")
    return bytes(data)


def debian(application):
    data = {
        "usr/bin/linuxbakim": (application, 0o755),
        f"usr/share/applications/{APP}.desktop": ((BASE / "packaging" / f"{APP}.desktop").read_bytes(), 0o644),
        f"usr/share/icons/hicolor/scalable/apps/{APP}.svg": ((BASE / "packaging" / f"{APP}.svg").read_bytes(), 0o644),
        "usr/share/doc/linuxbakim/copyright": ((BASE / "packaging" / "copyright").read_bytes(), 0o644),
        "usr/share/doc/linuxbakim/README.md": ((BASE / "README.md").read_bytes(), 0o644),
        "usr/share/doc/linuxbakim/changelog.Debian.gz": (
            gzip.compress(f"linuxbakim ({__version__}-1) unstable; urgency=medium\n\n  * Initial engineering preview.\n\n"
                          " -- Linux Bakim Merkezi Project <maintainer@linuxbakim.invalid>  Thu, 01 Jan 2026 00:00:00 +0000\n".encode(), mtime=0), 0o644),
    }
    control = f"""Package: linuxbakim
Version: {__version__}-1
Architecture: all
Maintainer: Oguzhan DUYAR <maintainer@linuxbakim.invalid>
Section: admin
Priority: optional
Installed-Size: {(sum(len(content) for content, _ in data.values()) + 1023) // 1024}
Depends: python3 (>= 3.9)
Suggests: python3-tk, iproute2, util-linux, smartmontools, pciutils, usbutils
Description: Linux system diagnostics and maintenance console
 Local desktop and headless tools for hardware, services, storage and package
 inspection. The graphical interface requires Python Tk and a display session.
"""
    checksums = "".join(f"{hashlib.md5(content, usedforsecurity=False).hexdigest()}  {name}\n" for name, (content, _) in sorted(data.items()))
    controls = {"control": (control.encode(), 0o644), "md5sums": (checksums.encode(), 0o644)}
    return ar_bytes((("debian-binary", b"2.0\n"), ("control.tar.gz", tar_bytes(controls)), ("data.tar.gz", tar_bytes(data))))


def source():
    files = [*BASE.glob("*.md"), *BASE.glob("*.py"), *BASE.glob("*.json"),
             *(BASE / "linuxbakim").glob("*.py"), *(BASE / "tests").glob("*.py"),
             *(BASE / "packaging").glob("*"), *(BASE / ".github/workflows").glob("*.yml")]
    return zip_contents({f"LinuxBakimMerkezi/{path.relative_to(BASE).as_posix()}": path.read_bytes()
                         for path in files if path.is_file()})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--portable-only", action="store_true")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    app = portable()
    artifacts = {PORTABLE: app}
    if not args.portable_only:
        source_zip = source()
        recipe = (BASE / "packaging/PKGBUILD.in").read_bytes()
        artifacts.update({SOURCE: source_zip, DEB: debian(app), ARCH_RECIPE:
                          recipe.replace(b"@SOURCE_SHA256@", hashlib.sha256(source_zip).hexdigest().encode("ascii"))})
    for name, data in artifacts.items():
        target = args.out / name
        target.write_bytes(data)
        if name.endswith(".pyz"):
            target.chmod(0o755)
        print(f"{hashlib.sha256(data).hexdigest()}  {name}")
    if not args.portable_only:
        checksums = "".join(f"{hashlib.sha256(data).hexdigest()}  {name}\n" for name, data in artifacts.items())
        (args.out / "LinuxBakimMerkezi-SHA256SUMS.txt").write_bytes(checksums.encode("ascii"))


if __name__ == "__main__":
    main()
