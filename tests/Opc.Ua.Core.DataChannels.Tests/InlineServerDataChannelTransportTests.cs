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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// The inline binding of Part 6 errata §6.1 and §6.2 carries data
    /// channels over the SecureChannel a Client already holds. Before this
    /// was wired, a Server advertising <c>opc.tcp</c> accepted
    /// <c>OpenDataChannel</c> and then discarded every frame, which §5.16
    /// forbids: a capability difference is refused at the Service level "and
    /// never by dropping frames".
    /// </summary>
    [TestFixture]
    [Category("DataChannels")]
    public sealed class InlineServerDataChannelTransportTests
    {
        [Test]
        public async Task ManagerResolvedForAnInlineSecureChannelActuallyEmitsFramesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using InlineTestChannel channel = InlineTestChannel.Create("inline-transport", telemetry);
            var byteTransport = new CapturingByteTransport();
            channel.AttachTransport(byteTransport);
            channel.Activate(0x0000A17C, 7);

            const string secureChannelId = "inline-transport-1";
            UaSCSecureChannelRegistry.Bind(secureChannelId, channel);

            try
            {
                var transport = new InlineServerDataChannelTransport();

                Assert.That(
                    transport.TryGetManager(
                        SecureChannel(secureChannelId, Profiles.UaTcpTransport),
                        Capabilities(),
                        telemetry,
                        out DataChannelManager manager,
                        out uint maxFrameSize,
                        out bool isReliable),
                    Is.True);

                await using (manager.ConfigureAwait(false))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(maxFrameSize, Is.GreaterThan(0u));
                        Assert.That(isReliable, Is.True);
                        Assert.That(manager, Is.SameAs(channel.DataChannels));
                    });

                    DataChannel source = manager.Register(
                        1,
                        new NodeId(1u),
                        new DataChannelSettings
                        {
                            Direction = DataChannelDirection.SourceToSink,
                            DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                            MaxFrameSize = 1024,
                            InitialCredit = 65536
                        },
                        isSource: true);

                    manager.MarkOpen(source.ChannelId);
                    source.Write([0x01, 0x02, 0x03], DataChannelFrameFlags.MessageStart);

                    await WaitForAsync(() => byteTransport.Chunks.Count > 0).ConfigureAwait(false);

                    Assert.That(
                        SpecVectors.MessageType(byteTransport.Chunks[0]),
                        Is.EqualTo("STR"),
                        "The frame has to reach the SecureChannel rather than being discarded.");
                }
            }
            finally
            {
                UaSCSecureChannelRegistry.Unbind(secureChannelId, channel);
            }
        }

        [Test]
        public void NoManagerIsResolvedForASecureChannelThatCarriesNoInlineTransport()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var transport = new InlineServerDataChannelTransport();

            Assert.That(
                transport.TryGetManager(
                    SecureChannel("inline-transport-absent", Profiles.UaTcpTransport),
                    Capabilities(),
                    telemetry,
                    out _,
                    out _,
                    out _),
                Is.False,
                "Refusing is what lets OpenDataChannel answer Bad_DataChannelTransportUnsupported.");
        }

        [Test]
        public void NoManagerIsResolvedForATransportThatIsNotInlineFramed()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using InlineTestChannel channel = InlineTestChannel.Create("inline-profile", telemetry);
            const string secureChannelId = "inline-transport-2";
            UaSCSecureChannelRegistry.Bind(secureChannelId, channel);

            try
            {
                var transport = new InlineServerDataChannelTransport();

                Assert.That(
                    transport.TryGetManager(
                        SecureChannel(secureChannelId, Profiles.UaQuicTransport),
                        Capabilities(),
                        telemetry,
                        out _,
                        out _,
                        out _),
                    Is.False,
                    "opc.quic carries its channels on QUIC streams, not inline.");
            }
            finally
            {
                UaSCSecureChannelRegistry.Unbind(secureChannelId, channel);
            }
        }

        private static SecureChannelContext SecureChannel(string secureChannelId, string profile)
        {
            return new SecureChannelContext(
                secureChannelId,
                new EndpointDescription
                {
                    TransportProfileUri = profile,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt
                },
                RequestEncoding.Binary);
        }

        private static DataChannelServerCapabilities Capabilities()
        {
            return new DataChannelServerCapabilities
            {
                MaxDataChannels = 16,
                MaxFrameSize = 65536,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            };
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (int ii = 0; ii < 500; ii++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.Fail("Timed out waiting for condition.");
        }

        private sealed class InlineTestChannel : UaSCUaBinaryChannel
        {
            private InlineTestChannel(
                string contextId,
                BufferManager bufferManager,
                ChannelQuotas quotas,
                ITelemetryContext telemetry)
                : base(
                    contextId,
                    bufferManager,
                    quotas,
                    serverCertificates: null,
                    endpoints: null,
                    securityMode: MessageSecurityMode.None,
                    securityPolicyUri: SecurityPolicies.None,
                    telemetry: telemetry)
            {
            }

            public static InlineTestChannel Create(string contextId, ITelemetryContext telemetry)
            {
                return new InlineTestChannel(
                    contextId,
                    new BufferManager(contextId, TcpMessageLimits.DefaultMaxBufferSize, telemetry),
                    new ChannelQuotas(ServiceMessageContext.CreateEmpty(telemetry)),
                    telemetry);
            }

            public void Activate(uint channelId, uint tokenId)
            {
                ChannelId = channelId;
                ChannelToken token = CreateToken();
                token.TokenId = tokenId;
                ActivateToken(token);
            }

            public void AttachTransport(IUaSCByteTransport transport)
            {
                Transport = transport;
            }
        }

        private sealed class CapturingByteTransport : IUaSCByteTransport
        {
            public IReadOnlyList<byte[]> Chunks
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_chunks];
                    }
                }
            }

            public EndPoint? LocalEndpoint => null;

            public EndPoint? RemoteEndpoint => null;

            public TransportChannelFeatures Features => TransportChannelFeatures.None;

            public string Implementation => "test";

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                lock (m_lock)
                {
                    m_chunks.Add(chunk.ToArray());
                }

                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                byte[] chunk = new byte[buffers.Sum(segment => segment.Count)];
                int offset = 0;

                foreach (ArraySegment<byte> segment in buffers)
                {
                    segment.AsSpan().CopyTo(chunk.AsSpan(offset, segment.Count));
                    offset += segment.Count;
                }

                lock (m_lock)
                {
                    m_chunks.Add(chunk);
                }

                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void Close()
            {
            }

            private readonly List<byte[]> m_chunks = [];
            private readonly Lock m_lock = new();
        }
    }
}
