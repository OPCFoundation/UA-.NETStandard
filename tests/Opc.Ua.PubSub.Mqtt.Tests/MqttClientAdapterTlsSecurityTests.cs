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
using System.Net;
#if !MQTTNET_V5
using System.Net.Security;
#endif
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
#if MQTTNET_V5
using MQTTnet;
#else
using MQTTnet.Client;
#endif
using NUnit.Framework;
using Moq;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Exercises the installed TLS options and legacy certificate callback without opening a connection.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [CancelAfter(10000)]
    public sealed class MqttClientAdapterTlsSecurityTests
    {
        /// <summary>
        /// Trust-only options cannot override a secure scheme on the actual adapter connect path.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityR23TrustOnlyMqttsOptionsCannotUsePlaintextConnectAsync()
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityR23 Trusted Issuer");
            var resolver = new Mock<IMqttTrustedIssuerResolver>();
            resolver.Setup(r => r.ResolveAsync(
                    It.IsAny<System.Collections.Generic.IReadOnlyList<string>>(),
                    It.IsAny<ITelemetryContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<CertificateCollection>(
                    new CertificateCollection { Certificate.FromRawData(issuer.RawData) }));
            var client = new Mock<IMqttClient>();
            MqttClientOptions? captured = null;
            client.Setup(c => c.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()))
                .Callback<MqttClientOptions, CancellationToken>((options, _) => captured = options)
                .ReturnsAsync(new MqttClientConnectResult());
            await using var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System, resolver.Object, client.Object);

            await adapter.ConnectAsync(new MqttConnectionOptions
            {
                Endpoint = "mqtts://broker.example",
                Tls = new MqttTlsOptions { TrustedIssuerCertificateSubjects = ["CN=SecurityR23 Trusted Issuer"] }
            }, CancellationToken.None).ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            var channel = (MqttClientTcpOptions)captured!.ChannelOptions;
            Assert.Multiple(() =>
            {
                Assert.That(channel.RemoteEndpoint, Is.TypeOf<DnsEndPoint>());
                var endpoint = (DnsEndPoint)channel.RemoteEndpoint;
                Assert.That(endpoint.Host, Is.EqualTo("broker.example"));
                Assert.That(endpoint.Port, Is.EqualTo(8883));
                Assert.That(channel.TlsOptions.UseTls, Is.True);
                Assert.That(channel.TlsOptions.AllowUntrustedCertificates, Is.False);
                Assert.That(channel.TlsOptions.IgnoreCertificateChainErrors, Is.False);
            });
#if MQTTNET_V5
            Assert.That(channel.TlsOptions.TrustChain, Has.Count.EqualTo(1));
            Assert.That(channel.TlsOptions.CertificateValidationHandler, Is.Null);
#else
            using X509Certificate2 broker = CreateBrokerCertificate(issuer);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateChainErrors), Is.True);
            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateNameMismatch), Is.False);
