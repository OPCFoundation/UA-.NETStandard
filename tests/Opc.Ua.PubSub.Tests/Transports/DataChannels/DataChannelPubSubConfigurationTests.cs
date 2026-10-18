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
        /// Accepts each supported carrier and payload combination with queue expiry disabled.
        /// </summary>
        [Test]
        public void EveryCarrierAcceptsBothPubSubProtocolsWithoutAnAddressOrSession(
            [Values] DataChannelCarrierKind carrier,
            [Values(DataChannelProfiles.UadpPayload, DataChannelProfiles.JsonPayload)] string protocol)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier, protocol);
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
                Assert.That(request.Parameters.Protocol, Is.EqualTo(protocol));
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

        [Test]
        public void ReconfigurationReplacesOnlyTheFlowProperty()
        {
            PubSubConnectionDataType connection = CreateConnection();
            DataChannelPubSubConfiguration.Configure(connection, DataChannelCarrierKind.Quic,
                CreateParameters(DataChannelProfiles.JsonPayload));

            Assert.Multiple(() =>
            {
                Assert.That(connection.ConnectionProperties, Has.Count.EqualTo(1));
                Assert.That(connection.TransportProfileUri, Is.EqualTo(DataChannelProfiles.Quic));
                Assert.That(DataChannelPubSubConfiguration.GetParameters(connection).Protocol,
                    Is.EqualTo(DataChannelProfiles.JsonPayload));
            });
        }

        /// <summary>
        /// Rejects expiry without changing either the existing connection or the caller's descriptor.
        /// </summary>
        [Test]
        public void QueueExpiryRequiresAPayloadAwarePublisherPolicy(
            [Values] DataChannelCarrierKind carrier,
            [Values(DataChannelProfiles.UadpPayload, DataChannelProfiles.JsonPayload)] string protocol)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier, protocol);
            DataChannelParametersDataType parameters = CreateParameters(protocol, "urn:test:pubsub:unsafe");
            parameters.AllowQueueExpiry = true;

            Assert.That(() => DataChannelPubSubConfiguration.Configure(connection, carrier, parameters),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("payload-aware Publisher"));

            DataChannelParametersDataType retained = DataChannelPubSubConfiguration.GetParameters(connection);
            Assert.Multiple(() =>
            {
                Assert.That(parameters.AllowQueueExpiry, Is.True);
                Assert.That(retained.AllowQueueExpiry, Is.False);
                Assert.That(retained.ResourceUri, Is.EqualTo("urn:test:pubsub:data"));
                Assert.That(retained.Protocol, Is.EqualTo(protocol));
                Assert.That(connection.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.GetTransportProfile(carrier)));
            });
        }

        /// <summary>
        /// Prevents loaded descriptors from bypassing the same fail-closed expiry check as fluent configuration.
        /// </summary>
        [Test]
        public void ImportedQueueExpiryCannotBypassConfigurationValidation(
            [Values] DataChannelCarrierKind carrier,
            [Values(DataChannelProfiles.UadpPayload, DataChannelProfiles.JsonPayload)] string protocol)
        {
            PubSubConnectionDataType connection = CreateConnection(carrier, protocol);
            DataChannelParametersDataType parameters = CreateParameters(protocol);
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
        [TestCase(DataChannelProfiles.JsonPayload)]
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
        [TestCase(DataChannelProfiles.JsonPayload)]
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
        [TestCase(16u, 32u)]
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
        /// Includes ChannelId for both UASC carriers and native datagrams, but not native ordered records.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc, DataChannelDeliveryMode.Ordered, 33u)]
        [TestCase(DataChannelCarrierKind.SharedUasc, DataChannelDeliveryMode.Ordered, 33u)]
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

        [TestCase(DataChannelProfiles.UadpPayload)]
        [TestCase(DataChannelProfiles.JsonPayload)]
        public void ExplicitMessageSettingsMustAgreeWithThePayload(string protocol)
        {
            PubSubConnectionDataType connection = CreateConnection(protocol: protocol);
            connection.WriterGroups =
            [
                new WriterGroupDataType
                {
                    MessageSettings = protocol == DataChannelProfiles.JsonPayload
                        ? new ExtensionObject(new UadpWriterGroupMessageDataType())
                        : new ExtensionObject(new JsonWriterGroupMessageDataType())
                }
            ];
            Assert.That(() => DataChannelPubSubConfiguration.GetParameters(connection), Throws.ArgumentException);
        }

        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        public void JsonDoesNotSilentlyDowngradeConfiguredMessageSecurity(MessageSecurityMode mode)
        {
            PubSubConnectionDataType connection = CreateConnection(protocol: DataChannelProfiles.JsonPayload);
            connection.ReaderGroups = [new ReaderGroupDataType { SecurityMode = mode }];
            Assert.That(() => DataChannelPubSubConfiguration.GetParameters(connection),
                Throws.TypeOf<NotSupportedException>());
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
    }
}
