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

extern alias publishersample;
extern alias subscribersample;

namespace Opc.Ua.Aot.Tests
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Opc.Ua.PubSub;
    using Opc.Ua.PubSub.Application;
    using Opc.Ua.PubSub.Configuration;
    using Opc.Ua.PubSub.DataSets;
    using Opc.Ua.PubSub.MetaData;
    using Opc.Ua.PubSub.StateMachine;
    using Opc.Ua.PubSub.Transports;
    using Opc.Ua.PubSub.Udp;
    using DataSetField = Opc.Ua.PubSub.Encoding.DataSetField;
    using PubSubFieldEncoding = Opc.Ua.PubSub.Encoding.PubSubFieldEncoding;
    using PubSubDataSetMessageType = Opc.Ua.PubSub.Encoding.PubSubDataSetMessageType;
    using PubSubNetworkMessage = Opc.Ua.PubSub.Encoding.PubSubNetworkMessage;
    using PubSubNetworkMessageContext = Opc.Ua.PubSub.Encoding.PubSubNetworkMessageContext;
    using PublisherId = Opc.Ua.PubSub.Encoding.PublisherId;
    using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;
    using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
    using UadpEncoder = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
    using UadpDecoder = Opc.Ua.PubSub.Encoding.Uadp.UadpDecoder;
    using JsonNetworkMessage = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
    using JsonDataSetMessage = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
    using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
    using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;

    /// <summary>
    /// AOT smoke tests that exercise the PubSub fluent builder, the
    /// XML configuration store, the publisher start/stop lifecycle,
    /// and both UADP and JSON network-message round-trips end-to-end
    /// through the NativeAOT-compiled binary. Protects the Part 14
    /// stack against AOT regressions per plan §10 acceptance 7.
    /// </summary>
    public class PubSubAotTests
    {
        [Test]
        public async Task BuildsPubSubApplication_FluentInCode()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            PubSubConfigurationDataType cfg =
                publishersample::Quickstarts.ConsoleReferencePublisher
                    .PublisherConfigurationBuilder.Build(
                        publishersample::Quickstarts.ConsoleReferencePublisher
                            .PublisherProfile.UdpUadp,
                        "opc.udp://239.0.0.250:4840",
                        publisherId: 1,
                        writerGroupId: 100,
                        dataSetWriterId: 1,
                        intervalMs: 100);

            IPubSubApplication app = new PubSubApplicationBuilder(telemetry)
                .WithApplicationId("urn:test:pubsub-aot")
                .UseAllStandardEncoders()
                .AddTransportFactory(new UdpPubSubTransportFactory(
                    Options.Create(new UdpTransportOptions())))
                .AddDataSetSource(
                    publishersample::Quickstarts.ConsoleReferencePublisher
                        .PublisherConfigurationBuilder.DataSetName,
                    new publishersample::Quickstarts.ConsoleReferencePublisher
                        .SampleDataSetSource())
                .UseConfiguration(cfg)
                .Build();

            await Assert.That(app).IsNotNull();
            await Assert.That(app.ApplicationId).IsNotNull();
            await Assert.That(app.ApplicationId.Length).IsGreaterThan(0);
            await Assert.That(app.Connections.Count).IsEqualTo(1);
            await Assert.That(app.State.State).IsEqualTo(PubSubState.Disabled);
            await app.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task BuildsPubSubApplication_FluentMqttBroker()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            PubSubConfigurationDataType cfg =
                subscribersample::Quickstarts.ConsoleReferenceSubscriber
                    .SubscriberConfigurationBuilder.Build(
                        subscribersample::Quickstarts.ConsoleReferenceSubscriber
                            .SubscriberProfile.MqttJson,
                        "mqtt://localhost:1883",
                        publisherIdFilter: 1,
                        writerGroupIdFilter: 100,
                        dataSetWriterIdFilter: 1);

            IPubSubApplication app = new PubSubApplicationBuilder(telemetry)
                .WithApplicationId("urn:test:pubsub-mqtt")
                .UseAllStandardEncoders()
                .AddTransportFactory(new FakeMqttJsonTransportFactory())
                .AddSubscribedDataSetSink(
                    subscribersample::Quickstarts.ConsoleReferenceSubscriber
                        .SubscriberConfigurationBuilder.ReaderName,
                    new subscribersample::Quickstarts.ConsoleReferenceSubscriber
                        .ConsoleLoggingSink(
                            telemetry.CreateLogger<subscribersample::Quickstarts
                                .ConsoleReferenceSubscriber.ConsoleLoggingSink>()))
                .UseConfiguration(cfg)
                .Build();

            await Assert.That(app).IsNotNull();
            await Assert.That(app.Connections.Count).IsEqualTo(1);
            await Assert.That(app.Connections[0].Configuration.TransportProfileUri)
                .IsEqualTo(Profiles.PubSubMqttJsonTransport);
            await app.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task LoadsPubSubConfigurationFromXml()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            PubSubConfigurationDataType original =
                publishersample::Quickstarts.ConsoleReferencePublisher
                    .PublisherConfigurationBuilder.Build(
                        publishersample::Quickstarts.ConsoleReferencePublisher
                            .PublisherProfile.UdpUadp,
                        "opc.udp://239.0.0.250:4840",
                        publisherId: 7,
                        writerGroupId: 200,
                        dataSetWriterId: 3,
                        intervalMs: 500);
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                $"opcua-pubsub-aot-{Guid.NewGuid():N}.xml");
            try
            {
                var store = new XmlPubSubConfigurationStore(tempFile, telemetry);
                await store.SaveAsync(original, CancellationToken.None)
                    .ConfigureAwait(false);

                PubSubConfigurationDataType loaded = await store
                    .LoadAsync(CancellationToken.None).ConfigureAwait(false);

                await Assert.That(loaded).IsNotNull();
                await Assert.That(loaded.Connections.Count).IsEqualTo(1);
                PubSubConnectionDataType conn = loaded.Connections.ToList()[0];
                await Assert.That(conn.TransportProfileUri)
                    .IsEqualTo(Profiles.PubSubUdpUadpTransport);
                await Assert.That(conn.WriterGroups.Count).IsEqualTo(1);
                WriterGroupDataType wg = conn.WriterGroups.ToList()[0];
                await Assert.That(wg.WriterGroupId).IsEqualTo((ushort)200);
                await Assert.That(wg.DataSetWriters.Count).IsEqualTo(1);
                await Assert.That(wg.DataSetWriters.ToList()[0].DataSetWriterId)
                    .IsEqualTo((ushort)3);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public async Task StartsAndStopsPublisher_UdpUadp()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            PubSubConfigurationDataType cfg =
                publishersample::Quickstarts.ConsoleReferencePublisher
                    .PublisherConfigurationBuilder.Build(
                        publishersample::Quickstarts.ConsoleReferencePublisher
                            .PublisherProfile.UdpUadp,
                        "opc.udp://239.0.0.250:4845",
                        publisherId: 9,
                        writerGroupId: 909,
                        dataSetWriterId: 1,
                        intervalMs: 50);

            IPubSubApplication app = new PubSubApplicationBuilder(telemetry)
                .WithApplicationId("urn:test:publisher-lifecycle")
                .UseAllStandardEncoders()
                .AddTransportFactory(new UdpPubSubTransportFactory(
                    Options.Create(new UdpTransportOptions())))
                .AddDataSetSource(
                    publishersample::Quickstarts.ConsoleReferencePublisher
                        .PublisherConfigurationBuilder.DataSetName,
                    new publishersample::Quickstarts.ConsoleReferencePublisher
                        .SampleDataSetSource())
                .UseConfiguration(cfg)
                .Build();

            await Assert.That(app.State.State).IsEqualTo(PubSubState.Disabled);
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await Assert.That(app.State.State).IsEqualTo(PubSubState.Operational);

            await Task.Delay(TimeSpan.FromMilliseconds(200))
                .ConfigureAwait(false);

            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await Assert.That(app.State.State).IsEqualTo(PubSubState.Disabled);
            await app.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task RoundTripsUadpNetworkMessage()
        {
            PubSubNetworkMessageContext context = NewContext();
            var msg = new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromUInt16(4242),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 7,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields =
                        [
                            new DataSetField { Value = new Variant(true) },
                            new DataSetField { Value = new Variant(12345) },
                            new DataSetField { Value = new Variant("aot") }
                        ]
                    }
                ]
            };

            ReadOnlyMemory<byte> bytes = await new UadpEncoder()
                .EncodeAsync(msg, context).ConfigureAwait(false);
            await Assert.That(bytes.Length).IsGreaterThan(0);

            PubSubNetworkMessage? decoded = await new UadpDecoder()
                .TryDecodeAsync(bytes, context).ConfigureAwait(false);
            await Assert.That(decoded).IsNotNull();
            var roundTripped = (UadpNetworkMessage)decoded!;
            await Assert.That(roundTripped.DataSetMessages.Count).IsEqualTo(1);
            var ds = (UadpDataSetMessage)roundTripped.DataSetMessages[0];
            await Assert.That(ds.DataSetWriterId).IsEqualTo((ushort)7);
            await Assert.That(ds.Fields.Count).IsEqualTo(3);
            await Assert.That(ds.Fields[0].Value).IsEqualTo(new Variant(true));
            await Assert.That(ds.Fields[1].Value).IsEqualTo(new Variant(12345));
            await Assert.That(ds.Fields[2].Value).IsEqualTo(new Variant("aot"));
        }

        [Test]
        public async Task RoundTripsJsonNetworkMessage()
        {
            var meta = new DataSetMetaDataType
            {
                Name = "AotJsonDataSet",
                Fields = new ArrayOf<FieldMetaData>(new[]
                {
                    new FieldMetaData
                    {
                        Name = "Bool",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                }),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(
                    PublisherId.FromUInt16(900), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext context = NewContext(registry);

            var msg = new JsonNetworkMessage
            {
                MessageId = "aot-msg",
                PublisherId = PublisherId.FromUInt16(900),
                DataSetMessages =
                [
                    new JsonDataSetMessage
                    {
                        DataSetWriterId = 1,
                        SequenceNumber = 42,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = meta.ConfigurationVersion,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Bool",
                                Value = new Variant(true)
                            },
                            new DataSetField
                            {
                                Name = "Int",
                                Value = new Variant(2026)
                            }
                        ]
                    }
                ]
            };

            ReadOnlyMemory<byte> bytes = await new JsonEncoder()
                .EncodeAsync(msg, context).ConfigureAwait(false);
            await Assert.That(bytes.Length).IsGreaterThan(0);

            PubSubNetworkMessage? decoded = await new JsonDecoder()
                .TryDecodeAsync(bytes, context).ConfigureAwait(false);
            await Assert.That(decoded).IsNotNull();
            var roundTripped = (JsonNetworkMessage)decoded!;
            await Assert.That(roundTripped.DataSetMessages.Count).IsEqualTo(1);
            var ds = (JsonDataSetMessage)roundTripped.DataSetMessages[0];
            await Assert.That(ds.Fields.Count).IsEqualTo(2);
            await Assert.That(ds.Fields[0].Value).IsEqualTo(new Variant(true));
            await Assert.That(ds.Fields[1].Value).IsEqualTo(new Variant(2026));
        }

        private static PubSubNetworkMessageContext NewContext(
            IDataSetMetaDataRegistry? registry = null)
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                registry ?? new DataSetMetaDataRegistry(),
                new Opc.Ua.PubSub.Diagnostics.PubSubDiagnostics(
                    Opc.Ua.PubSub.Diagnostics.PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }
    }

    /// <summary>
    /// Test-only transport factory that advertises the MQTT-JSON
    /// profile so the PubSub configuration validator accepts an
    /// MQTT broker connection without dragging in the full
    /// Opc.Ua.PubSub.Mqtt DI surface. The transport itself is never
    /// opened by these AOT smoke tests.
    /// </summary>
    public sealed class FakeMqttJsonTransportFactory : IPubSubTransportFactory
    {
        public string TransportProfileUri => Profiles.PubSubMqttJsonTransport;

        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            throw new NotSupportedException(
                "FakeMqttJsonTransportFactory does not open transports.");
        }
    }
}
