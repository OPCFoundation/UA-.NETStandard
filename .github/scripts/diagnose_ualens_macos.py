# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Collect owned macOS crash evidence and reproduce the native probe under LLDB."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time


def owned_ips(path, executable):
    header, body = path.read_text(encoding="utf-8").split("\n", 1)
    json.loads(header)
    crash = json.loads(body)
    process_path = crash.get("procPath")
    return isinstance(process_path, str) and Path(process_path).resolve() == executable.resolve()


def collect_reports(executable, destination, cutoff):
    reports = Path.home() / "Library" / "Logs" / "DiagnosticReports"
    deadline = time.monotonic() + 15
    errors = []
    while time.monotonic() < deadline:
        if reports.is_dir():
            for path in reports.glob("*.ips"):
                if not path.name.casefold().startswith(executable.name.casefold()):
                    continue
                try:
                    if path.stat().st_mtime < cutoff:
                        continue
                    if path.stat().st_size > 16 * 1024 * 1024:
                        raise ValueError("Owned crash report exceeded the diagnostic size bound.")
                    if owned_ips(path, executable):
                        target = destination / path.name
                        shutil.copyfile(path, target)
                        return [target.name], errors
                except (OSError, ValueError, json.JSONDecodeError) as error:
                    errors.append(f"{path.name}: {error}")
        time.sleep(1)
    return [], errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--result", required=True, type=Path)
    parser.add_argument("--timeout-seconds", type=int, default=120)
    args = parser.parse_args()
    if sys.platform != "darwin":
        parser.error("This diagnostic requires macOS and the installed Xcode LLDB.")
    if not 1 <= args.timeout_seconds <= 180:
        parser.error("--timeout-seconds must be between 1 and 180")
    result_path = args.result.resolve(strict=True)
    result = json.loads(result_path.read_text(encoding="utf-8"))
    if result.get("passed") or not result.get("nativeViewport"):
        parser.error("Expected a failed native viewport result.")
    executable = Path(result["executable"]).resolve(strict=True)
    debugger = shutil.which("lldb")
    if debugger is None:
        parser.error("LLDB is unavailable; no debugger or system tooling will be installed.")
    output = result_path.parent / "macos-diagnostics"
    output.mkdir(exist_ok=True)
    reports, errors = collect_reports(executable, output, result_path.stat().st_mtime - 120)
    sys.dont_write_bytecode = True
    runner_path = Path(__file__).resolve().parents[2] / "tools" / "Opc.Ua.Lens" / "validate-artifact.py"
    spec = importlib.util.spec_from_file_location("ualens_artifact", runner_path)
    runner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runner)
    timed_out = False
    with tempfile.TemporaryDirectory(prefix="ualens-macos-debug-") as directory:
        environment = runner.artifact_environment(Path(directory))
        environment["NSZombieEnabled"] = "YES"
        command = [
            debugger, "--batch",
            "-o", "settings set target.disable-aslr false",
            "-k", "thread backtrace",
            "-k", "image list",
            "-o", "run",
            "--", str(executable), "--smoke-test", "--smoke-native-viewport",
            "--smoke-artifact-directory", str(output / "replay-images"),
        ]
        with subprocess.Popen(
            command, cwd=directory, env=environment,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=True,
        ) as process:
            try:
                transcript, _ = process.communicate(timeout=args.timeout_seconds)
            except subprocess.TimeoutExpired:
                timed_out = True
                # This process group was created solely for the owned debugger and its child.
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                try:
                    transcript, _ = process.communicate(timeout=5)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    transcript, _ = process.communicate()
            debugger_exit_code = process.returncode
    (output / "lldb.txt").write_bytes(transcript)
    print("Owned native-crash debugger transcript:")
    print(transcript.decode("utf-8", errors="replace"))
    captured_backtrace = b"frame #0:" in transcript
    summary = {
        "kind": "diagnostic-only",
        "originalExitCode": result.get("exitCode"),
        "executable": str(executable),
        "ownedCrashReports": reports,
        "reportCollectionErrors": errors,
        "debuggerExitCode": debugger_exit_code,
        "debuggerTimedOut": timed_out,
        "capturedBacktrace": captured_backtrace,
        "objcZombieDiagnostics": True,
    }
    (output / "diagnostic.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary))
    return 0 if not timed_out and (reports or captured_backtrace) else 1


if __name__ == "__main__":
    sys.exit(main())
