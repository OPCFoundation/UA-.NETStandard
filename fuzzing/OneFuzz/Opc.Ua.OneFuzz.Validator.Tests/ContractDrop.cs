/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    internal sealed class ContractDrop : IDisposable
    {
        internal const string AssemblyDll = "Opc.Ua.OneFuzz.TestTarget.dll";
        internal const string DependencyDll = "Opc.Ua.OneFuzz.TestDependency.dll";
        internal const string CallbackType = "Opc.Ua.OneFuzz.TestTarget.ContractCallbacks";
        internal const string AreaId = "synthetic-contract";
        internal const string InspectId = "contract.inspect";
        internal const string MeasureId = "contract.measure";
        internal const string ControlId = "contract.negative-control";
        internal const string FirstBucket = "fuzzing/ContractFixture/Corpus/Inspect";
        internal const string SecondBucket = "fuzzing/ContractFixture/Corpus/RequiredBucket";
        internal const string MeasureBucket = "fuzzing/ContractFixture/Corpus/Measure";
        internal const string ControlSeed = "fuzzing/ContractFixture/Corpus/NegativeControl/safe.bin";
        internal const string FirstDictionary = "fuzzing/Dictionaries/contract-first.dict";
        internal const string SecondDictionary = "fuzzing/Dictionaries/contract-second.dict";
        internal const string MergedDictionary = "fuzzing/OneFuzz/Generated/contract-merged.dict";

        internal static readonly string[] PublishedFiles =
        [
            AssemblyDll,
            "Opc.Ua.OneFuzz.TestTarget.pdb",
            "Opc.Ua.OneFuzz.TestTarget.deps.json",
            "Opc.Ua.OneFuzz.TestTarget.runtimeconfig.json",
            DependencyDll,
            "Opc.Ua.OneFuzz.TestDependency.pdb"
        ];

        internal ContractDrop(PublishedFixture fixture, bool withConfig = true)
        {
            Home = Path.Combine(fixture.Root, "case-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(Home, "relocated drop with spaces");
            Directory.CreateDirectory(Root);
            foreach (string file in Directory.EnumerateFiles(fixture.TargetDirectory))
            {
                File.Copy(file, Path.Combine(Root, Path.GetFileName(file)));
            }

            AddInput(InspectId, FirstBucket + "/Z-first seed.bin", [0, 1, 0xff, 0x7f]);
            AddInput(InspectId, FirstBucket + "/nested/empty.bin", []);
            AddInput(InspectId, SecondBucket + "/required.bin", [0x42, 0, 0x24]);
            AddInput(MeasureId, MeasureBucket + "/first.bin", [0x10, 0x20]);
            AddInput(MeasureId, MeasureBucket + "/same bytes different path.bin", [0x10, 0x20]);
            AddInput(ControlId, ControlSeed, "ordinary synthetic seed"u8.ToArray());
            WriteText(FirstDictionary, "# first source\n\"alpha\"\n\"shared\"\n");
            WriteText(SecondDictionary, "\"beta\"\n\"shared\"\n");
            WriteText(MergedDictionary,
                "# union, order and comments are insignificant\n\"shared\"\n\"beta\"\n\"alpha\"\n");

            Manifest = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["areas"] = new JsonArray(new JsonObject
                {
                    ["id"] = AreaId,
                    ["project"] = "fuzzing/OneFuzz/Opc.Ua.OneFuzz.TestTarget/Opc.Ua.OneFuzz.TestTarget.csproj",
                    ["assembly"] = AssemblyDll,
                    ["type"] = CallbackType,
                    ["targets"] = new JsonArray(
                        CreateTarget(InspectId, "Inspect", [FirstBucket, SecondBucket],
                            [FirstDictionary, SecondDictionary]),
                        CreateTarget(MeasureId, "Measure", [MeasureBucket], []),
                        CreateTarget(ControlId, "NegativeControl", [ControlSeed], []))
                })
            };
            if (withConfig)
            {
                Config = new JsonObject
                {
                    ["ConfigVersion"] = 3,
                    [nameof(Entries)] = new JsonArray(
                        CreateEntry(InspectId, "Inspect", [FirstDictionary, SecondDictionary, MergedDictionary]),
                        CreateEntry(MeasureId, "Measure", []),
                        CreateEntry(ControlId, "NegativeControl", []))
                };
            }
        }

        internal string Home { get; }
        internal string Root { get; }
        internal string Results => Path.Combine(Home, "new results");
        internal string ManifestPath => Path.Combine(Root, "fuzz-targets.json");
        internal string ConfigPath => Path.Combine(Root, "OneFuzzConfig.json");
        internal JsonObject Manifest { get; }
        internal JsonObject? Config { get; }
        internal JsonObject Area => Manifest["areas"]![0]!.AsObject();
        internal JsonArray Targets => Area["targets"]!.AsArray();
        internal JsonArray Entries => Config![nameof(Entries)]!.AsArray();
        internal Dictionary<string, Dictionary<string, byte[]>> Inputs { get; } = new(StringComparer.Ordinal);

        internal string PathFor(string relative)
        {
            return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        internal void Save()
        {
            WriteText("fuzz-targets.json", Manifest.ToJsonString(s_jsonOptions));
            if (Config is not null)
            {
                WriteText("OneFuzzConfig.json", Config.ToJsonString(s_jsonOptions));
            }
        }

        internal void AddInput(string target, string relative, byte[] bytes)
        {
            WriteBytes(relative, bytes);
            if (!Inputs.TryGetValue(target, out Dictionary<string, byte[]>? inputs))
            {
                inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                Inputs.Add(target, inputs);
            }

            inputs[relative] = bytes;
        }

        internal void WriteText(string relative, string text)
        {
            WriteBytes(relative, Encoding.UTF8.GetBytes(text));
        }

        internal void WriteBytes(string relative, byte[] bytes)
        {
            string path = PathFor(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        internal Dictionary<string, string> Snapshot()
        {
            return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(Root, path).Replace('\\', '/'),
                    static path => Hash(File.ReadAllBytes(path)),
                    StringComparer.Ordinal);
        }

        internal static string Hash(byte[] bytes)
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        internal static JsonArray Strings(IEnumerable<string> values)
        {
            return new JsonArray([.. values.Select(static value => JsonValue.Create(value))]);
        }

        public void Dispose()
        {
            if (Directory.Exists(Home))
            {
                Directory.Delete(Home, recursive: true);
            }
        }

        private static JsonObject CreateTarget(
            string id,
            string method,
            string[] corpus,
            string[] dictionaries)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["method"] = method,
                ["corpus"] = Strings(corpus),
                ["dictionaries"] = Strings(dictionaries),
                ["classification"] = "continuous",
                ["aflMethods"] = new JsonArray(),
                ["adapter"] = "synthetic-contract-fixture-only",
                ["oracle"] = "callback marker and independent SHA256 receipt",
                ["forkRequirements"] = new JsonArray()
            };
        }

        private JsonObject CreateEntry(string id, string method, string[] dictionaries)
        {
            var job = new JsonObject
            {
                ["ProjectName"] = "synthetic-onefuzz-contract-tests",
                ["TargetName"] = "synthetic-" + id,
                ["SeedCorpusContainer"] = "synthetic-" + id.Replace('.', '-')
            };
            if (dictionaries.Length != 0)
            {
                job["FuzzingTargetOptions"] = Strings(["-dict={setup_dir}/" + MergedDictionary]);
            }

            return new JsonObject
            {
                ["Fuzzer"] = new JsonObject
                {
                    ["$type"] = "libfuzzerDotNet",
                    ["Dll"] = AssemblyDll,
                    ["Class"] = CallbackType,
                    ["Method"] = method
                },
                ["Skip"] = false,
                ["MinAvailableMemoryMB"] = 100,
                ["FuzzerTimeoutInSeconds"] = 120,
                ["OneFuzzJobs"] = new JsonArray(job),
                ["JobDependencies"] = Strings(PublishedFiles.Concat(Inputs[id].Keys).Concat(dictionaries)),
                ["JobNotificationEmail"] = "onefuzz@contract-tests.invalid",
                ["SdlWorkItemId"] = 12345,
                ["AdoTemplate"] = new JsonObject
                {
                    ["Org"] = "synthetic-contract-tests",
                    ["Project"] = "synthetic-fixture-project",
                    ["AssignedTo"] = "owner@contract-tests.invalid",
                    ["AreaPath"] = "Synthetic\\ContractTests",
                    ["IterationPath"] = "Synthetic\\ContractTests",
                    ["Type"] = "Bug",
                    ["AdoFields"] = new JsonObject { ["System.Title"] = "Synthetic contract test, not a service job" },
                    ["UniqueFields"] = Strings(["System.Title"])
                },
                ["CodeCoverage"] = new JsonObject
                {
                    ["Org"] = "synthetic-contract-tests",
                    ["Project"] = "synthetic-fixture-project",
                    ["PipelineId"] = "67890"
                }
            };
        }

        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    }
}
