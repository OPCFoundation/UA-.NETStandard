/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Connector;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.Tools.Tests.XRegistryConnector
{
    [TestFixture]
    [Category("XRegistryConnector")]
    [NonParallelizable]
    public sealed class XRegistryConnectorHostTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ReadinessDoesNotAdvertiseMutationCapabilityWhenTheBackendIsUnavailable(
            bool ready, bool atomicWrites)
        {
            using JsonDocument document = JsonDocument.Parse(XRegistryConnectorOutput.Readiness(ready, atomicWrites));
            Assert.That(document.RootElement.GetProperty("ready").GetBoolean(), Is.EqualTo(ready));
            if (ready)
            {
                Assert.That(document.RootElement.GetProperty("atomicWrites").GetBoolean(), Is.EqualTo(atomicWrites));
            }
            else
            {
                Assert.That(document.RootElement.TryGetProperty("atomicWrites", out _), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConflictsOnPristineStorageReturnsEmptyJsonWithoutCreatingFilesAsync(bool existingDirectory)
        {
            using var state = new TemporaryStateDirectory();
            if (existingDirectory)
            {
                _ = Directory.CreateDirectory(state.RootPath);
            }
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Conflicts,
                StateDirectory = state.RootPath,
                JobId = k_job
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(console.Error, Is.Empty);
            using JsonDocument output = ParseSingleOutputLine(console);
            Assert.That(output.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("conflicts").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(output.RootElement.GetProperty("conflicts").GetArrayLength(), Is.Zero);
            Assert.That(Directory.Exists(state.JobPath), Is.False);
            Assert.That(Directory.Exists(state.RootPath), Is.EqualTo(existingDirectory));
            if (existingDirectory)
            {
                Assert.That(Directory.EnumerateFileSystemEntries(state.RootPath), Is.Empty);
            }
        }

        [Test]
        public async Task ConflictsReadsActiveEvidenceWithoutChangingAnyStateFilesAsync()
        {
            using var state = new TemporaryStateDirectory();
            await CommitStateAsync(state.JobPath, OfflineStateFixture()).ConfigureAwait(false);
            Dictionary<string, ByteString> before = await ReadFilesAsync(state.RootPath).ConfigureAwait(false);
            Assert.That(before, Is.Not.Empty);
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Conflicts,
                StateDirectory = state.RootPath,
                JobId = k_job
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(console.Error, Is.Empty);
            using JsonDocument output = ParseSingleOutputLine(console);
            Assert.That(output.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
            JsonElement conflicts = output.RootElement.GetProperty("conflicts");
            Assert.That(conflicts.GetArrayLength(), Is.EqualTo(2));
            Assert.That(conflicts[0].EnumerateObject().Count(), Is.EqualTo(6));
            Assert.That(conflicts[0].GetProperty("id").GetString(), Is.EqualTo("conflict-a"));
            Assert.That(conflicts[0].GetProperty("path").GetString(), Is.EqualTo("/groups/a"));
            Assert.That(conflicts[0].GetProperty("reason").GetString(), Is.EqualTo("simultaneous_change"));
            Assert.That(conflicts[0].GetProperty("status").GetString(), Is.EqualTo("Active"));
            Assert.That(conflicts[0].GetProperty("resolution").GetString(), Is.EqualTo("Manual"));
            Assert.That(conflicts[0].GetProperty("operationid").GetString(), Is.EqualTo("operation-a"));
            Assert.That(conflicts[1].GetProperty("id").GetString(), Is.EqualTo("conflict-b"));
            Assert.That(conflicts[1].GetProperty("path").GetString(), Is.EqualTo("/groups/b"));
            Assert.That(conflicts[1].GetProperty("reason").GetString(), Is.EqualTo("second_change"));
            Assert.That(conflicts[1].GetProperty("operationid").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Dictionary<string, ByteString> after = await ReadFilesAsync(state.RootPath).ConfigureAwait(false);
            Assert.That(after, Is.EqualTo(before));
        }

        [TestCase("prefer-opcua", "PreferOpcUa", 1)]
        [TestCase("prefer-http", "PreferHttp", 2)]
        public async Task ResolvePersistsOnlyTheGuardedOfflineDecisionAndRendersConflictAsync(
            string resolution,
            string outputResolution,
            int storedResolution)
        {
            using var state = new TemporaryStateDirectory();
            await CommitStateAsync(state.JobPath, OfflineStateFixture()).ConfigureAwait(false);
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Resolve,
                StateDirectory = state.RootPath,
                JobId = k_job,
                ConflictId = "conflict-a",
                Resolution = resolution
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(console.Error, Is.Empty);
            using JsonDocument output = ParseSingleOutputLine(console);
            JsonElement conflicts = output.RootElement.GetProperty("conflicts");
            Assert.That(conflicts.GetArrayLength(), Is.EqualTo(1));
            Assert.That(conflicts[0].EnumerateObject().Count(), Is.EqualTo(6));
            Assert.That(conflicts[0].GetProperty("id").GetString(), Is.EqualTo("conflict-a"));
            Assert.That(conflicts[0].GetProperty("path").GetString(), Is.EqualTo("/groups/a"));
            Assert.That(conflicts[0].GetProperty("reason").GetString(), Is.EqualTo("simultaneous_change"));
            Assert.That(conflicts[0].GetProperty("status").GetString(), Is.EqualTo("ResolutionRequested"));
            Assert.That(conflicts[0].GetProperty("resolution").GetString(), Is.EqualTo(outputResolution));
            Assert.That(conflicts[0].GetProperty("operationid").GetString(), Is.EqualTo("operation-a"));
            ByteString stored = await ReadStateAsync(state.JobPath).ConfigureAwait(false);
            using JsonDocument saved = JsonDocument.Parse(stored.Memory);
            JsonElement payload = saved.RootElement.GetProperty("payload");
            Assert.That(payload.GetProperty("generation").GetInt64(), Is.EqualTo(8));
            Assert.That(payload.GetProperty("sequence").GetInt64(), Is.EqualTo(3));
            Assert.That(payload.GetProperty("baselines").GetArrayLength(), Is.Zero);
            Assert.That(payload.GetProperty("intents").GetArrayLength(), Is.Zero);
            Assert.That(payload.GetProperty("tombstones").GetArrayLength(), Is.Zero);
            JsonElement evidence = payload.GetProperty("conflicts");
            Assert.That(evidence.GetArrayLength(), Is.EqualTo(3));
            Assert.That(evidence[0].GetProperty("id").GetString(), Is.EqualTo("conflict-a"));
            Assert.That(evidence[0].GetProperty("status").GetInt32(), Is.EqualTo(1));
            Assert.That(evidence[0].GetProperty("resolution").GetInt32(), Is.EqualTo(storedResolution));
            Assert.That(evidence[0].GetProperty("observedAt").GetString(),
                Is.EqualTo("2024-01-02T03:04:05.0000000+00:00"));
            Assert.That(evidence[0].GetProperty("resolutionRequestedAt").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(evidence[1].GetProperty("id").GetString(), Is.EqualTo("conflict-b"));
            Assert.That(evidence[1].GetProperty("status").GetInt32(), Is.Zero);
            Assert.That(evidence[1].GetProperty("resolution").GetInt32(), Is.Zero);
            Assert.That(evidence[2].GetProperty("id").GetString(), Is.EqualTo("conflict-retired"));
            Assert.That(evidence[2].GetProperty("status").GetInt32(), Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResolveMissingConflictFailsWithoutCreatingStateArtifactsAsync(bool existingDirectory)
        {
            using var state = new TemporaryStateDirectory();
            if (existingDirectory)
            {
                _ = Directory.CreateDirectory(state.RootPath);
            }
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Resolve,
                StateDirectory = state.RootPath,
                JobId = k_job,
                ConflictId = "unknown-conflict",
                Resolution = "prefer-http"
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(console.Output, Is.Empty);
            Assert.That(console.Error, Does.Contain("The conflict does not exist"));
            Assert.That(Directory.Exists(state.JobPath), Is.False);
            Assert.That(Directory.Exists(state.RootPath), Is.EqualTo(existingDirectory));
            if (existingDirectory)
            {
                Assert.That(Directory.EnumerateFileSystemEntries(state.RootPath), Is.Empty);
            }
        }

        [TestCase(XRegistryConnectorCommand.HttpGateway, "--opcua")]
        [TestCase(XRegistryConnectorCommand.OpcUaGateway, "--http-root")]
        [TestCase(XRegistryConnectorCommand.Sync, "--opcua")]
        [TestCase(XRegistryConnectorCommand.Inspect, "Inspect requires")]
        [TestCase(XRegistryConnectorCommand.Conflicts, "--state")]
        [TestCase(XRegistryConnectorCommand.Resolve, "--conflict")]
        public async Task PublicRunValidatesEveryModeBeforeRuntimeSetupOrStateCreationAsync(
            XRegistryConnectorCommand command,
            string diagnostic)
        {
            using var state = new TemporaryStateDirectory();
            var settings = new XRegistryConnectorSettings
            {
                Command = command,
                StateDirectory = command == XRegistryConnectorCommand.Conflicts ? null : state.RootPath,
                JobId = k_job
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(console.Output, Is.Empty);
            Assert.That(console.Error, Does.Contain(diagnostic));
            Assert.That(Directory.Exists(state.RootPath), Is.False);
        }

        [Test]
        public async Task PublicRunRejectsNullSettingsWithoutWritingOutputAsync()
        {
            using var console = new ConsoleCapture();

            await Assert.ThatAsync(() => XRegistryConnectorHost.RunAsync(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("settings")).ConfigureAwait(false);

            Assert.That(console.Output, Is.Empty);
            Assert.That(console.Error, Is.Empty);
        }

        [TestCase(XRegistryConnectorCommand.Conflicts)]
        [TestCase(XRegistryConnectorCommand.Resolve)]
        public async Task PublicRunCancellationCreatesNoOfflineStateOrSuccessOutputAsync(
            XRegistryConnectorCommand command)
        {
            using var state = new TemporaryStateDirectory();
            var settings = new XRegistryConnectorSettings
            {
                Command = command,
                StateDirectory = state.RootPath,
                JobId = k_job,
                ConflictId = "conflict-a",
                Resolution = "prefer-http"
            };
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings, cancellation.Token).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero);
            Assert.That(console.Output, Is.Empty);
            Assert.That(console.Error, Is.Empty);
            Assert.That(Directory.Exists(state.RootPath), Is.False);
        }

        [Test]
        public async Task CorruptOfflineStateIsAnExplicitFailureNotAnEmptyConflictListAsync()
        {
            using var state = new TemporaryStateDirectory();
            await CommitStateAsync(state.JobPath, ByteString.From("not-json"u8)).ConfigureAwait(false);
            Dictionary<string, ByteString> before = await ReadFilesAsync(state.RootPath).ConfigureAwait(false);
            var settings = new XRegistryConnectorSettings
            {
                Command = XRegistryConnectorCommand.Conflicts,
                StateDirectory = state.RootPath,
                JobId = k_job
            };
            using var console = new ConsoleCapture();

            int exitCode = await XRegistryConnectorHost.RunAsync(settings).ConfigureAwait(false);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(console.Output, Is.Empty);
            Assert.That(console.Error, Does.Contain("Synchronization state is malformed"));
            Dictionary<string, ByteString> after = await ReadFilesAsync(state.RootPath).ConfigureAwait(false);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public async Task ReadyOutputHasExactJsonLineShapeAndEscapesTextAsync()
        {
            using var console = new ConsoleCapture();

            await XRegistryConnectorOutput.ReadyAsync("http-gateway", "https://registry.example/\"quoted\"/\u00e9")
                .ConfigureAwait(false);

            using JsonDocument output = ParseSingleOutputLine(console);
            Assert.That(output.RootElement.EnumerateObject().Count(), Is.EqualTo(3));
            Assert.That(output.RootElement.GetProperty("status").GetString(), Is.EqualTo("ready"));
            Assert.That(output.RootElement.GetProperty("mode").GetString(), Is.EqualTo("http-gateway"));
            Assert.That(output.RootElement.GetProperty("address").GetString(),
                Is.EqualTo("https://registry.example/\"quoted\"/\u00e9"));
            Assert.That(console.Error, Is.Empty);
        }

        [Test]
        public async Task DescriptionOutputWrapsTypedSnapshotAndGuaranteesInOneJsonLineAsync()
        {
            XRegistryEndpointDescription description;
            using (JsonDocument model = JsonDocument.Parse("""{"groups":{"devices":{}}}"""))
            using (JsonDocument capabilities = JsonDocument.Parse("""{"flags":["filter"],"epoch":4294967296}"""))
            {
                description = new XRegistryEndpointDescription("registry-a")
                {
                    Profile = "qualified-fixture",
                    Model = model.RootElement,
                    Capabilities = capabilities.RootElement,
                    SupportsAtomicMutations = true,
                    SupportsConditionalMutations = false,
                    SupportsWriteTouch = true,
                    SupportsOperationReplay = false
                };
            }
            using var console = new ConsoleCapture();

            await XRegistryConnectorOutput.DescriptionAsync("opcua", description).ConfigureAwait(false);

            using JsonDocument output = ParseSingleOutputLine(console);
            Assert.That(output.RootElement.EnumerateObject().Count(), Is.EqualTo(2));
            Assert.That(output.RootElement.GetProperty("side").GetString(), Is.EqualTo("opcua"));
            JsonElement encoded = output.RootElement.GetProperty("description");
            Assert.That(encoded.GetProperty("format").GetInt32(), Is.EqualTo(1));
            Assert.That(encoded.GetProperty("registryId").GetString(), Is.EqualTo("registry-a"));
            Assert.That(encoded.GetProperty("profile").GetString(), Is.EqualTo("qualified-fixture"));
            Assert.That(encoded.GetProperty("model").GetProperty("groups").GetProperty("devices").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
            Assert.That(encoded.GetProperty("capabilities").GetProperty("flags")[0].GetString(), Is.EqualTo("filter"));
            Assert.That(encoded.GetProperty("capabilities").GetProperty("epoch").GetRawText(),
                Is.EqualTo("4294967296"));
            Assert.That(encoded.GetProperty("atomicMutations").GetBoolean(), Is.True);
            Assert.That(encoded.GetProperty("conditionalMutations").GetBoolean(), Is.False);
            Assert.That(encoded.GetProperty("writeTouch").GetBoolean(), Is.True);
            Assert.That(encoded.GetProperty("operationReplay").GetBoolean(), Is.False);
            Assert.That(console.Error, Is.Empty);
        }

        [Test]
        public async Task EmptyConflictOutputIsAnObjectWithAnArrayNotABareArrayAsync()
        {
            using var console = new ConsoleCapture();

            await XRegistryConnectorOutput.ConflictsAsync([]).ConfigureAwait(false);

            using JsonDocument output = ParseSingleOutputLine(console);
            Assert.That(output.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(output.RootElement.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(output.RootElement.GetProperty("conflicts").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(output.RootElement.GetProperty("conflicts").GetArrayLength(), Is.Zero);
            Assert.That(console.Error, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReportOutputRendersRealStateFailureAndRecordsWithoutContactingEndpointsAsync(bool dryRun)
        {
            var state = new MemoryXRegistrySyncStateStore();
            await using var stateLifetime = state.ConfigureAwait(false);
            IXRegistrySyncStateSession session = await state.OpenAsync().ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                await session.CommitAsync(ByteString.From("not-json"u8)).ConfigureAwait(false);
            }
            var native = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            var http = new Mock<IXRegistryEndpoint>(MockBehavior.Strict);
            ITelemetryContext telemetry =
                Mock.Of<ITelemetryContext>(context => context.LoggerFactory == NullLoggerFactory.Instance);
            var synchronizer = new XRegistrySynchronizer(native.Object, http.Object, state,
                new XRegistrySyncOptions(k_job, "opcua-fixture", "http-fixture"), telemetry);
            XRegistrySyncReport report = await synchronizer.RunOnceAsync(dryRun).ConfigureAwait(false);
            using var console = new ConsoleCapture();

            await XRegistryConnectorOutput.ReportAsync(report).ConfigureAwait(false);

            using JsonDocument output = ParseSingleOutputLine(console);
            JsonElement root = output.RootElement;
            Assert.That(root.EnumerateObject().Count(), Is.EqualTo(13));
            Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("Failed"));
            Assert.That(root.GetProperty("exitcode").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("dryrun").GetBoolean(), Is.EqualTo(dryRun));
            Assert.That(root.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("observed").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("converged").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("applied").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("deleted").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("planned").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("conflicts").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("pending").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("failures").GetInt32(), Is.EqualTo(1));
            JsonElement records = root.GetProperty("records");
            Assert.That(records.GetArrayLength(), Is.EqualTo(1));
            Assert.That(records[0].EnumerateObject().Count(), Is.EqualTo(5));
            Assert.That(records[0].GetProperty("path").GetString(), Is.EqualTo("/"));
            Assert.That(records[0].GetProperty("kind").GetString(), Is.EqualTo("Failure"));
            Assert.That(records[0].GetProperty("detail").GetString(),
                Is.EqualTo("Synchronization state is malformed; explicit recovery is required."));
            Assert.That(records[0].GetProperty("operationid").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(records[0].GetProperty("conflictid").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(console.Error, Is.Empty);
            native.VerifyNoOtherCalls();
            http.VerifyNoOtherCalls();
        }

        private static async Task CommitStateAsync(string directory, ByteString state)
        {
            var store = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, directory);
            await using var storeLifetime = store.ConfigureAwait(false);
            IXRegistrySyncStateSession session = await store.OpenAsync().ConfigureAwait(false);
            await using var sessionLifetime = session.ConfigureAwait(false);
            _ = await session.ReadAsync().ConfigureAwait(false);
            await session.CommitAsync(state).ConfigureAwait(false);
        }

        private static async Task<ByteString> ReadStateAsync(string directory)
        {
            var store = new FileXRegistrySyncStateStore(LocalFileSystem.Instance, directory);
            await using var storeLifetime = store.ConfigureAwait(false);
            IXRegistrySyncStateSession session = await store.OpenAsync(readOnly: true).ConfigureAwait(false);
            await using var sessionLifetime = session.ConfigureAwait(false);
            return await session.ReadAsync().ConfigureAwait(false);
        }

        private static async Task<Dictionary<string, ByteString>> ReadFilesAsync(string directory)
        {
            var files = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                files.Add(Path.GetRelativePath(directory, file),
                    ByteString.From(await File.ReadAllBytesAsync(file).ConfigureAwait(false)));
            }
            return files;
        }

        private static ByteString OfflineStateFixture()
        {
            // Properties are in ordinal order, independent of the internal state codec's canonicalizer.
            JsonNode payload = JsonNode.Parse(k_statePayload)!;
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var envelope = new JsonObject { ["format"] = 1, ["sha256"] = checksum, ["payload"] = payload };
            return ByteString.From(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        }

        private static JsonDocument ParseSingleOutputLine(ConsoleCapture console)
        {
            Assert.That(console.Output, Does.EndWith(Environment.NewLine));
            Assert.That(console.Output.Count(character => character == '\n'), Is.EqualTo(1));
            return JsonDocument.Parse(console.Output);
        }

        private sealed class TemporaryStateDirectory : IDisposable
        {
            public TemporaryStateDirectory()
            {
                RootPath = Path.Combine(Path.GetTempPath(), "xregistry-host-" + Guid.NewGuid().ToString("N"));
            }

            public string RootPath { get; }

            public string JobPath => Path.Combine(RootPath, k_job);

            public void Dispose()
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
        }

        private sealed class ConsoleCapture : IDisposable
        {
            public ConsoleCapture()
            {
                m_originalOutput = Console.Out;
                m_originalError = Console.Error;
                Console.SetOut(m_output);
                Console.SetError(m_error);
            }

            public string Output => m_output.ToString();

            public string Error => m_error.ToString();

            public void Dispose()
            {
                Console.SetOut(m_originalOutput);
                Console.SetError(m_originalError);
                m_output.Dispose();
                m_error.Dispose();
            }

            private readonly TextWriter m_originalOutput;
            private readonly TextWriter m_originalError;
            private readonly StringWriter m_output = new(CultureInfo.InvariantCulture);
            private readonly StringWriter m_error = new(CultureInfo.InvariantCulture);
        }

        private const string k_job = "offline-job";
        private const string k_statePayload = """
            {
              "baselines": [],
              "configuration": "0000000000000000000000000000000000000000000000000000000000000000",
              "conflicts": [
                {
                  "baseline": null,
                  "http": null,
                  "id": "conflict-a",
                  "observedAt": "2024-01-02T03:04:05.0000000+00:00",
                  "opcUa": null,
                  "operationId": "operation-a",
                  "path": "/groups/a",
                  "reason": "simultaneous_change",
                  "resolution": 0,
                  "resolutionRequestedAt": null,
                  "status": 0
                },
                {
                  "baseline": null,
                  "http": null,
                  "id": "conflict-b",
                  "observedAt": "2024-01-02T03:04:05.0000000+00:00",
                  "opcUa": null,
                  "operationId": null,
                  "path": "/groups/b",
                  "reason": "second_change",
                  "resolution": 0,
                  "resolutionRequestedAt": null,
                  "status": 0
                },
                {
                  "baseline": null,
                  "http": null,
                  "id": "conflict-retired",
                  "observedAt": "2024-01-02T03:04:05.0000000+00:00",
                  "opcUa": null,
                  "operationId": null,
                  "path": "/groups/retired",
                  "reason": "already_resolved",
                  "resolution": 0,
                  "resolutionRequestedAt": null,
                  "status": 2
                }
              ],
              "generation": 7,
              "intents": [],
              "job": "offline-job",
              "scope": "",
              "sequence": 3,
              "tombstones": []
            }
            """;
    }
}
#endif
