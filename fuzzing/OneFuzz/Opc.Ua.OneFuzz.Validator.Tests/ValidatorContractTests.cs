/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    [TestFixture]
    [Category("OneFuzzContract")]
    [NonParallelizable]
    public sealed partial class ValidatorContractTests : IDisposable
    {
        [OneTimeSetUp]
        public async Task PublishFixtureAsync()
        {
            m_errorReporting = WindowsErrorReporting.Suppress();
            m_fixture = new PublishedFixture();
            await m_fixture.PublishAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            m_fixture?.Dispose();
            m_errorReporting?.Dispose();
        }

        [Test]
        public async Task DiscoverPublishedLibraryReturnsOnlyExactPublicStaticSpanCallbacksAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            ProcessResult result = await RunAsync(
                drop, "discover", "--assembly", drop.PathFor(ContractDrop.AssemblyDll)).ConfigureAwait(false);

            AssertSuccess(result);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement discovery = document.RootElement;
            Assert.That(discovery.EnumerateObject().Select(static field => field.Name),
                Is.EquivalentTo(s_discoveryFields));
            Assert.That(discovery.GetProperty("assembly").GetString(), Is.EqualTo(ContractDrop.AssemblyDll));
            JsonElement[] callbacks = [.. discovery.GetProperty("callbacks").EnumerateArray()];
            Assert.That(callbacks.Select(static callback =>
                (callback.GetProperty("type").GetString(), callback.GetProperty("method").GetString())),
                Is.EqualTo(new[]
                {
                    (ContractDrop.CallbackType, "Inspect"),
                    (ContractDrop.CallbackType, "Measure"),
                    (ContractDrop.CallbackType, "NegativeControl")
                }));
            foreach (JsonElement callback in callbacks)
            {
                Assert.That(callback.EnumerateObject().Select(static field => field.Name),
                    Is.EquivalentTo(s_callbackFields));
            }

            Assert.That(Directory.Exists(drop.Results), Is.False);
            Assert.That(result.Output, Does.Not.Contain("contract-executed:"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ValidateRelocatedPublishedDropReplaysExactInventoryWithOrWithoutDllSuffixAsync(
            bool omitDllSuffix)
        {
            using var drop = new ContractDrop(m_fixture);
            if (omitDllSuffix)
            {
                drop.Area["assembly"] = Path.GetFileNameWithoutExtension(ContractDrop.AssemblyDll);
            }

            drop.Save();
            Dictionary<string, string> before = drop.Snapshot();

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertCompleted(drop, result, hasConfig: true);
            Assert.That(drop.Snapshot(), Is.EquivalentTo(before), "Validation must not rewrite the relocated drop.");
        }

        [Test]
        public async Task ValidateWithoutOptionalServiceConfigStillRunsAllPublishedCallbacksAsync()
        {
            using var drop = new ContractDrop(m_fixture, withConfig: false);
            drop.Save();

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            AssertCompleted(drop, result, hasConfig: false);
        }

        private Task<ProcessResult> ValidateAsync(ContractDrop drop, params string[] options)
        {
            var arguments = new List<string>
            {
                "validate", "--drop", drop.Root, "--results", drop.Results
            };
            arguments.AddRange(options);
            return RunAsync(drop, [.. arguments]);
        }

        private Task<ProcessResult> RunAsync(ContractDrop drop, params string[] arguments)
        {
            // Working directory is neither the repository nor the published library.
            // Only the copied closure in the drop can satisfy callback dependencies.
            return ProcessRunner.RunAsync(
                ProcessRunner.DotNetHost,
                new[] { m_fixture.ValidatorDll }.Concat(arguments),
                drop.Home,
                TimeSpan.FromSeconds(45));
        }

        private static void AssertSuccess(ProcessResult result)
        {
            Assert.That(result.TimedOut, Is.False, result.Diagnostics);
            Assert.That(result.ExitCode, Is.Zero, result.Diagnostics);
            Assert.That(result.Error, Is.Empty, result.Diagnostics);
        }

        private static void AssertCompleted(
            ContractDrop drop,
            ProcessResult result,
            bool hasConfig,
            string? manifestPath = null,
            string? configPath = null)
        {
            AssertSuccess(result);
            Assert.That(result.Output, Does.Contain("Validated 3 callbacks; zero skips."));
            string[] targetIds = [ContractDrop.InspectId, ContractDrop.MeasureId, ContractDrop.ControlId];
            string[] methods = ["Inspect", "Measure", "NegativeControl"];
            string[] resultFiles = [.. targetIds.SelectMany(static id =>
                new[] { id + ".json", id + ".stdout.log", id + ".stderr.log" }), "validation.json"];
            Assert.That(Directory.EnumerateFiles(drop.Results).Select(Path.GetFileName),
                Is.EquivalentTo(resultFiles));

            using JsonDocument reportDocument = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(drop.Results, "validation.json")));
            JsonElement report = reportDocument.RootElement;
            Assert.That(report.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("skipped").GetInt32(), Is.Zero);
            Assert.That(report.GetProperty("serviceCanary").GetBoolean(), Is.False);
            Assert.That(report.GetProperty("runtime").GetString(), Does.StartWith(".NET 10."));
            Assert.That(report.GetProperty("architecture").GetString(),
                Is.EqualTo(RuntimeInformation.ProcessArchitecture.ToString()));
            Assert.That(report.GetProperty("manifestSha256").GetString(),
                Is.EqualTo(ContractDrop.Hash(File.ReadAllBytes(manifestPath ?? drop.ManifestPath))));
            Assert.That(report.TryGetProperty("configSha256", out JsonElement configHash), Is.EqualTo(hasConfig));
            if (hasConfig)
            {
                Assert.That(configHash.GetString(),
                    Is.EqualTo(ContractDrop.Hash(File.ReadAllBytes(configPath ?? drop.ConfigPath))));
            }

            JsonElement[] targets = [.. report.GetProperty("targets").EnumerateArray()];
            Assert.That(targets.Select(static target => target.GetProperty("id").GetString()),
                Is.EqualTo(targetIds));
            for (int i = 0; i < targetIds.Length; i++)
            {
                string id = targetIds[i];
                Assert.That(targets[i].GetProperty("assembly").GetString(), Is.EqualTo(ContractDrop.AssemblyDll));
                Assert.That(targets[i].GetProperty("type").GetString(), Is.EqualTo(ContractDrop.CallbackType));
                Assert.That(targets[i].GetProperty("method").GetString(), Is.EqualTo(methods[i]));
                Assert.That(targets[i].GetProperty("inputs").GetInt32(), Is.EqualTo(drop.Inputs[id].Count));
                AssertCallbackCompleted(drop, id, methods[i]);
            }

            Dictionary<string, string?> published = report.GetProperty("publishedFiles").EnumerateObject()
                .ToDictionary(static file => file.Name, static file => file.Value.GetString(), StringComparer.Ordinal);
            Assert.That(published.Keys, Is.EquivalentTo(ContractDrop.PublishedFiles));
            foreach (string file in ContractDrop.PublishedFiles)
            {
                Assert.That(published[file], Is.EqualTo(ContractDrop.Hash(File.ReadAllBytes(drop.PathFor(file)))));
            }
        }

        private static void AssertCallbackCompleted(ContractDrop drop, string id, string method)
        {
            KeyValuePair<string, byte[]>[] inputs = [.. drop.Inputs[id].OrderBy(
                static input => input.Key, StringComparer.Ordinal)];
            using JsonDocument receiptDocument = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(drop.Results, id + ".json")));
            JsonElement receipt = receiptDocument.RootElement;
            Assert.That(receipt.GetProperty("target").GetString(), Is.EqualTo(id));
            Assert.That(receipt.GetProperty("type").GetString(), Is.EqualTo(ContractDrop.CallbackType));
            Assert.That(receipt.GetProperty("method").GetString(), Is.EqualTo(method));
            Assert.That(receipt.GetProperty("inputs").EnumerateArray().Select(static input =>
                (input.GetProperty("path").GetString(), input.GetProperty("sha256").GetString())),
                Is.EqualTo(inputs.Select(static input => (input.Key, ContractDrop.Hash(input.Value)))));

            string[] output = File.ReadAllLines(Path.Combine(drop.Results, id + ".stdout.log"));
            Assert.That(output, Is.EqualTo(inputs.SelectMany(input => new[]
            {
                $"{id}: {input.Key}",
                $"contract-executed:{method}:{ContractDrop.Hash(input.Value)}"
            })), "A validator-authored receipt alone does not prove the callback ran.");
            Assert.That(File.ReadAllText(Path.Combine(drop.Results, id + ".stderr.log")), Is.Empty);
        }

        private static readonly string[] s_discoveryFields = ["assembly", "callbacks"];
        private static readonly string[] s_callbackFields = ["type", "method"];
        private WindowsErrorReporting? m_errorReporting;
        private PublishedFixture m_fixture = null!;
    }
}
