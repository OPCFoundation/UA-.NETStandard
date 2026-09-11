#!/usr/bin/env python3
"""Launch a shipped UaLens artifact on a real desktop with bounded lifetime.

Uses only the Python standard library. On Linux, run this under xvfb-run or
provide an existing DISPLAY. This does not build or reference the test project.
"""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", required=True, type=Path)
    parser.add_argument("--log-directory", required=True, type=Path)
    parser.add_argument("--timeout-seconds", default=90, type=int)
    args = parser.parse_args()
    executable = args.executable.resolve(strict=True)
    logs = args.log_directory.resolve()
    logs.mkdir(parents=True, exist_ok=True)
    if args.timeout_seconds <= 0:
        parser.error("--timeout-seconds must be positive")
    if sys.platform.startswith("linux") and not os.environ.get("DISPLAY"):
        parser.error("A real X11 display is required; use xvfb-run for CI.")

    with tempfile.TemporaryDirectory(prefix="ualens-artifact-") as directory:
        home = Path(directory)
        environment = os.environ.copy()
        if "XAUTHORITY" not in environment:
            authority = Path.home() / ".Xauthority"
            if authority.is_file():
                environment["XAUTHORITY"] = str(authority)
        # Do not read a developer's saved preferences/workspaces or write their
        # certificate stores. Preserve display authorization and DOTNET_ROOT.
        for variable, child in {
            "HOME": "home",
            "USERPROFILE": "home",
            "APPDATA": "roaming",
            "LOCALAPPDATA": "local",
            "XDG_CONFIG_HOME": "config",
            "XDG_DATA_HOME": "data",
            "XDG_CACHE_HOME": "cache",
            "DOTNET_CLI_HOME": "dotnet",
        }.items():
            path = home / child
            path.mkdir(exist_ok=True)
            environment[variable] = str(path)
        environment["DOTNET_NOLOGO"] = "true"

        try:
            result = subprocess.run(
                [str(executable), "--smoke-test"],
                cwd=directory,
                env=environment,
                capture_output=True,
                timeout=args.timeout_seconds,
                check=False,
            )
            stdout, stderr = result.stdout, result.stderr
            exit_code = result.returncode
            timed_out = False
        except subprocess.TimeoutExpired as error:
            stdout, stderr = error.stdout or b"", error.stderr or b""
            exit_code = None
            timed_out = True
        except OSError as error:
            stdout, stderr = b"", str(error).encode("utf-8")
            exit_code = None
            timed_out = False

    (logs / "stdout.txt").write_bytes(stdout)
    (logs / "stderr.txt").write_bytes(stderr)
    output = stdout.decode("utf-8", errors="replace")
    passed = (
        not timed_out
        and exit_code == 0
        and "UALENS_DESKTOP_SMOKE_PASS" in output.splitlines()
    )
    summary = {
        "executable": str(executable),
        "timeoutSeconds": args.timeout_seconds,
        "timedOut": timed_out,
        "exitCode": exit_code,
        "passed": passed,
    }
    (logs / "result.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(output, end="")
    print(stderr.decode("utf-8", errors="replace"), end="", file=sys.stderr)
    print(json.dumps(summary))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
