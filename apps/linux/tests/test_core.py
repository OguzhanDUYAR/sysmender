import io
import json
import os
import socket
import sys
import tempfile
import threading
import time
import unittest
from contextlib import redirect_stdout
from dataclasses import replace
from pathlib import Path
from unittest.mock import Mock, patch

from linuxbakim.catalog import (
    CANCELLED, FAILED, OK, REVIEW, UNAVAILABLE, Context, Result, TOOLS, endpoint, run, validate_target,
)
from linuxbakim.cli import exit_code, main
from linuxbakim.host import Host, distro_family, parse_os_release
from linuxbakim.metrics import Sampler, cpu_counters, cpu_percent, disk_counters, memory_values, net_counters, rates
from linuxbakim.reports import report, save_new
from linuxbakim.runner import CommandResult, clean, execute
from linuxbakim.workflows import native_apply, plan_from_output, repair


def fake_host(**settings):
    host = replace(Host.detect(), linux=True, distro_id="debian", family="debian", package="apt",
                   init="systemd", container=False, wsl=False, ssh=False, root=False, immutable=False)
    return replace(host, **settings)


PLAN = """Reading package lists...
0 upgraded, 1 newly installed, 0 to remove and 3 not upgraded.
Inst libfoo:amd64 (2.0 Test [amd64])
Conf libfoo:amd64 (2.0 Test [amd64])
"""


class DetectionTests(unittest.TestCase):
    def test_os_release_is_data(self):
        text = 'ID=cachyos\nID_LIKE="arch linux"\nPRETTY_NAME="CachyOS Linux"\nOTHER=$(touch /tmp/file)\n'
        data = parse_os_release(text)
        self.assertEqual(data, {"ID": "cachyos", "ID_LIKE": "arch linux", "PRETTY_NAME": "CachyOS Linux"})
        self.assertEqual(distro_family(data["ID"], tuple(data["ID_LIKE"].split())), "arch")
        self.assertEqual(distro_family("fictional", ("debian",)), "debian")

    def test_invalid_targets(self):
        for host in ("", "--help", "localhost; ls", "fe80::1%lo", "a..b", "http://a"):
            with self.subTest(host=host), self.assertRaises(ValueError):
                validate_target(host, 443)
        self.assertEqual(validate_target(" 2001:0db8::1 ", 443), "2001:db8::1")
        self.assertEqual(validate_target("example.invalid.", 53), "example.invalid.")
        with self.assertRaises(ValueError):
            validate_target("localhost", 65536)

    @unittest.skipUnless(sys.platform.startswith("linux"), "Linux loopback smoke")
    def test_endpoint_can_connect_to_loopback(self):
        with socket.socket() as server:
            server.bind(("127.0.0.1", 0))
            server.listen(1)
            result = endpoint(Context(fake_host()), "127.0.0.1", server.getsockname()[1])
        self.assertEqual(result.status, OK, result.detail)

    def test_endpoint_attempts_only_the_requested_address_and_port(self):
        with patch("linuxbakim.catalog.socket.socket") as create:
            result = endpoint(Context(fake_host()), "127.0.0.1", 5555)
        self.assertEqual(result.status, OK)
        create.return_value.__enter__.return_value.connect.assert_called_once_with(("127.0.0.1", 5555))

    def test_windows_gate_precedes_subprocess(self):
        call = Mock(side_effect=AssertionError("Unexpected process"))
        result = run("space", Context(fake_host(linux=False), "en", execute=call))
        self.assertEqual(result.status, UNAVAILABLE)
        call.assert_not_called()


