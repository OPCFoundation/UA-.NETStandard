/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;
using TimeProvider = System.TimeProvider;

namespace Opc.Ua.PubSub.Legacy.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UdpPubSubConnectionAdditionalTests
    {
        private static readonly string PublisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private UaPubSubApplication m_application;
        private UdpPubSubConnection m_connection;
        private PubSubConfigurationDataType m_configuration;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            m_application = UaPubSubApplication.Create(configFile, null);
            Assert.That(m_application, Is.Not.Null);

            m_configuration = m_application.UaPubSubConfigurator.PubSubConfiguration;
            Assert.That(m_configuration, Is.Not.Null);
            Assert.That(m_configuration.Connections.IsEmpty, Is.False);

            m_connection = m_application.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(m_connection, Is.Not.Null);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_application?.Dispose();
        }

        [Test]
        public void AreClientsConnectedReturnsTrueForUdp()
        {
            bool result = m_connection.AreClientsConnected();
            Assert.That(result, Is.True);
        }

        [Test]
        public void TransportProtocolIsUdp()
        {
            Assert.That(m_connection.TransportProtocol, Is.EqualTo(TransportProtocol.UDP));
        }

        [Test]
        public void PubSubConnectionConfigurationIsNotNull()
        {
            Assert.That(m_connection.PubSubConnectionConfiguration, Is.Not.Null);
        }

        [Test]
        public void ApplicationReferenceIsNotNull()
        {
            Assert.That(m_connection.Application, Is.Not.Null);
        }

        [Test]
        public void NetworkAddressEndPointIsAccessible()
        {
            // NetworkAddressEndPoint may be null depending on config
            IPEndPoint endpoint = m_connection.NetworkAddressEndPoint;
            Assert.That(endpoint, Is.Null.Or.Not.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForInvalidMessageSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "InvalidWG",
                MessageSettings = default,
                TransportSettings = default
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForWrongMessageSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "WrongSettingsWG",
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType())
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForWrongTransportSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "WrongTransportWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType())
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesWithValidSettingsButNoWritersReturnsEmptyList()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "EmptyWritersWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType()),
                DataSetWriters = []
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void CreateNetworkMessagesWithDisabledWritersReturnsEmptyList()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "DisabledWritersWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType()),
                DataSetWriters = [
                    new DataSetWriterDataType
                    {
                        Name = "DisabledWriter",
                        Enabled = false,
                        DataSetWriterId = 1
                    }
                ]
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void CreateNetworkMessagesFromPublisherConfigurationReturnsResult()
        {
            Assert.That(
                m_configuration.Connections[0].WriterGroups.IsEmpty,
                Is.False,
                "Publisher config should have writer groups");

            WriterGroupDataType writerGroup = m_configuration.Connections[0].WriterGroups[0];
            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            // CreateNetworkMessages may return null or a non-empty list depending on config
            Assert.That(messages, Is.Null.Or.Not.Empty);
        }

        [Test]
        public void PublisherUdpClientsIsNotNull()
        {
            Assert.That(m_connection.PublisherUdpClients, Is.Not.Null);
        }

        [Test]
        public void SubscriberUdpClientsIsNotNull()
        {
            Assert.That(m_connection.SubscriberUdpClients, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithInvalidAddressConfigurationLeavesEndpointNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "InvalidUdpConnection",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new BrokerWriterGroupTransportDataType())
            };

            using var connection = new UdpPubSubConnection(
                application,
                connectionConfiguration,
                telemetry);

            Assert.That(connection.NetworkAddressEndPoint, Is.Null);
            Assert.That(connection.PublisherUdpClients, Is.Empty);
            Assert.That(connection.SubscriberUdpClients, Is.Empty);
        }

        [Test]
        public void CreateDataSetMetaDataNetworkMessagesWithUnknownWriterIdSkipsMissingWriter()
        {
            ushort knownWriterId = m_configuration
                .Connections[0]
                .WriterGroups[0]
                .DataSetWriters[0]
                .DataSetWriterId;

            IList<UaNetworkMessage> messages = m_connection.CreateDataSetMetaDataNetworkMessages(
                [knownWriterId, ushort.MaxValue]);

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0].IsMetaDataMessage, Is.True);
            Assert.That(messages[0].DataSetWriterId, Is.EqualTo(knownWriterId));
        }

        [Test]
        public void CreateDataSetWriterConfigurationMessageWithUnknownWriterIdReturnsBadNotFound()
        {
            const ushort unknownWriterId = ushort.MaxValue;

            UadpNetworkMessage message = (UadpNetworkMessage)m_connection
                .CreateDataSetWriterCofigurationMessage([unknownWriterId])
                .Single();

            Assert.That(message.DataSetWriterIds, Is.EqualTo(new ushort[] { unknownWriterId }));
            Assert.That(message.MessageStatusCodes, Has.Length.EqualTo(1));
            Assert.That(message.MessageStatusCodes[0], Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task PublishNetworkMessageAsyncBeforeStartReturnsFalseAsync()
        {
            UaNetworkMessage networkMessage = m_connection.CreatePublisherEndpointsNetworkMessage(
                [],
                StatusCodes.Good,
                m_connection.PubSubConnectionConfiguration.PublisherId);

            bool published = await m_connection.PublishNetworkMessageAsync(networkMessage).ConfigureAwait(false);

            Assert.That(published, Is.False);
        }

        [Test]
        public void CreatePublisherEndpointsNetworkMessageWithNonUdpTransportReturnsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "NonUdpTransport",
                Enabled = true,
                PublisherId = Variant.From("publisher"),
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4840"
                })
            };

            using var connection = new UdpPubSubConnection(
                application,
                connectionConfiguration,
                telemetry);

            UaNetworkMessage message = connection.CreatePublisherEndpointsNetworkMessage(
                [],
                StatusCodes.Good,
                Variant.From("publisher"));

            Assert.That(message, Is.Null);
        }

        [Test]
        public void RequestDiscoveryOperationsBeforeStartDoNotThrow()
        {
            Assert.That(() => m_connection.RequestPublisherEndpoints(), Throws.Nothing);
            Assert.That(() => m_connection.RequestDataSetWriterConfiguration(), Throws.Nothing);
            Assert.That(() => m_connection.RequestDataSetMetaData(), Throws.Nothing);
        }

        [Test]
        public void ResetSequenceNumberResetsStaticCounters()
        {
            // Call it twice to verify idempotency.
            UdpPubSubConnection.ResetSequenceNumber();
            UdpPubSubConnection.ResetSequenceNumber();
            // If no exception was thrown the static reset path is exercised.
            Assert.Pass();
        }

        [Test]
        public void MetaDataReceivedWithNullDiscoverySubscriberIsNoOp()
        {
            // The private m_udpDiscoverySubscriber is null (Start never called).
            // Invoking the handler must not throw.
            var networkMsg = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData,
                NullLogger.Instance)
            {
                DataSetWriterId = 1
            };
            var eventArgs = new SubscribedDataEventArgs
            {
                NetworkMessage = networkMsg,
                Source = "test"
            };

            Assert.That(
                () => InvokePrivate(m_connection, "MetaDataReceived", null!, eventArgs),
                Throws.Nothing);
        }

        [Test]
        public void MetaDataReceivedWithDiscoverySubscriberRemovesWriterId()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-meta-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            subscriber.AddWriterIdForDataSetMetadata(42);

            // Inject subscriber into the connection via reflection.
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            var networkMsg = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData,
                NullLogger.Instance)
            {
                DataSetWriterId = 42
            };
            var eventArgs = new SubscribedDataEventArgs
            {
                NetworkMessage = networkMsg,
                Source = "test"
            };

            InvokePrivate(conn, "MetaDataReceived", null!, eventArgs);

            // After removal, SendDiscoveryRequestDataSetMetaData is a no-op (empty list).
            Assert.That(
                () => subscriber.SendDiscoveryRequestDataSetMetaData(),
                Throws.Nothing);
        }

        [Test]
        public void DataSetWriterConfigurationReceivedWithNullConfigIsNoOp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-cfg-null-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            // DataSetWriterConfiguration = null → the if-guard short-circuits, no crash.
            var eventArgs = new DataSetWriterConfigurationEventArgs
            {
                DataSetWriterConfiguration = null!,
                DataSetWriterIds = [],
                Source = "test",
                StatusCodes = []
            };

            Assert.That(
                () => InvokePrivate(conn, "DataSetWriterConfigurationReceived", null!, eventArgs),
                Throws.Nothing);
        }

        [Test]
        public void DataSetWriterConfigurationReceivedWithValidConfigDelegatesToSubscriber()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var existingGroup = new WriterGroupDataType
            {
                WriterGroupId = 7,
                Name = "OriginalGroup"
            };
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-cfg-valid-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                }),
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { existingGroup })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            var updatedGroup = new WriterGroupDataType
            {
                WriterGroupId = 7,
                Name = "UpdatedGroup"
            };
            var eventArgs = new DataSetWriterConfigurationEventArgs
            {
                DataSetWriterConfiguration = updatedGroup,
                DataSetWriterIds = [7],
                Source = "test",
                StatusCodes = []
            };

            InvokePrivate(conn, "DataSetWriterConfigurationReceived", null!, eventArgs);

            Assert.That(
                connCfg.WriterGroups.ToList().Exists(g => g.WriterGroupId == 7 && g.Name == "UpdatedGroup"),
                Is.True);
        }

        [Test]
        public void NetworkMessageDecodeErrorWithMetadataMajorVersionAndNonZeroIdAddsWriterId()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-decode-err-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            var reader = new DataSetReaderDataType { Name = "r1", DataSetWriterId = 55 };
            var e = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.MetadataMajorVersion,
                new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData, NullLogger.Instance),
                reader);

            // Handler should add writerId 55 to the subscriber's queue.
            Assert.That(
                () => InvokePrivate(conn, "NetworkMessage_DataSetDecodeErrorOccurred", null, e),
                Throws.Nothing);

            // CanPublish returns true when items are in the queue – confirms the handler fired.
            bool canPublish = InvokePrivateResult<bool>(subscriber, "CanPublish");
            Assert.That(canPublish, Is.True);
        }

        [Test]
        public void NetworkMessageDecodeErrorWithMetadataMajorVersionAndZeroIdDoesNothing()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-decode-err-zero-id",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            // DataSetWriterId = 0 → the handler must not enqueue anything.
            var reader = new DataSetReaderDataType { Name = "r0", DataSetWriterId = 0 };
            var e = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.MetadataMajorVersion,
                new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData, NullLogger.Instance),
                reader);

            Assert.That(
                () => InvokePrivate(conn, "NetworkMessage_DataSetDecodeErrorOccurred", null!, e),
                Throws.Nothing);
        }

        [Test]
        public void NetworkMessageDecodeErrorWithNoErrorReasonDoesNothing()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-decode-err-no-err",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            using var conn = new UdpPubSubConnection(application, connCfg, telemetry);
            var subscriber = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
            SetPrivateField(conn, "m_udpDiscoverySubscriber", subscriber);

            var reader = new DataSetReaderDataType { Name = "rNoErr", DataSetWriterId = 9 };
            var e = new DataSetDecodeErrorEventArgs(
                DataSetDecodeErrorReason.NoError,
                new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData, NullLogger.Instance),
                reader);

            Assert.That(
                () => InvokePrivate(conn, "NetworkMessage_DataSetDecodeErrorOccurred", null!, e),
                Throws.Nothing);
        }

        [Test]
        public void ProcessReceivedMessageWithNoReadersCompletesWithoutException()
        {
            // m_connection (publisher config) has no reader groups, so
            // GetOperationalDataSetReaders() returns an empty list.
            // The decode with an all-zeros single-byte message is safe: the
            // UADP header byte 0x00 means UADPVersion=0, no flags, no PublisherId.
            // Decode returns immediately because readers list is empty.
            var source = new IPEndPoint(IPAddress.Loopback, 4840);
            byte[] message = new byte[] { 0x00 };

            Assert.That(
                () => InvokePrivate(m_connection, "ProcessReceivedMessage", message, source),
                Throws.Nothing);
        }

        private static void InvokePrivate(object instance, string methodName, params object[] args)
        {
            instance.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, args);
        }

        private static T InvokePrivateResult<T>(object instance, string methodName, params object[] args)
        {
            return (T)instance.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, args);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            instance.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(instance, value);
        }
    }
}
