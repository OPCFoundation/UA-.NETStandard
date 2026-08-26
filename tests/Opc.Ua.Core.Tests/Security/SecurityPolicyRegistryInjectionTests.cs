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

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security
{
    /// <summary>
    /// Proves that a security policy registered through the container is the
    /// policy set the stack actually resolves against, rather than the
    /// process-wide <see cref="SecurityPolicies.Default"/> fallback.
    /// </summary>
    [TestFixture]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class SecurityPolicyRegistryInjectionTests
    {
        [Test]
        public void ChannelQuotasCarriesTheSecurityPolicyRegistry()
        {
            SecurityPolicies registry = CreateCustomRegistry(CreateCustomPolicy());
            var quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry))
            {
                SecurityPolicyRegistry = registry
            };

            Assert.That(quotas.SecurityPolicyRegistry, Is.SameAs(registry));
            Assert.That(
                new ChannelQuotas(ServiceMessageContext.Create(m_telemetry)).SecurityPolicyRegistry,
                Is.Null,
                "A channel with no registry falls back to the built-in policy set.");
        }

        [Test]
        public void TransportSettingsCarryTheSecurityPolicyRegistry()
        {
            SecurityPolicies registry = CreateCustomRegistry(CreateCustomPolicy());

            var channelSettings = new TransportChannelSettings
            {
                SecurityPolicyRegistry = registry
            };
            var listenerSettings = new TransportListenerSettings
            {
                SecurityPolicyRegistry = registry
            };

            Assert.Multiple(() =>
            {
                Assert.That(channelSettings.SecurityPolicyRegistry, Is.SameAs(registry));
                Assert.That(listenerSettings.SecurityPolicyRegistry, Is.SameAs(registry));
                Assert.That(new TransportChannelSettings().SecurityPolicyRegistry, Is.Null);
                Assert.That(new TransportListenerSettings().SecurityPolicyRegistry, Is.Null);
            });
        }

        [Test]
        public void UnsupportedAsymmetricEncryptionFailsClosed()
        {
            var policy = new SecurityPolicyInfo("urn:test:unsupported")
            {
                AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None
            };
            MethodInfo getPadding = typeof(UaSCUaBinaryChannel).GetMethod(
                "GetAsymmetricPadding",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var exception = Assert.Throws<TargetInvocationException>(
                () => getPadding.Invoke(null, [policy]));

            Assert.That(
                exception!.InnerException,
                Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        /// <summary>
        /// A channel constructed against the registry an application composed
        /// through <c>AddSecurityPolicy</c> negotiates that application's
        /// policy. The same channel built without the registry cannot resolve
        /// it, which is what every production path did before the registry was
        /// threaded through the transport.
        /// </summary>
        [Test]
        public void ChannelNegotiatesAPolicyRegisteredThroughDependencyInjection()
        {
            SecurityPolicyInfo policy = CreateCustomPolicy();

            var services = new ServiceCollection();
            services.AddOpcUa().AddSecurityPolicy(policy);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ISecurityPolicyRegistry>();

            using var injected = new PolicyProbeChannel(m_telemetry, registry, policy.Uri);
            using var fallback = new PolicyProbeChannel(m_telemetry, null, policy.Uri);

            Assert.Multiple(() =>
            {
                Assert.That(
                    injected.ResolvedPolicy,
                    Is.SameAs(policy),
                    "The channel must resolve handshake policies through the injected registry.");
                Assert.That(
                    fallback.ResolvedPolicy,
                    Is.Null,
                    "The built-in policy set does not know the application's policy.");
                Assert.That(SecurityPolicies.Default.GetInfo(policy.Uri), Is.Null);
            });
        }

        [Test]
        public void IdentitySelectionContextResolvesThroughTheInjectedRegistry()
        {
            SecurityPolicyInfo policy = CreateCustomPolicy();
            SecurityPolicies registry = CreateCustomRegistry(policy);

            var description = new EndpointDescription { SecurityPolicyUri = policy.Uri };
            var withRegistry = new IdentitySelectionContext(
                description,
                default,
                ServiceMessageContext.Create(m_telemetry))
            {
                SecurityPolicyRegistry = registry
            };
            var withoutRegistry = new IdentitySelectionContext(
                description,
                default,
                ServiceMessageContext.Create(m_telemetry));

            Assert.Multiple(() =>
            {
                Assert.That(withRegistry.EffectiveSecurityPolicies, Is.SameAs(registry));
                Assert.That(withRegistry.EffectiveSecurityPolicies.GetInfo(policy.Uri), Is.SameAs(policy));
                Assert.That(
                    withoutRegistry.EffectiveSecurityPolicies,
                    Is.SameAs(SecurityPolicies.Default));
            });
        }


        /// <summary>
        /// A client and a listener composed from the same container complete a
        /// real secure channel handshake on a policy the built-in set does not
        /// carry. Before the registry was threaded through the transport, both
        /// ends resolved handshake policies against
        /// <see cref="SecurityPolicies.Default"/> and rejected it.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ClientAndListenerNegotiateAPolicyRegisteredThroughDependencyInjectionAsync()
        {
            SecurityPolicyInfo policy = CreateCustomPolicy();

            var services = new ServiceCollection();
            services.AddOpcUa().AddSecurityPolicy(policy);

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ISecurityPolicyRegistry>();
            Assert.That(
                SecurityPolicies.Default.GetInfo(policy.Uri),
                Is.Null,
                "The policy must be unknown outside the application that registered it.");

            using Certificate serverCertificate = s_certificateFactory
                .CreateCertificate("CN=policy-registry-server")
                .CreateForRSA();
            using Certificate clientCertificate = s_certificateFactory
                .CreateCertificate("CN=policy-registry-client")
                .CreateForRSA();
            using var serverChain = new CertificateCollection();
            using var clientChain = new CertificateCollection();

            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl.ToString(),
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = policy.Uri,
                TransportProfileUri = Profiles.UaTcpTransport,
                ServerCertificate = serverCertificate.RawData.ToByteString()
            };

            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 20000;
            configuration.MaxMessageSize = 64 * 1024;
            configuration.MaxBufferSize = 64 * 1024;
            configuration.ChannelLifetime = 60000;
            configuration.SecurityTokenLifetime = 60000;

            var callback = new PolicyCapturingCallback();
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry.SetupGet(r => r.SendCertificateChain).Returns(false);
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(policy.Uri))
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

            await using var listener = new TcpTransportListener(m_telemetry);
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
                    MaxChannelCount = 10,
                    SecurityPolicyRegistry = registry
                },
                callback,
                CancellationToken.None).ConfigureAwait(false);

            using var channel = new UaSCUaBinaryTransportChannel(
                new TcpByteTransportFactory(m_telemetry),
                m_telemetry)
            {
                OperationTimeout = 20000
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
                    Factory = EncodeableFactory.Create(),
                    SecurityPolicyRegistry = registry
                },
                CancellationToken.None).ConfigureAwait(false);

            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest
                {
                    RequestHeader = new RequestHeader { TimeoutHint = 20000 },
                    NodesToRead = new ArrayOf<ReadValueId>()
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response, Is.InstanceOf<ReadResponse>());
                Assert.That(
                    callback.NegotiatedSecurityPolicyUri,
                    Is.EqualTo(policy.Uri),
                    "The listener must accept the policy its application registered.");
            });

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a registry that carries the built-in policies as well as the
        /// application's own, the way the container composes one.
        /// </summary>
        private static SecurityPolicies CreateCustomRegistry(SecurityPolicyInfo policy)
        {
            var registry = new SecurityPolicies();
            registry.Register(policy);
            return registry;
        }

        /// <summary>
        /// Builds a policy the built-in set does not carry. It reuses the
        /// Basic256Sha256 cryptography so a peer configured with the same
        /// registry can complete a real handshake with it.
        /// </summary>
        private static SecurityPolicyInfo CreateCustomPolicy()
        {
            return new SecurityPolicyInfo(SecurityPolicyInfo.Basic256Sha256)
            {
                Name = "InjectedPolicy",
                Uri = SecurityPolicies.BaseUri + "InjectedPolicy",
                IsDefault = false
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

        /// <summary>
        /// Records the security policy the listener negotiated for the channel
        /// the request arrived on.
        /// </summary>
        private sealed class PolicyCapturingCallback : ITransportListenerCallback
        {
            public string? NegotiatedSecurityPolicyUri => Volatile.Read(ref m_securityPolicyUri);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Volatile.Write(
                    ref m_securityPolicyUri,
                    secureChannelContext?.EndpointDescription?.SecurityPolicyUri);
                return new ValueTask<IServiceResponse>(
                    new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                    });
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

            private string? m_securityPolicyUri;
        }

        /// <summary>
        /// Exposes the protected policy the channel resolved for its
        /// handshake.
        /// </summary>
        private sealed class PolicyProbeChannel : UaSCUaBinaryChannel
        {
            public PolicyProbeChannel(
                ITelemetryContext telemetry,
                ISecurityPolicyRegistry? securityPolicies,
                string securityPolicyUri)
                : base(
                    "probe",
                    new BufferManager("probe", TcpMessageLimits.DefaultMaxBufferSize, telemetry),
                    new ChannelQuotas(ServiceMessageContext.Create(telemetry))
                    {
                        SecurityPolicyRegistry = securityPolicies
                    },
                    serverCertificate: null,
                    endpoints: new List<EndpointDescription>(),
                    securityMode: MessageSecurityMode.SignAndEncrypt,
                    securityPolicyUri: securityPolicyUri,
                    telemetry: telemetry)
            {
            }

            public SecurityPolicyInfo? ResolvedPolicy => SecurityPolicy;
        }

        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;
        private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();
    }
}
