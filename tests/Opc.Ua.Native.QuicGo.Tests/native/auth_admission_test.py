# Copyright (c) OPC Foundation. Licensed under the MIT License.
"""Run the same auth-admission assertions against sealed ABI1 and candidate ABI2."""
import argparse
import asyncio
import base64
import ctypes as c
import hashlib
import json
from pathlib import Path

from generic_network_test import Abi, Driver, fixture


async def run(args):
    driver = Driver(args.library)
    abi = Abi(size=c.sizeof(Abi))
    assert driver.abi(c.byref(abi)) == 0 and abi.version in (1, 2, 3, 4)
    pki = fixture(args.fixture)
    results = []
    for flag, expected in (("client-auth", -3), ("confirm", -8)):
        process = await asyncio.create_subprocess_exec(
            str(args.peer_server.absolute()), f"-{flag}=false",
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE)
        try:
            encoded = {"root": base64.b64encode(pki["root"]).decode(), "identities": [
                {"cert": base64.b64encode(i["cert"]).decode(), "key": base64.b64encode(i["key"]).decode()}
                for i in pki["identities"]]}
            process.stdin.write(json.dumps(encoded).encode())
            await process.stdin.drain()
            process.stdin.close()
            address = json.loads(await asyncio.wait_for(process.stdout.readline(), 10))
            config, storage = driver.configuration(pki, 0, (1,), address, timeout=500)
            status, operation = driver.create(False, config, storage)
            assert status == 0
            result, _ = await driver.finish(operation, None)
            results.append({
                "case": flag, "expected_status": expected, "actual_status": result.status,
                "exposed_connection": result.connection.value != 0,
                "passed": result.status == expected and result.connection.value == 0,
            })
            print(("PASS " if results[-1]["passed"] else "FAIL ") + flag +
                  f": expected={expected}, actual={result.status}, exposed={result.connection.value != 0}",
                  flush=True)
        finally:
            await driver.cleanup()
            if process.returncode is None:
                try:
                    await asyncio.wait_for(process.wait(), 7)
                except asyncio.TimeoutError:
                    process.kill()
                    await process.wait()
    report = {
        "abi": abi.version, "library_sha256": hashlib.sha256(args.library.read_bytes()).hexdigest(),
        "passed": all(r["passed"] for r in results), "cases": results,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--peer-server", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    raise SystemExit(asyncio.run(run(parser.parse_args())))
