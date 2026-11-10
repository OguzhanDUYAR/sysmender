"""Headless entry point. JSON contains stable IDs and normalized result states."""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

from . import AUTHOR, __version__
from .catalog import CANCELLED, FAILED, OK, REVIEW, UNAVAILABLE, Context, Result, TOOLS, endpoint, run
from .host import Host
from .metrics import Sampler
from .reports import encode, report, save_new
from .runner import clean
from .workflows import REPAIR, SCENARIOS, diagnose, repair

STATUS = {"ok": ("Tamamlandı", "Completed"), "review": ("İncele", "Review"), "failed": ("Başarısız", "Failed"),
          "unavailable": ("Kullanılamıyor", "Unavailable"), "cancelled": ("İptal edildi", "Cancelled")}


def status_label(ctx, status):
    return ctx.text(*STATUS[status])


def exit_code(results):
    states = {item.status for item in results}
    if CANCELLED in states:
        return 130
    if FAILED in states:
        return 2
    if states == {UNAVAILABLE}:
        return 3
    if REVIEW in states or UNAVAILABLE in states:
        return 1
    return 0


def parser():
    root = argparse.ArgumentParser(prog="linuxbakim", description="Linux Bakım Merkezi / Linux Maintenance Center")
    root.add_argument("--version", action="version", version=f"Linux Bakım Merkezi {__version__} · {AUTHOR}")
    root.add_argument("--lang", choices=("tr", "en"), default="tr")
    root.add_argument("--json", action="store_true", help="JSON output (before the subcommand)")
    sub = root.add_subparsers(dest="command")
    sub.add_parser("list", help="List tool IDs")
    sub.add_parser("gui", help="Open the graphical console")
    for command in ("info", "health", "network", "storage", "services"):
        sub.add_parser(command)
    run_one = sub.add_parser("run", help="Run one read-only tool")
    run_one.add_argument("tool", choices=tuple(TOOLS))
    batch = sub.add_parser("diagnose", help="Run a diagnostic workflow")
    batch.add_argument("--scenario", choices=tuple(SCENARIOS), default="health")
    update = sub.add_parser("updates", help="Query existing package metadata")
    update.add_argument("--check", action="store_true")
    security = sub.add_parser("security", help="Read local security indicators")
    security.add_argument("--audit", action="store_true")
    target = sub.add_parser("target", help="Resolve one host and attempt one TCP port")
    target.add_argument("host")
    target.add_argument("--port", type=int, required=True)
    export = sub.add_parser("report", help="Run a workflow and export a private local JSON file")
    export.add_argument("--format", choices=("json",), default="json")
    export.add_argument("--scenario", choices=tuple(SCENARIOS), default="health")
    export.add_argument("--output", type=Path, required=True)
    export.add_argument("--include-details", action="store_true", help="Include private machine information and command output")
    edit = sub.add_parser("repair", help="Preview or apply an eligible repair")
    edit.add_argument("action", choices=(REPAIR,))
    edit.add_argument("--apply", action="store_true", help="Ask for interactive approval, then use sudo and native APT")
    monitor = sub.add_parser("monitor", help="Sample Linux counters")
    monitor.add_argument("--samples", type=int, default=5, choices=range(1, 61), metavar="1-60")
    return root


def main(argv=None):
    arg_parser = parser()
    args = arg_parser.parse_args(argv)
    if args.command is None:
        arg_parser.print_help()
        return 0
    ctx = Context(Host.detect(), args.lang)
    action = args.command
    if action == "gui":
        try:
            from .gui import open_gui
        except ImportError:
            print(ctx.text("Masaüstü arayüzü için Python Tk gerekir.", "Python Tk is required for the desktop interface."), file=sys.stderr)
            return 3
        return open_gui(ctx)
    if action == "list":
        rows = [{"id": tool.id, "group": tool.group, "name": tool.name(ctx), "risk": tool.risk} for tool in TOOLS.values()]
        if args.json:
            print(encode({"schema": 1, "tools": rows}), end="")
        else:
            for row in rows:
                print(f"{row['id']:<18} {row['name']}")
        return 0
    try:
        if action == "monitor" and ctx.host.linux:
            sample = Sampler()
            collected = []
            for index in range(args.samples):
                if index:
                    time.sleep(1.0)
                values = sample.sample()
                if args.json:
                    collected.append(values)
                else:
                    print(json.dumps(values, ensure_ascii=False), flush=True)
            if args.json:
                print(encode({"schema": 1, "samples": collected}), end="")
            return 0
        if action == "repair":
            if args.apply and args.json:
                arg_parser.error("--apply requires interactive text output")
            results = [repair(ctx, apply=args.apply)]
        elif action == "target":
            results = [endpoint(ctx, args.host, args.port)]
        elif action == "run" or action in {"info", "updates", "monitor"}:
            tool = args.tool if action == "run" else "resources" if action == "monitor" else action
            results = [run(tool, ctx)]
        else:
            scenario = args.scenario if action in ("diagnose", "report") else action
            results = list(diagnose(scenario, ctx))
        if action == "report":
            save_new(args.output, report(results, args.include_details))
            print(str(args.output))
        elif args.json:
            print(encode(report(results, True)), end="")
        else:
            for result in results:
                print(f"[{status_label(ctx, result.status)}] {result.tool}\n{clean(result.detail)}")
                if result.truncated:
                    print(ctx.text("Çıktı boyut sınırına ulaştı.", "Output reached its size limit."))
                print()
        return exit_code(results)
    except (ValueError, OSError) as error:
        print(clean(str(error)), file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        return 130
