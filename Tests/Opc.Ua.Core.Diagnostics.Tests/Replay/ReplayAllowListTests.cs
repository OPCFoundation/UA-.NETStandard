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
using Opc.Ua.Bindings;

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
