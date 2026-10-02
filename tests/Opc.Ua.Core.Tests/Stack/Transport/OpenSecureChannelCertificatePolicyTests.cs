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

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// OPC 10000-4 §6.1.3 (Security Policy Check) and OPC 10000-6 §6.1: the
    /// certificate a peer opens a secure channel with has to carry a key the
    /// security policy allows.
    /// </summary>
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class OpenSecureChannelCertificatePolicyTests
    {
        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;

        [Test]
        [CancelAfter(60000)]
        public async Task ServerRejectsClientKeyLongerThanPolicyAllowsAsync()
        {
            // Basic256 allows RSA keys of 1024 to 2048 bits.
            using Certificate server = CreateRsa("CN=server", 2048);
            using Certificate client = CreateRsa("CN=client", 4096);

            ServiceResultException? error = await OpenAsync(
                SecurityPolicies.Basic256, server, client, ObjectTypeIds.RsaSha256ApplicationCertificateType)
                .ConfigureAwait(false);

            Assert.That(error, Is.Not.Null, "the server accepted a 4096 bit client key on Basic256");
            Assert.That(error!.StatusCode, Is.EqualTo((uint)StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ClientRejectsServerKeyLongerThanPolicyAllowsAsync()
        {
            using Certificate server = CreateRsa("CN=server", 4096);
            using Certificate client = CreateRsa("CN=client", 2048);

            ServiceResultException? error = await OpenAsync(
                SecurityPolicies.Basic256, server, client, ObjectTypeIds.RsaSha256ApplicationCertificateType)
                .ConfigureAwait(false);

            Assert.That(error, Is.Not.Null, "the client accepted a 4096 bit server key on Basic256");
        }

        [Test]
        [CancelAfter(60000)]
        public async Task KeysWithinPolicyWindowStillOpenAsync()
        {
            using Certificate server = CreateRsa("CN=server", 2048);
            using Certificate client = CreateRsa("CN=client", 2048);

            ServiceResultException? error = await OpenAsync(
                SecurityPolicies.Basic256, server, client, ObjectTypeIds.RsaSha256ApplicationCertificateType)
                .ConfigureAwait(false);

            Assert.That(error, Is.Null);
        }

        /// <summary>
        /// A 256-bit curve policy accepts certificates on the larger curve of
        /// the same family.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task NistP256PolicyAcceptsP384CertificatesAsync()
        {
            if (SecurityPolicies.Default.GetInfo(SecurityPolicies.ECC_nistP256) == null)
            {
                Assert.Ignore("ECC_nistP256 is not supported on this platform.");
                return;
            }

            using Certificate server = CreateEcc("CN=server", ECCurve.NamedCurves.nistP384);
            using Certificate client = CreateEcc("CN=client", ECCurve.NamedCurves.nistP384);

            ServiceResultException? error = await OpenAsync(
                SecurityPolicies.ECC_nistP256, server, client, ObjectTypeIds.EccNistP384ApplicationCertificateType)
                .ConfigureAwait(false);

            Assert.That(error, Is.Null);
        }

        private static Certificate CreateRsa(string subject, ushort keySize)
        {
            return s_certificateFactory.CreateCertificate(subject).SetRSAKeySize(keySize).CreateForRSA();
        }

        private static Certificate CreateEcc(string subject, ECCurve curve)
        {
            return s_certificateFactory.CreateCertificate(subject).SetECCurve(curve).CreateForECDsa();
        }

        private static async Task<ServiceResultException?> OpenAsync(
            string securityPolicyUri,
            Certificate serverCertificate,
            Certificate clientCertificate,
            NodeId serverCertificateType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var serverChain = new CertificateCollection();
            using var clientChain = new CertificateCollection();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = securityPolicyUri,
                TransportProfileUri = Profiles.UaTcpTransport,
                ServerCertificate = serverCertificate.RawData.ToByteString()
            };
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry.SetupGet(r => r.SendCertificateChain).Returns(false);
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(securityPolicyUri))
                .Returns(() => new CertificateEntry(serverCertificate, serverChain, serverCertificateType));
            var validator = new Mock<ICertificateValidatorEx>();
            validator
                .Setup(v => v.ValidateAsync(
                    It.IsAny<Certificate>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(CertificateValidationResult.Success));
            validator
                .Setup(v => v.ValidateAsync(
                    It.IsAny<CertificateCollection>(),
                    It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<Opc.Ua.Security.Certificates.CertificateValidationOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(CertificateValidationResult.Success));

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                new TransportListenerSettings
                {
                    Descriptions = new List<EndpointDescription> { endpoint },
                    Configuration = configuration,
                    ServerCertificates = certificateRegistry.Object,
                    CertificateValidator = validator.Object,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    MaxChannelCount = 10
                },
                new Mock<ITransportListenerCallback>().Object,
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                using var channel = new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(telemetry), telemetry)
                {
                    OperationTimeout = 10000
                };
                try
                {
                    await channel.OpenAsync(
                        endpointUrl,
                        new TransportChannelSettings
                        {
                            Description = endpoint,
                            Configuration = configuration,
                            ClientCertificate = clientCertificate,
                            ClientCertificateChain = clientChain,
                            ServerCertificate = serverCertificate,
                            CertificateValidator = validator.Object,
                            NamespaceUris = new NamespaceTable(),
                            Factory = EncodeableFactory.Create()
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException e)
                {
                    return e;
                }

                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            finally
            {
                await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
