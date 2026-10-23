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
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using NUnit.Framework;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Inspects the real Confluent producer and consumer configurations without constructing native clients.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [CancelAfter(10000)]
    public sealed class ConfluentKafkaClientSecurityTests
    {
        /// <summary>
        /// Both clients use the same final TLS protocol and preserve SASL credential mapping.
        /// </summary>
        [TestCase(KafkaSecurityProtocol.Plaintext, false, SecurityProtocol.Plaintext)]
        [TestCase(KafkaSecurityProtocol.Plaintext, true, SecurityProtocol.Ssl)]
        [TestCase(KafkaSecurityProtocol.SaslPlaintext, false, SecurityProtocol.SaslPlaintext)]
        [TestCase(KafkaSecurityProtocol.SaslPlaintext, true, SecurityProtocol.SaslSsl)]
        [TestCase(KafkaSecurityProtocol.Ssl, false, SecurityProtocol.Ssl)]
        [TestCase(KafkaSecurityProtocol.Ssl, true, SecurityProtocol.Ssl)]
        [TestCase(KafkaSecurityProtocol.SaslSsl, false, SecurityProtocol.SaslSsl)]
        [TestCase(KafkaSecurityProtocol.SaslSsl, true, SecurityProtocol.SaslSsl)]
        [Category("SecurityRegression")]
        public async Task SecurityF13BothConfluentConfigsUseEffectiveTls(
            KafkaSecurityProtocol protocol,
            bool useTls,
            SecurityProtocol expectedProtocol)
        {
            bool credentials = protocol is KafkaSecurityProtocol.SaslPlaintext or KafkaSecurityProtocol.SaslSsl;
            KafkaConnectionOptions options = CreateOptions(protocol, useTls, credentials);
            options.AllowCredentialsOverPlaintext = true;
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
            ProducerConfig producer = ConfluentKafkaClientAdapter.CreateProducerConfig(options);
            ConsumerConfig consumer = ConfluentKafkaClientAdapter.CreateConsumerConfig(options);

            Assert.That(adapter.IsConnected, Is.True);
            AssertEffectiveConfig(producer, expectedProtocol, credentials);
            AssertEffectiveConfig(consumer, expectedProtocol, credentials);
        }

        /// <summary>
        /// Explicit TLS protects PLAIN credentials without requiring a plaintext-policy opt-out.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF13TlsUpgradeProtectsPlainCredentialsWithoutOptOut()
        {
            KafkaConnectionOptions options = CreateOptions(KafkaSecurityProtocol.SaslPlaintext, useTls: true);
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
            ProducerConfig producer = ConfluentKafkaClientAdapter.CreateProducerConfig(options);
            ConsumerConfig consumer = ConfluentKafkaClientAdapter.CreateConsumerConfig(options);

            Assert.Multiple(() =>
            {
                Assert.That(options.AllowCredentialsOverPlaintext, Is.False);
                Assert.That(options.SecurityProtocol, Is.EqualTo(KafkaSecurityProtocol.SaslPlaintext));
                Assert.That(adapter.IsConnected, Is.True);
            });
            AssertEffectiveConfig(producer, SecurityProtocol.SaslSsl, credentials: true);
            AssertEffectiveConfig(consumer, SecurityProtocol.SaslSsl, credentials: true);
        }

        /// <summary>
        /// Neither the connection guard nor either client configuration can emit disallowed plaintext credentials.
        /// </summary>
        [TestCase(KafkaSecurityProtocol.Plaintext)]
        [TestCase(KafkaSecurityProtocol.SaslPlaintext)]
        [Category("SecurityRegression")]
        public async Task SecurityF13PlaintextCredentialsAreRejectedByConnectAndBothBuilders(
            KafkaSecurityProtocol protocol)
        {
            KafkaConnectionOptions options = CreateOptions(protocol, useTls: false);
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await Assert.ThatAsync(
                async () => await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("require TLS")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(adapter.IsConnected, Is.False);
                Assert.That(
                    () => ConfluentKafkaClientAdapter.CreateProducerConfig(options),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("require TLS"));
                Assert.That(
                    () => ConfluentKafkaClientAdapter.CreateConsumerConfig(options),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains("require TLS"));
            });
        }

        /// <summary>
        /// Explicitly accepted plaintext SASL deployments retain their credentials and protocol.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF13PlaintextCredentialsRequireExplicitOptOut()
        {
            KafkaConnectionOptions options = CreateOptions(KafkaSecurityProtocol.SaslPlaintext, useTls: false);
            options.AllowCredentialsOverPlaintext = true;
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);

            Assert.That(adapter.IsConnected, Is.True);
            AssertEffectiveConfig(
                ConfluentKafkaClientAdapter.CreateProducerConfig(options),
                SecurityProtocol.SaslPlaintext,
                credentials: true);
            AssertEffectiveConfig(
                ConfluentKafkaClientAdapter.CreateConsumerConfig(options),
                SecurityProtocol.SaslPlaintext,
                credentials: true);
        }

        /// <summary>
        /// A secure endpoint requires TLS even when bootstrap servers and disabled TLS options are supplied directly.
        /// </summary>
        [TestCase(KafkaSecurityProtocol.Plaintext, SecurityProtocol.Ssl)]
        [TestCase(KafkaSecurityProtocol.SaslPlaintext, SecurityProtocol.SaslSsl)]
        [Category("SecurityRegression")]
        public async Task SecurityF13TlsEndpointCannotBeDowngraded(
            KafkaSecurityProtocol protocol,
            SecurityProtocol expectedProtocol)
        {
            bool credentials = protocol == KafkaSecurityProtocol.SaslPlaintext;
            KafkaConnectionOptions options = CreateOptions(protocol, useTls: false, credentials);
            options.Endpoint = "kafkas://broker.example:9093";
            options.BootstrapServers = "broker.example:9093";
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
            ProducerConfig producer = ConfluentKafkaClientAdapter.CreateProducerConfig(options);
            ConsumerConfig consumer = ConfluentKafkaClientAdapter.CreateConsumerConfig(options);

            Assert.That(adapter.IsConnected, Is.True);
            AssertEffectiveConfig(producer, expectedProtocol, credentials);
            AssertEffectiveConfig(consumer, expectedProtocol, credentials);
        }

        /// <summary>
        /// Factory-normalized secure endpoints retain their TLS protocol in both real configuration builders.
        /// </summary>
        [TestCase(KafkaSecurityProtocol.Plaintext, SecurityProtocol.Ssl)]
        [TestCase(KafkaSecurityProtocol.SaslPlaintext, SecurityProtocol.SaslSsl)]
        [Category("SecurityRegression")]
        public async Task SecurityF13NormalizedTlsEndpointProtectsBothConfluentConfigs(
            KafkaSecurityProtocol protocol,
            SecurityProtocol expectedProtocol)
        {
            KafkaConnectionOptions options = CreateOptions(protocol, useTls: false, credentials: false);
            KafkaPubSubTransportFactory factory = KafkaTestHelper.NewFactory(options: options);
            await using var transport = (KafkaBrokerTransport)factory.Create(
                KafkaTestHelper.NewConnection("kafkas://broker.example:9093"),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            ProducerConfig producer = ConfluentKafkaClientAdapter.CreateProducerConfig(transport.Options);
            ConsumerConfig consumer = ConfluentKafkaClientAdapter.CreateConsumerConfig(transport.Options);

            Assert.Multiple(() =>
            {
                Assert.That(transport.Options.Tls, Is.Not.Null);
                Assert.That(transport.Options.Tls!.UseTls, Is.True);
                Assert.That(options.Tls!.UseTls, Is.False);
                Assert.That(options.SecurityProtocol, Is.EqualTo(protocol));
            });
            AssertEffectiveConfig(producer, expectedProtocol, credentials: false);
            AssertEffectiveConfig(consumer, expectedProtocol, credentials: false);
        }

        /// <summary>
        /// Scheme-derived defaults remain effective when there is no explicit TLS options object.
        /// </summary>
        [TestCase("kafka://broker.example:9092", SecurityProtocol.Plaintext)]
        [TestCase("kafkas://broker.example:9093", SecurityProtocol.Ssl)]
        [Category("SecurityRegression")]
        public void SecurityF13MissingTlsOptionsPreserveEndpointSecurity(
            string endpoint,
            SecurityProtocol expectedProtocol)
        {
            KafkaConnectionOptions options = CreateOptions(
                KafkaSecurityProtocol.Plaintext, useTls: false, credentials: false);
            options.Endpoint = endpoint;
            options.Tls = null;

            ProducerConfig producer = ConfluentKafkaClientAdapter.CreateProducerConfig(options);
            ConsumerConfig consumer = ConfluentKafkaClientAdapter.CreateConsumerConfig(options);

            Assert.That(producer.SecurityProtocol, Is.EqualTo(expectedProtocol));
            Assert.That(consumer.SecurityProtocol, Is.EqualTo(expectedProtocol));
            Assert.That(producer.SaslPassword, Is.Null);
            Assert.That(consumer.SaslPassword, Is.Null);
        }

        /// <summary>
        /// An unknown security protocol is rejected rather than silently mapped to plaintext.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF13InvalidProtocolFailsBeforeClientCreation()
        {
            KafkaConnectionOptions options = CreateOptions((KafkaSecurityProtocol)(-1), useTls: true);
            await using var adapter = new ConfluentKafkaClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System);

            await Assert.ThatAsync(
                async () => await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentOutOfRangeException>()).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(adapter.IsConnected, Is.False);
                Assert.That(
                    () => ConfluentKafkaClientAdapter.CreateProducerConfig(options),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => ConfluentKafkaClientAdapter.CreateConsumerConfig(options),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        /// <summary>
        /// Creates managed options containing synthetic, non-production credentials.
        /// </summary>
        private static KafkaConnectionOptions CreateOptions(
            KafkaSecurityProtocol protocol,
            bool useTls,
            bool credentials = true)
        {
            return new KafkaConnectionOptions
            {
                Endpoint = "kafka://broker.example:9092",
                ClientId = "security-f13-client",
                GroupId = "security-f13-group",
                SecurityProtocol = protocol,
                SaslMechanism = credentials ? KafkaSaslMechanism.Plain : KafkaSaslMechanism.None,
                UserName = credentials ? TestUserName : null,
                PasswordBytes = credentials ? System.Text.Encoding.UTF8.GetBytes(TestPassword) : null,
                Tls = new KafkaTlsOptions
                {
                    UseTls = useTls,
                    ValidateServerCertificate = true
                }
            };
        }

        /// <summary>
        /// Checks final native-client configuration values rather than the input TLS flags.
        /// </summary>
        private static void AssertEffectiveConfig(
            ClientConfig config,
            SecurityProtocol expectedProtocol,
            bool credentials)
        {
            Assert.Multiple(() =>
            {
                Assert.That(config.SecurityProtocol, Is.EqualTo(expectedProtocol));
                Assert.That(config.SaslUsername, Is.EqualTo(credentials ? TestUserName : null));
                Assert.That(config.SaslPassword, Is.EqualTo(credentials ? TestPassword : null));
                Assert.That(config.EnableSslCertificateVerification, Is.True);
                if (credentials)
                {
                    Assert.That(config.SaslMechanism, Is.EqualTo(SaslMechanism.Plain));
                }
                else
                {
                    Assert.That(config.SaslMechanism, Is.Null);
                }
            });
        }

        /// <summary>Non-production user name used only by managed configuration tests.</summary>
        private const string TestUserName = "security-regression-user";

        /// <summary>Non-secret test data with non-ASCII characters to check password decoding.</summary>
        private const string TestPassword = "security-regression-only-p\u00E4ssword";
    }
}
