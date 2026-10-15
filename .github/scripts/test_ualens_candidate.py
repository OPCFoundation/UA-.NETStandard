# Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
# Licensed under the OPC Foundation MIT License 1.00.
# See http://opcfoundation.org/License/MIT/1.00/.

"""Exercise candidate provenance and isolation guards without network access."""

from contextlib import redirect_stdout
from copy import deepcopy
import hashlib
import importlib.util
from io import BytesIO, StringIO
import json
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile


sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location(
    "ualens_candidate", Path(__file__).with_name("prepare_ualens_candidate.py"),
)
CANDIDATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CANDIDATE)
MANIFEST = CANDIDATE.REPOSITORY / ".github" / "ualens-openusd-candidate.json"


class CandidatePreparationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix="ualens-candidate-guard-")
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        self.feed = self.root / "feed"
        self.feed.mkdir()

    def writeManifest(self, manifest):
        path = self.root / "manifest.json"
        path.write_text(json.dumps(manifest), encoding="utf-8")
        return path

    def packageBytes(self, expected, *, source=None, version=None, url=None):
        package = ET.Element("package")
        metadata = ET.SubElement(package, "metadata")
        ET.SubElement(metadata, "id").text = expected["id"]
        ET.SubElement(metadata, "version").text = version or self.manifest["version"]
        repository = ET.SubElement(
            metadata, "repository", commit=source or self.manifest["sourceSha"],
        )
        repository_url = url or expected["repositoryUrl"]
        if repository_url is not None:
            repository.set("url", repository_url)
        output = BytesIO()
        with zipfile.ZipFile(output, "w") as archive:
            archive.writestr(f"{expected['id']}.nuspec", ET.tostring(package))
        return output.getvalue()

    def packageFixture(self, **kwargs):
        expected = deepcopy(self.manifest["packages"][0])
        data = self.packageBytes(expected, **kwargs)
        expected.update(bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
        path = self.root / expected["file"]
        path.write_bytes(data)
        return path, expected, data

    def archiveFixture(self, data, filename, *, mode=stat.S_IFREG):
        path = self.root / "artifact.zip"
        with zipfile.ZipFile(path, "w") as archive:
            member = zipfile.ZipInfo(filename)
            # Retain the raw spelling instead of ZipInfo's Windows normalization.
            member.filename = filename
            member.external_attr = (mode | 0o600) << 16
            archive.writestr(member, data)
        return path, {"bytes": path.stat().st_size, "sha256": CANDIDATE.sha256(path)}

    def testPinnedManifestRequiresAllRidPairs(self):
        verified = CANDIDATE.read_manifest(MANIFEST)
        self.assertEqual(len(verified["packages"]), 30)
        self.assertEqual({item["rid"] for item in verified["artifacts"]}, CANDIDATE.RIDS)
        self.manifest["packages"] = [
            item for item in self.manifest["packages"] if item["id"] != "OpenUsd.Runtime.Core.osx-arm64"
        ]
        with self.assertRaisesRegex(ValueError, "missing required"):
            CANDIDATE.read_manifest(self.writeManifest(self.manifest))

    def testManifestRejectsUntrustedOrAmbiguousInputs(self):
        mutations = (
            lambda value: value.update(repository="another/repository"),
            lambda value: value.update(sourceSha="branch-name"),
            lambda value: value.update(version='0.15.0-alpha" Inject="true'),
            lambda value: value.update(avaloniaVersion="12.1.4\nNUGET_PACKAGES=elsewhere"),
            lambda value: value.update(packageRunId=True),
            lambda value: value["packages"].append(deepcopy(value["packages"][0])),
            lambda value: value["artifacts"][0].update(id=value["artifacts"][1]["id"]),
            lambda value: value["packages"][0].update(file="../outside.nupkg"),
            lambda value: value["packages"][0].update(sha256="not-a-hash"),
            lambda value: value["packages"][0].update(bytes=CANDIDATE.MAX_ARCHIVE_BYTES + 1),
            lambda value: value["packages"][0].update(bytes=0),
            lambda value: value["packages"][0].update(repositoryUrl=None),
        )
        for index, mutate in enumerate(mutations):
            with self.subTest(case=index):
                value = deepcopy(self.manifest)
                mutate(value)
                with self.assertRaises(ValueError):
                    CANDIDATE.read_manifest(self.writeManifest(value))

    def testPackageRequiresExactNuspecSourceDespiteMatchingBytes(self):
        path, expected, _ = self.packageFixture(source="f" * 40)
        with self.assertRaisesRegex(ValueError, "identity or source"):
            CANDIDATE.verify_package(path, expected, self.manifest)

    def testPackageRequiresExactNuspecVersionAndRepository(self):
        for overrides in ({"version": "0.14.0-alpha"}, {"url": "https://github.com/another/repository"}):
            with self.subTest(overrides=overrides):
                path, expected, _ = self.packageFixture(**overrides)
                with self.assertRaisesRegex(ValueError, "identity or source"):
                    CANDIDATE.verify_package(path, expected, self.manifest)

    def testPackageRejectsChangedPayloadHash(self):
        path, expected, _ = self.packageFixture()
        expected["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "SHA256"):
            CANDIDATE.verify_package(path, expected, self.manifest)

    def testArchiveStagesOnlyVerifiedPackageBytes(self):
        _, expected, data = self.packageFixture()
        archive, identity = self.archiveFixture(data, "packages/" + expected["file"])
        manifest = {**self.manifest, "packages": [expected]}
        staged = CANDIDATE.stage_archive(archive, identity, manifest, self.feed)
        self.assertEqual(staged, {expected["file"]})
        self.assertEqual((self.feed / expected["file"]).read_bytes(), data)

    def testArchiveRejectsChangedOuterHashBeforeExtraction(self):
        _, expected, data = self.packageFixture()
        archive, identity = self.archiveFixture(data, expected["file"])
        identity["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "SHA256"):
            CANDIDATE.stage_archive(archive, identity, {**self.manifest, "packages": [expected]}, self.feed)
        self.assertEqual(list(self.feed.iterdir()), [])

    def testArchiveRejectsTraversalSymlinksAndUnlistedPackages(self):
        _, expected, data = self.packageFixture()
        for index, (filename, mode) in enumerate((
            ("../" + expected["file"], stat.S_IFREG),
            ("/" + expected["file"], stat.S_IFREG),
            ("nested\\" + expected["file"], stat.S_IFREG),
            (expected["file"], stat.S_IFLNK),
            ("OpenUsd.Unlisted.0.15.0-alpha.nupkg", stat.S_IFREG),
        )):
            with self.subTest(filename=filename, mode=mode):
                archive, identity = self.archiveFixture(data, filename, mode=mode)
                feed = self.root / f"invalid-{index}"
                feed.mkdir()
                with self.assertRaises(ValueError):
                    CANDIDATE.stage_archive(archive, identity, {**self.manifest, "packages": [expected]}, feed)
                self.assertEqual(list(feed.iterdir()), [])

    def testArchiveRejectsCorruptExistingFeedBytes(self):
        _, expected, data = self.packageFixture()
        archive, identity = self.archiveFixture(data, expected["file"])
        (self.feed / expected["file"]).write_bytes(bytes(len(data)))
        with self.assertRaisesRegex(ValueError, "SHA256"):
            CANDIDATE.stage_archive(archive, identity, {**self.manifest, "packages": [expected]}, self.feed)

    def testRunRejectsAnotherSourceOrRepository(self):
        run = {
            "id": self.manifest["packageRunId"],
            "repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
            "head_repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
            "head_sha": self.manifest["sourceSha"],
        }
        CANDIDATE.verify_run(run, self.manifest["packageRunId"], self.manifest["sourceSha"])
        for field, value in (
            ("head_sha", "f" * 40), ("id", run["id"] + 1),
            ("head_repository", {"full_name": "another/repository"}),
        ):
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "run does not match"):
                CANDIDATE.verify_run({**run, field: value}, self.manifest["packageRunId"], self.manifest["sourceSha"])

    def testArtifactRejectsExpiredMismatchedOrUnboundBytes(self):
        expected = self.manifest["artifacts"][0]
        metadata = {
            "id": expected["id"], "name": expected["name"], "digest": "sha256:" + expected["sha256"],
            "size_in_bytes": expected["bytes"], "expired": False,
            "workflow_run": {"id": self.manifest["packageRunId"], "head_sha": self.manifest["sourceSha"]},
        }
        CANDIDATE.verify_artifact(metadata, expected, self.manifest)
        for field, value in (
            ("expired", True), ("digest", "sha256:" + "0" * 64), ("size_in_bytes", expected["bytes"] + 1),
            ("workflow_run", {"id": self.manifest["packageRunId"] + 1, "head_sha": self.manifest["sourceSha"]}),
        ):
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "artifact identity"):
                CANDIDATE.verify_artifact({**metadata, field: value}, expected, self.manifest)

    def testOverridesKeepProductionPinsAndAllAvaloniaEntriesAligned(self):
        repository = self.root / "repository"
        repository.mkdir()
        production = repository / "Directory.Packages.props"
        original = (
            b'<Project><ItemGroup><PackageVersion Include="OpenUsd" Version="0.14.0-alpha"/>'
            b'</ItemGroup></Project>'
        )
        production.write_bytes(original)
        environment = CANDIDATE.write_overrides(self.root, self.feed, self.manifest, repository)
        props = ET.parse(environment["DirectoryPackagesPropsPath"]).getroot()
        self.assertEqual(props.find("Import").get("Project"), str(production))
        versions = {item.get("Update"): item.get("Version") for item in props.findall("ItemGroup/PackageVersion")}
        self.assertEqual(versions, {
            "OpenUsd": "0.15.0-alpha",
            "OpenUsd.Viewer": "0.15.0-alpha",
            "OpenUsd.Rendering": "0.15.0-alpha",
            "OpenUsd.Rendering.Silk": "0.15.0-alpha",
            "OpenUsd.Rendering.Silk.D3D12": "0.15.0-alpha",
            "OpenUsd.Rendering.Silk.Vulkan": "0.15.0-alpha",
            "OpenUsd.Runtime.Core": "0.15.0-alpha",
            "OpenUsd.Runtime.Imaging": "0.15.0-alpha",
            "Avalonia": "12.1.4",
            "Avalonia.Desktop": "12.1.4",
            "Avalonia.Fonts.Inter": "12.1.4",
            "Avalonia.Themes.Fluent": "12.1.4",
        })
        self.assertEqual(production.read_bytes(), original)
        config = ET.parse(environment["RestoreConfigFile"]).getroot()
        self.assertIsNotNone(config.find("packageSources/clear"))
        self.assertEqual(
            [(item.get("key"), item.get("value")) for item in config.findall("packageSources/add")],
            [("candidate", str(self.feed)), ("nuget.org", "https://api.nuget.org/v3/index.json")],
        )
        self.assertEqual(config.find("packageSourceMapping/packageSource[@key='candidate']/package").get("pattern"),
                         "OpenUsd*")
        self.assertEqual(config.find("auditSources/add").get("value"), "https://api.nuget.org/v3/index.json")
        self.assertEqual(environment["RestorePackagesPath"], environment["NUGET_PACKAGES"])
        self.assertEqual(environment["NUGET_PACKAGES"], str(self.root / "packages"))

    def testFailedSourceValidationDoesNotActivateOverrides(self):
        environment = self.root / "github-env"
        environment.write_text("EXISTING=value\n", encoding="utf-8")
        run = {
            "id": self.manifest["packageRunId"],
            "repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
            "head_repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
            "head_sha": "f" * 40,
        }
        with patch.object(CANDIDATE, "github_json", return_value=run), self.assertRaises(ValueError):
            CANDIDATE.prepare(MANIFEST, self.root / "work", self.root / "evidence.json", environment)
        self.assertEqual(environment.read_text(encoding="utf-8"), "EXISTING=value\n")
        self.assertFalse((self.root / "work" / "Directory.Packages.props").exists())
        self.assertFalse((self.root / "evidence.json").exists())

    def preparationFixture(self, missing_package=None):
        manifest = deepcopy(self.manifest)
        package_data = {}
        for expected in manifest["packages"]:
            data = self.packageBytes(expected)
            expected.update(bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
            package_data[expected["file"]] = data
        archives = {}
        for index, expected in enumerate(manifest["artifacts"]):
            output = BytesIO()
            with zipfile.ZipFile(output, "w") as archive:
                for package in manifest["packages"][index::3]:
                    if package["file"] != missing_package:
                        archive.writestr("packages/" + package["file"], package_data[package["file"]])
            data = output.getvalue()
            expected.update(bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
            archives[expected["id"]] = data

        def metadata(endpoint):
            identity = int(endpoint.rsplit("/", 1)[1])
            if "/runs/" in endpoint:
                return {
                    "id": identity, "repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
                    "head_repository": {"full_name": CANDIDATE.SOURCE_REPOSITORY},
                    "head_sha": manifest["sourceSha"],
                }
            expected = next(item for item in manifest["artifacts"] if item["id"] == identity)
            return {
                "id": identity, "name": expected["name"], "digest": "sha256:" + expected["sha256"],
                "size_in_bytes": expected["bytes"], "expired": False,
                "workflow_run": {"id": manifest["packageRunId"], "head_sha": manifest["sourceSha"]},
            }

        def execute(command, **kwargs):
            if command[:2] == ["gh", "api"]:
                kwargs["stdout"].write(archives[int(command[-1].split("/")[-2])])
                return subprocess.CompletedProcess(command, 0)
            self.assertEqual(command, ["git", "rev-parse", "HEAD"])
            return subprocess.CompletedProcess(command, 0, "c" * 40 + "\n")

        return manifest, package_data, metadata, execute

    def testPreparationBindsEveryPackageBeforeActivatingTheIsolatedFeed(self):
        manifest, package_data, metadata, execute = self.preparationFixture()
        environment = self.root / "github-env"
        manifest_path = self.writeManifest(manifest)
        with (
            patch.object(CANDIDATE, "github_json", side_effect=metadata),
            patch.object(CANDIDATE.subprocess, "run", side_effect=execute),
            redirect_stdout(StringIO()),
        ):
            result = CANDIDATE.prepare(
                manifest_path, self.root / "work", self.root / "evidence.json", environment,
            )
        self.assertEqual(result["verifiedPackageCount"], len(package_data))
        self.assertEqual(result["consumerSourceSha"], "c" * 40)
        self.assertEqual(result["inputManifestSha256"], CANDIDATE.sha256(manifest_path))
        self.assertEqual(result["metadataFindings"], ["OpenUsd.Viewer nuspec omits its repository URL."])
        self.assertEqual(json.loads((self.root / "evidence.json").read_text(encoding="utf-8")), result)
        self.assertEqual(
            {path.name: path.read_bytes() for path in (self.root / "work" / "feed").iterdir()}, package_data,
        )
        self.assertEqual(
            dict(line.split("=", 1) for line in environment.read_text(encoding="utf-8").splitlines()),
            result["environment"],
        )
        self.assertEqual(list((self.root / "work").glob("*.zip")), [])

    def testIncompleteFeedDoesNotActivateOverrides(self):
        manifest, _, metadata, execute = self.preparationFixture(self.manifest["packages"][0]["file"])
        environment = self.root / "github-env"
        with (
            patch.object(CANDIDATE, "github_json", side_effect=metadata),
            patch.object(CANDIDATE.subprocess, "run", side_effect=execute),
            self.assertRaisesRegex(ValueError, "feed is incomplete"),
        ):
            CANDIDATE.prepare(
                self.writeManifest(manifest), self.root / "work", self.root / "evidence.json", environment,
            )
        self.assertFalse(environment.exists())
        self.assertFalse((self.root / "work" / "Directory.Packages.props").exists())
        self.assertFalse((self.root / "evidence.json").exists())

    def testGitHubAccessFailureDoesNotActivateOverrides(self):
        environment = self.root / "github-env"
        failure = subprocess.CalledProcessError(1, ["gh", "api"])
        with (
            patch.object(CANDIDATE, "github_json", side_effect=failure),
            self.assertRaises(subprocess.CalledProcessError),
        ):
            CANDIDATE.prepare(MANIFEST, self.root / "work", self.root / "evidence.json", environment)
        self.assertFalse(environment.exists())
        self.assertFalse((self.root / "work" / "Directory.Packages.props").exists())


if __name__ == "__main__":
    unittest.main()
