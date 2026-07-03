# Apache Kafka PubSub transport

`Opc.Ua.PubSub.Kafka` implements the OPC UA Part 14 Annex B.2 Apache Kafka transport mapping for broker-based PubSub deployments. It registers two transport profiles: `pubsub-kafka-uadp` (`KafkaProfiles.PubSubKafkaUadpTransport`) for UADP NetworkMessages and `pubsub-kafka-json` (`KafkaProfiles.PubSubKafkaJsonTransport`) for JSON NetworkMessages.

Use `kafka://host[:port][,host[:port]...]` for plaintext bootstrap servers and `kafkas://host[:port][,...]` for TLS-protected bootstrap servers. The default Kafka port is `9092`, so `kafka://localhost` and `kafka://localhost:9092` resolve to the same bootstrap server list.

## Dependency injection

Register the transport on the PubSub builder with `AddKafkaTransport()`. The extension lives in the `Microsoft.Extensions.DependencyInjection` namespace and registers both Kafka profile factories. Options are supplied with `KafkaConnectionOptions`, either by callback or from the `OpcUa:PubSub:Kafka` configuration section.

Important `KafkaConnectionOptions` fields include `Endpoint` and `BootstrapServers` (bootstrap broker selection), `GroupId` (subscriber consumer group), `SecurityProtocol`, `SaslMechanism`, `UserName`, `PasswordSecretId`, `AuthenticationProfileUri`, `ResourceUri`, `Tls`, `DeliveryGuarantee`, `AutoOffsetReset`, `EnableAutoCommit`, `MaxMessageSize`, and `Topics.Prefix`. `PasswordSecretId` is resolved through the OPC UA secret store so configuration does not carry plaintext passwords.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Kafka;

const string DataTopic = "opcua.json.data.Line1.Simple";
const string MetadataTopic = "opcua.json.metadata.Line1.Simple";

HostApplicationBuilder publisherHost = Host.CreateApplicationBuilder(args);
publisherHost.Services.AddOpcUa().AddPubSub(pubsub =>
{
    pubsub.AddPublisher()
        .AddKafkaTransport(options =>
        {
            options.Endpoint = "kafkas://broker1.example.com:9093,broker2.example.com:9093";
            options.BootstrapServers = "broker1.example.com:9093,broker2.example.com:9093";
            options.SecurityProtocol = KafkaSecurityProtocol.SaslSsl;
            options.SaslMechanism = KafkaSaslMechanism.ScramSha256;
            options.UserName = "pubsub-publisher";
            options.PasswordSecretId = "KafkaPublisherPassword";
            options.DeliveryGuarantee = KafkaQualityOfService.AtLeastOnce;
            options.Tls = new KafkaTlsOptions
            {
                ValidateServerCertificate = true,
                CaCertificatePath = "pki/kafka/ca.pem"
            };
        })
        .AddDataSetSource("Simple", publishedDataSetSource)
        .ConfigureApplication(app => app.UseConfiguration(BuildKafkaPublisherConfiguration()));
});

HostApplicationBuilder subscriberHost = Host.CreateApplicationBuilder(args);
subscriberHost.Services.AddOpcUa().AddPubSub(pubsub =>
{
    pubsub.AddSubscriber()
        .AddKafkaTransport(options =>
        {
            options.Endpoint = "kafkas://broker1.example.com:9093,broker2.example.com:9093";
            options.GroupId = "opcua-reference-subscribers";
            options.SecurityProtocol = KafkaSecurityProtocol.SaslSsl;
            options.SaslMechanism = KafkaSaslMechanism.ScramSha256;
            options.UserName = "pubsub-subscriber";
            options.PasswordSecretId = "KafkaSubscriberPassword";
            options.AutoOffsetReset = KafkaAutoOffsetReset.Latest;
            options.Tls = new KafkaTlsOptions
            {
                ValidateServerCertificate = true,
                CaCertificatePath = "pki/kafka/ca.pem"
            };
        })
        .AddSubscribedDataSetSink("Reader 1", sp => subscribedDataSetSink)
        .ConfigureApplication(app => app.UseConfiguration(BuildKafkaSubscriberConfiguration()));
});

