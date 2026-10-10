/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    public sealed partial class ValidatorContractTests
    {
        [TestCase(2)]
        [TestCase(4)]
        public async Task AutoDetectedConfigurationRequiresVersionThreeAsync(int version)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Config!["ConfigVersion"] = version;

            await AssertPreflightRejectedAsync(drop, "ConfigVersion 3").ConfigureAwait(false);
        }

        [TestCase("Fuzzer/$type", "\"libfuzzer\"", "unexpected configured callback")]
        [TestCase("Fuzzer/$type", "\"libfuzzerdotnet\"", "unexpected configured callback")]
        [TestCase("Fuzzer/Dll", "\"Wrong.dll\"", "unexpected configured callback")]
        [TestCase("Fuzzer/Dll", "\"Opc.Ua.OneFuzz.TestTarget\"", "unexpected configured callback")]
        [TestCase("Fuzzer/Dll", "\"opc.Ua.OneFuzz.TestTarget.dll\"", "unexpected configured callback")]
        [TestCase("Fuzzer/Class", "\"Opc.Ua.OneFuzz.TestTarget.CallbackLookalikes\"",
            "unexpected configured callback")]
        [TestCase("Fuzzer/Method", "\"NoSuchCallback\"", "unexpected configured callback")]
        [TestCase("Fuzzer/Method", "\"inspect\"", "unexpected configured callback")]
        [TestCase("Skip", "true", "Skipped, duplicate, or unexpected configured callback")]
        [TestCase("Skip", "\"false\"", "InvalidOperationException")]
        [TestCase("MinAvailableMemoryMB", "99", "schema or baseline policy changed")]
        [TestCase("MinAvailableMemoryMB", "101", "schema or baseline policy changed")]
        [TestCase("FuzzerTimeoutInSeconds", "119", "schema or baseline policy changed")]
        [TestCase("FuzzerTimeoutInSeconds", "121", "schema or baseline policy changed")]
        public async Task ManagedFuzzerMappingAndBaselinePolicyAreExactAsync(
            string property,
            string json,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            SetJsonValue(drop.Entries[0]!, property, json);

            await AssertPreflightRejectedAsync(drop, diagnostic).ConfigureAwait(false);
        }

        [TestCase("$type")]
        [TestCase("Dll")]
        [TestCase("Class")]
        [TestCase("Method")]
        public async Task EveryExactManagedFuzzerKeyIsRequiredAsync(string key)
        {
            using var drop = new ContractDrop(m_fixture);
            Assert.That(drop.Entries[0]!["Fuzzer"]!.AsObject().Remove(key), Is.True);

            await AssertPreflightRejectedAsync(drop, "KeyNotFoundException").ConfigureAwait(false);
        }

        [Test]
        public async Task ManagedFuzzerMustHaveExactlyFourKeysWithNoLegacyAliasAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["Fuzzer"]!["Assembly"] = ContractDrop.AssemblyDll;

            await AssertPreflightRejectedAsync(drop, "approved managed fuzzer schema").ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationCannotOmitAnyManifestCallbackAsync(bool removeAll)
        {
            using var drop = new ContractDrop(m_fixture);
            if (removeAll)
            {
                drop.Entries.Clear();
            }
            else
            {
                drop.Entries.RemoveAt(1);
            }

            await AssertPreflightRejectedAsync(drop, "does not exactly cover the manifest callback set")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigurationCannotRepeatTheSameCallbackAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries.Add(drop.Entries[0]!.DeepClone());

            await AssertPreflightRejectedAsync(drop, "duplicate, or unexpected configured callback")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task DuplicateJsonPropertyInAutoDetectedConfigurationIsRejectedAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            string json = drop.Config!.ToJsonString();
            Assert.That(json, Does.Contain("\"Skip\":false"));
            drop.WriteText("OneFuzzConfig.json", json.Replace(
                "\"Skip\":false", "\"Skip\":false,\"Skip\":false", StringComparison.Ordinal));

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "Duplicate JSON property: Skip");
        }

        [Test]
        public async Task MalformedAutoDetectedConfigurationIsNotSilentlyIgnoredAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Save();
            drop.WriteText("OneFuzzConfig.json", "{\"ConfigVersion\":3,\"Entries\":[");

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertRejected(drop, result, beforeReplay: true, "JsonReaderException");
        }

        [TestCase("JobNotificationEmail", "\"not-an-email\"", "valid supplied notification address")]
        [TestCase("JobNotificationEmail", "\"\"", "'JobNotificationEmail' must be a nonempty string")]
        [TestCase("SdlWorkItemId", "0", "valid supplied notification address")]
        [TestCase("SdlWorkItemId", "-1", "valid supplied notification address")]
        [TestCase("AdoTemplate/Org", "\"\"", "'Org' must be a nonempty string")]
        [TestCase("AdoTemplate/Project", "\"\"", "'Project' must be a nonempty string")]
        [TestCase("AdoTemplate/AssignedTo", "\"\"", "'AssignedTo' must be a nonempty string")]
        [TestCase("AdoTemplate/AreaPath", "\"\"", "'AreaPath' must be a nonempty string")]
        [TestCase("AdoTemplate/IterationPath", "\"\"", "'IterationPath' must be a nonempty string")]
        [TestCase("AdoTemplate/Type", "\"\"", "'Type' must be a nonempty string")]
        [TestCase("AdoTemplate/AdoFields", "{}", "Explicit AdoFields are required")]
        [TestCase("AdoTemplate/AdoFields", "[]", "Explicit AdoFields are required")]
        [TestCase("AdoTemplate/UniqueFields", "[]", "'UniqueFields' must not be empty")]
        [TestCase("AdoTemplate/UniqueFields", "[\"\"]", "'UniqueFields' requires distinct nonempty strings")]
        [TestCase("CodeCoverage/Org", "\"\"", "'Org' must be a nonempty string")]
        [TestCase("CodeCoverage/Project", "\"\"", "'Project' must be a nonempty string")]
        [TestCase("CodeCoverage/PipelineId", "\"0\"", "positive supplied coverage PipelineId")]
        [TestCase("CodeCoverage/PipelineId", "\"-1\"", "positive supplied coverage PipelineId")]
        [TestCase("CodeCoverage/PipelineId", "\"1.5\"", "positive supplied coverage PipelineId")]
        [TestCase("CodeCoverage/PipelineId", "\" 67890 \"", "positive supplied coverage PipelineId")]
        [TestCase("CodeCoverage/PipelineId", "67890", "'PipelineId' must be a nonempty string")]
        public async Task RequiredSyntheticOwnershipFieldsCannotBeEmptyOrInvalidAsync(
            string property,
            string json,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            SetJsonValue(drop.Entries[0]!, property, json);

            await AssertPreflightRejectedAsync(drop, diagnostic).ConfigureAwait(false);
        }

        [TestCase("$(SyntheticOwner)")]
        [TestCase("${SyntheticOwner}")]
        [TestCase("<synthetic-owner>")]
        [TestCase("REQUIRED")]
        [TestCase("replace_me")]
        public async Task OwnershipMustBeSuppliedRatherThanAnUnexpandedPlaceholderAsync(string placeholder)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["AdoTemplate"]!["AssignedTo"] = placeholder;

            await AssertPreflightRejectedAsync(drop, "Explicit ownership is required", "AssignedTo")
                .ConfigureAwait(false);
        }

        [TestCase(0)]
        [TestCase(2)]
        public async Task EachEntryRequiresExactlyOneOwnedServiceJobAsync(int count)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonArray jobs = drop.Entries[0]!["OneFuzzJobs"]!.AsArray();
            if (count == 0)
            {
                jobs.Clear();
            }
            else
            {
                jobs.Add(jobs[0]!.DeepClone());
            }

            await AssertPreflightRejectedAsync(drop, "Exactly one explicitly owned service job")
                .ConfigureAwait(false);
        }

        [TestCase("ProjectName")]
        [TestCase("TargetName")]
        [TestCase("SeedCorpusContainer")]
        public async Task EveryServiceJobOwnershipFieldMustBeExplicitAsync(string field)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["OneFuzzJobs"]![0]![field] = string.Empty;

            await AssertPreflightRejectedAsync(drop, $"'{field}' must be a nonempty string").ConfigureAwait(false);
        }

        [Test]
        public async Task DifferentCallbacksCannotShareTheSameServiceProjectAndTargetIdentityAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            JsonNode firstJob = drop.Entries[0]!["OneFuzzJobs"]![0]!;
            JsonNode secondJob = drop.Entries[1]!["OneFuzzJobs"]![0]!;
            secondJob["ProjectName"] = firstJob["ProjectName"]!.DeepClone();
            secondJob["TargetName"] = firstJob["TargetName"]!.DeepClone();

            await AssertPreflightRejectedAsync(drop, "Duplicate service job").ConfigureAwait(false);
        }

        [TestCase("ct")]
        [TestCase("-synthetic-container")]
        [TestCase("synthetic-container-")]
        [TestCase("synthetic--container")]
        [TestCase("Synthetic-container")]
        [TestCase("synthetic_container")]
        public async Task ExplicitSeedContainerMustUsePortableContainerNamingAsync(string container)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["OneFuzzJobs"]![0]!["SeedCorpusContainer"] = container;

            await AssertPreflightRejectedAsync(drop, "invalid explicit corpus container").ConfigureAwait(false);
        }

        [Test]
        public async Task SeedContainerLongerThanSixtyThreeCharactersIsRejectedAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["OneFuzzJobs"]![0]!["SeedCorpusContainer"] = "ct-" + new string('x', 61);

            await AssertPreflightRejectedAsync(drop, "invalid explicit corpus container").ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PositiveOwnershipIdAndContainerLengthBoundariesAreAcceptedAsync(bool longestContainer)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["SdlWorkItemId"] = 1;
            drop.Entries[0]!["CodeCoverage"]!["PipelineId"] = "1";
            drop.Entries[0]!["OneFuzzJobs"]![0]!["SeedCorpusContainer"] =
                longestContainer ? "ct-" + new string('x', 60) : "ct0";
            drop.Save();

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertCompleted(drop, result, hasConfig: true);
        }

        [TestCase(ContractDrop.AssemblyDll)]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.deps.json")]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.runtimeconfig.json")]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.pdb")]
        [TestCase(ContractDrop.DependencyDll)]
        [TestCase("Opc.Ua.OneFuzz.TestDependency.pdb")]
        [TestCase(ContractDrop.FirstBucket + "/Z-first seed.bin")]
        [TestCase(ContractDrop.SecondBucket + "/required.bin")]
        [TestCase(ContractDrop.FirstDictionary)]
        [TestCase(ContractDrop.SecondDictionary)]
        [TestCase(ContractDrop.MergedDictionary)]
        public async Task JobDependenciesMustExplicitlyContainEveryClosureSeedAndDictionaryFileAsync(string file)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonArray dependencies = drop.Entries[0]!["JobDependencies"]!.AsArray();
            JsonNode item = dependencies.Single(node => node!.GetValue<string>() == file)!;
            Assert.That(dependencies.Remove(item), Is.True);
            Assert.That(File.Exists(drop.PathFor(file)), Is.True,
                "The file exists; only the job mapping is incomplete.");

            await AssertPreflightRejectedAsync(drop, "omits published closure or seed files", file)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AnotherTargetsSeedDependenciesDoNotSatisfyThisTargetsMappingAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["JobDependencies"] = drop.Entries[1]!["JobDependencies"]!.DeepClone();

            await AssertPreflightRejectedAsync(
                drop, "omits published closure or seed files", ContractDrop.InspectId, ContractDrop.FirstBucket)
                .ConfigureAwait(false);
        }

        [TestCase(ContractDrop.FirstBucket, "actual files, not directories")]
        [TestCase("fuzzing/ContractFixture/Corpus/*", "Expected a portable relative path")]
        [TestCase("../outside.dll", "Expected a portable relative path")]
        [TestCase("MissingDependency.dll", "Required path is missing")]
        [TestCase("opc.Ua.OneFuzz.TestTarget.dll", "missing or has incorrect case")]
        public async Task JobDependenciesAreExistingExactCaseFilePathsNotDirectoriesOrPatternsAsync(
            string path,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[0]!["JobDependencies"]!.AsArray().Add(path);

            await AssertPreflightRejectedAsync(drop, diagnostic, path).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task JobDependenciesMustBeNonemptyAndDistinctAsync(bool duplicate)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonArray dependencies = drop.Entries[0]!["JobDependencies"]!.AsArray();
            if (duplicate)
            {
                dependencies.Add(dependencies[0]!.DeepClone());
            }
            else
            {
                dependencies.Clear();
            }

            await AssertPreflightRejectedAsync(drop, duplicate
                ? "'JobDependencies' requires distinct nonempty strings"
                : "'JobDependencies' must not be empty").ConfigureAwait(false);
        }

        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("absolute")]
        [TestCase("two")]
        public async Task DictionaryMappingRequiresOneRelocatableDictionaryOptionAsync(string change)
        {
            using var drop = new ContractDrop(m_fixture);
            JsonObject job = drop.Entries[0]!["OneFuzzJobs"]![0]!.AsObject();
            switch (change)
            {
                case "missing":
                    job.Remove("FuzzingTargetOptions");
                    break;
                case "empty":
                    job["FuzzingTargetOptions"] = new JsonArray();
                    break;
                case "absolute":
                    job["FuzzingTargetOptions"] = ContractDrop.Strings(
                        ["-dict=" + drop.PathFor(ContractDrop.MergedDictionary)]);
                    break;
                case "two":
                    job["FuzzingTargetOptions"] = ContractDrop.Strings(
                    [
                        "-dict={setup_dir}/" + ContractDrop.MergedDictionary,
                        "-dict={setup_dir}/" + ContractDrop.FirstDictionary
                    ]);
                    break;
            }

            await AssertPreflightRejectedAsync(drop, "Exactly one relocatable dictionary option")
                .ConfigureAwait(false);
        }

        [TestCase("\"alpha\"\n")]
        [TestCase("\"alpha\"\n\"beta\"\n\"shared\"\n\"extra\"\n")]
        public async Task MergedDictionaryMustPreserveExactlyTheUnionOfManifestTokensAsync(string tokens)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.WriteText(ContractDrop.MergedDictionary, tokens);

            await AssertPreflightRejectedAsync(drop, "does not preserve all manifest tokens", ContractDrop.InspectId)
                .ConfigureAwait(false);
        }

        [TestCase(ContractDrop.FirstDictionary)]
        [TestCase(ContractDrop.MergedDictionary)]
        public async Task CommentOnlyDictionaryIsNotAUsableDictionaryAsync(string dictionary)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.WriteText(dictionary, "# no tokens here\n  \n");

            await AssertPreflightRejectedAsync(drop, "required dictionary has no tokens").ConfigureAwait(false);
        }

        [Test]
        public async Task MissingGeneratedDictionaryCannotBeReplacedByItsSourceDictionariesAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            File.Delete(drop.PathFor(ContractDrop.MergedDictionary));

            await AssertPreflightRejectedAsync(drop, "Required path is missing", ContractDrop.MergedDictionary)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task DictionaryFreeTargetCannotBorrowAnotherTargetsDictionaryOptionAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Entries[1]!["OneFuzzJobs"]![0]!["FuzzingTargetOptions"] =
                ContractDrop.Strings(["-dict={setup_dir}/" + ContractDrop.MergedDictionary]);

            await AssertPreflightRejectedAsync(drop, "Unexpected dictionary option", ContractDrop.MeasureId)
                .ConfigureAwait(false);
        }

        private static void SetJsonValue(JsonNode root, string path, string json)
        {
            string[] segments = path.Split('/');
            JsonNode parent = root;
            foreach (string segment in segments[..^1])
            {
                parent = parent is JsonArray array
                    ? array[int.Parse(segment, CultureInfo.InvariantCulture)]!
                    : parent[segment]!;
            }

            parent[segments[^1]] = JsonNode.Parse(json);
        }
    }
}
