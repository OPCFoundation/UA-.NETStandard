#if NET9_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    [Parallelizable(ParallelScope.All)]
    public class QuicRegistrationTests
    {
        [Test]
        public void FactoriesReportOpcQuicAndCreateExpectedInstances()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var channelFactory = new QuicTransportChannelFactory();
            var listenerFactory = new QuicTransportListenerFactory();

            using ITransportChannel channel = channelFactory.Create(telemetry);
            ITransportListener listener = listenerFactory.Create(telemetry);

            Assert.Multiple(() =>
            {
                Assert.That(channelFactory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcQuic));
                Assert.That(listenerFactory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcQuic));
                Assert.That(channel, Is.InstanceOf<QuicTransportChannel>());
                Assert.That(listener, Is.InstanceOf<QuicTransportListener>());
            });
        }

        [Test]
        public async Task CreateServiceHostAsyncPublishesDiscoverableQuicEndpoints()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new CapturingServer(telemetry);
            ApplicationConfiguration configuration = CreateConfiguration(telemetry);
            server.Initialize(configuration);

            using Certificate certificate = CertificateBuilder
                .Create("CN=QuicRegistration")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var certificateEntry = new CertificateEntry(
                certificate,
                new CertificateCollection(),
                new NodeId(0));

            var registry = new Mock<ICertificateRegistry>(MockBehavior.Strict);
            registry.SetupGet(x => x.SendCertificateChain).Returns(false);
            registry
                .Setup(x => x.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => certificateEntry.AddRef());

            var validator = new Mock<ICertificateValidatorEx>(MockBehavior.Loose);
            var hosts = new Dictionary<string, ServiceHost>();
            ServerSecurityPolicy[] configuredPolicies =
            [
                new()
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                new()
                {
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            ];

            ArrayOf<ServerSecurityPolicy> policies = [];
            foreach (ServerSecurityPolicy policy in configuredPolicies)
            {
                policies = policies.AddItem(policy);
            }

            ArrayOf<string> baseAddresses = [];
            baseAddresses = baseAddresses.AddItem("opc.tcp://localhost:4840/UA/Test");
            baseAddresses = baseAddresses.AddItem("opc.quic://localhost:4840/UA/Test");

            List<EndpointDescription> endpoints = await new QuicTransportListenerFactory()
                .CreateServiceHostAsync(
                    server,
                    hosts,
                    configuration,
                    baseAddresses,
                    CreateServerDescription(),
                    policies,
                    registry.Object,
                    validator.Object)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(endpoints, Has.Count.EqualTo(policies.Count));
                Assert.That(server.OpenedEndpoints, Has.Count.EqualTo(1));
                Assert.That(hosts, Has.Count.EqualTo(1));
                Assert.That(endpoints.Select(e => e.SecurityPolicyUri),
                    Is.EquivalentTo(configuredPolicies.Select(p => p.SecurityPolicyUri)));
            });

            foreach (EndpointDescription endpoint in endpoints)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(endpoint.TransportProfileUri, Is.EqualTo(Profiles.UaQuicTransport));
                    Assert.That(
                        new Uri(endpoint.EndpointUrl!).Scheme,
                        Is.EqualTo(Utils.UriSchemeOpcQuic));
                    Assert.That(endpoint.Server, Is.SameAs(endpoints[0].Server));
                    Assert.That(endpoint.UserIdentityTokens, Is.Not.Empty);
                });
            }
        }

        [Test]
        public void OpcQuicEndpointRequiresBinaryEncoding()
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.quic://localhost:4840/UA/Test",
                TransportProfileUri = Profiles.UaQuicTransport
            };

            Assert.That(endpoint.EncodingSupport, Is.EqualTo(BinaryEncodingSupport.Required));
        }

        [Test]
        public void AddQuicTransportRegistersChannelAndListenerFactories()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddQuicTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<QuicTransportChannelFactory>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<QuicTransportListenerFactory>(), Is.Not.Null);
                Assert.That(registry.HasChannelFactory(Utils.UriSchemeOpcQuic), Is.True);
                Assert.That(registry.HasListenerFactory(Utils.UriSchemeOpcQuic), Is.True);
                Assert.That(registry.GetChannelFactory(Utils.UriSchemeOpcQuic),
                    Is.InstanceOf<QuicTransportChannelFactory>());
                Assert.That(registry.GetListenerFactory(Utils.UriSchemeOpcQuic),
                    Is.InstanceOf<QuicTransportListenerFactory>());
            });
        }

        [Test]
        public void QuicByteTransportFactoryAcceptsMissingOptionsAndValidatesRequiredInputs()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var factory = new QuicByteTransportFactory(telemetry);

            Assert.That(factory.Implementation, Is.EqualTo(QuicMultiplexedTransport.ImplementationName));
            Assert.That(
                () => factory.Create(null!, 8192, telemetry),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("bufferManager"));
        }

        [Test]
        public void QuicByteTransportRefusesMissingUrlClearly()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var bufferManager = new BufferManager("QuicRegistration", 8192, telemetry);
            IUaSCByteTransport transport = new QuicByteTransportFactory(telemetry)
                .Create(bufferManager, 8192, telemetry);

            Assert.That(
                async () => await transport.ConnectAsync(null!, CancellationToken.None),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("url"));
        }

        [Test]
        public void NonQuicUrlDoesNotResolveToTheQuicChannelFactory()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddQuicTransport();

            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();

            Assert.That(
                registry.GetChannelFactory(Utils.UriSchemeOpcTcp),
                Is.Not.InstanceOf<QuicTransportChannelFactory>());
        }

        [Test]
        public async Task MismatchedAlpnListenerIsRefused()
        {
            if (!QuicConnection.IsSupported)
            {
                Assert.Ignore("QUIC is unavailable on this platform.");
            }

            using X509Certificate2 certificate = CreateTlsCertificate();
            var wrongProtocol = new SslApplicationProtocol("not-opcua");
            await using QuicListener listener = await QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    // Dual stack, like QuicTransportListener: "localhost"
                    // resolves to ::1 first on some hosts, and a listener bound
                    // to the IPv4 loopback would never see the handshake. This
                    // test would then pass for the wrong reason, because a
                    // connection that never arrives also throws.
                    ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                    ApplicationProtocols = [wrongProtocol],
                    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(
                        new QuicServerConnectionOptions
                        {
                            DefaultStreamErrorCode = 0x0A,
                            DefaultCloseErrorCode = 0x0B,
                            ServerAuthenticationOptions = new SslServerAuthenticationOptions
                            {
                                ApplicationProtocols = [wrongProtocol],
                                ServerCertificate = certificate
                            }
                        })
                }).ConfigureAwait(false);

            var options = new QuicClientOptions
            {
                HandshakeTimeout = TimeSpan.FromSeconds(5),
                ServerCertificateValidation = (_, _, _, _) => true
            };

            Exception exception = Assert.CatchAsync(
                async () => await QuicConnectionBuilder
                    .ConnectAsync(
                        QuicTransport.CreateUrl("localhost", listener.LocalEndPoint.Port),
                        options,
                        CancellationToken.None)
                    .ConfigureAwait(false))!;

            Assert.That(
                exception,
                Is.TypeOf<ServiceResultException>()
                    .Or.TypeOf<AuthenticationException>());
        }

        private static ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "QuicRegistrationTests",
                ApplicationUri = "urn:localhost:QuicRegistrationTests",
                ApplicationType = ApplicationType.Server,
                ServerConfiguration = new ServerConfiguration
                {
                    UserTokenPolicies = new ArrayOf<UserTokenPolicy>().AddItem(
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }),
                    SecurityPolicies = []
                },
                TransportQuotas = new TransportQuotas()
            };
        }

        private static ApplicationDescription CreateServerDescription()
        {
            return new ApplicationDescription
            {
                ApplicationName = new LocalizedText("QuicRegistrationTests"),
                ApplicationUri = "urn:localhost:QuicRegistrationTests",
                ApplicationType = ApplicationType.Server,
                ProductUri = "urn:localhost:QuicRegistrationTests"
            };
        }

        private static X509Certificate2 CreateTlsCertificate()
        {
            using Certificate created = CertificateBuilder
                .Create("CN=QuicRegistration")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:localhost:QuicRegistrationTests",
                    ["localhost"]))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] pfx = created.AsX509Certificate2().Export(X509ContentType.Pfx);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
        }

        private sealed class CapturingServer : ServerBase
        {
            public CapturingServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public List<IReadOnlyList<EndpointDescription>> OpenedEndpoints { get; } = [];

            public void Initialize(ApplicationConfiguration configuration)
            {
                ServiceMessageContext messageContext = configuration.CreateMessageContext();
                messageContext.NamespaceUris = new NamespaceTable();
                typeof(ServerBase)
                    .GetField("m_messageContext", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(this, messageContext);
            }

            public override ValueTask CreateServiceHostEndpointAsync(
                Uri endpointUri,
                List<EndpointDescription> endpoints,
                EndpointConfiguration endpointConfiguration,
                ITransportListener listener,
                ICertificateValidatorEx certificateValidator,
                CancellationToken ct = default)
            {
                OpenedEndpoints.Add([.. endpoints]);
                return ValueTask.CompletedTask;
            }
        }
    }
}
#endif
