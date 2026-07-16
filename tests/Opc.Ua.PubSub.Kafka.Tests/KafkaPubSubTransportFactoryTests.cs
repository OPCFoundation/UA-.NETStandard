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
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies Kafka transport factory profile validation and connection resolution.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka transport factory")]
    [CancelAfter(5000)]
    public sealed class KafkaPubSubTransportFactoryTests
    {
        [Test]
        public void ConstructorRejectsInvalidArguments()
        {
            Assert.That(
                () => new KafkaPubSubTransportFactory(
                    string.Empty,
                    new FakeKafkaClientFactory(),
                    Options.Create(new KafkaConnectionOptions())),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new KafkaPubSubTransportFactory(
                    Profiles.PubSubUdpUadpTransport,
                    new FakeKafkaClientFactory(),
                    Options.Create(new KafkaConnectionOptions())),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new KafkaPubSubTransportFactory(
                    KafkaProfiles.PubSubKafkaJsonTransport,
                    clientFactory: null!,
                    Options.Create(new KafkaConnectionOptions())),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new KafkaPubSubTransportFactory(
                    KafkaProfiles.PubSubKafkaJsonTransport,
                    new FakeKafkaClientFactory(),
                    defaultOptions: null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void TransportProfileUriReturnsConstructorValue()
        {
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory(
                KafkaProfiles.PubSubKafkaUadpTransport);

            Assert.That(factory.TransportProfileUri, Is.EqualTo(KafkaProfiles.PubSubKafkaUadpTransport));
        }

        [Test]
        public void CreateValidConnectionReturnsKafkaBrokerTransport()
        {
            IPubSubTransport transport = KafkaTestHelper.NewFactory().Create(
                KafkaTestHelper.NewConnection(),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<KafkaBrokerTransport>());
            Assert.That(transport.TransportProfileUri, Is.EqualTo(KafkaProfiles.PubSubKafkaJsonTransport));
        }

        [Test]
        public void CreateReadsNetworkAddressUrlAndAppliesTlsEndpoint()
        {
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory();
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection("kafkas://broker.example.com:19092");

            var transport = (KafkaBrokerTransport)factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Endpoint.BootstrapServers, Is.EqualTo("broker.example.com:19092"));
            Assert.That(transport.Endpoint.UseTls, Is.True);
            Assert.That(transport.Options.Endpoint, Is.EqualTo("kafkas://broker.example.com:19092"));
            Assert.That(transport.Options.BootstrapServers, Is.EqualTo("broker.example.com:19092"));
            Assert.That(transport.Options.Tls, Is.Not.Null);
            Assert.That(transport.Options.Tls!.UseTls, Is.True);
            Assert.That(transport.Options.SecurityProtocol, Is.EqualTo(KafkaSecurityProtocol.Ssl));
        }

        [Test]
        public void CreateAppliesBrokerTransportAuthenticationSettings()
        {
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory();
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection();
            connection.TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType
            {
                AuthenticationProfileUri = "http://opcfoundation.org/UA/Security/UserToken/Server/Password",
                ResourceUri = "alice"
            });

            var transport = (KafkaBrokerTransport)factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Options.AuthenticationProfileUri, Does.Contain("Password"));
            Assert.That(transport.Options.ResourceUri, Is.EqualTo("alice"));
            Assert.That(transport.Options.UserName, Is.EqualTo("alice"));
        }

        [Test]
        public void CreateDoesNotOverrideDefaultAuthenticationProfile()
        {
            var options = new KafkaConnectionOptions
            {
                AuthenticationProfileUri = "default-profile",
                ResourceUri = "default-resource",
                UserName = "configured"
            };
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory(options: options);
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection();
            connection.TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType
            {
                AuthenticationProfileUri = "broker-profile",
                ResourceUri = "broker-resource"
            });

            var transport = (KafkaBrokerTransport)factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Options.AuthenticationProfileUri, Is.EqualTo("default-profile"));
            Assert.That(transport.Options.ResourceUri, Is.EqualTo("default-resource"));
            Assert.That(transport.Options.UserName, Is.EqualTo("configured"));
        }

        [Test]
        public void CreateResolvesPasswordViaSecretRegistryMock()
        {
            byte[] expected = [0xAA, 0xBB, 0xCC];
            var secret = new TestSecret(expected);
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(r => r.TryGet(It.Is<SecretIdentifier>(id =>
                    id.StoreType == InMemorySecretStore.DefaultStoreType && id.Name == "kafka-password")))
                .Returns(secret);
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory(
                options: new KafkaConnectionOptions { PasswordSecretId = "InMemory:kafka-password" },
                secretRegistry: registry.Object);

            var transport = (KafkaBrokerTransport)factory.Create(
                KafkaTestHelper.NewConnection(),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Options.PasswordBytes, Is.EqualTo(expected));
            Assert.That(secret.DisposeCount, Is.EqualTo(1));
            registry.VerifyAll();
        }

        [Test]
        public void CreatePasswordSecretFailuresThrowInvalidOperationException()
        {
            KafkaPubSubTransportFactory noRegistry = KafkaTestHelper.NewFactory(options: new KafkaConnectionOptions
            {
                PasswordSecretId = "InMemory:kafka-password"
            });
            var registry = new Mock<ISecretRegistry>(MockBehavior.Strict);
            registry.Setup(r => r.TryGet(It.IsAny<SecretIdentifier>())).Returns((ISecret?)null);
            KafkaPubSubTransportFactory missingSecret = KafkaTestHelper.NewFactory(
                options: new KafkaConnectionOptions { PasswordSecretId = "missing-secret" },
                secretRegistry: registry.Object);

            Assert.That(
                () => noRegistry.Create(KafkaTestHelper.NewConnection(), NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => missingSecret.Create(
                    KafkaTestHelper.NewConnection(),
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CreateDeterminesDirectionFromWriterAndReaderGroups()
        {
            Assert.That(CreateDirection(KafkaTestHelper.NewConnection(writer: true, reader: false)),
                Is.EqualTo(PubSubTransportDirection.Send));
            Assert.That(CreateDirection(KafkaTestHelper.NewConnection(writer: false, reader: true)),
                Is.EqualTo(PubSubTransportDirection.Receive));
            Assert.That(CreateDirection(KafkaTestHelper.NewConnection(writer: true, reader: true)),
                Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void CreateNoGroupsDefaultsToSendReceiveDirection()
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "Conn",
                TransportProfileUri = KafkaProfiles.PubSubKafkaJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = KafkaTestHelper.EndpointUrl
                })
            };

            Assert.That(CreateDirection(connection), Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void CreateWriterGroupDeliveryGuaranteeOverridesDefaultOptions()
        {
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection(
                requestedDeliveryGuarantee: BrokerTransportQualityOfService.ExactlyOnce);

            var transport = (KafkaBrokerTransport)KafkaTestHelper.NewFactory().Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Options.DeliveryGuarantee, Is.EqualTo(KafkaQualityOfService.ExactlyOnce));
        }

        [Test]
        public void CreateUadpFactoryProducesUadpTransport()
        {
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory(KafkaProfiles.PubSubKafkaUadpTransport);
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection(
                profile: KafkaProfiles.PubSubKafkaUadpTransport);

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.TransportProfileUri, Is.EqualTo(KafkaProfiles.PubSubKafkaUadpTransport));
        }

        [Test]
        public void CreateRejectsInvalidCreateArgumentsAndAddresses()
        {
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory();
            PubSubConnectionDataType connection = KafkaTestHelper.NewConnection();
            var noAddress = new PubSubConnectionDataType { Name = "NoAddress" };
            var noUrl = new PubSubConnectionDataType
            {
                Name = "NoUrl",
                Address = new ExtensionObject(new NetworkAddressUrlDataType())
            };

            Assert.That(() => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => factory.Create(connection, null!, TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => factory.Create(connection, NUnitTelemetryContext.Create(), null!),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => factory.Create(noAddress, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(() => factory.Create(noUrl, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        private static PubSubTransportDirection CreateDirection(PubSubConnectionDataType connection)
        {
            return KafkaTestHelper.NewFactory()
                .Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System)
                .Direction;
        }

        private sealed class TestSecret : ISecret
        {
            private readonly byte[] m_bytes;

            public TestSecret(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public int DisposeCount { get; private set; }

            public ReadOnlySpan<byte> Bytes => m_bytes;

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
