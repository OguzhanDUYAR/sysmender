"""Read-only Linux integration smoke. Execute with python -m tests.linux_smoke."""
import socket
import sys
import time

from linuxbakim.catalog import OK, REVIEW, Context, endpoint, run
from linuxbakim.host import Host
from linuxbakim.metrics import Sampler


def check():
    host = Host.detect()
    if not host.linux:
        raise SystemExit("Linux integration smoke requires a Linux kernel")
    ctx = Context(host, "en")
    for identifier in ("info", "resources", "processes", "space", "mounts"):
        if identifier in {"space", "mounts"} and not ctx.find({"space": "df", "mounts": "findmnt"}[identifier]):
            print("SKIP", identifier, "(executable unavailable)")
            continue
        result = run(identifier, ctx)
        assert result.status in {OK, REVIEW}, (identifier, result.status, result.detail)
        print(result.status, identifier)
    if host.package != "unknown":
        result = run("packages", ctx)
        assert result.status in {OK, REVIEW, "unavailable"}, (result.status, result.detail)
        print(result.status, "packages")
    sampler = Sampler()
    sampler.sample()
    time.sleep(0.15)
    second = sampler.sample()
    assert second["memory_total"] > 0 and 0 <= second["cpu_percent"] <= 100, second
    with socket.socket() as server:
        server.bind(("127.0.0.1", 0))
        server.listen(1)
        result = endpoint(ctx, "127.0.0.1", server.getsockname()[1])
    assert result.status == OK, result.detail
    print("PASS", sys.platform, host.pretty_name, host.architecture, host.init, host.package)


if __name__ == "__main__":
    check()