static PubSubConfigurationDataType BuildKafkaPublisherConfiguration()
{
    return PubSubConfigurationBuilder.Create()
        .AddPublishedDataSet("Simple", dataSet => dataSet
            .AddField("Value", (byte)DataTypes.Int32, DataTypeIds.Int32))
        .AddConnection("Kafka Publisher", connection => connection
            .WithPublisherId(new Variant((ushort)1))
            .WithTransportProfile(KafkaProfiles.PubSubKafkaJsonTransport)
            .WithAddress("kafkas://broker1.example.com:9093,broker2.example.com:9093")
            .AddWriterGroup("WriterGroup 1", group => group
                .WithWriterGroupId(100)
                .WithTransportSettings(new BrokerWriterGroupTransportDataType
                {
                    QueueName = DataTopic
                })
                .WithMessageSettings(new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader |
                        JsonNetworkMessageContentMask.DataSetMessageHeader |
                        JsonNetworkMessageContentMask.PublisherId)
                })
                .AddDataSetWriter("Writer 1", writer => writer
                    .WithDataSetName("Simple")
                    .WithDataSetWriterId(1)
                    .WithTransportSettings(new BrokerDataSetWriterTransportDataType
                    {
                        QueueName = DataTopic,
                        MetaDataQueueName = MetadataTopic,
                        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                    }))))
        .Build();
}

static PubSubConfigurationDataType BuildKafkaSubscriberConfiguration()
{
    return PubSubConfigurationBuilder.Create()
        .AddConnection("Kafka Subscriber", connection => connection
            .WithPublisherId(new Variant((ushort)1))
            .WithTransportProfile(KafkaProfiles.PubSubKafkaJsonTransport)
            .WithAddress("kafkas://broker1.example.com:9093,broker2.example.com:9093")
            .AddReaderGroup("ReaderGroup 1", group => group
                .AddDataSetReader("Reader 1", reader => reader
                    .WithFilter(new Variant((ushort)1), 100, 1)
                    .WithTransportSettings(new BrokerDataSetReaderTransportDataType
                    {
                        QueueName = DataTopic,
                        MetaDataQueueName = MetadataTopic,
                        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                    })
                    .WithMirrorSubscribedDataSet("Reader 1"))))
        .Build();
}
```

## Topic mapping

Kafka uses the OPC UA broker transport settings as topic names. `BrokerDataSetWriterTransportDataType.QueueName` and `BrokerDataSetReaderTransportDataType.QueueName` select the data topic for a specific DataSetWriter/DataSetReader. `BrokerWriterGroupTransportDataType.QueueName` is the writer-group fallback for data messages. `MetaDataQueueName` selects the metadata topic; if it is not set, the transport generates a deterministic fallback topic from `KafkaConnectionOptions.Topics.Prefix`, the encoding (`uadp` or `json`), message type, PublisherId, WriterGroupId, and DataSetWriterId.

Kafka topic names should use Kafka-safe characters such as letters, digits, `.`, `_`, and `-`. The fallback topic builder uses `.` as the segment separator.

## Record headers and delivery guarantees

Every produced Kafka record carries the normative `content-type` record header from Part 14 Annex B.2: `application/opcua+uadp` for `pubsub-kafka-uadp` and `application/json` for `pubsub-kafka-json`. The record key is derived from the PubSubConnection PublisherId when available so records for the same publisher preserve partition ordering.

`KafkaConnectionOptions.DeliveryGuarantee` maps the Part 14 broker QoS intent to Kafka producer settings: `BestEffort` uses `acks=0`, `AtMostOnce` uses `acks=1`, `AtLeastOnce` uses `acks=all` with producer idempotence enabled, and `ExactlyOnce` uses `acks=all` with producer idempotence enabled. Per-writer `RequestedDeliveryGuarantee` values on broker transport settings override the connection default when specified.

## SASL and TLS

Use `kafkas://` or set `KafkaConnectionOptions.Tls.UseTls` for TLS. `KafkaTlsOptions` carries CA, client certificate, and client key PEM paths. Use `SecurityProtocol = KafkaSecurityProtocol.SaslSsl` with `SaslMechanism`, `UserName`, and `PasswordSecretId` for SASL over TLS. Sending SASL credentials over plaintext `kafka://` is rejected unless `AllowCredentialsOverPlaintext` is explicitly enabled for local development or a controlled network.

## NativeAOT note

On `net10.0`, `Opc.Ua.PubSub.Kafka` uses the pure-managed Dekaf Kafka client and is intended to be NativeAOT-compatible. On `net472`, `net48`, `netstandard2.1`, `net8.0`, and `net9.0`, the library uses Confluent.Kafka, which wraps native librdkafka and is not NativeAOT-compatible.
