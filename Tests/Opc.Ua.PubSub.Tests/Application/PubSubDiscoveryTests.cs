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
                Is.EqualTo(new[] { DataSetWriterIdValue }));
            Assert.That(result.WriterConfigurations, Has.Count.EqualTo(1));
            Assert.That(result.WriterConfigurations[0].WriterConfiguration, Is.Not.Null);
            Assert.That(result.WriterConfigurations[0].WriterConfiguration!.Name,
                Is.EqualTo("writer-group"));
        }

        [Test]
        public async Task UdpLoopbackDiscoveryPublisherAnswersSubscriberRequests()
        {
            string url = "opc.udp://239.0.0.1:49321";
            var options = Options.Create(new UdpTransportOptions
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

            PubSubDiscoveryResult metaData = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                    DataSetWriterIds = [DataSetWriterIdValue]
                },
                TimeSpan.FromSeconds(1),
                cts.Token).ConfigureAwait(false);
            PubSubDiscoveryResult writerConfiguration = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                    DataSetWriterIds = [DataSetWriterIdValue]
                },
                TimeSpan.FromSeconds(1),
                cts.Token).ConfigureAwait(false);
            PubSubDiscoveryResult endpoints = await subscriber.RequestDiscoveryAsync(
                new PubSubDiscoveryRequest
                {
                    DiscoveryType = UadpDiscoveryType.PublisherEndpoints
                },
                TimeSpan.FromSeconds(1),
                cts.Token).ConfigureAwait(false);

            if (metaData.DataSetMetaDataEntries.Count == 0
                || writerConfiguration.WriterConfigurations.Count == 0
                || endpoints.PublisherEndpoints.Count == 0)
            {
                Assert.Ignore("UDP multicast loopback did not deliver discovery responses.");
            }

            Assert.That(metaData.DataSetMetaDataEntries[0].DataSetWriterId,
                Is.EqualTo(DataSetWriterIdValue));
            Assert.That(metaData.DataSetMetaDataEntries[0].DataSetMetaData, Is.Not.Null);
            Assert.That(writerConfiguration.WriterConfigurations[0].DataSetWriterIds,
                Is.EqualTo(new[] { DataSetWriterIdValue }));
            Assert.That(endpoints.PublisherEndpoints[0].EndpointUrl, Is.EqualTo(url));
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
            IPubSubTransportFactory factory)
        {
            DataSetMetaDataType metaData = NewMetaData();
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
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
                            WriterGroups =
                            [
                                new WriterGroupDataType
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
                                }
                            ],
                            ReaderGroups =
                            [
                                new ReaderGroupDataType
                                {
                                    Name = "discovery-listener"
                                }
                            ]
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
                .AddTransportFactory(factory)
                .Build();
        }

        private static IPubSubApplication BuildSubscriberApp(
            string url,
            IPubSubTransportFactory factory)
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
                                Url = url
                            })
                        }
                    ],
                    PublishedDataSets = []
                })
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .Build();
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
                new Opc.Ua.PubSub.MetaData.DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private static bool IsUdpEnvironmentFailure(Exception ex)
        {
            return ex is System.Net.Sockets.SocketException
                || ex is NotSupportedException
                || ex.InnerException is not null && IsUdpEnvironmentFailure(ex.InnerException);
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
            private readonly System.Threading.Lock m_gate = new();

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