#endif
            client.Verify(c => c.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
            resolver.Verify(r => r.ResolveAsync(
                It.IsAny<System.Collections.Generic.IReadOnlyList<string>>(),
                It.IsAny<ITelemetryContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Explicit opt-in TLS and secure endpoint schemes compose monotonically, retaining intentional plaintext.
        /// </summary>
        [TestCase("mqtt://broker.example", false, false)]
        [TestCase("mqtt://broker.example", true, true)]
        [TestCase("mqtts://broker.example", false, true)]
        [TestCase("mqtts://broker.example", true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityR23SchemeAndTlsOptionMatrixPreservesIntentionalPlaintextPolicyAsync(
            string endpoint, bool optionTls, bool expectedTls)
        {
            var client = new Mock<IMqttClient>();
            MqttClientOptions? captured = null;
            client.Setup(c => c.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()))
                .Callback<MqttClientOptions, CancellationToken>((options, _) => captured = options)
                .ReturnsAsync(new MqttClientConnectResult());
            await using var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System, client: client.Object);
            await adapter.ConnectAsync(new MqttConnectionOptions
            {
                Endpoint = endpoint,
                Tls = new MqttTlsOptions { UseTls = optionTls }
            }, CancellationToken.None).ConfigureAwait(false);
            Assert.That(((MqttClientTcpOptions)captured!.ChannelOptions).TlsOptions?.UseTls ?? false,
                Is.EqualTo(expectedTls));
        }

        /// <summary>
        /// Store cancellation or failure must not fall back to an unprotected broker connection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR23IssuerResolutionFailureCannotFallBackToPlaintextAsync(bool cancelled)
        {
            var client = new Mock<IMqttClient>();
            var resolver = new Mock<IMqttTrustedIssuerResolver>();
            Exception failure = cancelled ? new OperationCanceledException() : new InvalidOperationException("store");
            resolver.Setup(r => r.ResolveAsync(
                    It.IsAny<System.Collections.Generic.IReadOnlyList<string>>(),
                    It.IsAny<ITelemetryContext>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            await using var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(), TimeProvider.System, resolver.Object, client.Object);
            await Assert.ThatAsync(async () => await adapter.ConnectAsync(new MqttConnectionOptions
            {
                Endpoint = "mqtts://broker.example",
                Tls = new MqttTlsOptions { TrustedIssuerCertificateSubjects = ["CN=issuer"] }
            }, CancellationToken.None).ConfigureAwait(false), Throws.Exception.SameAs(failure));
            client.Verify(c => c.ConnectAsync(It.IsAny<MqttClientOptions>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Missing custom issuers leave the platform's certificate validation in place.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF05DefaultTlsRetainsPlatformValidation(bool emptyTrustChain)
        {
            MqttClientTlsOptions tls = CreateTlsChannel(
                emptyTrustChain ? [] : null).TlsOptions;

            Assert.Multiple(() =>
            {
                Assert.That(tls.UseTls, Is.True);
                Assert.That(tls.CertificateValidationHandler, Is.Null);
                Assert.That(tls.AllowUntrustedCertificates, Is.False);
                Assert.That(tls.IgnoreCertificateChainErrors, Is.False);
                Assert.That(tls.IgnoreCertificateRevocationErrors, Is.False);
            });
        }

#if MQTTNET_V5
        /// <summary>
        /// Modern MQTT uses custom roots without replacing the platform's identity and usage checks.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05ModernCustomTrustRetainsPlatformValidation()
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            var trustedIssuers = new X509Certificate2Collection(issuer);

            MqttClientTlsOptions tls = CreateTlsChannel(trustedIssuers).TlsOptions;

            Assert.Multiple(() =>
            {
                Assert.That(tls.UseTls, Is.True);
                Assert.That(tls.TrustChain, Is.EqualTo(trustedIssuers));
                Assert.That(tls.CertificateValidationHandler, Is.Null);
                Assert.That(tls.AllowUntrustedCertificates, Is.False);
                Assert.That(tls.IgnoreCertificateChainErrors, Is.False);
                Assert.That(tls.IgnoreCertificateRevocationErrors, Is.False);
            });
        }
#else
        /// <summary>
        /// A trusted issuer cannot override the TLS handshake's broker identity errors.
        /// </summary>
        [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch)]
        [TestCase(SslPolicyErrors.RemoteCertificateNotAvailable)]
        [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)]
        [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
        [TestCase(SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateChainErrors)]
        [TestCase(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable |
            SslPolicyErrors.RemoteCertificateChainErrors)]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustRetainsTlsIdentityErrors(SslPolicyErrors errors)
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            string brokerName = (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0
                ? "attacker.example"
                : "broker.example";
            using X509Certificate2 broker = CreateBrokerCertificate(issuer, dnsName: brokerName);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(InvokeInstalledValidation(channel, broker, platformChain, errors), Is.False);
        }

        /// <summary>
        /// Custom trust accepts the configured issuer despite its absence from the machine root store.
        /// </summary>
        [TestCase(SslPolicyErrors.None)]
        [TestCase(SslPolicyErrors.RemoteCertificateChainErrors)]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustAcceptsConfiguredIssuer(SslPolicyErrors errors)
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            using X509Certificate2 broker = CreateBrokerCertificate(issuer);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(
                Array.Exists(platformChain.ChainStatus,
                    status => (status.Status & X509ChainStatusFlags.UntrustedRoot) != 0),
                Is.True);
            Assert.That(InvokeInstalledValidation(channel, broker, platformChain, errors), Is.True);
        }

        /// <summary>
        /// A configured intermediate CA remains a supported custom trust anchor.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustAcceptsConfiguredIntermediateAnchor()
        {
            using X509Certificate2 root = CreateIssuer("CN=SecurityF05 Unconfigured Root");
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Intermediate", root);
            using X509Certificate2 broker = CreateBrokerCertificate(issuer);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(
                Array.Exists(platformChain.ChainStatus,
                    status => (status.Status & X509ChainStatusFlags.PartialChain) != 0),
                Is.True);
            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateChainErrors), Is.True);
        }

        /// <summary>
        /// The installed callback cannot accept a chain from an unconfigured issuer.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustRejectsUnrelatedIssuer()
        {
            using X509Certificate2 trustedIssuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            using X509Certificate2 otherIssuer = CreateIssuer("CN=SecurityF05 Other Issuer");
            using X509Certificate2 broker = CreateBrokerCertificate(otherIssuer);
            using X509Chain platformChain = CreatePlatformChain(otherIssuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(trustedIssuer));

            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateChainErrors), Is.False);
        }

        /// <summary>
        /// Server-authentication usage is enforced both when supplied by TLS and when rebuilding the chain.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustRejectsInvalidServerUsage(bool platformChecksUsage)
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            using X509Certificate2 broker = CreateBrokerCertificate(issuer, serverUsage: false);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker, platformChecksUsage);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(
                Array.Exists(platformChain.ChainStatus,
                    status => (status.Status & X509ChainStatusFlags.NotValidForUsage) != 0),
                Is.EqualTo(platformChecksUsage));
            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateChainErrors), Is.False);
        }

        /// <summary>
        /// Trust-anchor substitution does not suppress other chain-validation failures.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustRejectsExpiredCertificate()
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            using X509Certificate2 broker = CreateBrokerCertificate(issuer, expired: true);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(
                Array.Exists(platformChain.ChainStatus,
                    status => (status.Status & X509ChainStatusFlags.NotTimeValid) != 0),
                Is.True);
            Assert.That(InvokeInstalledValidation(
                channel, broker, platformChain, SslPolicyErrors.RemoteCertificateChainErrors), Is.False);
        }

        /// <summary>
        /// An absent certificate cannot pass the custom callback even without a supplied policy error.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustRejectsMissingCertificate()
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            MqttClientTcpOptions channel = CreateTlsChannel(new X509Certificate2Collection(issuer));

            Assert.That(InvokeInstalledValidation(channel, null, null, SslPolicyErrors.None), Is.False);
        }

        /// <summary>
        /// Disabling certificate validation remains an explicit opt-out rather than the custom-trust default.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public void SecurityF05LegacyTrustAllowsExplicitValidationOptOut()
        {
            using X509Certificate2 issuer = CreateIssuer("CN=SecurityF05 Trusted Issuer");
            using X509Certificate2 broker = CreateBrokerCertificate(issuer, serverUsage: false);
            using X509Chain platformChain = CreatePlatformChain(issuer, broker);
            MqttClientTcpOptions channel = CreateTlsChannel(
                new X509Certificate2Collection(issuer), validateServerCertificate: false);

            Assert.That(channel.TlsOptions.AllowUntrustedCertificates, Is.True);
            Assert.That(InvokeInstalledValidation(channel, broker, platformChain,
                SslPolicyErrors.RemoteCertificateNameMismatch |
                SslPolicyErrors.RemoteCertificateNotAvailable |
                SslPolicyErrors.RemoteCertificateChainErrors), Is.True);
        }