class MetricTests(unittest.TestCase):
    def test_cpu_excludes_guest_and_includes_iowait(self):
        old = cpu_counters("cpu  100 0 20 200 10 0 0 0 9 0\ncpu0 1 2 3 4")
        new = cpu_counters("cpu  120 0 30 260 20 0 0 0 19 0")
        self.assertEqual(cpu_percent(old, new), 30.0)
        self.assertIsNone(cpu_percent(new, old))
        self.assertIsNone(cpu_counters("cpu 1 2 garbage 4"))
        self.assertIsNone(cpu_percent(new, new))

    def test_memory_parsing_and_malformed_values(self):
        values = memory_values("MemTotal: 100 kB\nMemAvailable: 40 kB\nSwapTotal: 0 kB\nSwapFree: 0 kB")
        self.assertEqual(values["memory_used"], 60 * 1024)
        self.assertEqual(values["swap_used"], 0)
        self.assertIsNone(memory_values("MemTotal: 10 kB\nMemAvailable: 15 kB")["memory_used"])

    def test_rates_reject_reset_or_interface_change(self):
        first = {"eth0": (100, 200)}
        self.assertEqual(rates(first, {"eth0": (160, 210)}, 2), (30, 5))
        self.assertIsNone(rates(first, {"eth0": (90, 210)}, 2))
        self.assertIsNone(rates(first, {"eth0": (160, 210), "eth1": (0, 0)}, 2))
        self.assertIsNone(rates(first, {"eth0": (160, 210)}, 21))

    def test_network_and_block_counters(self):
        stats = "lo: 1 0 0 0 0 0 0 0 1 0 0 0 0 0 0 0\neth0: 3 0 0 0 0 0 0 0 9 0 0 0 0 0 0 0\n"
        self.assertEqual(net_counters(stats), {"eth0": (3, 9)})
        disks = "8 0 sda 1 0 4 0 1 0 9 0 0 0 0\n8 1 sda1 1 0 4 0 1 0 9 0 0 0 0"
        self.assertEqual(disk_counters(disks, {"sda"}), {"sda": (2048, 4608)})

    def test_sampler_no_fake_values(self):
        with tempfile.TemporaryDirectory() as name:
            values = Sampler(Path(name), Path(name)).sample()
        self.assertIsNone(values["cpu_percent"])
        self.assertIsNone(values["memory_total"])
        self.assertIsNone(values["disk_bytes_per_second"])


class RunnerTests(unittest.TestCase):
    def test_real_subprocess_bounded_output(self):
        answer = execute((sys.executable, "-c", "print('x'*300000)"), cap=1024)
        self.assertEqual(answer.code, 0)
        self.assertTrue(answer.truncated)
        self.assertEqual(len(answer.output), 1024)

    def test_real_timeout(self):
        start = time.monotonic()
        answer = execute((sys.executable, "-c", "import time; time.sleep(20)"), timeout=0.25)
        self.assertEqual(answer.reason, "timeout")
        self.assertLess(time.monotonic() - start, 4)

    def test_real_cancel(self):
        stop = threading.Event()
        timer = threading.Timer(0.15, stop.set)
        timer.start()
        try:
            self.assertEqual(execute((sys.executable, "-c", "import time; time.sleep(20)"), stop=stop).reason, "cancelled")
        finally:
            timer.cancel()
            timer.join()

    def test_sanitizer(self):
        self.assertEqual(clean("\x1b[31mfoo\r\nbar\x00\u202e"), "foo\nbar")


class CatalogTests(unittest.TestCase):
    def test_unknown_and_missing_binary(self):
        ctx = Context(fake_host(), "en", find=lambda _: None)
        self.assertEqual(run("space", ctx).status, UNAVAILABLE)
        self.assertEqual(run("missing", ctx).status, UNAVAILABLE)

    def test_dnf_code100_is_valid_update_data(self):
        execute = Mock(return_value=CommandResult(100, "Updates found"))
        result = run("updates", Context(fake_host(package="dnf"), find=lambda name: "/usr/bin/" + name, execute=execute))
        self.assertEqual(result.status, OK)
        self.assertEqual(execute.call_args.args[0], ("/usr/bin/dnf", "-C", "check-update"))

    def test_apt_audit_with_findings_is_review(self):
        execute = Mock(return_value=CommandResult(0, "The following packages are only half configured: test"))
        result = run("package-audit", Context(fake_host(), find=lambda _: "/usr/bin/dpkg", execute=execute))
        self.assertEqual(result.status, REVIEW)

    def test_catalog_and_inactive_systemd(self):
        self.assertGreaterEqual(len(TOOLS), 50)
        invoke = Mock(side_effect=AssertionError("Unexpected systemd command"))
        self.assertEqual(run("failed-services", Context(fake_host(init="runit"), execute=invoke)).status, UNAVAILABLE)


