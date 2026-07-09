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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("TcpTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class UaSCBinaryLoopbackTests
    {
            private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;

            [Test]
            [CancelAfter(15000)]
            public async Task ClientAndTcpListenerExchangeRequestAndCloseAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointDescription endpoint = CreateEndpoint(endpointUrl);
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;
            var callback = new EchoCallback();
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns((CertificateEntry?)null);

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                new TransportListenerSettings
                {
                    Descriptions = new List<EndpointDescription> { endpoint },
                    Configuration = configuration,
                    ServerCertificates = certificateRegistry.Object,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create(),
                    MaxChannelCount = 10
                },
                callback,
                CancellationToken.None).ConfigureAwait(false);

            using var channel = new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(telemetry), telemetry)
            {
                OperationTimeout = 5000
            };
            await channel.OpenAsync(
                endpointUrl,
                new TransportChannelSettings
                {
                    Description = endpoint,
                    Configuration = configuration,
                    NamespaceUris = new NamespaceTable(),
                    Factory = EncodeableFactory.Create()
                },
                CancellationToken.None).ConfigureAwait(false);

            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 5000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(callback.RequestCount, Is.EqualTo(1));
            Assert.That(channel.EndpointDescription.EndpointUrl, Is.EqualTo(endpointUrl.ToString()));
            Assert.That(channel.EndpointConfiguration.OperationTimeout, Is.EqualTo(configuration.OperationTimeout));
            Assert.That(channel.MessageContext, Is.Not.Null);
            Assert.That(channel.CurrentToken, Is.Not.Null);
            Assert.That(channel.SupportedFeatures, Is.Not.EqualTo(TransportChannelFeatures.None));

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(15000)]
        public async Task SecureClientAndTcpListenerExchangeSignedEncryptedRequestAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using Certificate serverCertificate = s_certificateFactory.CreateCertificate("CN=server").CreateForRSA();
            using Certificate clientCertificate = s_certificateFactory.CreateCertificate("CN=client").CreateForRSA();
            using var serverChain = new CertificateCollection();
            using var clientChain = new CertificateCollection();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            EndpointDescription endpoint = CreateEndpoint(endpointUrl);
            endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.ServerCertificate = serverCertificate.RawData.ToByteString();
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;
            var callback = new EchoCallback();
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry.SetupGet(r => r.SendCertificateChain).Returns(false);
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => new CertificateEntry(
                    serverCertificate,
                    serverChain,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType));
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
                callback,
                CancellationToken.None).ConfigureAwait(false);

            using var channel = new UaSCUaBinaryTransportChannel(new TcpByteTransportFactory(telemetry), telemetry)
            {
                OperationTimeout = 5000
            };
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

            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 5000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(callback.RequestCount, Is.EqualTo(1));
            Assert.That(channel.ClientChannelCertificate, Is.EqualTo(clientCertificate.RawData));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(serverCertificate.RawData));
            Assert.That(channel.ChannelThumbprint, Is.Not.Empty);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private static EndpointDescription CreateEndpoint(Uri endpointUrl)
        {
            return new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
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

        private sealed class EchoCallback : ITransportListenerCallback
        {
            public int RequestCount { get; private set; }

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                RequestCount++;
                Assert.That(secureChannelContext, Is.Not.Null);
                Assert.That(request, Is.InstanceOf<ReadRequest>());
                return new ValueTask<IServiceResponse>(
                    new ReadResponse { ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good } });
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }
        }
    }
}
