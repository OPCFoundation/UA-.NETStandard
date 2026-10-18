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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Samples.Tests
{
    /// <summary>
    /// Launches the real sample executable: no mock carrier, broker, serializer or in-process shortcut.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("SecureChannelPubSubSamples")]
    public sealed class SecureChannelPubSubProcessTests
    {
        /// <summary>
        /// Verifies the default mode uses dedicated confidential flow with no Session or model.
        /// </summary>
        [Test]
        [CancelAfter(90000)]
        public async Task DefaultExecutableUsesRealDedicatedSocketsWithoutSessionsOrModelAsync()
        {
            using var workspace = new OwnedWorkspace();
            string report = Path.Combine(workspace.Root, "default.json");
            string pki = Path.Combine(workspace.Root, "pki");
            var process = SampleProcess.Start(workspace, ["--result", report, "--pki", pki]);
            await using ConfiguredAsyncDisposable processLifetime = process.ConfigureAwait(false);
            await process.AssertSuccessAsync().ConfigureAwait(false);
            using JsonDocument result = await ReadAsync(report).ConfigureAwait(false);
            JsonElement[] peers = [.. result.RootElement.GetProperty("Endpoints").EnumerateArray()];
            AssertPeers(peers, false, false, false, false, 8);
            Assert.That(Directory.Exists(pki), Is.False, "The demo must remove its owned private-key resources.");
        }

        /// <summary>
        /// Exercises each codec, carrier, authorization and publisher role in the self-contained executable.
        /// </summary>
        [Test]
        [Combinatorial]
        [CancelAfter(90000)]
        public async Task OneProcessPreservesExactNetworkMessagesValuesAndLiveRevisionAsync(
            [Values] bool shared,
            [Values] bool json,
            [Values] bool session,
            [Values] bool reverse)
        {
            using var workspace = new OwnedWorkspace();
            string report = Path.Combine(workspace.Root, "demo.json");
            List<string> arguments = ModeArguments(shared, json, session, reverse);
            arguments.AddRange(["--pki", Path.Combine(workspace.Root, "pki"), "--result", report]);
            var process = SampleProcess.Start(workspace, arguments);
            await using ConfiguredAsyncDisposable processLifetime = process.ConfigureAwait(false);
            await process.AssertSuccessAsync().ConfigureAwait(false);
            using JsonDocument result = await ReadAsync(report).ConfigureAwait(false);
            JsonElement[] peers = [.. result.RootElement.GetProperty("Endpoints").EnumerateArray()];
            AssertPeers(peers, shared, json, session, reverse, SampleCount);
        }

        /// <summary>
        /// Proves the identical production path across independent processes and explicitly provisioned PKI.
        /// </summary>
        [Test]
        [Combinatorial]
        [CancelAfter(120000)]
        public async Task TwoProcessesPreserveExactNetworkMessagesValuesAndLiveRevisionAsync(
            [Values] bool shared,
            [Values] bool json,
            [Values] bool session,
            [Values] bool reverse)
        {
            using var workspace = new OwnedWorkspace();
            await SetupAsync(workspace).ConfigureAwait(false);
            string ready = Path.Combine(workspace.Root, "ready.json");
            string serverReport = Path.Combine(workspace.Root, "server.json");
            string clientReport = Path.Combine(workspace.Root, "client.json");
            List<string> common = ModeArguments(shared, json, session, reverse);
            common.AddRange(["--pki", workspace.Pki, "--ready", ready]);
            var server = SampleProcess.Start(workspace, ["server", .. common, "--result", serverReport]);
            await using ConfiguredAsyncDisposable serverLifetime = server.ConfigureAwait(false);
            await server.WaitForReadyAsync(ready).ConfigureAwait(false);
            var client = SampleProcess.Start(workspace, ["client", .. common, "--result", clientReport]);
            await using ConfiguredAsyncDisposable clientLifetime = client.ConfigureAwait(false);
            ProcessResult[] results = await Task.WhenAll(server.CompleteAsync(), client.CompleteAsync())
                .ConfigureAwait(false);
            foreach (ProcessResult result in results)
            {
                await TestContext.Out.WriteLineAsync($"{result.Output}\n{result.Error}").ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(results[0].ExitCode, Is.Zero, $"{results[0].Output}\n{results[0].Error}");
                Assert.That(results[1].ExitCode, Is.Zero, $"{results[1].Output}\n{results[1].Error}");
            });
            using JsonDocument serverResult = await ReadAsync(serverReport).ConfigureAwait(false);
            using JsonDocument clientResult = await ReadAsync(clientReport).ConfigureAwait(false);
            AssertPeers(
                [
                    serverResult.RootElement.GetProperty("Endpoints")[0],
                    clientResult.RootElement.GetProperty("Endpoints")[0]
                ],
                shared, json, session, reverse, SampleCount);
        }

        /// <summary>
        /// Invalid input fails before provisioning or choosing a different transport.
        /// </summary>
        [TestCase("--encoding", "mqtt")]
        [TestCase("--authorization", "anonymous")]
        [TestCase("--count", "3")]
        [TestCase("--count", "1001")]
        [TestCase("--timeout", "0")]
        [TestCase("--port", "65536")]
        public async Task InvalidOptionsFailWithoutFallbackOrPrivateStateAsync(string option, string value)
        {
            using var workspace = new OwnedWorkspace();
            var process = SampleProcess.Start(workspace, [option, value]);
            await using ConfiguredAsyncDisposable processLifetime = process.ConfigureAwait(false);
            ProcessResult result = await process.CompleteAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Error, Does.Contain(option));
                Assert.That(result.Output, Does.Not.Contain("SUCCESS"));
                Assert.That(Directory.Exists(workspace.Pki), Is.False);
            });
        }

        /// <summary>
        /// Persistent process-mode keys require an explicitly supplied secret.
        /// </summary>
        [Test]
        public async Task SetupWithoutSecretFailsWithActionableErrorAsync()
        {
            using var workspace = new OwnedWorkspace();
            var process = SampleProcess.Start(
                workspace, ["setup", "--pki", workspace.Pki], supplySecret: false);
            await using ConfiguredAsyncDisposable processLifetime = process.ConfigureAwait(false);
            ProcessResult result = await process.CompleteAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Error, Does.Contain("OPCUA_SAMPLE_KEY_PASSWORD"));
                Assert.That(Directory.Exists(workspace.Pki), Is.False);
            });
        }

        /// <summary>
        /// Running setup again cannot replace another process's identity or trusted peer.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task SetupRefusesToOverwriteExistingIdentitiesAsync()
        {
            using var workspace = new OwnedWorkspace();
            await SetupAsync(workspace).ConfigureAwait(false);
            string[] keys = Directory.GetFiles(workspace.Pki, "*.pfx", SearchOption.AllDirectories);
            Assert.That(keys, Has.Length.EqualTo(2));
            byte[][] before = await Task.WhenAll(keys.Select(async path =>
                SHA256.HashData(await File.ReadAllBytesAsync(path).ConfigureAwait(false)))).ConfigureAwait(false);
            var process = SampleProcess.Start(workspace, ["setup", "--pki", workspace.Pki]);
            await using ConfiguredAsyncDisposable processLifetime = process.ConfigureAwait(false);
            ProcessResult result = await process.CompleteAsync().ConfigureAwait(false);
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Error, Does.Contain("existing trust is not replaced"));
            for (int index = 0; index < keys.Length; index++)
            {
                byte[] after = await File.ReadAllBytesAsync(keys[index]).ConfigureAwait(false);
                Assert.That(SHA256.HashData(after), Is.EqualTo(before[index]));
            }
        }

        /// <summary>
        /// A bounded waiting server exits nonzero and releases its actual socket even without a peer.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task UnpairedServerDeadlineReleasesItsListenerAsync()
        {
            using var workspace = new OwnedWorkspace();
            await SetupAsync(workspace).ConfigureAwait(false);
            string ready = Path.Combine(workspace.Root, "ready.json");
            var server = SampleProcess.Start(
                workspace, ["server", "--pki", workspace.Pki, "--ready", ready, "--timeout", "5"]);
            await using ConfiguredAsyncDisposable serverLifetime = server.ConfigureAwait(false);
            await server.WaitForReadyAsync(ready).ConfigureAwait(false);
            using JsonDocument readyDocument = await ReadAsync(ready).ConfigureAwait(false);
            int port = new Uri(readyDocument.RootElement.GetProperty("FlowEndpoint").GetString()!).Port;
            ProcessResult result = await server.CompleteAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Error, Does.Contain("deadline"));
                Assert.That(result.Output, Does.Not.Contain("SUCCESS"));
            });
            using var listener = new TcpListener(IPAddress.Loopback, port);
            Assert.That(listener.Start, Throws.Nothing, "The owned real listener must be released after cancellation.");
        }

        /// <summary>
        /// Checks actual peer bytes, typed values, caller route, role direction, revision and ordinary traffic.
        /// </summary>
        private static void AssertPeers(
            JsonElement[] peers,
            bool shared,
            bool json,
            bool session,
            bool reverse,
            int count)
        {
            Assert.That(peers, Has.Length.EqualTo(2));
            JsonElement publisher = peers.Single(peer => peer.GetProperty("Publisher").GetBoolean());
            JsonElement subscriber = peers.Single(peer => !peer.GetProperty("Publisher").GetBoolean());
            JsonElement server = peers.Single(peer => peer.GetProperty("Role").GetString() == "server");
            JsonElement client = peers.Single(peer => peer.GetProperty("Role").GetString() == "client");
            string[] sent = Strings(publisher, "Sent");
            string[] received = Strings(subscriber, "Received");
            Assert.Multiple(() =>
            {
                Assert.That(sent, Has.Length.GreaterThanOrEqualTo(count));
                Assert.That(received, Has.Length.EqualTo(sent.Length));
                Assert.That(Strings(publisher, "Received"), Is.Empty);
                Assert.That(Strings(subscriber, "Sent"), Is.Empty);
                Assert.That(publisher.GetProperty("Role").GetString(), Is.EqualTo(reverse ? "client" : "server"));
                Assert.That(publisher.GetProperty("LocalOpener").GetBoolean(), Is.EqualTo(reverse));
                Assert.That(subscriber.GetProperty("LocalOpener").GetBoolean(), Is.EqualTo(!reverse));
                Assert.That(server.GetProperty("TransportInitiator").GetBoolean(), Is.False);
                Assert.That(client.GetProperty("TransportInitiator").GetBoolean(), Is.True);
                Assert.That(publisher.GetProperty("PublishedRevisions")
                    .EnumerateArray().Select(value => value.GetUInt32()),
                    Is.EqualTo(Enumerable.Repeat(1u, count / 2).Concat(Enumerable.Repeat(2u, count - (count / 2)))));
                Assert.That(subscriber.GetProperty("ReceivedRevisions")
                    .EnumerateArray().Select(value => value.GetUInt32()),
                    Is.EqualTo(publisher.GetProperty("SentRevisions")
                        .EnumerateArray().Select(value => value.GetUInt32())));
                Assert.That(server.GetProperty("ContextId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(server.GetProperty("ContextId").GetGuid(),
                    Is.EqualTo(client.GetProperty("ContextId").GetGuid()));
                Assert.That(server.GetProperty("ChannelId").GetUInt32(),
                    Is.EqualTo(client.GetProperty("ChannelId").GetUInt32()));
                Assert.That(server.GetProperty("ChannelId").GetUInt32(), Is.Not.EqualTo(42u));
                Assert.That(server.GetProperty("ModelEnabled").GetBoolean(), Is.EqualTo(session));
                Assert.That(server.GetProperty("Sessions").GetInt32(), Is.EqualTo(shared || session ? 1 : 0));
                Assert.That(client.GetProperty("Sessions").GetInt32(), Is.EqualTo(shared || session ? 1 : 0));
                Assert.That(client.GetProperty("Calls").GetInt32(), Is.EqualTo(session ? 2 : 0));
                Assert.That(client.GetProperty("SecureChannelId").GetUInt32(), Is.GreaterThan(0u));
                Assert.That(client.GetProperty("SameServiceTransport").GetBoolean(), Is.EqualTo(shared));
                Assert.That(subscriber.GetProperty("Counters").EnumerateArray().Select(value => value.GetInt32()),
                    Is.EqualTo(Enumerable.Range(1, count)));
                Assert.That(subscriber.GetProperty("OtherCounters").EnumerateArray().Select(value => value.GetInt32()),
                    Is.EqualTo(Enumerable.Range(1001, count)));
                Assert.That(subscriber.GetProperty("Toggles").EnumerateArray().Select(value => value.GetBoolean()),
                    Is.EqualTo(Enumerable.Range(1, count).Select(value => (value & 1) == 0)));
                Assert.That(subscriber.GetProperty("Temperatures").EnumerateArray().Select(value => value.GetDouble()),
                    Is.EqualTo(Enumerable.Range(1, count).Select(value => 20.0 + (value * 0.25))));
                Assert.That(subscriber.GetProperty("RejectedReaderDeliveries").GetInt32(), Is.Zero);
            });
            for (int index = 0; index < sent.Length; index++)
            {
                byte[] emitted = Convert.FromBase64String(sent[index]);
                byte[] delivered = Convert.FromBase64String(received[index]);
                Assert.That(delivered, Is.EqualTo(emitted), $"Complete NetworkMessage {index} changed in transit.");
                Assert.That(delivered, Is.Not.Empty);
                if (index > 0)
                {
                    Assert.That(delivered, Is.Not.EqualTo(Convert.FromBase64String(received[index - 1])),
                        "The actual message values must change, not merely repeat packets.");
                }
            }
            foreach (JsonElement peer in peers)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(peer.GetProperty("Revision").GetUInt32(), Is.EqualTo(2u));
                    Assert.That(peer.GetProperty("MaxMessageSize").GetUInt32(), Is.EqualTo(16384u));
                    Assert.That(peer.GetProperty("MaxReceiveBytes").GetUInt32(), Is.EqualTo(131072u));
                    Assert.That(peer.GetProperty("OrderlyEnd").GetBoolean(), Is.True);
                    Assert.That(peer.GetProperty("Protection").GetString(), Is.EqualTo("Confidential"));
                    Assert.That(peer.GetProperty("Direction").GetString(),
                        Is.EqualTo(reverse ? "OpenerToAcceptor" : "AcceptorToOpener"));
                    Assert.That(peer.GetProperty("Profile").GetString(),
                        Is.EqualTo(shared ? DataChannelProfiles.SharedUasc : DataChannelProfiles.DedicatedUasc));
                    Assert.That(peer.GetProperty("Protocol").GetString(),
                        Is.EqualTo(json ? DataChannelProfiles.JsonPayload : DataChannelProfiles.UadpPayload));
                });
            }
            if (shared)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(client.GetProperty("ServiceSecureChannelId").GetUInt32(),
                        Is.EqualTo(client.GetProperty("SecureChannelId").GetUInt32()));
                    Assert.That(client.GetProperty("Reads").GetInt32(), Is.GreaterThanOrEqualTo(1));
                    Assert.That(client.GetProperty("Publishes").GetInt32(), Is.GreaterThanOrEqualTo(2));
                    Assert.That(client.GetProperty("Notifications").GetInt32(), Is.GreaterThanOrEqualTo(2));
                    Assert.That(server.GetProperty("Reads").GetInt32(), Is.GreaterThanOrEqualTo(1));
                    Assert.That(server.GetProperty("Publishes").GetInt32(), Is.GreaterThanOrEqualTo(2));
                });
            }
            else if (!session)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(server.GetProperty("Reads").GetInt32(), Is.Zero);
                    Assert.That(server.GetProperty("Publishes").GetInt32(), Is.Zero);
                    Assert.That(server.GetProperty("Calls").GetInt32(), Is.Zero);
                    Assert.That(client.GetProperty("ServiceSecureChannelId").GetUInt32(), Is.Zero);
                });
            }
        }

        /// <summary>
        /// Gets the captured exact message strings without serialization.
        /// </summary>
        private static string[] Strings(JsonElement endpoint, string property)
        {
            return [.. endpoint.GetProperty(property).EnumerateArray().Select(value => value.GetString()!)];
        }

        /// <summary>
        /// Builds one finite configuration used unchanged by both independently launched peers.
        /// </summary>
        private static List<string> ModeArguments(bool shared, bool json, bool session, bool reverse)
        {
            List<string> arguments =
            [
                "--encoding", json ? "json" : "uadp",
                "--authorization", session ? "session" : "configured",
                "--count", SampleCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ];
            if (shared)
            {
                arguments.Add("--shared");
            }
            if (reverse)
            {
                arguments.Add("--reverse");
            }
            return arguments;
        }

        /// <summary>
        /// Provisions identities through the sample's real application/certificate-manager path.
        /// </summary>
        private static async Task SetupAsync(OwnedWorkspace workspace)
        {
            var setup = SampleProcess.Start(workspace, ["setup", "--pki", workspace.Pki]);
            await using ConfiguredAsyncDisposable setupLifetime = setup.ConfigureAwait(false);
            await setup.AssertSuccessAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the atomically completed evidence file.
        /// </summary>
        private static async Task<JsonDocument> ReadAsync(string path)
        {
            string content = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonDocument.Parse(content);
        }

        /// <summary>
        /// Owns precisely one non-stageable run directory and an ephemeral test secret.
        /// </summary>
        private sealed class OwnedWorkspace : IDisposable
        {
            /// <summary>
            /// Creates unique artifacts under this checkout's Git metadata, never an OS temporary directory.
            /// </summary>
            internal OwnedWorkspace()
            {
                string samplePath = typeof(SecureChannelPubSubProcessTests).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(attribute => attribute.Key == "SecureChannelSampleAssembly").Value!;
                SampleAssembly = Path.GetFullPath(samplePath.Replace('\\', Path.DirectorySeparatorChar));
                var repository = new DirectoryInfo(Path.GetDirectoryName(SampleAssembly)!);
                while (repository != null && !File.Exists(Path.Combine(repository.FullName, "UA.slnx")))
                {
                    repository = repository.Parent;
                }
                if (repository == null || !Directory.Exists(Path.Combine(repository.FullName, ".git")))
                {
                    throw new InvalidOperationException("Process tests require the checkout with its Git metadata.");
                }
                Root = Path.Combine(repository.FullName, ".git", "testagent", "dc-pubsub-sample",
                    "process-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                Pki = Path.Combine(Root, "pki");
                Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }

            /// <summary>
            /// Gets the uniquely owned run directory.
            /// </summary>
            internal string Root { get; }

            /// <summary>
            /// Gets the explicit PKI path shared by the two sample processes.
            /// </summary>
            internal string Pki { get; }

            /// <summary>
            /// Gets the already-built production sample executable.
            /// </summary>
            internal string SampleAssembly { get; }

            /// <summary>
            /// Gets the ephemeral secret supplied only through the child process environment.
            /// </summary>
            internal string Password { get; }

            /// <inheritdoc/>
            public void Dispose()
            {
                Directory.Delete(Root, true);
            }
        }

        /// <summary>
        /// Owns and bounds one real dotnet child process and drains both redirected streams concurrently.
        /// </summary>
        private sealed class SampleProcess : IAsyncDisposable
        {
            /// <summary>
            /// Captures asynchronous output immediately to prevent pipe-buffer deadlocks.
            /// </summary>
            private SampleProcess(Process process)
            {
                m_process = process;
                m_output = process.StandardOutput.ReadToEndAsync();
                m_error = process.StandardError.ReadToEndAsync();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (!m_process.HasExited)
                    {
                        m_process.Kill(true);
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await m_process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                    }
                    await Task.WhenAll(m_output, m_error).ConfigureAwait(false);
                }
                finally
                {
                    m_process.Dispose();
                }
            }

            /// <summary>
            /// Starts the already-built sample without invoking restore, build, a broker or a protocol stub.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal static SampleProcess Start(
                OwnedWorkspace workspace,
                IEnumerable<string> arguments,
                bool supplySecret = true)
            {
                var start = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                    WorkingDirectory = workspace.Root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add(workspace.SampleAssembly);
                foreach (string argument in arguments)
                {
                    start.ArgumentList.Add(argument);
                }
                start.Environment.Remove("OPCUA_SAMPLE_KEY_PASSWORD");
                if (supplySecret)
                {
                    start.Environment["OPCUA_SAMPLE_KEY_PASSWORD"] = workspace.Password;
                }
                Process process = Process.Start(start) ?? throw new InvalidOperationException("Process start failed.");
                return new SampleProcess(process);
            }

            /// <summary>
            /// Checks readiness only after the real standard-server listener reports successful startup.
            /// </summary>
            internal async Task WaitForReadyAsync(string path)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!File.Exists(path))
                {
                    if (m_process.HasExited)
                    {
                        ProcessResult exited = await CompleteAsync().ConfigureAwait(false);
                        Assert.Fail(
                            $"Server exited before readiness: {exited.ExitCode}\n{exited.Output}\n{exited.Error}");
                    }
                    await Task.Delay(25, deadline.Token).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Requires a clean bounded process exit with all ordinary cleanup completed.
            /// </summary>
            internal async Task AssertSuccessAsync()
            {
                ProcessResult result = await CompleteAsync().ConfigureAwait(false);
                Assert.That(result.ExitCode, Is.Zero, $"{result.Output}\n{result.Error}");
                await TestContext.Out.WriteLineAsync(result.Output).ConfigureAwait(false);
            }

            /// <summary>
            /// Awaits this owned PID and retrieves all of its finite output.
            /// </summary>
            internal async Task<ProcessResult> CompleteAsync()
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await m_process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return new ProcessResult(m_process.ExitCode,
                    await m_output.ConfigureAwait(false), await m_error.ConfigureAwait(false));
            }

            private readonly Process m_process;
            private readonly Task<string> m_output;
            private readonly Task<string> m_error;
        }

        /// <summary>
        /// Complete captured output from one owned process.
        /// </summary>
        private sealed record ProcessResult(int ExitCode, string Output, string Error);

        private const int SampleCount = 6;
    }
}
