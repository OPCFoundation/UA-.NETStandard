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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Tests.Security;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Subscriber-side PubSub discovery API tests.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.6", Summary = "PubSub discovery")]
    public class PubSubDiscoveryTests
    {
        private const ushort PublisherIdValue = 17;
        private const ushort WriterGroupIdValue = 7;
        private const ushort DataSetWriterIdValue = 42;
        private const string PublishedDataSetName = "pds-1";
        private const string SecurityGroupIdValue = "discovery-group";

        [Test]
        public async Task RequestDiscoveryAsyncEncodesRequestAndCollectsResponse()
        {
            PubSubNetworkMessageContext context = NewContext();
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupId = WriterGroupIdValue,
                DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                DataSetWriterIds = [DataSetWriterIdValue],
                WriterConfiguration = new WriterGroupDataType
                {
                    Name = "writer-group",
                    WriterGroupId = WriterGroupIdValue
                },
                StatusCode = StatusCodes.Good,
                SequenceNumber = 1
            };
            var factory = new AutoResponseTransportFactory(UadpDiscoveryCoder.Encode(response, context));
            await using IPubSubApplication app = BuildDiscoveryOnlyApp(factory);
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            PubSubDiscoveryResult result = await app.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                    DataSetWriterIds = [DataSetWriterIdValue]
                },
                TimeSpan.FromMilliseconds(100),
                cts.Token).ConfigureAwait(false);

            Assert.That(factory.Transport, Is.Not.Null);
            Assert.That(factory.Transport!.SentRequests, Has.Count.EqualTo(1));
            Assert.That(factory.Transport.SentRequests[0].DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetWriterConfiguration));
            Assert.That(factory.Transport.SentRequests[0].DataSetWriterIds,
                Is.EqualTo([DataSetWriterIdValue]));
            Assert.That(result.WriterConfigurations, Has.Count.EqualTo(1));
            Assert.That(result.WriterConfigurations[0].WriterConfiguration, Is.Not.Null);
            Assert.That(result.WriterConfigurations[0].WriterConfiguration!.Name,
                Is.EqualTo("writer-group"));
        }

        [Test]
        public async Task UdpLoopbackDiscoveryPublisherAnswersSubscriberRequests()
        {
            const string url = "opc.udp://239.0.0.1:49321";
            IOptions<UdpTransportOptions> options = Options.Create(new UdpTransportOptions
            {
                MulticastLoopback = true
            });
            var diagnostics = new PubSubDiagnostics(PubSubDiagnosticsLevel.Low);
            var udpFactory = new UdpPubSubTransportFactory(options, diagnostics);
            await using IPubSubApplication publisher = BuildPublisherApp(url, udpFactory);
            await using IPubSubApplication subscriber = BuildSubscriberApp(url, udpFactory);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await publisher.StartAsync(cts.Token).ConfigureAwait(false);
                await subscriber.StartAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUdpEnvironmentFailure(ex))
            {
                Assert.Ignore("UDP multicast loopback is not available in this environment: " + ex.Message);
                return;
            }

            PubSubDiscoveryResult metaData;
            PubSubDiscoveryResult writerConfiguration;
            PubSubDiscoveryResult endpoints;
            try
            {
                metaData = await subscriber.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest
                    {
                        DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                        DataSetWriterIds = [DataSetWriterIdValue]
                    },
                    TimeSpan.FromSeconds(1),
                    cts.Token).ConfigureAwait(false);
                writerConfiguration = await subscriber.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest
                    {
                        DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                        DataSetWriterIds = [DataSetWriterIdValue]
                    },
                    TimeSpan.FromSeconds(1),
                    cts.Token).ConfigureAwait(false);
                endpoints = await subscriber.RequestDiscoveryAsync(
                    new PubSubDiscoveryRequest
                    {
                        DiscoveryType = UadpDiscoveryType.PublisherEndpoints
                    },
                    TimeSpan.FromSeconds(1),
                    cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUdpEnvironmentFailure(ex))
            {
                Assert.Ignore("UDP multicast loopback is not available in this environment: " + ex.Message);
                return;
            }

            if (metaData.DataSetMetaDataEntries.Count == 0 ||
                writerConfiguration.WriterConfigurations.Count == 0 ||
                endpoints.PublisherEndpoints.Count == 0)
            {
                Assert.Ignore("UDP multicast loopback did not deliver discovery responses.");
            }

            Assert.That(metaData.DataSetMetaDataEntries[0].DataSetWriterId,
                Is.EqualTo(DataSetWriterIdValue));
            Assert.That(metaData.DataSetMetaDataEntries[0].DataSetMetaData, Is.Not.Null);
            Assert.That(writerConfiguration.WriterConfigurations[0].DataSetWriterIds,
                Is.EqualTo([DataSetWriterIdValue]));
            Assert.That(endpoints.PublisherEndpoints[0].EndpointUrl, Is.EqualTo(url));
        }

        /// <summary>
        /// A subscriber and a publisher whose connections are secured with the same
        /// SecurityGroup exchange discovery requests, responses, and the publisher's
        /// transport-specific announcements, and no frame leaves either connection
        /// without a SecurityHeader.
        /// </summary>
        [Test]
        [TestSpec("7.2.4.6.3")]
        public async Task SecuredSubscriberDiscoversSecuredPublisherWithoutPlaintextFramesAsync()
        {
            const string url = "opc.udp://239.0.0.1:4840";
            using PubSubSecurityKeyRing publisherRing = NewKeyRing();
            using PubSubSecurityKeyRing subscriberRing = NewKeyRing();
            var bus = new LoopbackBus();
            await using IPubSubApplication publisher = BuildPublisherApp(
                url,
                new LoopbackTransportFactory(bus),
                new StaticSecurityKeyProvider(SecurityGroupIdValue, publisherRing));
            await using IPubSubApplication subscriber = BuildSubscriberApp(
                url,
                new LoopbackTransportFactory(bus),
                new StaticSecurityKeyProvider(SecurityGroupIdValue, subscriberRing));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await publisher.StartAsync(cts.Token).ConfigureAwait(false);
            await subscriber.StartAsync(cts.Token).ConfigureAwait(false);

            // Each request collects responses for its whole timeout, so the endpoints
            // request is sent after the publisher's startup announcement of its
            // endpoints has left the 500 ms duplicate-response window.
            PubSubDiscoveryResult metaData = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                    DataSetWriterIds = [DataSetWriterIdValue]
                },
                TimeSpan.FromMilliseconds(600),
                cts.Token).ConfigureAwait(false);
            PubSubDiscoveryResult writerConfiguration = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                    DataSetWriterIds = [DataSetWriterIdValue]
                },
                TimeSpan.FromMilliseconds(600),
                cts.Token).ConfigureAwait(false);
            PubSubDiscoveryResult endpoints = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.PublisherEndpoints
                },
                TimeSpan.FromMilliseconds(600),
                cts.Token).ConfigureAwait(false);

            Assert.That(metaData.DataSetMetaDataEntries, Has.Count.EqualTo(1));
            Assert.That(
                metaData.DataSetMetaDataEntries[0].DataSetMetaData?.Name,
                Is.EqualTo(PublishedDataSetName));
            Assert.That(writerConfiguration.WriterConfigurations, Has.Count.EqualTo(1));
            Assert.That(endpoints.PublisherEndpoints, Is.Not.Empty);
            Assert.That(bus.AnnouncementCount, Is.GreaterThan(0),
                "The publisher sent no transport-specific discovery announcement.");
            Assert.That(bus.PlaintextFrames, Is.Empty,
                "Every frame of a secured connection must carry a SecurityHeader.");
        }

        private static IPubSubApplication BuildDiscoveryOnlyApp(IPubSubTransportFactory factory)
        {
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("discovery-subscriber")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections =
                    [
                        new PubSubConnectionDataType
                        {
                            Name = "subscriber",
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(new NetworkAddressUrlDataType
                            {
                                Url = "opc.udp://239.0.0.1:4840"
                            })
                        }
                    ],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .Build();
        }

        private static IPubSubApplication BuildPublisherApp(
            string url,
            IPubSubTransportFactory factory,
            IPubSubSecurityKeyProvider? securityKeyProvider = null)
        {
            DataSetMetaDataType metaData = NewMetaData();
            var writerGroup = new WriterGroupDataType
            {
                Name = "writer-group",
                WriterGroupId = WriterGroupIdValue,
                PublishingInterval = 600_000,
                DataSetWriters =
                [
                    new DataSetWriterDataType
                    {
                        Name = "writer",
                        DataSetWriterId = DataSetWriterIdValue,
                        DataSetName = PublishedDataSetName
                    }
                ]
            };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "discovery-listener"
            };
            if (securityKeyProvider is not null)
            {
                SecureGroup(writerGroup);
                SecureGroup(readerGroup);
            }
            PubSubApplicationBuilder builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("discovery-publisher")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections =
                    [
                        new PubSubConnectionDataType
                        {
                            Name = "publisher",
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            PublisherId = new Variant(PublisherIdValue),
                            Address = new ExtensionObject(new NetworkAddressUrlDataType
                            {
                                Url = url
                            }),
                            WriterGroups = [writerGroup],
                            ReaderGroups = [readerGroup]
                        }
                    ],
                    PublishedDataSets =
                    [
                        new PublishedDataSetDataType
                        {
                            Name = PublishedDataSetName,
                            DataSetMetaData = metaData
                        }
                    ]
                })
                .AddDataSetSource(PublishedDataSetName, new MetaDataOnlySource(metaData))
                .UseAllStandardEncoders()
                .AddTransportFactory(factory);
            if (securityKeyProvider is not null)
            {
                builder.AddSecurityKeyProvider(securityKeyProvider);
            }
            return builder.Build();
        }

        private static IPubSubApplication BuildSubscriberApp(
            string url,
            IPubSubTransportFactory factory,
            IPubSubSecurityKeyProvider? securityKeyProvider = null)
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "subscriber",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                })
            };
            if (securityKeyProvider is not null)
            {
                var readerGroup = new ReaderGroupDataType
                {
                    Name = "secured-listener"
                };
                SecureGroup(readerGroup);
                connection.ReaderGroups = [readerGroup];
            }
            PubSubApplicationBuilder builder = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("discovery-subscriber")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = [connection],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(factory);
            if (securityKeyProvider is not null)
            {
                builder.AddSecurityKeyProvider(securityKeyProvider);
            }
            return builder.Build();
        }

        private static void SecureGroup(PubSubGroupDataType group)
        {
            group.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            group.SecurityGroupId = SecurityGroupIdValue;
            group.SecurityKeyServices = new ArrayOf<EndpointDescription>(new[]
            {
                new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840/SecurityKeyService" }
            });
        }

        private static PubSubSecurityKeyRing NewKeyRing()
        {
            var ring = new PubSubSecurityKeyRing(SecurityGroupIdValue);
            ring.SetCurrent(TestSecurityKeyFactory.Create(
                1,
                PubSubAes256CtrPolicy.Instance.SigningKeyLength,
                PubSubAes256CtrPolicy.Instance.EncryptingKeyLength));
            return ring;
        }

        private static DataSetMetaDataType NewMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = PublishedDataSetName,
                Fields = [new FieldMetaData { Name = "temperature" }],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }

        private static PubSubNetworkMessageContext NewContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new PubSub.MetaData.DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private static bool IsUdpEnvironmentFailure(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException ||
                ex is NotSupportedException ||
                (ex.InnerException is not null && IsUdpEnvironmentFailure(ex.InnerException));
        }

        private sealed class AutoResponseTransportFactory : IPubSubTransportFactory
        {
            private readonly ReadOnlyMemory<byte> m_response;

            public AutoResponseTransportFactory(ReadOnlyMemory<byte> response)
            {
                m_response = response;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public AutoResponseTransport? Transport { get; private set; }

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                Transport = new AutoResponseTransport(m_response);
                return Transport;
            }
        }

        private sealed class AutoResponseTransport : IPubSubTransport
        {
            private readonly ReadOnlyMemory<byte> m_response;
            private readonly Queue<PubSubTransportFrame> m_frames = new();
            private readonly SemaphoreSlim m_signal = new(0, int.MaxValue);
            private readonly Lock m_gate = new();

            public AutoResponseTransport(ReadOnlyMemory<byte> response)
            {
                m_response = response;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected { get; private set; }

            public List<UadpDiscoveryRequestMessage> SentRequests { get; } = [];

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                IsConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = topic;
                cancellationToken.ThrowIfCancellationRequested();
                PubSubNetworkMessage? decoded = UadpDecoder.Decode(payload, NewContext());
                if (decoded is UadpDiscoveryRequestMessage request)
                {
                    SentRequests.Add(request);
                    Enqueue(m_response);
                }
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await m_signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    PubSubTransportFrame frame;
                    lock (m_gate)
                    {
                        frame = m_frames.Dequeue();
                    }
                    yield return frame;
                }
            }

            public ValueTask DisposeAsync()
            {
                IsConnected = false;
                m_signal.Dispose();
                return default;
            }

            private void Enqueue(ReadOnlyMemory<byte> payload)
            {
                lock (m_gate)
                {
                    m_frames.Enqueue(new PubSubTransportFrame(
                        payload,
                        topic: null,
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
                }
                m_signal.Release();
            }
        }

        /// <summary>
        /// Delivers every frame that one transport sends to all other transports of
        /// the bus, and records the frames that carry no SecurityHeader.
        /// </summary>
        private sealed class LoopbackBus
        {
            private readonly List<LoopbackTransport> m_transports = [];
            private readonly List<string> m_plaintextFrames = [];
            private readonly Lock m_gate = new();
            private int m_announcementCount;

            public int AnnouncementCount => Volatile.Read(ref m_announcementCount);

            public IReadOnlyList<string> PlaintextFrames
            {
                get
                {
                    lock (m_gate)
                    {
                        return [.. m_plaintextFrames];
                    }
                }
            }

            public void Attach(LoopbackTransport transport)
            {
                lock (m_gate)
                {
                    m_transports.Add(transport);
                }
            }

            public void Deliver(LoopbackTransport sender, ReadOnlyMemory<byte> payload, bool isAnnouncement)
            {
                if (isAnnouncement)
                {
                    Interlocked.Increment(ref m_announcementCount);
                }
                var receivers = new List<LoopbackTransport>();
                lock (m_gate)
                {
                    if (!UadpDecoder.TryReadOuterPrefix(payload, out _, out bool securityEnabled, out _, out _) ||
                        !securityEnabled)
                    {
                        byte[] head = payload.Slice(0, Math.Min(8, payload.Length)).ToArray();
                        m_plaintextFrames.Add(BitConverter.ToString(head));
                    }
                    foreach (LoopbackTransport transport in m_transports)
                    {
                        if (!ReferenceEquals(transport, sender))
                        {
                            receivers.Add(transport);
                        }
                    }
                }
                foreach (LoopbackTransport receiver in receivers)
                {
                    receiver.Enqueue(payload);
                }
            }
        }

        private sealed class LoopbackTransportFactory : IPubSubTransportFactory
        {
            private readonly LoopbackBus m_bus;

            public LoopbackTransportFactory(LoopbackBus bus)
            {
                m_bus = bus;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                var transport = new LoopbackTransport(m_bus);
                m_bus.Attach(transport);
                return transport;
            }
        }

        /// <summary>
        /// An in-memory datagram transport that, like the UDP transport, sends
        /// transport-specific discovery announcements to a separate destination.
        /// </summary>
        private sealed class LoopbackTransport : IPubSubTransport, IPubSubDiscoveryAnnouncementTransport
        {
            private readonly LoopbackBus m_bus;
            private readonly Queue<PubSubTransportFrame> m_frames = new();
            private readonly SemaphoreSlim m_signal = new(0, int.MaxValue);
            private readonly Lock m_gate = new();
            private bool m_disposed;

            public LoopbackTransport(LoopbackBus bus)
            {
                m_bus = bus;
            }

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected { get; private set; }

            public uint DiscoveryAnnounceRate => 0;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                IsConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                IsConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default)
            {
                _ = topic;
                cancellationToken.ThrowIfCancellationRequested();
                m_bus.Deliver(this, payload, isAnnouncement: false);
                return default;
            }

            public ValueTask SendDiscoveryAnnouncementAsync(
                ReadOnlyMemory<byte> payload,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_bus.Deliver(this, payload, isAnnouncement: true);
                return default;
            }

            public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await m_signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    PubSubTransportFrame frame;
                    lock (m_gate)
                    {
                        frame = m_frames.Dequeue();
                    }
                    yield return frame;
                }
            }

            public ValueTask DisposeAsync()
            {
                lock (m_gate)
                {
                    m_disposed = true;
                    IsConnected = false;
                }
                m_signal.Dispose();
                return default;
            }

            public void Enqueue(ReadOnlyMemory<byte> payload)
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_frames.Enqueue(new PubSubTransportFrame(
                        payload.ToArray(),
                        topic: null,
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
                    m_signal.Release();
                }
            }
        }

        private sealed class MetaDataOnlySource : IPublishedDataSetSource
        {
            private readonly DataSetMetaDataType m_metaData;

            public MetaDataOnlySource(DataSetMetaDataType metaData)
            {
                m_metaData = metaData;
            }

            public DataSetMetaDataType BuildMetaData()
            {
                return m_metaData;
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                _ = metaData;
                _ = cancellationToken;
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        new ConfigurationVersionDataType(),
                        [],
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
            }
        }
    }
}