class RepairTests(unittest.TestCase):
    def prepare(self, *outputs, launch_code=0, host=None):
        execute = Mock(side_effect=[value if isinstance(value, CommandResult) else CommandResult(0, value) for value in outputs])
        ctx = Context(host or fake_host(), "en", find=lambda name: "/usr/bin/" + name, execute=execute)
        audit = Mock()
        launch = Mock(return_value=launch_code)
        parameters = {"terminal": True, "prompt": Mock(return_value="REPAIR"), "audit_factory": Mock(return_value=audit),
                      "launch": launch, "trust": lambda path, _: path, "display": Mock()}
        return ctx, audit, launch, parameters

    def test_preview_parser_checks_removals_and_malformed_plan(self):
        self.assertEqual(len(plan_from_output(PLAN).actions), 2)
        for invalid in ("", PLAN.replace("0 to remove", "1 to remove"), PLAN + "Remv libc6 [2.36]\n",
                        PLAN.replace("Inst libfoo", "Inst --bad"), PLAN.replace("1 newly", "2 newly")):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                plan_from_output(invalid)

    def test_preview_never_launches(self):
        ctx, _, launch, kwargs = self.prepare(PLAN)
        self.assertEqual(repair(ctx, **kwargs).status, REVIEW)
        launch.assert_not_called()

    def test_forbidden_environment_and_no_terminal(self):
        for flag in ("container", "wsl", "ssh", "root", "immutable"):
            ctx, _, launch, kwargs = self.prepare(host=fake_host(**{flag: True}))
            self.assertEqual(repair(ctx, apply=True, **kwargs).status, UNAVAILABLE)
            ctx.execute.assert_not_called()
            launch.assert_not_called()
        ctx, _, launch, kwargs = self.prepare(PLAN)
        kwargs["terminal"] = False
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, UNAVAILABLE)
        launch.assert_not_called()

    def test_rejected_confirmation(self):
        ctx, _, launch, kwargs = self.prepare(PLAN)
        kwargs["prompt"].return_value = "no"
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, CANCELLED)
        launch.assert_not_called()

    def test_changed_plan_never_launches(self):
        ctx, _, launch, kwargs = self.prepare(PLAN, PLAN.replace("2.0", "2.1"))
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, REVIEW)
        launch.assert_not_called()

    def test_unwritable_audit_prevents_repair(self):
        ctx, _, launch, kwargs = self.prepare(PLAN, PLAN)
        kwargs["audit_factory"].side_effect = PermissionError("Not private")
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, FAILED)
        launch.assert_not_called()

    def test_success_needs_verification_and_audit(self):
        ctx, audit, launch, kwargs = self.prepare(PLAN, PLAN, "", "Reading package lists...")
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, OK)
        self.assertEqual([call.args[0] for call in audit.write.call_args_list], ["begin", "finish"])
        self.assertTrue(audit.write.call_args.kwargs["verified"])
        audit.close.assert_called_once()
        launch.assert_called_once_with("/usr/bin/sudo", "/usr/bin/apt-get")

    def test_native_zero_with_broken_database_is_not_success(self):
        ctx, audit, _, kwargs = self.prepare(PLAN, PLAN, "broken package remains", "")
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, REVIEW)
        self.assertFalse(audit.write.call_args.kwargs["verified"])

    def test_native_failure_is_not_success(self):
        ctx, _, _, kwargs = self.prepare(PLAN, PLAN, "", "", launch_code=100)
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, FAILED)

    def test_truncated_preview_never_launches(self):
        ctx, _, launch, kwargs = self.prepare(CommandResult(0, PLAN, truncated=True))
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, FAILED)
        launch.assert_not_called()

    def test_cancelled_preview_never_launches(self):
        ctx, _, launch, kwargs = self.prepare(CommandResult(None, "", reason="cancelled"))
        self.assertEqual(repair(ctx, apply=True, **kwargs).status, CANCELLED)
        launch.assert_not_called()

    def test_interrupt_does_not_force_kill_native_apt(self):
        with patch("linuxbakim.workflows.subprocess.Popen") as popen:
            process = popen.return_value
            process.wait.side_effect = [KeyboardInterrupt, 130]
            self.assertEqual(native_apply("/usr/bin/sudo", "/usr/bin/apt-get"), 130)
        process.kill.assert_not_called()
        self.assertIn("--no-remove", popen.call_args.args[0])
        self.assertNotIn("-s", popen.call_args.args[0])


class ReportAndCliTests(unittest.TestCase):
    def test_summary_removes_details_and_files_are_exclusive(self):
        result = Result("info", OK, "host user token content")
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "report.json"
            save_new(path, report([result]))
            with self.assertRaises(FileExistsError):
                save_new(path, report([result], True))
            doc = json.loads(path.read_text(encoding="utf-8"))
        self.assertFalse(doc["details_included"])
        self.assertNotIn("detail", doc["results"][0])

    def test_cli_json(self):
        output = io.StringIO()
        with redirect_stdout(output), patch("linuxbakim.cli.Host.detect", return_value=fake_host(linux=False)):
            self.assertEqual(main(["--lang", "en", "--json", "info"]), 3)
        self.assertEqual(json.loads(output.getvalue())["results"][0]["status"], UNAVAILABLE)

    def test_exit_statuses(self):
        good = Result("info", OK, "")
        self.assertEqual(exit_code([good]), 0)
        self.assertEqual(exit_code([good, Result("space", UNAVAILABLE, "")]), 1)
        self.assertEqual(exit_code([Result("space", FAILED, "")]), 2)
        self.assertEqual(exit_code([Result("space", CANCELLED, "")]), 130)


if __name__ == "__main__":
    unittest.main()
