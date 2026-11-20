/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.Frame;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;
using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Tests.Replay
{
    /// <summary>
    /// Tests the mock-client replay endpoint consent flag and hostname
    /// allow-list gate.
    /// </summary>
    [TestFixture]
    public sealed class ReplayAllowListTests
    {
        [Test]
        public void MockClientReplayThrowsWhenAllowMockClientReplayIsFalse()
        {
            var options = new PcapOptions
            {
                AllowedReplayEndpoints = ["a.example.com"]
            };

            Assert.That(
                () => CreateReplay(options, "opc.tcp://a.example.com:4840"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("AllowMockClientReplay"));
        }

        [Test]
        public void MockClientReplayThrowsWhenAllowListIsEmpty()
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = []
            };

            Assert.That(
                () => CreateReplay(options, "opc.tcp://a.example.com:4840"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("AllowedReplayEndpoints is empty"));
        }

        [Test]
        public void MockClientReplayThrowsWhenHostNotInAllowList()
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = ["a.example.com"]
            };

            Assert.That(
                () => CreateReplay(options, "opc.tcp://b.example.com:4840"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("is not in"));
        }

        [Test]
        public async Task MockClientReplayAcceptsHostInAllowList()
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = ["a.example.com"]
            };

            MockClientReplay? replay = null;
            Assert.DoesNotThrow(() => replay = CreateReplay(options, "opc.tcp://a.example.com:4840"));
            if (replay is not null)
            {
                await replay.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MockClientReplayHostMatchIsCaseInsensitive()
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = ["a.example.com"]
            };

            MockClientReplay? replay = null;
            Assert.DoesNotThrow(() => replay = CreateReplay(options, "OPC.tcp://A.EXAMPLE.COM:4840"));
            if (replay is not null)
            {
                await replay.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void MockClientReplayThrowsWhenTargetUrlIsMalformed()
        {
            var options = new PcapOptions
            {
                AllowMockClientReplay = true,
                AllowedReplayEndpoints = ["a.example.com"]
            };

            Assert.That(
                () => CreateReplay(options, "not a url"),
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("valid absolute URI"));
        }

        [Test]
        public void PcapOptionsAllowMockClientReplayDefaultsToFalse()
        {
            var options = new PcapOptions();

            Assert.That(options.AllowMockClientReplay, Is.False);
        }

        [Test]
        public void PcapOptionsAllowedReplayEndpointsDefaultsToEmptyList()
        {
            var options = new PcapOptions();

            Assert.That(options.AllowedReplayEndpoints, Is.Empty);
        }

        private static MockClientReplay CreateReplay(PcapOptions options, string targetEndpointUrl)
        {
            return new MockClientReplay(new EmptyCaptureSource(), targetEndpointUrl, options);
        }

        private sealed class EmptyCaptureSource : ICaptureSource
        {
            public IReadOnlySet<FormatKind> SupportedFormats { get; } = new HashSet<FormatKind>();

            public long FrameCount => 0;

            public long ByteCount => 0;

            public ValueTask StartAsync(StartCaptureRequest request, CancellationToken ct)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask StopAsync(CancellationToken ct)
            {
                return ValueTask.CompletedTask;
            }

            public string? GetRawPcapFilePath()
            {
                return null;
            }

            public string? GetKeyLogFilePath()
            {
                return null;
            }

            public async IAsyncEnumerable<ChannelKeyMaterial> ReadKeyMaterialAsync(
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public async IAsyncEnumerable<CaptureFrame> ReadCapturedFramesAsync(
                long? maxFrames,
                [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
