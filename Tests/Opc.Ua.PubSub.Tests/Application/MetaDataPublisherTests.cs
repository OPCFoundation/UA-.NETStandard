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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Coverage for <see cref="MetaDataPublisher"/>: startup
    /// announcement, change re-publication, MQTT retained-metadata
    /// path, UADP discovery response shape, and clean unsubscribe on
    /// dispose. Covers
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.4">
    /// Part 14 §7.3.4.7.4</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
    /// §7.3.4.8</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
    /// §7.2.4.6.4</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5.2">
    /// §7.2.5.5.2</see>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.3.4.7.4", Summary = "MQTT metadata topic")]
    [TestSpec("7.3.4.8", Summary = "Retained discovery messages")]
    [TestSpec("7.2.4.6.4", Summary = "UADP DataSetMetaData announcement")]
    [TestSpec("7.2.5.5.2", Summary = "JSON metadata message")]
    public class MetaDataPublisherTests
    {
        private const string UadpProfile = Profiles.PubSubUdpUadpTransport;
        private const string JsonMqttProfile = Profiles.PubSubMqttJsonTransport;
        private const ushort PublisherIdValue = 17;
        private const ushort WriterGroupIdValue = 7;
        private const ushort DataSetWriterIdValue = 42;

        [Test]
        public async Task OnStartup_PublishesMetaData_ToMatchingTransport()
        {
            var factory = new RecordingTransportFactory(UadpProfile, supportsTopics: false);
            await using IPubSubApplication app = BuildApp(UadpProfile, factory);

            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);

            await WaitUntilAsync(
                () => factory.Transport is { } t && t.Sends.Count >= 1,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(factory.Transport, Is.Not.Null);
            Assert.That(factory.Transport!.Sends, Has.Count.EqualTo(1));
            Assert.That(factory.Transport.Sends[0].Payload.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task OnMetaDataChanged_RepublishesMetaData()
        {
            var factory = new RecordingTransportFactory(UadpProfile, supportsTopics: false);
            await using IPubSubApplication app = BuildApp(UadpProfile, factory);

            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);

            await WaitUntilAsync(
                () => factory.Transport is { } t && t.Sends.Count >= 1,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            int initialCount = factory.Transport!.Sends.Count;

            // Trigger a change; the registry fires MetaDataChanged
            // because MajorVersion differs from any previously stored
            // value.
            DataSetMetaDataKey key = NewKey();
            app.MetaDataRegistry.Register(in key, NewMeta(majorVersion: 2));

            await WaitUntilAsync(
                () => factory.Transport.Sends.Count > initialCount,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.That(
                factory.Transport.Sends,
                Has.Count.GreaterThan(initialCount),
                "MetaDataChanged must trigger an additional metadata publish.");
        }

        [Test]
        public async Task MqttPath_UsesMetaDataTopicOnTopicProviderTransport()
        {
            var factory = new RecordingTransportFactory(JsonMqttProfile, supportsTopics: true);
            await using IPubSubApplication app = BuildApp(JsonMqttProfile, factory);

            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);

            await WaitUntilAsync(
                () => factory.Transport is { } t && t.Sends.Count >= 1,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            Assert.That(factory.Transport!.Sends, Is.Not.Empty);
            string? topic = factory.Transport.Sends[0].Topic;
            Assert.That(topic, Is.Not.Null);
            Assert.That(topic, Does.Contain("/metadata/"),
                "MQTT metadata topic must contain '/metadata/' so the broker " +
                "transport sets Retain=true per Part 14 §7.3.4.8.");
        }

        [Test]
        public async Task UadpPath_EncodesDiscoveryResponse()
        {
            var factory = new RecordingTransportFactory(UadpProfile, supportsTopics: false);
            await using IPubSubApplication app = BuildApp(UadpProfile, factory);

            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);

            await WaitUntilAsync(
                () => factory.Transport is { } t && t.Sends.Count >= 1,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            ReadOnlyMemory<byte> payload = factory.Transport!.Sends[0].Payload;
            PubSubNetworkMessageContext ctx = NewDecodeContext();

            PubSubNetworkMessage? decoded = UadpDecoder.Decode(payload, ctx);

            Assert.That(decoded, Is.InstanceOf<UadpDiscoveryResponseMessage>());
            UadpDiscoveryResponseMessage response = (UadpDiscoveryResponseMessage)decoded!;
            Assert.That(
                response.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.DataSetMetaData));
            Assert.That(response.DataSetMetaData, Is.Not.Null);
            Assert.That(response.DataSetWriterId, Is.EqualTo(DataSetWriterIdValue));
        }

        [Test]
        public async Task DisposeAsync_UnsubscribesFromRegistry()
        {
            var factory = new RecordingTransportFactory(UadpProfile, supportsTopics: false);
            IPubSubApplication app = BuildApp(UadpProfile, factory);

            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await WaitUntilAsync(
                () => factory.Transport is { } t && t.Sends.Count >= 1,
                TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            // Capture a strong reference to the registry before disposing
            // the application; disposing the publisher must remove its
            // event handler from this exact instance.
            IDataSetMetaDataRegistry registry = app.MetaDataRegistry;
            int sendsBeforeDispose = factory.Transport!.Sends.Count;

            await app.DisposeAsync().ConfigureAwait(false);

            // After dispose, registering must not produce any new send
            // because the publisher unsubscribed from MetaDataChanged.
            DataSetMetaDataKey key = NewKey();
            registry.Register(in key, NewMeta(majorVersion: 99));

            await Task.Delay(150).ConfigureAwait(false);
            Assert.That(
                factory.Transport.Sends,
                Has.Count.EqualTo(sendsBeforeDispose),
                "Disposed publisher must not respond to MetaDataChanged events.");
        }

        private static IPubSubApplication BuildApp(
            string transportProfileUri,
            RecordingTransportFactory factory)
        {
            string addressUrl = transportProfileUri == JsonMqttProfile
                ? "mqtt://localhost:1883"
                : "opc.udp://localhost:4840";
            var connection = new PubSubConnectionDataType
            {
                Name = "conn-1",
                TransportProfileUri = transportProfileUri,
                PublisherId = new Variant(PublisherIdValue),
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType
                    {
                        Url = addressUrl
                    }),
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[]
                {
                    new WriterGroupDataType
                    {
                        Name = "wg-1",
                        WriterGroupId = WriterGroupIdValue,
                        PublishingInterval = 600_000,
                        DataSetWriters = new ArrayOf<DataSetWriterDataType>(new[]
                        {
                            new DataSetWriterDataType
                            {
                                Name = "writer-1",
                                DataSetWriterId = DataSetWriterIdValue,
                                DataSetName = "pds-1"
                            }
                        })
                    }
                })
            };
            var pds = new PublishedDataSetDataType
            {
                Name = "pds-1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "pds-1",
                    Fields = [new FieldMetaData { Name = "f1" }],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
            return new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .WithApplicationId("metadata-tests")
                .UseConfiguration(new PubSubConfigurationDataType
                {
                    Connections = new ArrayOf<PubSubConnectionDataType>(new[] { connection }),
                    PublishedDataSets =
                        new ArrayOf<PublishedDataSetDataType>(new[] { pds })
                })
                .AddDataSetSource("pds-1", new MetaDataOnlySource(pds.DataSetMetaData))
                .UseAllStandardEncoders()
                .AddTransportFactory(factory)
                .Build();
        }

        private static DataSetMetaDataKey NewKey()
        {
            return new DataSetMetaDataKey(
                PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupIdValue,
                DataSetWriterIdValue,
                Uuid.Empty,
                majorVersion: 0);
        }

        private static DataSetMetaDataType NewMeta(uint majorVersion = 1)
        {
            return new DataSetMetaDataType
            {
                Name = "pds-1",
                Fields = [new FieldMetaData { Name = "f1" }],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = 0
                }
            };
        }

        private static PubSubNetworkMessageContext NewDecodeContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private static async Task WaitUntilAsync(
            Func<bool> condition,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            Assert.Fail($"Condition not met within {timeout.TotalMilliseconds:F0} ms.");
        }

        private sealed class RecordingTransportFactory : IPubSubTransportFactory
        {
            private readonly bool m_supportsTopics;

            public RecordingTransportFactory(string profile, bool supportsTopics)
            {
                TransportProfileUri = profile;
                m_supportsTopics = supportsTopics;
            }

            public string TransportProfileUri { get; }

            public RecordingTransport? Transport { get; private set; }

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                _ = connection;
                _ = telemetry;
                _ = timeProvider;
                Transport = m_supportsTopics
                    ? new RecordingMqttTransport(TransportProfileUri)
                    : new RecordingTransport(TransportProfileUri);
                return Transport;
            }
        }

        private class RecordingTransport : IPubSubTransport
        {
            public RecordingTransport(string profile)
            {
                TransportProfileUri = profile;
            }

            public string TransportProfileUri { get; }

            public PubSubTransportDirection Direction
                => PubSubTransportDirection.SendReceive;

            public bool IsConnected { get; private set; }

            public List<(ReadOnlyMemory<byte> Payload, string? Topic)> Sends { get; }
                = new();

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
                _ = cancellationToken;
                lock (Sends)
                {
                    Sends.Add((payload, topic));
                }
                return default;
            }

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                _ = cancellationToken;
                return AsyncEnumerable.Empty<PubSubTransportFrame>();
            }

            public ValueTask DisposeAsync()
            {
                IsConnected = false;
                return default;
            }
        }

        private sealed class RecordingMqttTransport
            : RecordingTransport, IPubSubTopicProvider
        {
            public RecordingMqttTransport(string profile)
                : base(profile)
            {
            }

            public string BuildMetaDataTopic(
                PublisherId publisherId,
                ushort writerGroupId,
                ushort dataSetWriterId)
            {
                _ = publisherId;
                return $"opcua/json/metadata/p17/{writerGroupId}/{dataSetWriterId}";
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
