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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    internal static class KafkaTestHelper
    {
        public const string EndpointUrl = "kafka://broker.example.com:9092";
        public const string JsonTopic = "opcua.json.data.42.1.3";
        public const string UadpTopic = "opcua.uadp.data.42.1.3";
        public const string MetadataTopic = "opcua.json.metadata.42.1.3";

        public static PubSubConnectionDataType NewConnection(
            string url = EndpointUrl,
            bool writer = true,
            bool reader = false,
            string profile = KafkaProfiles.PubSubKafkaJsonTransport,
            string? dataTopic = null,
            string? metadataTopic = null,
            BrokerTransportQualityOfService requestedDeliveryGuarantee =
                BrokerTransportQualityOfService.NotSpecified)
        {
            var connection = new PubSubConnectionDataType
            {
                Name = writer ? "Publisher" : "Subscriber",
                TransportProfileUri = profile,
                PublisherId = new Variant((uint)42),
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = url })
            };
            if (writer)
            {
                var group = new WriterGroupDataType
                {
                    Name = "WriterGroup1",
                    WriterGroupId = 1,
                    MessageSettings = new ExtensionObject(CreateWriterGroupMessageSettings(profile))
                };
                if (requestedDeliveryGuarantee != BrokerTransportQualityOfService.NotSpecified)
                {
                    group.TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType
                    {
                        RequestedDeliveryGuarantee = requestedDeliveryGuarantee
                    });
                }
                var dataSetWriter = new DataSetWriterDataType
                {
                    Name = "DataSetWriter1",
                    DataSetWriterId = 3
                };
                if (dataTopic is not null || metadataTopic is not null)
                {
                    dataSetWriter.TransportSettings = new ExtensionObject(new BrokerDataSetWriterTransportDataType
                    {
                        QueueName = dataTopic,
                        MetaDataQueueName = metadataTopic
                    });
                }
                group.DataSetWriters = group.DataSetWriters.AddItem(dataSetWriter);
                connection.WriterGroups = connection.WriterGroups.AddItem(group);
            }
            if (reader)
            {
                var readerGroup = new ReaderGroupDataType { Name = "ReaderGroup1" };
                var dataSetReader = new DataSetReaderDataType { Name = "DataSetReader1" };
                if (dataTopic is not null || metadataTopic is not null)
                {
                    dataSetReader.TransportSettings = new ExtensionObject(new BrokerDataSetReaderTransportDataType
                    {
                        QueueName = dataTopic,
                        MetaDataQueueName = metadataTopic
                    });
                }
                readerGroup.DataSetReaders = readerGroup.DataSetReaders.AddItem(dataSetReader);
                connection.ReaderGroups = connection.ReaderGroups.AddItem(readerGroup);
            }
            return connection;
        }

        public static KafkaBrokerTransport NewTransport(
            FakeKafkaClientFactory factory,
            PubSubTransportDirection direction = PubSubTransportDirection.Send,
            KafkaConnectionOptions? options = null,
            PubSubConnectionDataType? connection = null,
            IPubSubDiagnostics? diagnostics = null)
        {
            PubSubConnectionDataType conn = connection ?? NewConnection();
            return new KafkaBrokerTransport(
                conn,
                KafkaEndpointParser.Parse(EndpointUrl),
                direction,
                options ??
                new KafkaConnectionOptions
                {
                    Endpoint = EndpointUrl,
                    BootstrapServers = "broker.example.com:9092"
                },
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                diagnostics);
        }

        public static KafkaPubSubTransportFactory NewFactory(
            string transportProfileUri = KafkaProfiles.PubSubKafkaJsonTransport,
            IKafkaClientFactory? clientFactory = null,
            KafkaConnectionOptions? options = null,
            ISecretRegistry? secretRegistry = null,
            IPubSubDiagnostics? diagnostics = null)
        {
            return new KafkaPubSubTransportFactory(
                transportProfileUri,
                clientFactory ?? new FakeKafkaClientFactory(),
                Options.Create(options ?? new KafkaConnectionOptions()),
                secretRegistry,
                diagnostics);
        }

        public static async Task<PubSubTransportFrame?> ReceiveOneAsync(
            IPubSubTransport transport,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (PubSubTransportFrame frame in transport.ReceiveAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    return frame;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            return null;
        }

        public static KafkaMessage FirstProduced(FakeKafkaClientAdapter adapter)
        {
            Assert.That(adapter.ProducedMessages, Has.Count.EqualTo(1));
            var queue = (ConcurrentQueue<KafkaMessage>)adapter.ProducedMessages;
            bool hasValue = queue.TryPeek(out KafkaMessage message);
            Assert.That(hasValue, Is.True);
            return message;
        }

        private static IEncodeable CreateWriterGroupMessageSettings(string profile)
        {
            return string.Equals(profile, KafkaProfiles.PubSubKafkaUadpTransport, StringComparison.Ordinal)
                ? new UadpWriterGroupMessageDataType()
                : new JsonWriterGroupMessageDataType();
        }
    }
}
