/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.DataChannel.Samples.Tests
{
    /// <summary>
    /// Exercises the shipped executable rather than a substitute transport or direct manager.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class StreamingProcessTests
    {
        /// <summary>
        /// Requires exact ordered payload validation and complete closure over a real dedicated socket.
        /// </summary>
        [TestCase("dedicated", "DedicatedUasc", "server")]
        [TestCase("dedicated", "DedicatedUasc", "client")]
        [TestCase("shared", "SharedUasc", "server")]
        [TestCase("shared", "SharedUasc", "client")]
        public async Task ConfiguredStreamingDeliversExactBytesAndReleasesSockets(
            string carrier, string expectedCarrier, string publisher)
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync(
                "ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--publisher", publisher, "--count", "8", "--size", "64", "--revise"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Carrier").GetString(), Is.EqualTo(expectedCarrier));
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(8));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo(512));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("SourceSha256").GetString(),
                    Is.EqualTo(result.GetProperty("ReceivedSha256").GetString()));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo("5224D6393BC74F5C498041A1268C5835FC69AFE3E1CDB0D31E30373560610906"));
                Assert.That(result.GetProperty("Sessions").GetInt32(), Is.EqualTo(carrier == "shared" ? 1 : 0));
                Assert.That(result.GetProperty("ModelEnabled").GetBoolean(), Is.False);
                Assert.That(result.GetProperty("ContextId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(result.GetProperty("PublisherIsTransportInitiator").GetBoolean(),
                    Is.EqualTo(publisher == "client"));
                Assert.That(result.GetProperty("SourceIsOpener").GetBoolean(), Is.EqualTo(publisher == "client"));
                Assert.That(result.GetProperty("SameServiceTransport").GetBoolean(), Is.EqualTo(carrier == "shared"));
                Assert.That(result.GetProperty("SecureChannelId").GetUInt32(), Is.Not.Zero);
                Assert.That(result.GetProperty("ServiceSecureChannelId").GetUInt32(),
                    Is.EqualTo(carrier == "shared" ? result.GetProperty("SecureChannelId").GetUInt32() : 0));
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
            using var listener = new TcpListener(IPAddress.Loopback, result.GetProperty("Port").GetInt32());
            listener.Start();
        }

        /// <summary>
        /// Requires the actual optional model and generated normal Calls in addition to data-path readiness/revision.
        /// </summary>
        [TestCase("dedicated")]
        [TestCase("shared")]
        public async Task SessionStreamingUsesGeneratedControlAndCommitsRevision(string carrier)
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--authorization", "session", "--count", "8", "--size", "64", "--revise"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Authorization").GetString(), Is.EqualTo("Session"));
                Assert.That(result.GetProperty("Sessions").GetInt32(), Is.EqualTo(1));
                Assert.That(result.GetProperty("ModelEnabled").GetBoolean(), Is.True);
                Assert.That(result.GetProperty("Calls").GetInt32(), Is.GreaterThanOrEqualTo(2));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(8));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo("5224D6393BC74F5C498041A1268C5835FC69AFE3E1CDB0D31E30373560610906"));
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }

        /// <summary>
        /// Protects both accepted message-size boundaries and a single-message run without a revision.
        /// </summary>
        [TestCase(1, 4, "DF3F619804A92FDB4057192DC43DD748EA778ADC52BC498CE80524C014B81119")]
        [TestCase(2, 32768, "3C252502A4AA19142D375A1F66DA222D29DE3C5A48E4D6FEB047842A6B37E582")]
        public async Task CompleteMessagesPreserveMinimumAndMaximumAcceptedPayloads(int count, int size, string digest)
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--count", count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--size", size.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(count));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo((long)count * size));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(1));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(), Is.EqualTo(digest));
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
            });
        }

        /// <summary>
        /// Requires actual competing decoded traffic during the measured interval, not just an open Session.
        /// </summary>
        [Test]
        public async Task SharedBenchmarkUsesOneSecureChannelForFlowReadAndPublish()
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", "shared", "--mode", "benchmark", "--count", "80", "--size", "64",
                    "--interval-ms", "20", "--monitored-items", "3", "--repeat", "1"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            var results = new List<JsonElement>();
            foreach (string line in process.Output.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("BENCHMARK ", StringComparison.Ordinal))
                {
                    using JsonDocument document = JsonDocument.Parse(
                        line[line.IndexOf('{', StringComparison.Ordinal)..]);
                    results.Add(document.RootElement.Clone());
                }
            }
            Assert.That(results, Has.Count.EqualTo(2), process.Output);
            Assert.Multiple(() =>
            {
                Assert.That(results[0].GetProperty("Publishes").GetInt32(), Is.Zero);
                Assert.That(results[0].GetProperty("ReadResponsesDuringStreaming").GetInt32(), Is.Zero);
                Assert.That(results[1].GetProperty("ContextId").GetGuid(),
                    Is.Not.EqualTo(results[0].GetProperty("ContextId").GetGuid()));
                Assert.That(results[1].GetProperty("ReadResponsesDuringStreaming").GetInt32(), Is.GreaterThan(0));
                Assert.That(results[1].GetProperty("PublishResponsesDuringStreaming").GetInt32(), Is.GreaterThan(0));
                Assert.That(results[1].GetProperty("NotificationsDuringStreaming").GetInt32(), Is.GreaterThan(0));
                Assert.That(results[1].GetProperty("SameServiceTransport").GetBoolean(), Is.True);
                Assert.That(results[1].GetProperty("ServiceSecureChannelId").GetUInt32(),
                    Is.EqualTo(results[1].GetProperty("SecureChannelId").GetUInt32()).And.Not.Zero);
                Assert.That(results[1].GetProperty("Messages").GetInt32(), Is.EqualTo(80));
                Assert.That(results[1].GetProperty("Bytes").GetInt64(), Is.EqualTo(5120));
                Assert.That(results[1].GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo("5A9EDF8CC288553BB8ED02BEA52320EE848B4834F86FC54756FE7A5CD3C2B081"));
                Assert.That(results[1].GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }
    }

    /// <summary>
    /// Owns one bounded child process and only its fresh repository-local output directory.
    /// </summary>
    internal sealed class SampleProcessRun : IDisposable
    {
        /// <summary>
        /// Allocates no shared temporary or PKI location.
        /// </summary>
        internal SampleProcessRun()
        {
            Repository = FindRepository();
            DirectoryPath = Path.Combine(
                Repository, "tests", "Opc.Ua.DataChannel.Samples.Tests", "TestResults", "process",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            StateDirectory = Path.Combine(DirectoryPath, "pki");
        }

        /// <summary>
        /// Gets the repository containing the actual sample projects.
        /// </summary>
        internal string Repository { get; }

        /// <summary>
        /// Gets this test's exclusively owned directory.
        /// </summary>
        internal string DirectoryPath { get; }

        /// <summary>
        /// Gets the fresh PKI path the child must release before returning.
        /// </summary>
        internal string StateDirectory { get; }

        /// <summary>
        /// Runs a finite executable and drains its stdout/stderr before observing success.
        /// </summary>
        internal async Task<ProcessResult> ExecuteAsync(string sample, IEnumerable<string> arguments)
        {
            string configuration = Environment.GetEnvironmentVariable("OPCUA_SAMPLE_CONFIGURATION") ??
                DefaultConfiguration;
            string assembly = Path.Combine(Repository, "samples", "Core", sample, "bin", configuration,
                "net10.0", sample + ".dll");
            Assert.That(File.Exists(assembly), Is.True, $"Build the actual sample first: {assembly}");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Repository,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(assembly);
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            start.ArgumentList.Add("--state");
            start.ArgumentList.Add(StateDirectory);
            using Process process = Process.Start(start) ??
                throw new InvalidOperationException("The sample process did not start.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return new ProcessResult(process.ExitCode,
                    await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        /// <summary>
        /// Resolves the checkout without guessing machine-specific paths.
        /// </summary>
        private static string FindRepository()
        {
            for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
                directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "samples", "Core")))
                {
                    return directory.FullName;
                }
            }
            throw new InvalidOperationException("Run these acceptance tests from a repository build.");
        }

#if DEBUG
        /// <summary>Matches the build configuration of the actual sample project references.</summary>
        private const string DefaultConfiguration = "Debug";
#else
        /// <summary>Matches the build configuration of the actual sample project references.</summary>
        private const string DefaultConfiguration = "Release";
#endif
    }

    /// <summary>
    /// Captures the actual process result without interpreting ordinary log output as evidence.
    /// </summary>
    internal sealed record ProcessResult(int ExitCode, string Output)
    {
        /// <summary>
        /// Requires exactly one final evidence line, emitted only after resource cleanup.
        /// </summary>
        internal JsonElement Summary()
        {
            string? summary = null;
            foreach (string line in Output.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("SUMMARY ", StringComparison.Ordinal))
                {
                    Assert.That(summary, Is.Null, "The sample emitted competing summaries.");
                    summary = line["SUMMARY ".Length..];
                }
            }
            Assert.That(summary, Is.Not.Null, Output);
            using JsonDocument document = JsonDocument.Parse(summary!);
            return document.RootElement.Clone();
        }
    }
}
