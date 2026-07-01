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
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Verifies <see cref="MqttPubSubTransportFactory"/> URI scheme
    /// dispatch, direction inference based on Writer / Reader groups,
    /// and TransportProfileUri propagation.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.4")]
    [CancelAfter(5000)]
    public sealed class MqttPubSubTransportFactoryTests
    {
        private static MqttPubSubTransportFactory NewFactory(
            string transportProfileUri = Profiles.PubSubMqttJsonTransport,
            FakeMqttClientFactory? clientFactory = null,
            MqttConnectionOptions? options = null)
        {
            return new MqttPubSubTransportFactory(
                transportProfileUri,
                clientFactory ?? new FakeMqttClientFactory(),
                Options.Create(options ?? new MqttConnectionOptions()));
        }

        private static PubSubConnectionDataType NewConnection(
            string url,
            WriterGroupDataType[]? writerGroups = null,
            ReaderGroupDataType[]? readerGroups = null,
            string transportProfileUri = "")
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "Conn",
                TransportProfileUri = transportProfileUri,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                })
            };
            if (writerGroups is not null && writerGroups.Length > 0)
            {
                connection.WriterGroups = new ArrayOf<WriterGroupDataType>(writerGroups);
            }
            if (readerGroups is not null && readerGroups.Length > 0)
            {
                connection.ReaderGroups = new ArrayOf<ReaderGroupDataType>(readerGroups);
            }
            return connection;
        }

        [Test]
        public void Constructor_RejectsNullOrEmptyProfileUri()
        {
            Assert.Throws<ArgumentException>(() => new MqttPubSubTransportFactory(
                string.Empty,
                new FakeMqttClientFactory(),
                Options.Create(new MqttConnectionOptions())));
        }

        [Test]
        public void Constructor_RejectsNonMqttProfileUri()
        {
            Assert.Throws<ArgumentException>(() => new MqttPubSubTransportFactory(
                Profiles.PubSubUdpUadpTransport,
                new FakeMqttClientFactory(),
                Options.Create(new MqttConnectionOptions())));
        }

        [Test]
        public void Constructor_RejectsNullClientFactory()
        {
            Assert.Throws<ArgumentNullException>(() => new MqttPubSubTransportFactory(
                Profiles.PubSubMqttJsonTransport,
                clientFactory: null!,
                Options.Create(new MqttConnectionOptions())));
        }

        [Test]
        public void Constructor_RejectsNullDefaultOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new MqttPubSubTransportFactory(
                Profiles.PubSubMqttJsonTransport,
                new FakeMqttClientFactory(),
                defaultOptions: null!));
        }

        [Test]
        public void TransportProfileUri_ReturnsConstructorValue_Json()
        {
            MqttPubSubTransportFactory factory = NewFactory(Profiles.PubSubMqttJsonTransport);
            Assert.That(factory.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
        }

        [Test]
        public void TransportProfileUri_ReturnsConstructorValue_Uadp()
        {
            MqttPubSubTransportFactory factory = NewFactory(Profiles.PubSubMqttUadpTransport);
            Assert.That(factory.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttUadpTransport));
        }

        [Test]
        public void Create_ValidConnection_ReturnsMqttBrokerTransport()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection(
                "mqtt://broker.example.com:1883",
                writerGroups: new[]
                {
                    new WriterGroupDataType
                    {
                        Name = "WG",
                        MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType())
                    }
                });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<MqttBrokerTransport>());
            Assert.That(
                transport.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubMqttJsonTransport));
        }

        [Test]
        public void Create_WriterGroupsOnly_PicksSendDirection()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection(
                "mqtt://broker.example.com:1883",
                writerGroups: new[]
                {
                    new WriterGroupDataType
                    {
                        Name = "WG",
                        MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType())
                    }
                });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.Send));
        }

        [Test]
        public void Create_ReaderGroupsOnly_PicksReceiveDirection()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection(
                "mqtt://broker.example.com:1883",
                readerGroups: new[] { new ReaderGroupDataType { Name = "RG" } });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.Receive));
        }

        [Test]
        public void Create_BothGroupsPresent_PicksSendReceiveDirection()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection(
                "mqtt://broker.example.com:1883",
                writerGroups: new[]
                {
                    new WriterGroupDataType
                    {
                        Name = "WG",
                        MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType())
                    }
                },
                readerGroups: new[] { new ReaderGroupDataType { Name = "RG" } });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void Create_NoGroups_DefaultsToSendReceive()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void Create_NullConnection_Throws()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            Assert.Throws<ArgumentNullException>(() => factory.Create(
                null!,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void Create_NullTelemetry_Throws()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");
            Assert.Throws<ArgumentNullException>(() => factory.Create(
                connection,
                null!,
                TimeProvider.System));
        }

        [Test]
        public void Create_NullTimeProvider_Throws()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");
            Assert.Throws<ArgumentNullException>(() => factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                null!));
        }

        [Test]
        public void Create_NullAddress_Throws()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "C",
                TransportProfileUri = Profiles.PubSubMqttJsonTransport
            };
            Assert.Throws<NotSupportedException>(() => factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void Create_UadpFactory_ProducesUadpTransport()
        {
            MqttPubSubTransportFactory factory = NewFactory(Profiles.PubSubMqttUadpTransport);
            PubSubConnectionDataType connection = NewConnection(
                "mqtt://broker.example.com:1883",
                writerGroups: new[]
                {
                    new WriterGroupDataType
                    {
                        Name = "WG",
                        MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType())
                    }
                });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(
                transport.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubMqttUadpTransport));
        }

        [Test]
        public void Create_MqttsUrl_ReturnsTransportWithTlsEndpoint()
        {
            MqttPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("mqtts://broker.example.com:8883");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<MqttBrokerTransport>());
            var mqtt = (MqttBrokerTransport)transport;
            Assert.That(mqtt.Endpoint.UseTls, Is.True);
        }

        [Test]
        public void Create_PasswordSecretIdSetWithoutSecretRegistry_Throws()
        {
            var defaultOptions = new MqttConnectionOptions
            {
                PasswordSecretId = "Default:secret"
            };
            MqttPubSubTransportFactory factory = NewFactory(options: defaultOptions);
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");

            Assert.Throws<InvalidOperationException>(() => factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public async Task Create_PasswordSecretIdResolved_FromSecretRegistry()
        {
            var store = new InMemorySecretStore();
            byte[] expected = new byte[] { 0xAA, 0xBB, 0xCC };
            await store.SetAsync(
                new SecretIdentifier("mqtt-password", InMemorySecretStore.DefaultStoreType),
                expected).ConfigureAwait(false);
            var registry = new SecretRegistry(store);
            var defaultOptions = new MqttConnectionOptions
            {
                PasswordSecretId = "InMemory:mqtt-password",
                UserName = "alice"
            };
            var factory = new MqttPubSubTransportFactory(
                Profiles.PubSubMqttJsonTransport,
                new FakeMqttClientFactory(),
                Options.Create(defaultOptions),
                registry);

            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");
            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            var mqtt = (MqttBrokerTransport)transport;
            Assert.That(mqtt.Options.PasswordBytes, Is.EqualTo(expected));
        }

        [Test]
        public async Task Create_PasswordSecretId_WithoutColon_UsesDefaultStoreType()
        {
            var store = new InMemorySecretStore();
            byte[] expected = new byte[] { 1, 2, 3 };
            await store.SetAsync(
                new SecretIdentifier("plain-secret", InMemorySecretStore.DefaultStoreType),
                expected).ConfigureAwait(false);
            var registry = new SecretRegistry(store);
            var defaultOptions = new MqttConnectionOptions
            {
                PasswordSecretId = "plain-secret"
            };
            var factory = new MqttPubSubTransportFactory(
                Profiles.PubSubMqttJsonTransport,
                new FakeMqttClientFactory(),
                Options.Create(defaultOptions),
                registry);
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            var mqtt = (MqttBrokerTransport)transport;
            Assert.That(mqtt.Options.PasswordBytes, Is.EqualTo(expected));
        }

        [Test]
        public void Create_PasswordSecretId_NotFound_Throws()
        {
            var registry = new SecretRegistry(new InMemorySecretStore());
            var defaultOptions = new MqttConnectionOptions
            {
                PasswordSecretId = "InMemory:missing"
            };
            var factory = new MqttPubSubTransportFactory(
                Profiles.PubSubMqttJsonTransport,
                new FakeMqttClientFactory(),
                Options.Create(defaultOptions),
                registry);
            PubSubConnectionDataType connection = NewConnection("mqtt://broker.example.com:1883");

            Assert.Throws<InvalidOperationException>(() => factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }
    }
}
