"""Exercise the released shell installer on Linux with an isolated HOME."""
import os
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path


def main():
    if not sys.platform.startswith("linux") or os.geteuid() == 0:
        raise SystemExit("Use an unprivileged account on Linux.")
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python3 -m tests.installer_smoke <universal installer archive>")
    with tempfile.TemporaryDirectory() as folder:
        work = Path(folder)
        home = work / "home with spaces"
        home.mkdir()
        env = {**os.environ, "HOME": str(home), "XDG_DATA_HOME": str(home / "data")}
        with tarfile.open(sys.argv[1]) as archive:
            members = archive.getmembers()
            for entry in members:
                target = (work / entry.name).resolve()
                if not target.is_relative_to(work) or not (entry.isfile() or entry.isdir()):
                    raise ValueError(f"Unexpected archive path: {entry.name}")
            archive.extractall(work, members=members)
        script = next(work.glob("*/install.sh"))
        launcher = home / ".local/bin/linuxbakim"

        def setup(*args, check=True):
            return subprocess.run(("sh", str(script), *args, "--python", sys.executable),
                                  env=env, check=check, capture_output=True, text=True, timeout=30)

        assert setup("--check", "--cli").returncode == 0 and not launcher.exists()
        setup("--cli")
        setup("--cli")
        version = subprocess.check_output((str(launcher), "--version"), env=env, text=True, timeout=15)
        assert "Oguzhan DUYAR" in version
        assert not (home / "data/applications").exists()
        setup("--uninstall")
        assert not launcher.exists() and not (home / "data/linuxbakim/linuxbakim.pyz").exists()
        setup("--uninstall")
        tk = subprocess.run((sys.executable, "-I", "-c", "import tkinter; tkinter.Tcl()"), capture_output=True)
        if tk.returncode == 0:
            setup()
            assert (home / "data/applications/org.oguzhanduyar.LinuxBakim.desktop").is_file()
            setup("--uninstall")
            assert not launcher.exists()
        print("PASS Linux installer check, installation, launcher, reinstallation, uninstall")


if __name__ == "__main__":
    main()
