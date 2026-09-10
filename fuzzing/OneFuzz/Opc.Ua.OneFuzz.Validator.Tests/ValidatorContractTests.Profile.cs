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
        [TestCase(false)]
        [TestCase(true)]
        public async Task SuppliedProfileGeneratesExactServiceConfigAndReplaysPublishedCallbacksAsync(
            bool singleDictionary)
        {
            using var drop = new ContractDrop(m_fixture);
            if (singleDictionary)
            {
                drop.Targets[0]!["dictionaries"] = ContractDrop.Strings([ContractDrop.FirstDictionary]);
            }

            JsonObject profile = CreateProfile(drop);
            string profilePath = await SaveProfileAsync(drop, profile).ConfigureAwait(false);

            ProcessResult configured = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", profilePath).ConfigureAwait(false);

            AssertSuccess(configured);
            JsonObject actual = JsonNode.Parse(
                await File.ReadAllTextAsync(drop.ConfigPath).ConfigureAwait(false))!.AsObject();
            Assert.That(actual.Select(static property => property.Key),
                Is.EquivalentTo(s_generatedConfigFields));
            Assert.That(actual["ConfigVersion"]!.GetValue<int>(), Is.EqualTo(3));
            JsonArray entries = actual["Entries"]!.AsArray();
            Assert.That(entries, Has.Count.EqualTo(3));
            for (int index = 0; index < entries.Count; index++)
            {
                JsonNode entry = entries[index]!;
                JsonNode target = drop.Targets[index]!;
                string id = target["id"]!.GetValue<string>();
                foreach (var property in profile["ownership"]!.AsObject())
                {
                    Assert.That(JsonNode.DeepEquals(entry[property.Key], property.Value), Is.True, property.Key);
                }

                Assert.That(entry["Fuzzer"]!["$type"]!.GetValue<string>(), Is.EqualTo("libfuzzerDotNet"));
                Assert.That(entry["Fuzzer"]!["Dll"]!.GetValue<string>(), Is.EqualTo(ContractDrop.AssemblyDll));
                Assert.That(entry["Fuzzer"]!["Class"]!.GetValue<string>(), Is.EqualTo(ContractDrop.CallbackType));
                Assert.That(entry["Fuzzer"]!["Method"]!.GetValue<string>(),
                    Is.EqualTo(target["method"]!.GetValue<string>()));
                JsonNode job = entry["OneFuzzJobs"]![0]!;
                foreach (string field in new[] { "ProjectName", "TargetName", "SeedCorpusContainer" })
                {
                    Assert.That(JsonNode.DeepEquals(job[field], profile["targets"]![index]![field]), Is.True, field);
                }

                string[] dependencies = [.. entry["JobDependencies"]!.AsArray()
                    .Select(static file => file!.GetValue<string>())];
                Assert.That(dependencies, Is.SupersetOf(ContractDrop.PublishedFiles));
                Assert.That(dependencies, Is.SupersetOf(drop.Inputs[id].Keys));
                Assert.That(dependencies, Does.Contain("fuzz-targets.json"));
                Assert.That(dependencies, Is.Ordered.Using<string>(StringComparer.Ordinal));
                if (index != 0)
                {
                    Assert.That(job.AsObject().ContainsKey("FuzzingTargetOptions"), Is.False);
                }
            }

            string dictionary = singleDictionary
                ? ContractDrop.FirstDictionary
                : "onefuzz-dictionaries/" + ContractDrop.InspectId + ".dict";
            Assert.That(entries[0]!["OneFuzzJobs"]![0]!["FuzzingTargetOptions"]!.AsArray()
                .Select(static option => option!.GetValue<string>()),
                Is.EqualTo(new[] { "-dict={setup_dir}/" + dictionary }));
            if (!singleDictionary)
            {
                Assert.That(await File.ReadAllLinesAsync(drop.PathFor(dictionary)).ConfigureAwait(false),
                    Is.EqualTo(s_mergedProfileTokens));
            }

            Assert.That(File.Exists(drop.PathFor(Path.GetFileName(profilePath))), Is.False);
            ProcessResult replay = await ValidateAsync(drop).ConfigureAwait(false);
            AssertCompleted(drop, replay, hasConfig: true);
        }

        [TestCase("schemaVersion", "2", "ownership profile requires schemaVersion 1")]
        [TestCase("worker/os", "\"ubuntu\"", "supplied worker contract")]
        [TestCase("worker/framework", "\"net8.0\"", "supplied worker contract")]
        [TestCase("worker/architecture", "\"\"", "'architecture' must be a nonempty string")]
        [TestCase("worker/architecture", "\"guessed\"", "supplied worker contract")]
        [TestCase("worker/instrumentation", "\"local-sharpfuzz\"", "supplied worker contract")]
        [TestCase("ownership/JobNotificationEmail", "\"\"", "'JobNotificationEmail' must be a nonempty string")]
        [TestCase("ownership/SdlWorkItemId", "0", "valid supplied notification address")]
        [TestCase("ownership/CodeCoverage/PipelineId", "\"REQUIRED\"", "contains a placeholder")]
        [TestCase("ownership/AdoTemplate/AssignedTo", "\"$(owner)\"", "contains a placeholder")]
        public async Task UnresolvedOrIncompatibleOwnershipProfileNeverEmitsFinalConfigAsync(
            string property,
            string json,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonObject profile = CreateProfile(drop);
            SetJsonValue(profile, property, json);
            string path = await SaveProfileAsync(drop, profile).ConfigureAwait(false);

            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain(diagnostic));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
            Assert.That(Directory.EnumerateFiles(drop.Root, ".OneFuzzConfig-*.json"), Is.Empty);
            Assert.That(Directory.Exists(drop.Results), Is.False);
        }

        [TestCase("missing")]
        [TestCase("extra")]
        [TestCase("duplicate")]
        public async Task OwnershipTargetSetMustExactlyMatchTheManifestAsync(string change)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonObject profile = CreateProfile(drop);
            JsonArray targets = profile["targets"]!.AsArray();
            if (change == "missing")
            {
                targets.RemoveAt(0);
            }
            else
            {
                JsonNode copy = targets[0]!.DeepClone();
                if (change == "extra")
                {
                    copy["id"] = "synthetic.unmapped";
                }

                targets.Add(copy);
            }

            string path = await SaveProfileAsync(drop, profile).ConfigureAwait(false);
            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain(change == "duplicate"
                ? "Duplicate ownership profile target"
                : "must exactly cover the selected manifest targets"));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
        }

        [Test]
        public async Task ProfileGenerationRefusesToOverwriteAServiceConfigurationAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            string before = ContractDrop.Hash(await File.ReadAllBytesAsync(drop.ConfigPath).ConfigureAwait(false));
            string path = Path.Combine(drop.Home, "protected-profile.json");
            await File.WriteAllTextAsync(path, CreateProfile(drop).ToJsonString()).ConfigureAwait(false);

            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain("Refusing to replace an existing service configuration"));
            Assert.That(ContractDrop.Hash(await File.ReadAllBytesAsync(drop.ConfigPath).ConfigureAwait(false)),
                Is.EqualTo(before));
        }

        [Test]
        public async Task MissingOwnershipProfileCannotGenerateAServiceConfigurationAsync()
        {
            using var drop = new ContractDrop(m_fixture, withConfig: false);
            drop.Save();

            ProcessResult result = await RunAsync(drop, "configure", "--drop", drop.Root).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain("profile"));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
        }

        [Test]
        public async Task CompletedPublicDropCannotReceiveStaleServiceConfigurationEvidenceAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            string path = await SaveProfileAsync(drop, CreateProfile(drop)).ConfigureAwait(false);
            const string publication = "{\"schemaVersion\":1,\"configurationGenerated\":false}";
            drop.WriteText("publication.json", publication);

            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain("Cannot configure a completed drop"));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
            Assert.That(await File.ReadAllTextAsync(drop.PathFor("publication.json")).ConfigureAwait(false),
                Is.EqualTo(publication));
        }

        [Test]
        public async Task ServiceConfigCannotBeGeneratedFromUnpublishedManifestCallbacksAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]!["method"] = "NotPublished";
            string path = await SaveProfileAsync(drop, CreateProfile(drop)).ConfigureAwait(false);

            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain("Published callback inventory differs"));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
        }

        [TestCase("ProjectName", "", "must be a nonempty string")]
        [TestCase("TargetName", "$(target)", "contains a placeholder")]
        [TestCase("SeedCorpusContainer", "Wrong--Container", "invalid explicit corpus container")]
        public async Task ProfileJobsCannotGuessMissingOwnershipAsync(
            string field,
            string value,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonObject profile = CreateProfile(drop);
            profile["targets"]![0]![field] = value;
            string path = await SaveProfileAsync(drop, profile).ConfigureAwait(false);

            ProcessResult result = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Diagnostics, Does.Contain(diagnostic));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
            Assert.That(Directory.EnumerateFiles(drop.Root, ".OneFuzzConfig-*.json"), Is.Empty);
        }

        [Test]
        public async Task FailedGenerationRemovesOnlyNewDictionariesAndCanBeRetriedAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[1]!["dictionaries"] = ContractDrop.Strings(
                [ContractDrop.FirstDictionary, ContractDrop.SecondDictionary]);
            const string first = "onefuzz-dictionaries/" + ContractDrop.InspectId + ".dict";
            const string second = "onefuzz-dictionaries/" + ContractDrop.MeasureId + ".dict";
            drop.WriteText(second, "preexisting fixture data");
            string path = await SaveProfileAsync(drop, CreateProfile(drop)).ConfigureAwait(false);

            ProcessResult failed = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            Assert.That(failed.TimedOut, Is.False, failed.Diagnostics);
            Assert.That(failed.ExitCode, Is.Not.Zero);
            Assert.That(failed.Diagnostics, Does.Contain("Refusing to replace an existing merged dictionary"));
            Assert.That(File.Exists(drop.ConfigPath), Is.False);
            Assert.That(File.Exists(drop.PathFor(first)), Is.False);
            Assert.That(await File.ReadAllTextAsync(drop.PathFor(second)).ConfigureAwait(false),
                Is.EqualTo("preexisting fixture data"));
            File.Delete(drop.PathFor(second));

            ProcessResult configured = await RunAsync(
                drop, "configure", "--drop", drop.Root, "--profile", path).ConfigureAwait(false);

            AssertSuccess(configured);
            ProcessResult replay = await ValidateAsync(drop).ConfigureAwait(false);
            AssertCompleted(drop, replay, hasConfig: true);
        }

        private static JsonObject CreateProfile(ContractDrop drop)
        {
            var ownership = new JsonObject();
            foreach (string field in new[] { "JobNotificationEmail", "AdoTemplate", "CodeCoverage", "SdlWorkItemId" })
            {
                ownership[field] = drop.Entries[0]![field]!.DeepClone();
            }

            var targets = new JsonArray();
            for (int index = 0; index < drop.Targets.Count; index++)
            {
                JsonObject job = drop.Entries[index]!["OneFuzzJobs"]![0]!.DeepClone().AsObject();
                job.Remove("FuzzingTargetOptions");
                job["id"] = drop.Targets[index]!["id"]!.DeepClone();
                targets.Add(job);
            }

            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["worker"] = new JsonObject
                {
                    ["os"] = "azurelinux3",
                    ["framework"] = "net10.0",
                    ["architecture"] = "x64",
                    ["instrumentation"] = "service"
                },
                ["ownership"] = ownership,
                ["targets"] = targets
            };
        }

        private static async Task<string> SaveProfileAsync(ContractDrop drop, JsonObject profile)
        {
            drop.Save();
            File.Delete(drop.ConfigPath);
            string path = Path.Combine(drop.Home, "protected-profile.json");
            await File.WriteAllTextAsync(path, profile.ToJsonString()).ConfigureAwait(false);
            return path;
        }

        private static readonly string[] s_generatedConfigFields = ["ConfigVersion", "Entries"];
        private static readonly string[] s_mergedProfileTokens = ["\"alpha\"", "\"beta\"", "\"shared\""];
    }
}
