/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.DataChannel.Samples.Tests
{
    /// <summary>
    /// Requires explicitly supplied qualified native assets; missing prerequisites fail, never skip or fall back.
    /// </summary>
    [TestFixture]
    [Category("NativeDataChannelSamples")]
    [NonParallelizable]
    public sealed class NativeStreamingProcessTests
    {
        /// <summary>
        /// Runs exactly the same workload over authenticated QUIC or native draft-16 WebTransport.
        /// </summary>
        [TestCase("quic", "Quic", "server")]
        [TestCase("quic", "Quic", "client")]
        [TestCase("webtransport", "WebTransport", "server")]
        [TestCase("webtransport", "WebTransport", "client")]
        public async Task ConfiguredNativeStreamingHasNoSessionOrImplicitUasc(
            string carrier, string expectedCarrier, string publisher)
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--publisher", publisher, "--count", "8", "--size", "64", "--revise"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Carrier").GetString(), Is.EqualTo(expectedCarrier));
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(8));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo(512));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo("5224D6393BC74F5C498041A1268C5835FC69AFE3E1CDB0D31E30373560610906"));
                Assert.That(result.GetProperty("SourceSha256").GetString(),
                    Is.EqualTo(result.GetProperty("ReceivedSha256").GetString()));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("ContextId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(result.GetProperty("SourceIsOpener").GetBoolean(), Is.EqualTo(publisher == "client"));
                Assert.That(result.GetProperty("PublisherIsTransportInitiator").GetBoolean(),
                    Is.EqualTo(publisher == "client"));
                Assert.That(result.GetProperty("Sessions").GetInt32(), Is.Zero);
                Assert.That(result.GetProperty("SecureChannelId").GetUInt32(), Is.Zero);
                Assert.That(result.GetProperty("ServiceSecureChannelId").GetUInt32(), Is.Zero);
                Assert.That(result.GetProperty("ModelEnabled").GetBoolean(), Is.False);
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, result.GetProperty("Port").GetInt32()));
        }

        /// <summary>
        /// Separately opts in to normal Session control, without turning the native data carrier into a UASC lane.
        /// </summary>
        [TestCase("quic")]
        [TestCase("webtransport")]
        public async Task NativeSessionControlIsSeparateAndCommitsDataRevision(string carrier)
        {
            using var run = new SampleProcessRun();
            ProcessResult process = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", carrier, "--authorization", "session", "--count", "8", "--size", "64", "--revise"])
                .ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, process.Output);
            JsonElement result = process.Summary();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("Sessions").GetInt32(), Is.EqualTo(1));
                Assert.That(result.GetProperty("ModelEnabled").GetBoolean(), Is.True);
                Assert.That(result.GetProperty("Calls").GetInt32(), Is.GreaterThanOrEqualTo(2));
                Assert.That(result.GetProperty("SecureChannelId").GetUInt32(), Is.Zero);
                Assert.That(result.GetProperty("ServiceSecureChannelId").GetUInt32(), Is.Not.Zero);
                Assert.That(result.GetProperty("SameServiceTransport").GetBoolean(), Is.False);
                Assert.That(result.GetProperty("Messages").GetInt32(), Is.EqualTo(8));
                Assert.That(result.GetProperty("Bytes").GetInt64(), Is.EqualTo(512));
                Assert.That(result.GetProperty("ReceivedSha256").GetString(),
                    Is.EqualTo("5224D6393BC74F5C498041A1268C5835FC69AFE3E1CDB0D31E30373560610906"));
                Assert.That(result.GetProperty("Revision").GetUInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("ResourcesReleased").GetBoolean(), Is.True);
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }

        /// <summary>
        /// A present but unreviewed image fails hash verification without opening a substitute data carrier.
        /// </summary>
        [Test]
        public async Task IncorrectNativeImageHashIsRefused()
        {
            using var run = new SampleProcessRun();
            string? library = Environment.GetEnvironmentVariable("OPCUA_QUIC_LIBRARY") ??
                Environment.GetEnvironmentVariable("OPCUA_QUIC_NATIVE_PATH");
            Assert.That(library, Is.Not.Null.And.Not.Empty, "Set the reviewed native QUIC path prerequisite.");
            Assert.That(File.Exists(library), Is.True, "The reviewed native asset is required for this negative case.");
            ProcessResult result = await run.ExecuteAsync("ConsoleDataChannelStreaming",
                ["--carrier", "quic", "--quic-library", library!, "--quic-sha256", new string('0', 64)])
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
                Assert.That(result.Output, Does.Contain("does not match its reviewed SHA256"));
                Assert.That(result.Output, Does.Not.Contain("LISTENING ").And.Not.Contain("SUMMARY "));
                Assert.That(Directory.Exists(run.StateDirectory), Is.False);
            });
        }
    }
}
