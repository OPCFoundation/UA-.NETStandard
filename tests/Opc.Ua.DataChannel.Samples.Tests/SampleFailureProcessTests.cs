/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.DataChannel.Samples.Tests
{
    /// <summary>
    /// Covers command-line refusal and finite cleanup through the executable boundary.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SampleFailureProcessTests
    {
        /// <summary>
        /// Invalid choices are nonzero failures before certificate or socket setup.
        /// </summary>
        [TestCase("--carrier", "tcp")]
        [TestCase("--authorization", "anonymous")]
        [TestCase("--publisher", "anything")]
        [TestCase("--count", "0")]
        [TestCase("--count", "-1")]
        [TestCase("--count", "1000001")]
        [TestCase("--size", "3")]
        [TestCase("--size", "32769")]
        [TestCase("--size", "eight")]
        [TestCase("--interval-ms", "-1")]
        [TestCase("--timeout", "0")]
        [TestCase("--timeout", "301")]
        [TestCase("--repeat", "0")]
        [TestCase("--cancel-after-ms", "0")]
        [TestCase("--cancel-after-ms", "60001")]
        [TestCase("--mode", "benchmark")]
        public async Task UnknownCarrierAndInvalidBoundsFailWithoutStarting(string option, string value)
        {
            using var run = new SampleProcessRun();
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelStreaming", [option, value])
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1));
                Assert.That(result.Output, Does.Contain("FAILED:").And.Not.Contain("LISTENING"));
                Assert.That(result.Output, Does.Not.Contain("SUMMARY "));
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }

        /// <summary>
        /// An explicitly selected native carrier cannot succeed by switching to UASC or an unreviewed image.
        /// </summary>
        [TestCase("quic")]
        [TestCase("webtransport")]
        public async Task MissingNativeAssetsFailWithoutFallback(string carrier)
        {
            using var run = new SampleProcessRun();
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--quic-library", Path.Combine(run.DirectoryPath, "absent-native.dll"),
                    "--quic-sha256", new string('0', 64)]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1));
                Assert.That(result.Output, Does.Contain("QUIC requires").And.Contain("no fallback"));
                Assert.That(result.Output, Does.Not.Contain("LISTENING").And.Not.Contain("SUMMARY "));
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }

        /// <summary>
        /// A user-owned output file is never truncated, even before network setup.
        /// </summary>
        [Test]
        public async Task ExistingWaveIsNotOverwritten()
        {
            using var run = new SampleProcessRun();
            string path = Path.Combine(run.DirectoryPath, "existing.wav");
            byte[] sentinel = Encoding.UTF8.GetBytes("This file is not owned by the audio sample.");
            await File.WriteAllBytesAsync(path, sentinel).ConfigureAwait(false);
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelAudio", ["--wav", path])
                .ConfigureAwait(false);
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(await File.ReadAllBytesAsync(path).ConfigureAwait(false), Is.EqualTo(sentinel));
            Assert.That(Directory.Exists(run.StateDirectory), Is.False);
        }

        /// <summary>
        /// Refuses existing application state without deleting or replacing another owner's files.
        /// </summary>
        [Test]
        public async Task ExistingStateDirectoryIsNotReplaced()
        {
            using var run = new SampleProcessRun();
            Directory.CreateDirectory(run.StateDirectory);
            string marker = Path.Combine(run.StateDirectory, "owned-by-caller.txt");
            await File.WriteAllTextAsync(marker, "retain this exact state").ConfigureAwait(false);
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelStreaming", ["--count", "1"])
                .ConfigureAwait(false);
            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(await File.ReadAllTextAsync(marker).ConfigureAwait(false),
                Is.EqualTo("retain this exact state"));
            Assert.That(result.Output, Does.Not.Contain("LISTENING ").And.Not.Contain("SUMMARY "));
        }

        /// <summary>
        /// Rejects malformed audio durations before creating an output file, identity or socket.
        /// </summary>
        [TestCase("--frame-ms", "0")]
        [TestCase("--frame-ms", "251")]
        [TestCase("--duration-ms", "0")]
        [TestCase("--duration-ms", "60001")]
        [TestCase("--duration-ms", "21")]
        public async Task AudioDurationMustDescribeWholeBoundedMessages(string option, string value)
        {
            using var run = new SampleProcessRun();
            string path = Path.Combine(run.DirectoryPath, "invalid.wav");
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelAudio",
                ["--wav", path, option, value]).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
                Assert.That(result.Output, Does.Not.Contain("LISTENING ").And.Not.Contain("SUMMARY "));
                Assert.That(File.Exists(path), Is.False);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }

        /// <summary>
        /// Cancellation begins only after READY, then releases the actual listener and exclusively owned PKI.
        /// </summary>
        [TestCase("dedicated")]
        [TestCase("shared")]
        [TestCase("quic", Category = "NativeDataChannelSamples")]
        [TestCase("webtransport", Category = "NativeDataChannelSamples")]
        public async Task CancelledRunReleasesOwnedStateAndSockets(string carrier)
        {
            using var run = new SampleProcessRun();
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--count", "1000000", "--size", "64", "--interval-ms", "10",
                    "--cancel-after-ms", "100"])
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(130), result.Output);
                Assert.That(result.Output, Does.Contain("LISTENING ").And.Contain("CLEANUP "));
                Assert.That(result.Output, Does.Contain("resourcesReleased=True").And.Not.Contain("SUMMARY "));
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
            string ready = result.Output.Split('\n').Single(line =>
                line.StartsWith("LISTENING ", StringComparison.Ordinal));
            string portText = ready.Split(' ').Single(item => item.StartsWith("port=", StringComparison.Ordinal));
            int port = int.Parse(portText["port=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            if (carrier is "quic" or "webtransport")
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }
            else
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
        }
    }
}
