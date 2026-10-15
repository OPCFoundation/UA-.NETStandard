# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Exercise the desktop gate without building or launching the NUnit test host."""

import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest
import uuid
import xml.etree.ElementTree as ET


REPOSITORY = Path(__file__).resolve().parents[2]
RUNNER = REPOSITORY / ".github/scripts/test-lens-desktop.ps1"
ANSI = re.compile(r"\x1b\[[0-9;]*m")


class DesktopResultGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.powershell = shutil.which("pwsh")
        if cls.powershell is None:
            raise RuntimeError("PowerShell 7 (pwsh) is required; guard tests must not silently skip.")
        contract = RUNNER.read_text(encoding="utf-8")
        cls.expected = int(re.search(r"^\$expected = (\d+)$", contract, re.MULTILINE).group(1))
        cls.methods = {
            name: int(count)
            for name, count in re.findall(r"^    (\w+) = (\d+)$", contract, re.MULTILINE)
        }
        if cls.expected <= 0 or sum(cls.methods.values()) != cls.expected:
            raise AssertionError("The desktop contract must define a positive, consistent expected count.")

    def setUp(self):
        self.directory = (
            REPOSITORY / "TestResults/lens-desktop-guard-tests" / uuid.uuid4().hex
        ).resolve()
        if not self.directory.is_relative_to(REPOSITORY):
            raise RuntimeError("Guard artifacts must stay inside the repository.")
        self.directory.mkdir(parents=True)
        self.report = self.directory / "desktop.trx"
        self.dotnet_called = self.directory / "dotnet-called"
        self.environment = dict(os.environ)
        self.environment["TERM"] = "dumb"
        self.environment["NO_COLOR"] = "1"
        self.environment["PATH"] = str(self.directory) + os.pathsep + self.environment.get("PATH", "")
        if os.name == "nt":
            stub = self.directory / "dotnet.cmd"
            stub.write_text(
                f'@echo off\n@echo unexpected>"{self.dotnet_called}"\n@exit /b 97\n',
                encoding="ascii",
            )
        else:
            stub = self.directory / "dotnet"
            stub.write_text(
                f"#!/bin/sh\nprintf unexpected > '{self.dotnet_called}'\nexit 97\n",
                encoding="ascii",
            )
            stub.chmod(0o700)

    def tearDown(self):
        shutil.rmtree(self.directory)

    def create_report(self):
        root = ET.Element(
            "TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
        )
        summary = ET.SubElement(root, "ResultSummary", outcome="Completed")
        ET.SubElement(
            summary,
            "Counters",
            total=str(self.expected),
            executed=str(self.expected),
            passed=str(self.expected),
            failed="0",
            error="0",
            timeout="0",
            aborted="0",
            inconclusive="0",
            notExecuted="0",
        )
        results = ET.SubElement(root, "Results")
        for method, count in self.methods.items():
            for index in range(count):
                ET.SubElement(
                    results,
                    "UnitTestResult",
                    testName=method if count == 1 else f"{method}({index})",
                    testId=f"{method}-{index}",
                    outcome="Passed",
                )
        return root

    def invoke(self, root=None, *, validate_only=True):
        if root is not None:
            ET.ElementTree(root).write(self.report, encoding="utf-8", xml_declaration=True)
        arguments = [
            self.powershell,
            "-NoProfile",
            "-File",
            str(RUNNER),
            "-ResultsDirectory",
            str(self.directory.relative_to(REPOSITORY)),
        ]
        arguments.append("-ValidateOnly" if validate_only else "-NoBuild")
        result = subprocess.run(
            arguments,
            cwd=REPOSITORY,
            env=self.environment,
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
        self.assertFalse(self.dotnet_called.exists(), "A guard test attempted to invoke dotnet.")
        output = " ".join(ANSI.sub("", result.stdout + result.stderr).split())
        return result.returncode, output

    def assert_rejected(self, root, message):
        code, output = self.invoke(root)
        self.assertNotEqual(code, 0, output)
        self.assertIn(message, output)

    def test_valid_expected_report_passes(self):
        code, output = self.invoke(self.create_report())
        self.assertEqual(code, 0, output)
        self.assertIn(f"{self.expected}/{self.expected} executed and passed; zero skips", output)

    def test_missing_report_fails(self):
        self.assert_rejected(None, "produced no TRX")

    def test_zero_tests_fail(self):
        root = self.create_report()
        counters = root.find("ResultSummary/Counters")
        for name in ("total", "executed", "passed"):
            counters.set(name, "0")
        root.find("Results").clear()
        self.assert_rejected(root, "count mismatch")

    def test_skipped_test_fails(self):
        root = self.create_report()
        root.find("ResultSummary/Counters").set("notExecuted", "1")
        root.find("Results")[0].set("outcome", "NotExecuted")
        self.assert_rejected(root, "notExecuted=1")

    def test_skip_cannot_hide_behind_passed_counters(self):
        root = self.create_report()
        root.find("Results")[0].set("outcome", "NotExecuted")
        self.assert_rejected(root, "Every expected desktop case")

    def test_failed_test_fails(self):
        root = self.create_report()
        root.find("ResultSummary/Counters").set("failed", "1")
        root.find("Results")[0].set("outcome", "Failed")
        self.assert_rejected(root, "failed=1")

    def test_unexpected_count_fails(self):
        root = self.create_report()
        root.find("ResultSummary/Counters").set("total", str(self.expected + 1))
        self.assert_rejected(root, "count mismatch")

    def test_missing_result_cannot_hide_behind_passed_counters(self):
        root = self.create_report()
        results = root.find("Results")
        results.remove(results[0])
        self.assert_rejected(root, "Every expected desktop case")

    def test_duplicate_identity_fails(self):
        root = self.create_report()
        results = root.find("Results")
        results[1].set("testId", results[0].get("testId"))
        self.assert_rejected(root, "duplicate test identities")

    def test_unrelated_case_cannot_replace_expected_method(self):
        root = self.create_report()
        root.find("Results")[0].set("testName", "UnrelatedTest")
        self.assert_rejected(root, "Expected 1 result(s)")

    def test_failed_run_summary_fails_even_with_passed_counters(self):
        root = self.create_report()
        root.find("ResultSummary").set("outcome", "Failed")
        self.assert_rejected(root, "incomplete or failed test run")

    def test_missing_counters_fail(self):
        root = self.create_report()
        summary = root.find("ResultSummary")
        summary.remove(summary.find("Counters"))
        self.assert_rejected(root, "no result counters")

    if sys.platform.startswith("linux"):
        def test_missing_display_fails_before_starting_dotnet_or_accepting_stale_results(self):
            self.environment.pop("DISPLAY", None)
            code, output = self.invoke(self.create_report(), validate_only=False)
            self.assertNotEqual(code, 0, output)
            self.assertIn("require a reachable X11 DISPLAY", output)


if __name__ == "__main__":
    unittest.main(verbosity=2)
