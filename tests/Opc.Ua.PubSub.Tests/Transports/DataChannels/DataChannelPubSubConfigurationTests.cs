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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports.DataChannels
{
    /// <summary>
    /// Verifies carrier-independent payload selection and fail-closed flow configuration.
    /// </summary>
    [TestFixture]
    public sealed class DataChannelPubSubConfigurationTests
    {
        /// <summary>
        /// Accepts UADP on each supported carrier without an address or Session.
        /// </summary>
        [Test]
        public void EveryCarrierAcceptsUadpWithoutAnAddressOrSession([Values] DataChannelCarrierKind carrier)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier);
            var request = new DataChannelPubSubFlowRequest(connection);
            var validator = new PubSubConfigurationValidator(
                [DataChannelPubSubConfiguration.GetTransportProfile(carrier)]);

            PubSubConfigurationValidationResult result = validator.Validate(new PubSubConfigurationDataType
            {
                Connections = [connection]
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.True);
                Assert.That(request.CarrierKind, Is.EqualTo(carrier));
                Assert.That(request.Parameters.Protocol, Is.EqualTo(DataChannelProfiles.UadpPayload));
                Assert.That(request.Parameters.AllowQueueExpiry, Is.False);
                Assert.That(BitConverter.DoubleToInt64Bits(request.Parameters.MaxRate), Is.Zero);
                Assert.That(connection.Address.IsNull, Is.True);
                Assert.That(request.Direction, Is.EqualTo(PubSubTransportDirection.None));
                Assert.That(DataChannelPubSubConfiguration.TryGetCarrierKind(
                    connection.TransportProfileUri, out DataChannelCarrierKind actual), Is.True);
                Assert.That(actual, Is.EqualTo(carrier));
            });
        }

        [Test]
        public void JsonPayloadIsRejectedForPubSubDataChannels([Values] DataChannelCarrierKind carrier)
        {
            Assert.That(
                () => CreateConnection(carrier, DataChannelProfiles.JsonPayload),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("requires a UADP"));
        }

        [Test]
        public void ConfigurePreservesPubSubIdentitiesAndCopiesTheDescriptor()
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "connection",
                PublisherId = new Variant(73001u),
                ConnectionProperties =
                [
                    new KeyValuePair { Key = new QualifiedName("existing"), Value = new Variant(9) }
                ],
                WriterGroups =
                [
                    new WriterGroupDataType
                    {
                        WriterGroupId = 42,
                        DataSetWriters = [new DataSetWriterDataType { DataSetWriterId = 1001 }]
                    }
                ],
                ReaderGroups =
                [
                    new ReaderGroupDataType
                    {
                        DataSetReaders = [new DataSetReaderDataType { PublisherId = new Variant(73002u) }]
                    }
                ]
            };
            DataChannelParametersDataType parameters = CreateParameters();
            DataChannelPubSubConfiguration.Configure(connection, DataChannelCarrierKind.SharedUasc, parameters);
            parameters.Protocol = DataChannelProfiles.OpaquePayload;
            DataChannelParametersDataType returned = DataChannelPubSubConfiguration.GetParameters(connection);
            returned.MaxMessageSize = 1;

            Assert.Multiple(() =>
            {
                Assert.That(connection.PublisherId, Is.EqualTo(new Variant(73001u)));
                Assert.That(connection.WriterGroups[0].WriterGroupId, Is.EqualTo(42));
                Assert.That(connection.WriterGroups[0].DataSetWriters[0].DataSetWriterId, Is.EqualTo(1001));
                Assert.That(connection.ReaderGroups[0].DataSetReaders[0].PublisherId, Is.EqualTo(new Variant(73002u)));
                Assert.That(connection.ConnectionProperties[0].Value, Is.EqualTo(new Variant(9)));
                Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).Protocol,
                    Is.EqualTo(DataChannelProfiles.UadpPayload));
                Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).MaxMessageSize, Is.EqualTo(65536));
                Assert.That(new DataChannelPubSubFlowRequest(connection).Direction,
                    Is.EqualTo(PubSubTransportDirection.SendReceive));
            });
        }

        [TestCase("opc.tcp://uafx.example:4840")]
        [TestCase("opc.wss://uafx.example:443")]
        public void UafxConfigurationUsesStandardProfileAndKeepsLocalFlowPolicyOutOfPubSubConfig(string endpoint)
        {
            PubSubConnectionDataType connection = CreateUafxConnection(endpoint);
            connection.ConnectionProperties =
            [
                new KeyValuePair { Key = new QualifiedName("unrelated"), Value = new Variant(9) },
                new KeyValuePair
                {
                    Key = new QualifiedName(DataChannelPubSubConfiguration.ParametersProperty),
                    Value = new Variant(new ExtensionObject(CreateParameters()))
                }
            ];
            DataChannelParametersDataType parameters = CreateUafxParameters();

            DataChannelPubSubConfiguration.ConfigureUafx(connection, parameters);

            Assert.Multiple(() =>
            {
                Assert.That(connection.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                Assert.That(connection.ConnectionProperties, Has.Count.EqualTo(1));
                Assert.That(connection.ConnectionProperties[0].Key.Name, Is.EqualTo("unrelated"));
                Assert.That(connection.Address.TryGetValue(out NetworkAddressUrlDataType? address), Is.True);
                Assert.That(address!.Url, Is.EqualTo(endpoint));
                Assert.That(DataChannelPubSubConfiguration.TryGetCarrierKind(
                    connection.TransportProfileUri, out _), Is.False);
                Assert.That(new PubSubConfigurationValidator(
                    [DataChannelPubSubConfiguration.UafxTransportProfile]).Validate(
                        new PubSubConfigurationDataType { Connections = [connection] }).IsValid, Is.True);
            });

            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                provider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ => parameters);
            IPubSubTransport transport = factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(transport.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                Assert.That(((IPubSubEncodingTransport)transport).EncodingProfileUri,
                    Is.EqualTo(Profiles.PubSubUdpUadpTransport));
            });
            provider.VerifyNoOtherCalls();
        }

        [TestCase(DataChannelProfiles.JsonPayload, "application/octet-stream",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Confidential, false)]
        [TestCase(DataChannelProfiles.UadpAvApplicationPayload, "application/octet-stream",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Confidential, false)]
        [TestCase(DataChannelProfiles.UadpOpaqueApplicationPayload, "application/octet-stream",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Confidential, false)]
        [TestCase(DataChannelProfiles.UadpPayload, "application/opcua+uadp",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Confidential, false)]
        [TestCase(DataChannelProfiles.UadpPayload, "application/octet-stream",
            DataChannelDeliveryMode.Datagram, DataChannelProtectionMode.Confidential, false)]
        [TestCase(DataChannelProfiles.UadpPayload, "application/octet-stream",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Integrity, false)]
        [TestCase(DataChannelProfiles.UadpPayload, "application/octet-stream",
            DataChannelDeliveryMode.Ordered, DataChannelProtectionMode.Confidential, true)]
        public void UafxConfigurationRejectsNonconformingLocalFlowPolicy(
            string protocol,
            string contentType,
            DataChannelDeliveryMode delivery,
            DataChannelProtectionMode protection,
            bool allowQueueExpiry)
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            DataChannelParametersDataType parameters = CreateUafxParameters();
            parameters.Protocol = protocol;
            parameters.ContentType = contentType;
            parameters.Delivery = delivery;
            parameters.Protection = protection;
            parameters.AllowQueueExpiry = allowQueueExpiry;

            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, parameters),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        [Test]
        public void UafxConfigurationRejectsMessageSettingsFromOtherModels()
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writer",
                    MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType())
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>());

            connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writer",
                    DataSetWriters =
                    [
                        new DataSetWriterDataType
                        {
                            Name = "dataWriter",
                            MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType())
                        }
                    ]
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>());

            connection = CreateUafxConnection();
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "reader",
                    MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType())
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>());

            connection = CreateUafxConnection();
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "reader",
                    DataSetReaders =
                    [
                        new DataSetReaderDataType
                        {
                            Name = "dataReader",
                            MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType())
                        }
                    ]
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void UafxConfigurationAcceptsStandardUadpMessageSettings()
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writer",
                    MaxNetworkMessageSize = 65536,
                    MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                    DataSetWriters =
                    [
                        new DataSetWriterDataType
                        {
                            Name = "dataWriter",
                            MessageSettings = new ExtensionObject(new UadpDataSetWriterMessageDataType())
                        }
                    ]
                }
            ];
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "reader",
                    MaxNetworkMessageSize = 65536,
                    MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType()),
                    DataSetReaders =
                    [
                        new DataSetReaderDataType
                        {
                            Name = "dataReader",
                            MessageSettings = new ExtensionObject(new UadpDataSetReaderMessageDataType())
                        }
                    ]
                }
            ];

            Assert.That(
                DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Is.SameAs(connection));
        }

        [Test]
        public void UafxFlowRequestPreservesTypedReaderAssociations()
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    MaxNetworkMessageSize = 65536,
                    DataSetReaders =
                    [
                        new DataSetReaderDataType
                        {
                            PublisherId = new Variant(73001u),
                            WriterGroupId = 17,
                            DataSetWriterId = 101
                        },
                        new DataSetReaderDataType
                        {
                            PublisherId = new Variant("73001"),
                            WriterGroupId = 18,
                            DataSetWriterId = 102
                        }
                    ]
                }
            ];
            DataChannelParametersDataType localParameters = CreateUafxParameters();
            DataChannelPubSubConfiguration.ConfigureUafx(connection, localParameters);
            DataChannelParametersDataType validatedParameters =
                DataChannelPubSubConfiguration.GetUafxParameters(connection, _ => localParameters);
            var request = new DataChannelPubSubFlowRequest(connection, validatedParameters);

            Assert.Multiple(() =>
            {
                Assert.That(request.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(request.Direction, Is.EqualTo(PubSubTransportDirection.Receive));
                Assert.That(request.InboundPublisherIds.ToArray(), Is.EqualTo(new[]
                {
                    new Variant(73001u),
                    new Variant("73001")
                }));
                Assert.That(request.InboundAssociations.ToArray(), Has.Length.EqualTo(16));
            });
            AssertReaderAssociations(request.InboundAssociations, new Variant(73001u), 17, 101);
            AssertReaderAssociations(request.InboundAssociations, new Variant("73001"), 18, 102);
        }

        [TestCase("opc.quic://uafx.example:4840")]
        [TestCase("https://uafx.example:443")]
        public void UafxConfigurationRejectsUnsupportedEndpointSchemes(string endpoint)
        {
            PubSubConnectionDataType connection = CreateUafxConnection(endpoint);
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.ArgumentException);
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        [Test]
        public void UafxConfigurationRejectsMissingEndpointAddress()
        {
            PubSubConnectionDataType connection = CreateConnection();
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.ArgumentException);
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        [Test]
        public void UafxConfigurationRejectsConnectionTransportSettings()
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.TransportSettings = new ExtensionObject(new UadpWriterGroupMessageDataType());
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        /// <summary>
        /// Transport-specific network services at every nested scope require explicit support, not silent omission.
        /// </summary>
        [TestCase(0, true)]
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(3, false)]
        public void UafxConfigurationRejectsNestedTransportSettingsBeforeChangingProfile(int scope, bool enabled)
        {
            PubSubConnectionDataType connection = CreateUafxConnectionWithGroups(enabled);
            SetUafxTransportSettings(connection, scope);
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("TransportSettings"));
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));

            connection.TransportProfileUri = DataChannelPubSubConfiguration.UafxTransportProfile;
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                provider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ => CreateUafxParameters());

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("TransportSettings"));
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Caching local policy must not skip validation of the current PubSub configuration.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task UafxCachedPolicyStillRejectsChangedTransportSettings(int scope)
        {
            PubSubConnectionDataType connection = CreateUafxConnectionWithGroups(true);
            DataChannelParametersDataType parameters = CreateUafxParameters();
            DataChannelPubSubConfiguration.ConfigureUafx(connection, parameters);
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            int resolverCalls = 0;
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                provider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ =>
                {
                    resolverCalls++;
                    return parameters;
                });
            await using IPubSubTransport original = factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);
            SetUafxTransportSettings(connection, scope);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                    Throws.TypeOf<NotSupportedException>().With.Message.Contains("TransportSettings"));
                Assert.That(
                    () => new DataChannelPubSubFlowRequest(connection, parameters),
                    Throws.TypeOf<NotSupportedException>().With.Message.Contains("TransportSettings"));
                Assert.That(
                    () => ((IDataChannelPubSubConfigurationProvider)factory).GetParameters(connection),
                    Throws.TypeOf<NotSupportedException>().With.Message.Contains("TransportSettings"));
                Assert.That(resolverCalls, Is.EqualTo(1));
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Current group bounds remain authoritative even after the local policy has been cached.
        /// </summary>
        [TestCase(0u, true)]
        [TestCase(0u, false)]
        [TestCase(65537u, true)]
        [TestCase(65537u, false)]
        public async Task UafxCachedPolicyRevalidatesCurrentNetworkMessageBounds(uint maxMessageSize, bool writer)
        {
            PubSubConnectionDataType connection = CreateUafxConnectionWithGroups(true);
            DataChannelParametersDataType parameters = CreateUafxParameters();
            DataChannelPubSubConfiguration.ConfigureUafx(connection, parameters);
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            int resolverCalls = 0;
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                provider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ =>
                {
                    resolverCalls++;
                    return parameters;
                });
            await using IPubSubTransport original = factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);
            if (writer)
            {
                connection.WriterGroups[0].MaxNetworkMessageSize = maxMessageSize;
            }
            else
            {
                connection.ReaderGroups[0].MaxNetworkMessageSize = maxMessageSize;
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new DataChannelPubSubFlowRequest(connection, parameters),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => ((IDataChannelPubSubConfigurationProvider)factory).GetParameters(connection),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(resolverCalls, Is.EqualTo(1));
            });
            provider.VerifyNoOtherCalls();
        }

        [TestCase(0u, true)]
        [TestCase(0u, false)]
        [TestCase(65537u, true)]
        [TestCase(65537u, false)]
        public void UafxConfigurationRejectsUnboundedOrOversizedWriterGroups(
            uint maxNetworkMessageSize,
            bool enabled)
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writer",
                    Enabled = enabled,
                    MaxNetworkMessageSize = maxNetworkMessageSize
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        [TestCase(0u, true)]
        [TestCase(0u, false)]
        [TestCase(65537u, true)]
        [TestCase(65537u, false)]
        public void UafxConfigurationRejectsUnboundedOrOversizedReaderGroups(
            uint maxNetworkMessageSize,
            bool enabled)
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "reader",
                    Enabled = enabled,
                    MaxNetworkMessageSize = maxNetworkMessageSize
                }
            ];
            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
        }

        [Test]
        public void UafxConfigurationAcceptsExactFiniteGroupMessageBounds()
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType { Name = "writer", Enabled = true, MaxNetworkMessageSize = 65536 }
            ];
            connection.ReaderGroups =
            [
                new ReaderGroupDataType { Name = "reader", Enabled = true, MaxNetworkMessageSize = 65536 }
            ];

            Assert.That(
                () => DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters()),
                Throws.Nothing);
            Assert.That(connection.TransportProfileUri,
                Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
        }

        [Test]
        public async Task UafxLocalPolicyIsSnapshottedOnceForTheConnection()
        {
            var flowProvider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            int resolverCalls = 0;
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                flowProvider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ =>
                {
                    resolverCalls++;
                    DataChannelParametersDataType parameters = CreateUafxParameters();
                    parameters.ResourceUri = resolverCalls == 1 ? "urn:test:first" : "urn:test:changed";
                    return parameters;
                });
            PubSubConnectionDataType connection = CreateUafxConnection();
            DataChannelPubSubConfiguration.ConfigureUafx(connection, CreateUafxParameters());

            await using IPubSubTransport first = factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);
            await using IPubSubTransport second = factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(resolverCalls, Is.EqualTo(1));
                Assert.That(((IPubSubEncodingTransport)first).MaxNetworkMessageSize, Is.EqualTo(65536));
                Assert.That(((IPubSubEncodingTransport)second).MaxNetworkMessageSize, Is.EqualTo(65536));
            });
            flowProvider.VerifyNoOtherCalls();
        }

        [Test]
        public async Task UafxLocalBindingResolvesPubSubEncodingWithoutAddingConfigurationFields()
        {
            var flowProvider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            DataChannelParametersDataType localParameters = CreateUafxParameters();
            PubSubConnectionDataType connection = CreateUafxConnection();
            DataChannelPubSubConfiguration.ConfigureUafx(connection, localParameters);
            PubSubConfigurationDataType configuration = new()
            {
                Connections = [connection]
            };
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                flowProvider.Object,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ => localParameters);

            await using IPubSubApplication application = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(configuration)
                .AddTransportFactory(factory)
                .UseAllStandardEncoders()
                .Build();

            PubSubConnection runtimeConnection = (PubSubConnection)application.Connections[0];
            Assert.Multiple(() =>
            {
                Assert.That(runtimeConnection.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                Assert.That(runtimeConnection.EncodingProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
                Assert.That((runtimeConnection.Configuration.ConnectionProperties.ToArray() ?? []).Any(
                    property => property.Key == new QualifiedName(DataChannelPubSubConfiguration.ParametersProperty)),
                    Is.False);
            });
            flowProvider.VerifyNoOtherCalls();
        }

        [Test]
        public void ReconfigurationReplacesOnlyTheFlowProperty()
        {
            PubSubConnectionDataType connection = CreateConnection();
            DataChannelPubSubConfiguration.Configure(connection, DataChannelCarrierKind.Quic,
                CreateParameters());

            Assert.Multiple(() =>
            {
                Assert.That(connection.ConnectionProperties, Has.Count.EqualTo(1));
                Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.Quic));
                Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).Protocol,
                    Is.EqualTo(DataChannelProfiles.UadpPayload));
            });
        }

        /// <summary>
        /// Rejects expiry without changing either the existing connection or the caller's descriptor.
        /// </summary>
        [Test]
        public void QueueExpiryRequiresAPayloadAwarePublisherPolicy(
            [Values] DataChannelCarrierKind carrier)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier);
            DataChannelParametersDataType parameters = CreateParameters(
                DataChannelProfiles.UadpPayload, "urn:test:pubsub:unsafe");
            parameters.AllowQueueExpiry = true;

            Assert.That(() => DataChannelPubSubConfiguration.Configure(connection, carrier, parameters),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("payload-aware Publisher"));

            DataChannelParametersDataType retained = DataChannelPubSubConfiguration.GetParameters(connection);
            Assert.Multiple(() =>
            {
                Assert.That(parameters.AllowQueueExpiry, Is.True);
                Assert.That(retained.AllowQueueExpiry, Is.False);
                Assert.That(retained.ResourceUri, Is.EqualTo("urn:test:pubsub:data"));
                Assert.That(retained.Protocol, Is.EqualTo(DataChannelProfiles.UadpPayload));
                Assert.That(connection.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.GetTransportProfile(carrier)));
            });
        }

        /// <summary>
        /// Prevents loaded descriptors from bypassing the same fail-closed expiry check as fluent configuration.
        /// </summary>
        [Test]
        public void ImportedQueueExpiryCannotBypassConfigurationValidation(
            [Values] DataChannelCarrierKind carrier)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier);
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.AllowQueueExpiry = true;
            connection.ConnectionProperties =
            [
                new KeyValuePair
                {
                    Key = new QualifiedName(DataChannelPubSubConfiguration.ParametersProperty),
                    Value = new Variant(new ExtensionObject(parameters))
                }
            ];
            var validator = new PubSubConfigurationValidator(
                [DataChannelPubSubConfiguration.GetTransportProfile(carrier)]);

            Assert.Multiple(() =>
            {
                Assert.That(() => DataChannelPubSubConfiguration.GetParameters(connection),
                    Throws.TypeOf<NotSupportedException>());
                Assert.That(() => new DataChannelPubSubFlowRequest(connection),
                    Throws.TypeOf<NotSupportedException>());
                Assert.That(validator.Validate(new PubSubConfigurationDataType
                {
                    Connections = [connection]
                }).IsValid, Is.False);
                Assert.That(parameters.AllowQueueExpiry, Is.True);
            });
        }

        [TestCase(DataChannelProfiles.UadpPayload)]
        public void ExistingXmlConfigurationRoundTripRetainsTheTypedFlowDescriptor(string protocol)
        {
            PubSubConnectionDataType connection = CreateConnection(protocol: protocol);
            var original = new PubSubConfigurationDataType { Connections = [connection] };
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            byte[] xml = PubSubConfigurationXmlSerializer.EncodeXml(original, context);
            PubSubConfigurationDataType restored = PubSubConfigurationXmlSerializer.DecodeXml(xml, context);
            DataChannelParametersDataType parameters =
                DataChannelPubSubConfiguration.GetParameters(restored.Connections[0]);

            Assert.Multiple(() =>
            {
                Assert.That(parameters.Protocol, Is.EqualTo(protocol));
                Assert.That(parameters.ResourceUri, Is.EqualTo("urn:test:pubsub:data"));
                Assert.That(parameters.MaxMessageSize, Is.EqualTo(65536));
                Assert.That(parameters.MaxReceiveBytes, Is.EqualTo(262144));
                Assert.That(parameters.Direction, Is.EqualTo(DataChannelDirection.OpenerToAcceptor));
                Assert.That(parameters.Delivery, Is.EqualTo(DataChannelDeliveryMode.Ordered));
                Assert.That(parameters.Protection, Is.EqualTo(DataChannelProtectionMode.Confidential));
                Assert.That(restored.Connections[0].PublisherId, Is.EqualTo(connection.PublisherId));
                Assert.That(restored.Connections[0].TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
            });
        }

        [TestCase(DataChannelProfiles.UadpPayload)]
        public async Task StandaloneXmlStoreLoadsFlowConfigurationWithoutASession(string protocol)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                $"pubsub-flow-{Guid.NewGuid():N}.xml");
            try
            {
                using var store = new XmlPubSubConfigurationStore(path, NUnitTelemetryContext.Create());
                await store.SaveAsync(new PubSubConfigurationDataType
                {
                    Connections = [CreateConnection(protocol: protocol)]
                }).ConfigureAwait(false);
                PubSubConfigurationDataType restored = await store.LoadAsync().ConfigureAwait(false);
                DataChannelParametersDataType parameters =
                    DataChannelPubSubConfiguration.GetParameters(restored.Connections[0]);
                Assert.Multiple(() =>
                {
                    Assert.That(parameters.Protocol, Is.EqualTo(protocol));
                    Assert.That(parameters.ResourceUri, Is.EqualTo("urn:test:pubsub:data"));
                    Assert.That(parameters.MaxMessageSize, Is.EqualTo(65536));
                    Assert.That(restored.Connections[0].Address.IsNull, Is.True);
                });
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Rejects empty and unsupported protocols without substituting an encoding.
        /// </summary>
        [TestCase(null)]
        [TestCase(DataChannelProfiles.OpaquePayload)]
        [TestCase("urn:test:unknown")]
        [TestCase("")]
        public void UnsupportedPayloadNeverSelectsTheUadpFallback(string? protocol)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Protocol = protocol;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters),
                Throws.TypeOf<NotSupportedException>());
        }

        [TestCase(0u, 32u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(16u, 15u)]
        public void SharedFlowRejectsInvalidOrInsufficientReceiveBounds(uint maxMessageSize, uint maxReceiveBytes)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.MaxMessageSize = maxMessageSize;
            parameters.MaxReceiveBytes = maxReceiveBytes;

            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Includes the carrier envelope for native records and datagrams; UASC carries the body unchanged.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc, DataChannelDeliveryMode.Ordered, 16u)]
        [TestCase(DataChannelCarrierKind.SharedUasc, DataChannelDeliveryMode.Ordered, 16u)]
        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Ordered, 29u)]
        [TestCase(DataChannelCarrierKind.WebTransport, DataChannelDeliveryMode.Ordered, 29u)]
        [TestCase(DataChannelCarrierKind.Quic, DataChannelDeliveryMode.Datagram, 33u)]
        [TestCase(DataChannelCarrierKind.WebTransport, DataChannelDeliveryMode.Datagram, 33u)]
        public void EncodedReceiveCapacityIncludesTheCarrierEnvelope(
            DataChannelCarrierKind carrier, DataChannelDeliveryMode delivery, uint receiveBytes)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Delivery = delivery;
            parameters.MaxRate = 1;
            parameters.MaxMessageSize = 16;
            parameters.MaxReceiveBytes = receiveBytes;
            PubSubConnectionDataType connection = DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters);
            Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).MaxReceiveBytes,
                Is.EqualTo(receiveBytes));

            parameters.MaxReceiveBytes--;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase(DataChannelCarrierKind.QuicUasc)]
        [TestCase(DataChannelCarrierKind.WebTransportUasc)]
        public void UascRejectsDatagramDelivery(DataChannelCarrierKind carrier)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Delivery = DataChannelDeliveryMode.Datagram;
            parameters.MaxRate = 1024;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters), Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>
        /// Rejects both encodings of zero and accepts only explicit positive finite datagram rates.
        /// </summary>
        [Test]
        public void NativeDatagramsRequireAnExplicitFiniteRate(
            [Values(DataChannelCarrierKind.Quic, DataChannelCarrierKind.WebTransport)] DataChannelCarrierKind carrier,
            [Values(double.Epsilon, 1.0, double.MaxValue)] double maxRate)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Delivery = DataChannelDeliveryMode.Datagram;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters), Throws.TypeOf<ArgumentOutOfRangeException>());
            parameters.MaxRate = BitConverter.Int64BitsToDouble(long.MinValue);
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters), Throws.TypeOf<ArgumentOutOfRangeException>());
            parameters.MaxRate = maxRate;
            PubSubConnectionDataType connection = DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters);
            Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).MaxRate, Is.EqualTo(maxRate));
        }

        /// <summary>
        /// Rejects nonfinite and negative rates for both native delivery modes.
        /// </summary>
        [Test]
        public void NonfiniteAndNegativeRatesAreRejected(
            [Values] DataChannelDeliveryMode delivery,
            [Values(double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0)] double maxRate)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Delivery = delivery;
            parameters.MaxRate = maxRate;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.Quic, parameters),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Keeps every valid ordered rate unchanged in the configuration snapshot.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(double.Epsilon)]
        [TestCase(1.0)]
        [TestCase(double.MaxValue)]
        public void OrderedRatesArePreservedInSnapshots(double maxRate)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.MaxRate = maxRate;
            PubSubConnectionDataType connection = DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters);
            DataChannelParametersDataType snapshot = DataChannelPubSubConfiguration.GetParameters(connection);

            Assert.That(BitConverter.DoubleToInt64Bits(snapshot.MaxRate),
                Is.EqualTo(BitConverter.DoubleToInt64Bits(maxRate)));
        }

        /// <summary>
        /// Canonicalizes zero in configured and imported snapshots without mutating the original descriptor.
        /// </summary>
        [Test]
        public void OrderedRateSnapshotsNormalizeNegativeZeroWithoutMutatingTheSource(
            [Values] DataChannelCarrierKind carrier)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.MaxRate = BitConverter.Int64BitsToDouble(long.MinValue);
            PubSubConnectionDataType connection = DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), carrier, parameters);
            Assert.That(connection.ConnectionProperties[0].Value.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out DataChannelParametersDataType? configured), Is.True);
            Assert.That(configured, Is.Not.Null);
            Assert.That(BitConverter.DoubleToInt64Bits(configured!.MaxRate), Is.Zero);

            configured.MaxRate = parameters.MaxRate;
            DataChannelParametersDataType snapshot = DataChannelPubSubConfiguration.GetParameters(connection);
            var request = new DataChannelPubSubFlowRequest(connection);
            Assert.Multiple(() =>
            {
                Assert.That(BitConverter.DoubleToInt64Bits(snapshot.MaxRate), Is.Zero);
                Assert.That(BitConverter.DoubleToInt64Bits(request.Parameters.MaxRate), Is.Zero);
                Assert.That(BitConverter.DoubleToInt64Bits(configured.MaxRate), Is.EqualTo(long.MinValue));
                Assert.That(BitConverter.DoubleToInt64Bits(parameters.MaxRate), Is.EqualTo(long.MinValue));
            });
        }

        /// <summary>
        /// Requires a nonempty absolute resource URI.
        /// </summary>
        [TestCase(null)]
        [TestCase("resource")]
        [TestCase("")]
        public void RelativeOrEmptyResourcesAreRejected(string? resource)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.ResourceUri = resource;
            Assert.That(() => DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters),
                Throws.ArgumentException);
        }

        /// <summary>
        /// Validates maximum string lengths by encoded UTF-8 size rather than UTF-16 character count.
        /// </summary>
        [Test]
        public void ResourceAndContentTypeLimitsCountUtf8Bytes()
        {
            const string prefix = "urn:test:pubsub:";
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.ResourceUri = prefix + new string('\u03a9', (4096 - prefix.Length) / 2);
            parameters.ContentType = new string('\u03a9', 127) + "x";
            Assert.That(() => Configure(parameters), Throws.Nothing);
            parameters.ResourceUri += "x";
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
            parameters.ResourceUri = prefix + "data";
            parameters.ContentType += "x";
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
        }

        /// <summary>
        /// Rejects absent content types instead of allowing invalid binary CONTROL strings.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void EmptyContentTypesAreRejected(string? contentType)
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.ContentType = contentType;
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
        }

        /// <summary>
        /// Accepts the nonempty lower bound without rewriting the Publisher's content type.
        /// </summary>
        [Test]
        public void SingleByteContentTypeIsPreserved()
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.ContentType = "x";
            PubSubConnectionDataType connection = DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters);
            Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).ContentType, Is.EqualTo("x"));
        }

        [Test]
        public void InvalidDiscriminatorsAndOversizedContentTypesAreRejected()
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.Direction = (DataChannelDirection)99;
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
            parameters = CreateParameters();
            parameters.Delivery = (DataChannelDeliveryMode)99;
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
            parameters = CreateParameters();
            parameters.Protection = (DataChannelProtectionMode)99;
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
            parameters = CreateParameters();
            parameters.ContentType = new string('x', 256);
            Assert.That(() => Configure(parameters), Throws.ArgumentException);
            parameters.ContentType = new string('x', 255);
            Assert.That(() => Configure(parameters), Throws.Nothing);
        }

        [Test]
        public void MissingMalformedAndDuplicateDescriptorPropertiesFailValidation()
        {
            PubSubConnectionDataType connection = CreateConnection();
            KeyValuePair descriptor = connection.ConnectionProperties[0];
            var validator = new PubSubConfigurationValidator([DataChannelProfiles.SharedUasc]);
            var configuration = new PubSubConfigurationDataType { Connections = [connection] };
            connection.ConnectionProperties = [];
            Assert.That(validator.Validate(configuration).IsValid, Is.False);
            connection.ConnectionProperties =
            [
                new KeyValuePair
                {
                    Key = new QualifiedName(DataChannelPubSubConfiguration.ParametersProperty),
                    Value = new Variant(123)
                }
            ];
            Assert.That(validator.Validate(configuration).IsValid, Is.False);
            connection.ConnectionProperties = [descriptor, descriptor];
            Assert.That(validator.Validate(configuration).IsValid, Is.False);
        }

        [Test]
        public void JsonMessageSettingsAreRejectedForUadpFlows()
        {
            PubSubConnectionDataType connection = CreateConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType())
                }
            ];
            Assert.That(() => DataChannelPubSubConfiguration.GetParameters(connection), Throws.ArgumentException);
        }

        [Test]
        public async Task FluentConfigurationAndDirectBuilderUseTheActualFactory()
        {
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("flow", connection => connection
                    .WithPublisherId(new Variant(73001u))
                    .WithDataChannel(DataChannelCarrierKind.SharedUasc, CreateParameters()))
                .Build();
            await using IPubSubApplication application = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                .UseConfiguration(configuration)
                .UseDataChannelTransport(DataChannelCarrierKind.SharedUasc, provider.Object)
                .UseAllStandardEncoders()
                .Build();

            Assert.Multiple(() =>
            {
                Assert.That(configuration.Connections[0].TransportProfileUri,
                    Is.EqualTo(DataChannelProfiles.SharedUasc));
                Assert.That(DataChannelPubSubConfiguration.GetParameters(configuration.Connections[0]).ResourceUri,
                    Is.EqualTo("urn:test:pubsub:data"));
                Assert.That(application.Connections, Has.Count.EqualTo(1));
                Assert.That(application.Connections[0].PublisherId, Is.EqualTo(PublisherId.FromUInt32(73001u)));
                Assert.That(application.Connections[0].TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
            });
            provider.VerifyNoOtherCalls();
        }

        [Test]
        public async Task DependencyInjectionKeepsCarrierProvidersDistinctAndDisposesFactoryOwnedProviders()
        {
            var services = new ServiceCollection();
            var first = new DisposalProbeProvider();
            var second = new DisposalProbeProvider();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddDataChannelTransport(DataChannelCarrierKind.DedicatedUasc, _ => first)
                .AddDataChannelTransport(DataChannelCarrierKind.SharedUasc, _ => second));
            await using (ServiceProvider provider = services.BuildServiceProvider())
            {
                IPubSubTransportFactory[] factories = [.. provider.GetServices<IPubSubTransportFactory>()];
                Assert.Multiple(() =>
                {
                    Assert.That(factories.Select(factory => factory.TransportProfileUri),
                        Is.EquivalentTo(new[] { DataChannelProfiles.DedicatedUasc, DataChannelProfiles.SharedUasc }));
                    Assert.That(provider.GetRequiredKeyedService<IDataChannelPubSubFlowProvider>(
                        DataChannelCarrierKind.DedicatedUasc), Is.SameAs(first));
                    Assert.That(provider.GetRequiredKeyedService<IDataChannelPubSubFlowProvider>(
                        DataChannelCarrierKind.SharedUasc), Is.SameAs(second));
                });
            }
            Assert.Multiple(() =>
            {
                Assert.That(first.IsDisposed, Is.True);
                Assert.That(second.IsDisposed, Is.True);
            });
        }

        [Test]
        public async Task UafxDependencyInjectionRegistersStandardProfileAndResolvesLocalPolicy()
        {
            var services = new ServiceCollection();
            var sharedFlowProvider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            var uafxFlowProvider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            sharedFlowProvider
                .Setup(provider => provider.OpenAsync(
                    It.IsAny<DataChannelPubSubFlowRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataChannelPubSubFlow>(
                    Task.FromException<DataChannelPubSubFlow>(new InvalidOperationException("shared provider"))));
            uafxFlowProvider
                .Setup(provider => provider.OpenAsync(
                    It.IsAny<DataChannelPubSubFlowRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataChannelPubSubFlow>(
                    Task.FromException<DataChannelPubSubFlow>(new InvalidOperationException("UAFX provider"))));
            DataChannelParametersDataType localParameters = CreateUafxParameters();
            services.AddOpcUa().AddPubSub(pubsub => pubsub
                .AddDataChannelTransport(DataChannelCarrierKind.SharedUasc, _ => sharedFlowProvider.Object)
                .AddUafxDataChannelTransport(_ => uafxFlowProvider.Object, (_, _) => localParameters));

            await using (ServiceProvider provider = services.BuildServiceProvider())
            {
                IPubSubTransportFactory factory = provider.GetServices<IPubSubTransportFactory>()
                    .Single(item => item.TransportProfileUri == DataChannelPubSubConfiguration.UafxTransportProfile);
                PubSubConnectionDataType connection = CreateUafxConnection();
                DataChannelPubSubConfiguration.ConfigureUafx(connection, localParameters);
                await using IPubSubTransport transport = factory.Create(
                    connection, NUnitTelemetryContext.Create(), TimeProvider.System);
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await transport.OpenAsync().ConfigureAwait(false)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(factory.TransportProfileUri,
                        Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                    Assert.That(transport.TransportProfileUri,
                        Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                    Assert.That(((IPubSubEncodingTransport)transport).EncodingProfileUri,
                        Is.EqualTo(Profiles.PubSubUdpUadpTransport));
                    Assert.That(failure!.Message, Is.EqualTo("UAFX provider"));
                });
            }
            sharedFlowProvider.VerifyNoOtherCalls();
            uafxFlowProvider.Verify(provider => provider.OpenAsync(
                It.IsAny<DataChannelPubSubFlowRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void FactoryRejectsWrongCarrierAndNullDependencies()
        {
            var provider = new Mock<IDataChannelPubSubFlowProvider>(MockBehavior.Strict);
            var factory = new DataChannelPubSubTransportFactory(DataChannelCarrierKind.SharedUasc, provider.Object);
            Assert.Multiple(() =>
            {
                Assert.That(() => new DataChannelPubSubTransportFactory((DataChannelCarrierKind)99, provider.Object),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new DataChannelPubSubTransportFactory(DataChannelCarrierKind.SharedUasc, null!),
                    Throws.ArgumentNullException);
                Assert.That(() => new DataChannelPubSubTransportFactory(
                    DataChannelCarrierKind.SharedUasc,
                    provider.Object,
                    DataChannelPubSubConfiguration.UafxTransportProfile,
                    null!),
                    Throws.ArgumentNullException);
                Assert.That(() => factory.Create(CreateConnection(DataChannelCarrierKind.Quic),
                    NUnitTelemetryContext.Create(), TimeProvider.System), Throws.ArgumentException);
                Assert.That(() => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                    Throws.ArgumentNullException);
                Assert.That(() => factory.Create(CreateConnection(), null!, TimeProvider.System),
                    Throws.ArgumentNullException);
                Assert.That(() => factory.Create(CreateConnection(), NUnitTelemetryContext.Create(), null!),
                    Throws.ArgumentNullException);
            });
            provider.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Creates a minimal transport configuration without pretending a carrier is connected.
        /// </summary>
        internal static PubSubConnectionDataType CreateConnection(
            DataChannelCarrierKind carrier = DataChannelCarrierKind.SharedUasc,
            string protocol = DataChannelProfiles.UadpPayload,
            PubSubTransportDirection direction = PubSubTransportDirection.None,
            string resource = "urn:test:pubsub:data")
        {
            DataChannelParametersDataType parameters = CreateParameters(protocol, resource);
            var connection = new PubSubConnectionDataType
            {
                Name = resource,
                Enabled = true,
                PublisherId = new Variant(73001u),
                WriterGroups = (direction & PubSubTransportDirection.Send) == 0 ? [] : [new WriterGroupDataType()],
                ReaderGroups = (direction & PubSubTransportDirection.Receive) == 0 ? [] : [new ReaderGroupDataType()]
            };
            return DataChannelPubSubConfiguration.Configure(connection, carrier, parameters);
        }

        /// <summary>
        /// Creates an explicit configured flow descriptor with finite receive capacity.
        /// </summary>
        internal static DataChannelParametersDataType CreateParameters(
            string protocol = DataChannelProfiles.UadpPayload,
            string resource = "urn:test:pubsub:data")
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = resource,
                Protocol = protocol,
                ContentType = protocol == DataChannelProfiles.JsonPayload
                    ? "application/json" : "application/opcua+uadp",
                Direction = DataChannelDirection.OpenerToAcceptor,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 65536,
                MaxReceiveBytes = 262144
            };
        }

        /// <summary>
        /// Creates ordinary finite UADP groups with initially null transport settings.
        /// </summary>
        private static PubSubConnectionDataType CreateUafxConnectionWithGroups(bool enabled)
        {
            PubSubConnectionDataType connection = CreateUafxConnection();
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    Name = "writerGroup",
                    Enabled = enabled,
                    MaxNetworkMessageSize = 65536,
                    DataSetWriters = [new DataSetWriterDataType { Name = "writer", Enabled = enabled }]
                }
            ];
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    Name = "readerGroup",
                    Enabled = enabled,
                    MaxNetworkMessageSize = 65536,
                    DataSetReaders = [new DataSetReaderDataType { Name = "reader", Enabled = enabled }]
                }
            ];
            return connection;
        }

        /// <summary>
        /// Installs a concrete transport request at one configuration scope.
        /// </summary>
        private static void SetUafxTransportSettings(PubSubConnectionDataType connection, int scope)
        {
            var settings = new ExtensionObject(new DatagramWriterGroupTransportDataType());
            switch (scope)
            {
                case 0:
                    connection.WriterGroups[0].TransportSettings = settings;
                    break;
                case 1:
                    connection.WriterGroups[0].DataSetWriters[0].TransportSettings = settings;
                    break;
                case 2:
                    connection.ReaderGroups[0].TransportSettings = settings;
                    break;
                case 3:
                    connection.ReaderGroups[0].DataSetReaders[0].TransportSettings = settings;
                    break;
                case 4:
                    connection.TransportSettings = settings;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }

        private static PubSubConnectionDataType CreateUafxConnection(
            string endpoint = "opc.tcp://uafx.example:4840")
        {
            PubSubConnectionDataType connection = CreateConnection();
            connection.Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = endpoint });
            return connection;
        }

        private static DataChannelParametersDataType CreateUafxParameters()
        {
            DataChannelParametersDataType parameters = CreateParameters();
            parameters.ContentType = DataChannelPubSubConfiguration.UafxContentType;
            return parameters;
        }

        private static void Configure(DataChannelParametersDataType parameters)
        {
            DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType(), DataChannelCarrierKind.SharedUasc, parameters);
        }

        private sealed class DisposalProbeProvider : IDataChannelPubSubFlowProvider, IAsyncDisposable
        {
            public bool IsDisposed { get; private set; }

            public ValueTask<DataChannelPubSubFlow> OpenAsync(
                DataChannelPubSubFlowRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("This DI lifetime probe must never be used to open a flow.");
            }

            public ValueTask DisposeAsync()
            {
                IsDisposed = true;
                return default;
            }
        }

        [Test]
        public void FlowRequestSnapshotsDistinctReaderPublisherIdsInFirstSeenOrder()
        {
            PubSubConnectionDataType connection = CreateConnection(
                direction: PubSubTransportDirection.Receive);
            var firstReader = new DataSetReaderDataType { PublisherId = new Variant(73001u) };
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    DataSetReaders =
                    [
                        firstReader,
                        new DataSetReaderDataType { PublisherId = new Variant(73001u) },
                        new DataSetReaderDataType { PublisherId = new Variant("73001") },
                        new DataSetReaderDataType { PublisherId = Variant.Null },
                        new DataSetReaderDataType { PublisherId = Variant.Null }
                    ]
                }
            ];

            var request = new DataChannelPubSubFlowRequest(connection);
            firstReader.PublisherId = new Variant(999u);
            connection.ReaderGroups[0].DataSetReaders =
            [
                new DataSetReaderDataType { PublisherId = new Variant(1000u) }
            ];

            Assert.Multiple(() =>
            {
                Assert.That(request.Direction, Is.EqualTo(PubSubTransportDirection.Receive));
                Assert.That(request.InboundPublisherIds.ToArray(), Is.EqualTo(new[]
                {
                    new Variant(73001u),
                    new Variant("73001"),
                    Variant.Null
                }));
                Assert.That(connection.ReaderGroups[0].DataSetReaders[0].PublisherId,
                    Is.EqualTo(new Variant(1000u)));
            });
        }

        /// <summary>
        /// Snapshots distinct writer associations and detaches them from mutable reader configuration.
        /// </summary>
        [Test]
        public void FlowRequestSnapshotsReaderWriterAssociations()
        {
            PubSubConnectionDataType connection = CreateConnection(
                direction: PubSubTransportDirection.Receive);
            var firstReader = new DataSetReaderDataType
            {
                PublisherId = new Variant(73001u),
                WriterGroupId = 17,
                DataSetWriterId = 101
            };
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    DataSetReaders =
                    [
                        firstReader,
                        new DataSetReaderDataType
                        {
                            PublisherId = new Variant(73001u),
                            WriterGroupId = 17,
                            DataSetWriterId = 102
                        },
                        new DataSetReaderDataType
                        {
                            PublisherId = new Variant(73001u),
                            WriterGroupId = 18,
                            DataSetWriterId = 101
                        },
                        new DataSetReaderDataType
                        {
                            PublisherId = new Variant(73001u),
                            WriterGroupId = 17,
                            DataSetWriterId = 101
                        },
                        new DataSetReaderDataType
                        {
                            PublisherId = Variant.Null,
                            DataSetWriterId = 103
                        }
                    ]
                }
            ];

            var request = new DataChannelPubSubFlowRequest(connection);
            firstReader.PublisherId = new Variant(999u);
            firstReader.WriterGroupId = 99;
            firstReader.DataSetWriterId = 999;
            connection.ReaderGroups = [];

            Assert.That(request.InboundAssociations.ToArray(), Has.Length.EqualTo(32));
            AssertReaderAssociations(request.InboundAssociations, new Variant(73001u), 17, 101);
            AssertReaderAssociations(request.InboundAssociations, new Variant(73001u), 17, 102);
            AssertReaderAssociations(request.InboundAssociations, new Variant(73001u), 18, 101);
            AssertReaderAssociations(request.InboundAssociations, Variant.Null, null, 103);
        }

        [Test]
        public void FlowRequestSnapshotsPublisherIdsAcrossReaderGroups()
        {
            PubSubConnectionDataType connection = CreateConnection(
                direction: PubSubTransportDirection.Receive);
            var laterReader = new DataSetReaderDataType { PublisherId = new Variant(73002u) };
            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    DataSetReaders =
                    [
                        new DataSetReaderDataType { PublisherId = new Variant(73001u) },
                        new DataSetReaderDataType { PublisherId = Variant.Null }
                    ]
                },
                new ReaderGroupDataType
                {
                    DataSetReaders =
                    [
                        new DataSetReaderDataType { PublisherId = new Variant(73001u) },
                        laterReader,
                        new DataSetReaderDataType { PublisherId = Variant.Null },
                        new DataSetReaderDataType { PublisherId = new Variant("73001") }
                    ]
                }
            ];

            var request = new DataChannelPubSubFlowRequest(connection);
            laterReader.PublisherId = new Variant(999u);
            connection.ReaderGroups = [];

            Assert.Multiple(() =>
            {
                Assert.That(request.InboundPublisherIds.ToArray(), Is.EqualTo(new[]
                {
                    new Variant(73001u), Variant.Null, new Variant(73002u), new Variant("73001")
                }));
                Assert.That(request.Direction, Is.EqualTo(PubSubTransportDirection.Receive));
                Assert.That(connection.ReaderGroups, Is.Empty);
                Assert.That(laterReader.PublisherId, Is.EqualTo(new Variant(999u)));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FlowRequestSnapshotsEmptyPublisherIdsWithOrWithoutReaderGroup(bool hasReaderGroup)
        {
            PubSubConnectionDataType connection = CreateConnection(
                direction: hasReaderGroup ? PubSubTransportDirection.Receive : PubSubTransportDirection.None);
            var request = new DataChannelPubSubFlowRequest(connection);

            connection.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    DataSetReaders = [new DataSetReaderDataType { PublisherId = new Variant(73001u) }]
                }
            ];

            Assert.Multiple(() =>
            {
                Assert.That(request.InboundPublisherIds, Is.Empty);
                Assert.That(request.Direction, Is.EqualTo(
                    hasReaderGroup ? PubSubTransportDirection.Receive : PubSubTransportDirection.None));
                Assert.That(connection.ReaderGroups[0].DataSetReaders[0].PublisherId,
                    Is.EqualTo(new Variant(73001u)));
            });
        }

        private static void AssertReaderAssociations(
            ArrayOf<DataChannelPubSubRoute> inboundAssociations,
            Variant publisherId,
            ushort? writerGroupId,
            ushort dataSetWriterId)
        {
            DataChannelPubSubRoute[] associations = inboundAssociations.ToArray()
                ?? throw new InvalidOperationException("The receive association snapshot must not be null.");
            Assert.That(
                associations,
                Does.Contain(new DataChannelPubSubRoute(publisherId, writerGroupId, dataSetWriterId, 0, null)));
            Assert.That(
                associations,
                Does.Contain(new DataChannelPubSubRoute(publisherId, writerGroupId, dataSetWriterId, 2, null)));
            foreach (byte discoveryType in new byte[] { 1, 2, 3, 4, 5, 6 })
            {
                Assert.That(
                    associations,
                    Does.Contain(new DataChannelPubSubRoute(
                        publisherId, writerGroupId, dataSetWriterId, 1, discoveryType)));
            }
            foreach (byte discoveryType in new byte[] { 1, 2, 3, 4, 5 })
            {
                Assert.That(
                    associations,
                    Does.Not.Contain(new DataChannelPubSubRoute(
                        publisherId, writerGroupId, dataSetWriterId, 2, discoveryType)));
            }
        }
    }
}
