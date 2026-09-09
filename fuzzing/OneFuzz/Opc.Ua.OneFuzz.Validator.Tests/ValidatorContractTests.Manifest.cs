/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    public sealed partial class ValidatorContractTests
    {
        [Test]
        public async Task MissingDeclaredCallbackFailsExactInventoryBeforeReplayAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]!["method"] = "NoSuchCallback";

            await AssertPreflightRejectedAsync(drop, "Missing:", "NoSuchCallback").ConfigureAwait(false);
        }

        [Test]
        public async Task ExtraPublishedCallbackCannotBeIgnoredByOmittingItFromManifestAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets.RemoveAt(1);

            await AssertPreflightRejectedAsync(drop, "Extra:", "Measure").ConfigureAwait(false);
        }

        [Test]
        public async Task WrongManifestClassFailsExactInventoryBeforeReplayAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Area["type"] = "Opc.Ua.OneFuzz.TestTarget.CallbackLookalikes";

            await AssertPreflightRejectedAsync(drop, "Published callback inventory differs", "CallbackLookalikes")
                .ConfigureAwait(false);
        }

        [TestCase("id", "\"\"", "'id' must be a nonempty string")]
        [TestCase("id", "\"bad/id\"", "'id' must be a portable identifier")]
        [TestCase("id", "\"..\"", "'id' must be a portable identifier")]
        [TestCase("method", "\"\"", "'method' must be a nonempty string")]
        [TestCase("method", "\"Bad Method\"", "'method' must be a portable identifier")]
        [TestCase("method", "null", "'method' must be a nonempty string")]
        [TestCase("corpus", "[]", "'corpus' must not be empty")]
        [TestCase("corpus", "[\"\"]", "'corpus' requires distinct nonempty strings")]
        [TestCase("corpus", "[1]", "'corpus' requires distinct nonempty strings")]
        [TestCase("dictionaries", "[null]", "'dictionaries' requires distinct nonempty strings")]
        [TestCase("classification", "\"local-only\"", "Only explicitly continuous targets")]
        [TestCase("classification", "\"Continuous\"", "Only explicitly continuous targets")]
        public async Task MalformedTargetFieldsFailBeforeReplayAsync(string property, string json, string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]![property] = JsonNode.Parse(json);

            await AssertPreflightRejectedAsync(drop, diagnostic).ConfigureAwait(false);
        }

        [TestCase("id")]
        [TestCase("method")]
        public async Task DuplicateTargetIdentityOrCallbackFailsBeforeReplayAsync(string property)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[1]![property] = drop.Targets[0]![property]!.DeepClone();

            await AssertPreflightRejectedAsync(drop, "Duplicate target ID or callback").ConfigureAwait(false);
        }

        [TestCase("corpus", ContractDrop.FirstBucket)]
        [TestCase("dictionaries", ContractDrop.FirstDictionary)]
        public async Task DuplicateManifestPathsFailBeforeReplayAsync(string property, string path)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]![property] = ContractDrop.Strings([path, path]);

            await AssertPreflightRejectedAsync(drop, $"'{property}' requires distinct nonempty strings")
                .ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateAreaIdentityOrAssemblyFailsBeforeReplayAsync(bool duplicateAssemblyOnly)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonNode otherArea = drop.Area.DeepClone();
            if (duplicateAssemblyOnly)
            {
                otherArea["id"] = "different-area";
            }

            drop.Manifest["areas"]!.AsArray().Add(otherArea);
            await AssertPreflightRejectedAsync(drop, duplicateAssemblyOnly
                ? "Assembly names must be unique DLL filenames"
                : "Duplicate area ID").ConfigureAwait(false);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task UnsupportedManifestSchemaFailsBeforeReplayAsync(int version)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Manifest["schemaVersion"] = version;

            await AssertPreflightRejectedAsync(drop, "schemaVersion 1").ConfigureAwait(false);
        }

        [Test]
        public async Task SyntacticallyMalformedManifestFailsBeforeReplayAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            drop.WriteText("fuzz-targets.json", "{\"schemaVersion\":1,\"areas\":[");

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "JsonReaderException");
        }

        [TestCase("schemaVersion", "\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
        [TestCase("method", "\"method\":\"Inspect\"", "\"method\":\"Inspect\",\"method\":\"Inspect\"")]
        public async Task DuplicateJsonPropertiesInManifestFailBeforeReplayAsync(
            string property,
            string original,
            string duplicate)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            string json = drop.Manifest.ToJsonString();
            Assert.That(json, Does.Contain(original), "The negative control must actually duplicate a property.");
            drop.WriteText("fuzz-targets.json", json.Replace(original, duplicate, StringComparison.Ordinal));

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "Duplicate JSON property: " + property);
        }

        [Test]
        public async Task AreaSelectionLoadsOnlyTheSelectedAssemblyAndReplaysAllItsCallbacksAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            JsonNode unselected = drop.Area.DeepClone();
            unselected["id"] = "not-selected";
            unselected["assembly"] = "NotPublishedInThisDrop.dll";
            foreach (JsonNode? target in unselected["targets"]!.AsArray())
            {
                target!["id"] = "unselected-" + target["id"]!.GetValue<string>();
            }

            drop.Manifest["areas"]!.AsArray().Add(unselected);
            drop.Save();

            ProcessResult result = await ValidateAsync(drop, "--areas", ContractDrop.AreaId).ConfigureAwait(false);

            AssertCompleted(drop, result, hasConfig: true);
        }

        [TestCase("", "Area selection must contain nonempty, exact area IDs")]
        [TestCase("synthetic-contract,", "Area selection must contain nonempty, exact area IDs")]
        [TestCase(",synthetic-contract", "Area selection must contain nonempty, exact area IDs")]
        [TestCase("Synthetic-contract", "a requested area is absent")]
        [TestCase("missing-area", "a requested area is absent")]
        [TestCase("synthetic-contract,missing-area", "a requested area is absent")]
        public async Task InvalidAreaSelectionFailsBeforeReplayAsync(string selection, string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();

            ProcessResult result = await ValidateAsync(drop, "--areas", selection).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, diagnostic);
        }

        [Test]
        public async Task ExplicitManifestAndConfigurationInsideDropAreUsedAndHashedAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            string manifest = drop.PathFor("metadata/custom manifest.json");
            string config = drop.PathFor("metadata/custom config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.Move(drop.ManifestPath, manifest);
            File.Move(drop.ConfigPath, config);

            ProcessResult result = await ValidateAsync(
                drop, "--manifest", manifest, "--config", config).ConfigureAwait(false);

            AssertCompleted(drop, result, hasConfig: true, manifest, config);
        }

        [TestCase("--manifest")]
        [TestCase("--config")]
        public async Task ExplicitManifestOrConfigurationOutsideDropIsRejectedAsync(string option)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            string outside = Path.Combine(drop.Home, "outside.json");
            File.Copy(option == "--manifest" ? drop.ManifestPath : drop.ConfigPath, outside);

            ProcessResult result = await ValidateAsync(drop, option, outside).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "escaped the published drop", outside);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResultsPathMustBeNewAndExistingEvidenceIsPreservedAsync(bool isFile)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            if (!isFile)
            {
                Directory.CreateDirectory(drop.Results);
            }

            string sentinel = isFile ? drop.Results : Path.Combine(drop.Results, "existing-evidence.txt");
            File.WriteAllText(sentinel, "do not overwrite this synthetic evidence");

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "Use a new results directory");
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("do not overwrite this synthetic evidence"));
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("86401")]
        public async Task InvalidWatchdogLimitsFailBeforeReplayAsync(string seconds)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();

            ProcessResult result = await ValidateAsync(drop, "--timeout-seconds", seconds).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "watchdog must be 1-86400 seconds");
        }

        private async Task AssertPreflightRejectedAsync(ContractDrop drop, params string[] diagnostics)
        {
            drop.Save();
            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);
            AssertRejected(drop, result, beforeReplay: true, diagnostics);
        }

        private static void AssertRejected(
            ContractDrop drop,
            ProcessResult result,
            bool beforeReplay,
            params string[] diagnostics)
        {
            Assert.That(result.TimedOut, Is.False, "The test's safety timeout is not validator rejection.\n" +
                result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero, result.Diagnostics);
            foreach (string diagnostic in diagnostics)
            {
                Assert.That(result.Error, Does.Contain(diagnostic), result.Diagnostics);
            }

            Assert.That(result.Output, Does.Not.Contain("Validated ").And.Not.Contain("zero skips."));
            Assert.That(File.Exists(Path.Combine(drop.Results, "validation.json")), Is.False);
            if (beforeReplay && Directory.Exists(drop.Results))
            {
                Assert.That(Directory.EnumerateFiles(drop.Results).Select(Path.GetFileName),
                    Has.None.StartsWith("contract."), "Preflight rejection must not invoke any callback.");
            }
        }
    }
}
