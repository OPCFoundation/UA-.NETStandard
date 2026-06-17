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

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests.Encoding;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MqttPubSubConnectionAdditionalTests
    {
        private const ushort NamespaceIndexAllTypes = 3;

        [Test]
        public void ConstructorWithInvalidAddressConfigurationLeavesClientOptionsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "InvalidAddress",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new DatagramConnectionTransportDataType())
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            Assert.That(connection.PublisherMqttClientOptions, Is.Null);
            Assert.That(connection.SubscriberMqttClientOptions, Is.Null);
            Assert.That(connection.UrlScheme, Is.Null);
            Assert.That(connection.BrokerHostName, Is.EqualTo("localhost"));
            Assert.That(connection.BrokerPort, Is.EqualTo(Utils.MqttDefaultPort));
        }

        [Test]
        public void ConstructorWithInvalidUrlSchemeLeavesClientOptionsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "InvalidScheme",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "http://localhost:1883"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            Assert.That(connection.PublisherMqttClientOptions, Is.Null);
            Assert.That(connection.SubscriberMqttClientOptions, Is.Null);
            Assert.That(connection.UrlScheme, Is.Null);
        }

        [Test]
        public void StartWithInvalidUrlBlocksMqttOptionAccessUntilStop()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "InvalidStartUrl",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "http://localhost:1883"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            connection.Start();

            try
            {
                Assert.That(connection.IsRunning, Is.True);
                Assert.That(
                    () => _ = connection.PublisherMqttClientOptions,
                    Throws.TypeOf<InvalidConstraintException>());
                Assert.That(
                    () => connection.PublisherMqttClientOptions = null,
                    Throws.TypeOf<InvalidConstraintException>());
                Assert.That(
                    () => _ = connection.SubscriberMqttClientOptions,
                    Throws.TypeOf<InvalidConstraintException>());
                Assert.That(
                    () => connection.SubscriberMqttClientOptions = null,
                    Throws.TypeOf<InvalidConstraintException>());
            }
            finally
            {
                connection.Stop();
            }

            Assert.That(connection.IsRunning, Is.False);
            Assert.That(() => _ = connection.PublisherMqttClientOptions, Throws.Nothing);
            Assert.That(() => connection.PublisherMqttClientOptions = null, Throws.Nothing);
        }

        [Test]
        public void UnsupportedMessageMappingReturnsNullMessagesAndMetadata()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = CreateJsonPublisherConfiguration();
            using var application = UaPubSubApplication.Create(configuration, telemetry);
            MessagesHelper.LoadData(application, NamespaceIndexAllTypes);

            using var connection = new MqttPubSubConnection(
                application,
                configuration.Connections[0],
                (MessageMapping)int.MaxValue,
                telemetry);
            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            DataSetWriterDataType dataSetWriter = writerGroup.DataSetWriters[0];

            IList<UaNetworkMessage> messages = connection.CreateNetworkMessages(
                writerGroup,
                new WriterGroupPublishState());
            UaNetworkMessage metadataMessage = connection.CreateDataSetMetaDataNetworkMessage(
                writerGroup,
                dataSetWriter);

            Assert.That(messages, Is.Null);
            Assert.That(metadataMessage, Is.Null);
        }

        [Test]
        public void CreateDataSetMetaDataNetworkMessageWithMissingPublishedDataSetReturnsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = CreateJsonPublisherConfiguration();
            using var application = UaPubSubApplication.Create(configuration, telemetry);
            MessagesHelper.LoadData(application, NamespaceIndexAllTypes);

            var connection = (MqttPubSubConnection)application.PubSubConnections[0];
            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            DataSetWriterDataType dataSetWriter = writerGroup.DataSetWriters[0];
            dataSetWriter.DataSetName = "MissingDataSet";

            UaNetworkMessage metadataMessage = connection.CreateDataSetMetaDataNetworkMessage(
                writerGroup,
                dataSetWriter);

            Assert.That(metadataMessage, Is.Null);
        }

        [Test]
        public async Task PublishNetworkMessageAsyncBeforeStartReturnsFalseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = CreateJsonPublisherConfiguration();
            using var application = UaPubSubApplication.Create(configuration, telemetry);
            MessagesHelper.LoadData(application, NamespaceIndexAllTypes);

            var connection = (MqttPubSubConnection)application.PubSubConnections[0];
            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            UaNetworkMessage networkMessage = connection
                .CreateNetworkMessages(writerGroup, new WriterGroupPublishState())
                .First(message => !message.IsMetaDataMessage);

            bool published = await connection.PublishNetworkMessageAsync(networkMessage).ConfigureAwait(false);

            Assert.That(published, Is.False);
        }

        [Test]
        public void CanPublishMetaDataWhenConnectionIsNotRunningReturnsFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = CreateJsonPublisherConfiguration();
            using var application = UaPubSubApplication.Create(configuration, telemetry);
            MessagesHelper.LoadData(application, NamespaceIndexAllTypes);

            var connection = (MqttPubSubConnection)application.PubSubConnections[0];
            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            DataSetWriterDataType dataSetWriter = writerGroup.DataSetWriters[0];

            bool canPublishMetaData = connection.CanPublishMetaData(writerGroup, dataSetWriter);

            Assert.That(canPublishMetaData, Is.False);
        }

        [Test]
        public void MatchTopicSupportsWildcardsAndLengthChecks()
        {
            Assert.That(InvokePrivateStatic<bool>("MatchTopic", "#", "a/b/c"), Is.True);
            Assert.That(InvokePrivateStatic<bool>("MatchTopic", "a/+/c", "a/b/c"), Is.True);
            Assert.That(InvokePrivateStatic<bool>("MatchTopic", "a/b", "a/b/c"), Is.False);
            Assert.That(InvokePrivateStatic<bool>("MatchTopic", "a/b/c", "a/x/c"), Is.False);
        }

        [Test]
        public void GetMqttQualityOfServiceLevelMapsExpectedValues()
        {
            Assert.That(
                InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    BrokerTransportQualityOfService.AtLeastOnce),
                Is.EqualTo(MqttQualityOfServiceLevel.AtLeastOnce));
            Assert.That(
                InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    BrokerTransportQualityOfService.AtMostOnce),
                Is.EqualTo(MqttQualityOfServiceLevel.AtMostOnce));
            Assert.That(
                InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    BrokerTransportQualityOfService.ExactlyOnce),
                Is.EqualTo(MqttQualityOfServiceLevel.ExactlyOnce));
            Assert.That(
                InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    BrokerTransportQualityOfService.NotSpecified),
                Is.EqualTo(MqttQualityOfServiceLevel.AtLeastOnce));
            Assert.That(
                () => InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    (BrokerTransportQualityOfService)int.MaxValue),
                Throws.TypeOf<TargetInvocationException>()
                    .With.InnerException.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AreClientsConnectedReturnsTrueWhenNoClientsExist()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "NoClients",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://localhost:1883"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            Assert.That(connection.AreClientsConnected(), Is.True);
        }

        [Test]
        public void IsAcceptableStatusHonorsTlsFlags()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "TlsFlags",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://localhost:1883"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);
            SetPrivateField(
                connection,
                "m_mqttClientTlsOptions",
                new MqttClientTlsOptions
                {
                    IgnoreCertificateRevocationErrors = true,
                    IgnoreCertificateChainErrors = true,
                    AllowUntrustedCertificates = true
                });

            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableStatus", StatusCodes.BadCertificateRevoked),
                Is.True);
            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableStatus", StatusCodes.BadCertificateChainIncomplete),
                Is.True);
            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableStatus", StatusCodes.BadCertificateUntrusted),
                Is.True);
            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableStatus", StatusCodes.BadSecurityChecksFailed),
                Is.False);
        }

        private static PubSubConfigurationDataType CreateJsonPublisherConfiguration()
        {
            return MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                "mqtt://localhost:1883",
                Variant.From("publisher"),
                writerGroupId: 1,
                jsonNetworkMessageContentMask:
                    JsonNetworkMessageContentMask.NetworkMessageHeader |
                    JsonNetworkMessageContentMask.PublisherId |
                    JsonNetworkMessageContentMask.DataSetMessageHeader,
                jsonDataSetMessageContentMask: JsonDataSetMessageContentMask.DataSetWriterId,
                dataSetFieldContentMask: DataSetFieldContentMask.None,
                dataSetMetaDataArray:
                [
                    MessagesHelper.CreateDataSetMetaData1("DataSet1")
                ],
                nameSpaceIndexForData: NamespaceIndexAllTypes);
        }

        private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
        {
            object result = instance.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, args);
            return (T)result;
        }

        private static T InvokePrivateStatic<T>(string methodName, params object[] args)
        {
            object result = typeof(MqttPubSubConnection)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, args);
            return (T)result;
        }

        // -----------------------------------------------------------------------
        // ProcessMqttMessage – no matching readers
        // -----------------------------------------------------------------------

        [Test]
        public async Task ProcessMqttMessageWithNoMatchingReadersDoesNotThrowAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = CreateJsonPublisherConfiguration();
            using var application = UaPubSubApplication.Create(configuration, telemetry);
            MessagesHelper.LoadData(application, NamespaceIndexAllTypes);

            var connection = (MqttPubSubConnection)application.PubSubConnections[0];

            var appMsg = new MqttApplicationMessage { Topic = "no/matching/topic" };
            var args = new MqttApplicationMessageReceivedEventArgs(
                "clientId",
                appMsg,
                new MQTTnet.Packets.MqttPublishPacket(),
                null!);

            Task result = (Task)typeof(MqttPubSubConnection)
                .GetMethod("ProcessMqttMessage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, [args])!;

            Assert.That(async () => await result.ConfigureAwait(false), Throws.Nothing);
        }

        // -----------------------------------------------------------------------
        // ProcessMqttMessage – message marked as handled
        // -----------------------------------------------------------------------

        [Test]
        public async Task ProcessMqttMessageWithHandledRawDataEarlyReturnsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType configuration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                "mqtt://localhost:1883",
                Variant.From("publisher"),
                writerGroupId: 1,
                setDataSetWriterId: true,
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetMessageHeader,
                JsonDataSetMessageContentMask.DataSetWriterId,
                DataSetFieldContentMask.None,
                [MessagesHelper.CreateDataSetMetaData1("DataSet1")],
                NamespaceIndexAllTypes);

            using var application = UaPubSubApplication.Create(configuration, telemetry);
            var connection = (MqttPubSubConnection)application.PubSubConnections[0];

            bool eventWasRaised = false;
            application.RawDataReceived += (s, e) =>
            {
                eventWasRaised = true;
                e.Handled = true;
            };

            // "WriterGroup id:1" is the queue name created by the subscriber helper.
            var appMsg = new MqttApplicationMessage
            {
                Topic = "WriterGroup id:1",
                PayloadSegment = new ArraySegment<byte>(new byte[] { 0 })
            };
            var args = new MqttApplicationMessageReceivedEventArgs(
                "clientId",
                appMsg,
                new MQTTnet.Packets.MqttPublishPacket(),
                null!);

            Task result = (Task)typeof(MqttPubSubConnection)
                .GetMethod("ProcessMqttMessage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, [args])!;

            await result.ConfigureAwait(false);

            Assert.That(eventWasRaised, Is.True,
                "RawDataReceived event should have been raised for a matching topic");
        }

        // -----------------------------------------------------------------------
        // GetMqttClientOptions – valid mqtt:// URL produces non-null options
        // -----------------------------------------------------------------------

        [Test]
        public void GetMqttClientOptionsWithValidMqttUrlCreatesNonNullOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "ValidMqttUrl",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://broker.example.com:1883"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            Assert.That(connection.PublisherMqttClientOptions, Is.Not.Null);
            Assert.That(connection.SubscriberMqttClientOptions, Is.Not.Null);
            Assert.That(connection.BrokerHostName, Is.EqualTo("broker.example.com"));
            Assert.That(connection.BrokerPort, Is.EqualTo(1883));
            Assert.That(connection.UrlScheme, Is.EqualTo(Utils.UriSchemeMqtt));
        }

        [Test]
        public void GetMqttClientOptionsWithMqttsUrlUsesDefaultTlsPort()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "MqttsNoPort",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtts://secure.broker.example.com"
                })
            };

            using var connection = new MqttPubSubConnection(
                application,
                connectionConfiguration,
                MessageMapping.Json,
                telemetry);

            // No explicit port in the URL → should fall back to 8883 for mqtts
            Assert.That(connection.BrokerPort, Is.EqualTo(8883));
            Assert.That(connection.UrlScheme, Is.EqualTo(Utils.UriSchemeMqtts));
        }

        // -----------------------------------------------------------------------
        // GetMqttQualityOfServiceLevel – BestEffort maps to AtLeastOnce
        // -----------------------------------------------------------------------

        [Test]
        public void GetMqttQualityOfServiceLevelBestEffortMapsToAtLeastOnce()
        {
            Assert.That(
                InvokePrivateStatic<MqttQualityOfServiceLevel>(
                    "GetMqttQualityOfServiceLevel",
                    BrokerTransportQualityOfService.BestEffort),
                Is.EqualTo(MqttQualityOfServiceLevel.AtLeastOnce));
        }

        // -----------------------------------------------------------------------
        // IsAcceptableValidationFailure – various error-list combinations
        // -----------------------------------------------------------------------

        [Test]
        public void IsAcceptableValidationFailureWithMultipleErrorsAllAcceptableReturnsTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "TlsAllFlags",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://localhost:1883"
                })
            };
            using var connection = new MqttPubSubConnection(
                application, connectionConfiguration, MessageMapping.Json, telemetry);
            SetPrivateField(
                connection,
                "m_mqttClientTlsOptions",
                new MqttClientTlsOptions
                {
                    IgnoreCertificateRevocationErrors = true,
                    IgnoreCertificateChainErrors = true,
                    AllowUntrustedCertificates = true
                });

            var errors = new List<ServiceResult>
            {
                new ServiceResult(StatusCodes.BadCertificateRevoked),
                new ServiceResult(StatusCodes.BadCertificateChainIncomplete),
                new ServiceResult(StatusCodes.BadCertificateUntrusted)
            };
            var validationResult = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadCertificateRevoked,
                errors: errors,
                isSuppressible: true);

            bool accepted = InvokePrivate<bool>(connection, "IsAcceptableValidationFailure", validationResult);

            Assert.That(accepted, Is.True);
        }

        [Test]
        public void IsAcceptableValidationFailureWithSomeNotAcceptableReturnsFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "TlsOnlyRevocation",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://localhost:1883"
                })
            };
            using var connection = new MqttPubSubConnection(
                application, connectionConfiguration, MessageMapping.Json, telemetry);
            SetPrivateField(
                connection,
                "m_mqttClientTlsOptions",
                new MqttClientTlsOptions
                {
                    IgnoreCertificateRevocationErrors = true,
                    IgnoreCertificateChainErrors = false,
                    AllowUntrustedCertificates = false
                });

            // RevocationUnknown is acceptable; SecurityChecksFailed is NOT.
            var errors = new List<ServiceResult>
            {
                new ServiceResult(StatusCodes.BadCertificateRevoked),
                new ServiceResult(StatusCodes.BadSecurityChecksFailed)
            };
            var validationResult = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadSecurityChecksFailed,
                errors: errors,
                isSuppressible: false);

            bool accepted = InvokePrivate<bool>(connection, "IsAcceptableValidationFailure", validationResult);

            Assert.That(accepted, Is.False);
        }

        [Test]
        public void IsAcceptableValidationFailureWithEmptyErrorsListDelegatesToStatusCode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var application = UaPubSubApplication.Create(telemetry);
            var connectionConfiguration = new PubSubConnectionDataType
            {
                Name = "TlsEmptyErrors",
                Enabled = true,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://localhost:1883"
                })
            };
            using var connection = new MqttPubSubConnection(
                application, connectionConfiguration, MessageMapping.Json, telemetry);
            SetPrivateField(
                connection,
                "m_mqttClientTlsOptions",
                new MqttClientTlsOptions
                {
                    IgnoreCertificateRevocationErrors = true,
                    IgnoreCertificateChainErrors = false,
                    AllowUntrustedCertificates = false
                });

            // Empty errors list → delegates to IsAcceptableStatus(statusCode)
            // BadCertificateRevoked is acceptable when ignoreRevocation = true
            var acceptableResult = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadCertificateRevoked,
                errors: [],
                isSuppressible: true);

            // BadSecurityChecksFailed is NOT acceptable (no matching TLS flag)
            var notAcceptableResult = new CertificateValidationResult(
                isValid: false,
                statusCode: StatusCodes.BadSecurityChecksFailed,
                errors: [],
                isSuppressible: false);

            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableValidationFailure", acceptableResult),
                Is.True);
            Assert.That(
                InvokePrivate<bool>(connection, "IsAcceptableValidationFailure", notAcceptableResult),
                Is.False);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            instance.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(instance, value);
        }
    }
}
