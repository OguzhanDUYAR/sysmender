import hashlib
import importlib.util
import io
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import build_release

SPEC = importlib.util.spec_from_file_location("linuxbakim_setup", build_release.BASE / "packaging/setup.py")
setup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(setup)


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.paths = setup.layout(self.home)
        self.python = Path(sys.executable)
        self.bundle = {"linuxbakim.pyz": b"application content", f"{setup.APP_ID}.svg": b"<svg/>"}

    def desired(self, desktop=True):
        return setup.desired(self.paths, self.python, self.bundle, desktop)

    def test_desktop_install_repeat_and_uninstall(self):
        entries = self.desired()
        self.assertTrue(setup.apply_files(self.paths, entries))
        for key in entries:
            self.assertEqual(self.paths[key].read_bytes(), entries[key][0])
        changed = setup.apply_files(self.paths, entries)
        if os.name == "posix":
            self.assertFalse(changed)
        for key in entries:
            self.assertEqual(self.paths[key].read_bytes(), entries[key][0])
        self.assertTrue(setup.apply_files(self.paths, {}))
        self.assertFalse(any(path.exists() for path in self.paths.values()))

    def test_upgrade_and_switch_to_cli(self):
        setup.apply_files(self.paths, self.desired())
        self.bundle["linuxbakim.pyz"] = b"updated application"
        self.assertTrue(setup.apply_files(self.paths, self.desired(desktop=False)))
        self.assertEqual(self.paths["application"].read_bytes(), b"updated application")
        self.assertFalse(self.paths["desktop"].exists())
        self.assertFalse(self.paths["icon"].exists())

    def test_conflicting_launcher_does_not_modify_application(self):
        setup.replace_file(self.paths["launcher"], (b"outside", 0o755))
        with self.assertRaises(FileExistsError):
            setup.apply_files(self.paths, self.desired())
        self.assertEqual(self.paths["launcher"].read_bytes(), b"outside")
        self.assertFalse(self.paths["application"].exists())

    def test_modified_launcher_protected_on_remove(self):
        setup.apply_files(self.paths, self.desired())
        self.paths["launcher"].write_bytes(b"edited")
        with self.assertRaises(ValueError):
            setup.apply_files(self.paths, {})
        self.assertEqual(self.paths["launcher"].read_bytes(), b"edited")
        self.assertTrue(self.paths["application"].exists())

    def test_write_failure_restores_previous_application(self):
        original = self.desired()
        setup.apply_files(self.paths, original)
        self.bundle["linuxbakim.pyz"] = b"new payload"
        updated = self.desired()
        replace = setup.replace_file

        def fail_state_once(path, entry):
            if path == self.paths["state"] and entry == updated["state"]:
                raise OSError("simulated write failure")
            return replace(path, entry)

        with patch.object(setup, "replace_file", side_effect=fail_state_once):
            with self.assertRaisesRegex(OSError, "simulated write failure"):
                setup.apply_files(self.paths, updated)
        for key, entry in original.items():
            self.assertEqual(self.paths[key].read_bytes(), entry[0])

    def test_interrupt_after_replace_restores_previous_application(self):
        setup.apply_files(self.paths, self.desired())
        previous = self.paths["application"].read_bytes()
        self.bundle["linuxbakim.pyz"] = b"new payload"
        replace = setup.replace_file

        def interrupt_after_write(path, entry):
            replace(path, entry)
            if path == self.paths["application"] and entry[0] == b"new payload":
                raise KeyboardInterrupt

        with patch.object(setup, "replace_file", side_effect=interrupt_after_write):
            with self.assertRaises(KeyboardInterrupt):
                setup.apply_files(self.paths, self.desired())
        self.assertEqual(self.paths["application"].read_bytes(), previous)

    def test_invalid_manifest_cannot_claim_unrelated_file(self):
        setup.replace_file(self.paths["state"], (json.dumps({"schema": 1, "files": {"unrelated": "0" * 64}}).encode(), 0o600))
        with self.assertRaisesRegex(ValueError, "dosya listesi"):
            setup.apply_files(self.paths, {})

    def test_path_escaping_for_desktop_and_shell(self):
        home = self.home / 'name with spaces $and %'
        paths = setup.layout(home)
        entries = setup.desired(paths, self.python, self.bundle, True)
        self.assertIn(b'name with spaces \\\\$and %%', entries["desktop"][0])
        self.assertIn(b'name with spaces $and %', entries["launcher"][0])
        self.assertIn(b'"$@"', entries["launcher"][0])

    def test_custom_data_path_and_launcher_separate(self):
        data = self.home / "xdg"
        paths = setup.layout(self.home, data)
        setup.apply_files(paths, setup.desired(paths, self.python, self.bundle, True))
        self.assertEqual(paths["application"].parent, data / "linuxbakim")
        self.assertTrue((self.home / ".local/bin/linuxbakim").is_file())

    @unittest.skipUnless(sys.platform.startswith("linux"), "Linux flock")
    def test_install_lock_rejects_concurrent_writer(self):
        base = self.paths["state"].parent
        with setup.install_lock(base):
            with self.assertRaisesRegex(RuntimeError, "kurulumu çalışıyor"):
                with setup.install_lock(base):
                    self.fail("second writer acquired the installation lock")
        with setup.install_lock(base):
            self.assertTrue((base / ".install.lock").is_file())


class InstallerArchiveTests(unittest.TestCase):
    def test_archive_reproducible_and_valid(self):
        app = build_release.portable()
        blob = build_release.universal(app)
        self.assertEqual(blob, build_release.universal(app))
        with tarfile.open(fileobj=io.BytesIO(blob)) as archive:
            files = {Path(entry.name).name: entry for entry in archive if entry.isfile()}
            self.assertEqual(set(files), {"linuxbakim.pyz", "install.sh", "setup.py",
                                         "bundle.sha256", "KURULUM.md", f"{setup.APP_ID}.svg"})
            self.assertEqual(files["install.sh"].mode, 0o755)
            self.assertNotIn(b"\r", archive.extractfile(files["install.sh"]).read())
            with tempfile.TemporaryDirectory() as folder:
                target = Path(folder)
                for name in setup.BUNDLE_NAMES | {"bundle.sha256"}:
                    (target / name).write_bytes(archive.extractfile(files[name]).read())
                self.assertEqual(setup.read_bundle(target)["linuxbakim.pyz"], app)
                completed = subprocess.run((sys.executable, "-I", str(target / "linuxbakim.pyz"), "--version"),
                                           capture_output=True, check=True, timeout=10)
                self.assertIn(b"Oguzhan DUYAR", completed.stdout)
                (target / "linuxbakim.pyz").write_bytes(b"modified")
                with self.assertRaisesRegex(ValueError, "doğrulanamadı"):
                    setup.read_bundle(target)

    def test_bundle_manifest_rejects_unexpected_path(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder)
            (target / "bundle.sha256").write_text(f"{hashlib.sha256(b'data').hexdigest()}  ../setup.py\n", encoding="ascii")
            with self.assertRaisesRegex(ValueError, "geçersiz"):
                setup.read_bundle(target)


if __name__ == "__main__":
    unittest.main()