#endif

        /// <summary>
        /// Builds the same TLS channel configuration used by the connecting adapter.
        /// </summary>
        private static MqttClientTcpOptions CreateTlsChannel(
            X509Certificate2Collection? trustedIssuers,
            bool validateServerCertificate = true)
        {
            MqttClientOptions options = MqttClientAdapter.ConfigureTls(
                new MqttClientOptionsBuilder().WithTcpServer("broker.example", 8883),
                new MqttTlsOptions
                {
                    UseTls = true,
                    ValidateServerCertificate = validateServerCertificate
                },
                trustedIssuers).Build();

            Assert.That(options.ChannelOptions, Is.TypeOf<MqttClientTcpOptions>());
            return (MqttClientTcpOptions)options.ChannelOptions;
        }

        /// <summary>
        /// Creates an in-memory CA without installing it in a host certificate store.
        /// </summary>
        private static X509Certificate2 CreateIssuer(string subjectName, X509Certificate2? issuer = null)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(subjectName, key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var notBefore = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var notAfter = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
            if (issuer is null)
            {
                return request.CreateSelfSigned(notBefore, notAfter);
            }

            using X509Certificate2 certificate = request.Create(issuer, notBefore, notAfter, [2]);
            return certificate.CopyWithPrivateKey(key);
        }

#if !MQTTNET_V5
        /// <summary>
        /// Creates a public broker certificate with independently selectable usage and validity.
        /// </summary>
        private static X509Certificate2 CreateBrokerCertificate(
            X509Certificate2 issuer,
            bool serverUsage = true,
            bool expired = false,
            string dnsName = "broker.example")
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new(serverUsage ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(dnsName);
            request.CertificateExtensions.Add(names.Build());
            return request.Create(
                issuer,
                new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(expired ? 2002 : 2099, 1, 1, 0, 0, 0, TimeSpan.Zero),
                [1]);
        }

        /// <summary>
        /// Models the platform TLS chain with no revocation or issuer network retrieval.
        /// </summary>
        private static X509Chain CreatePlatformChain(
            X509Certificate2 issuer,
            X509Certificate2 broker,
            bool checkServerUsage = true)
        {
            var chain = new X509Chain();
            try
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.ExtraStore.Add(issuer);
                if (checkServerUsage)
                {
                    chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                }
                _ = chain.Build(broker);
                return chain;
            }
            catch
            {
                chain.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Invokes the callback actually installed on the built MQTTnet v4 channel.
        /// </summary>
        private static bool InvokeInstalledValidation(
            MqttClientTcpOptions channel,
            X509Certificate? certificate,
            X509Chain? platformChain,
            SslPolicyErrors errors)
        {
            Func<MqttClientCertificateValidationEventArgs, bool>? handler =
                channel.TlsOptions.CertificateValidationHandler;
            Assert.That(handler, Is.Not.Null);
            return handler!(new MqttClientCertificateValidationEventArgs(
                certificate!, platformChain!, errors, channel));
        }
#endif
    }
}
