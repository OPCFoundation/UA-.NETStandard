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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Exercises hostname binding and confidential key dispatch through the real GDS clients.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    public sealed class GdsClientSecurityRegressionTests
    {
        /// <summary>
        /// A trusted chain and a matching advertised application URI do not authenticate another hostname.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF03GdsAndPushRejectTrustedWrongHost(bool push)
        {
            await using var fixture = new ClientContext("requested.test", "other.test");
            await fixture.AssertTrustedAsync().ConfigureAwait(false);

            ServiceResultException failure;
            if (push)
            {
                await using ServerPushConfigurationClient client = CreatePushClient(fixture);
                failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false))
                    ?? throw new AssertionException("Expected a service failure.");
            }
            else
            {
                await using var client = new GlobalDiscoveryServerClient(
                    fixture.Configuration, fixture.Administrator, fixture.Factory);
                failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false))
                    ?? throw new AssertionException("Expected a service failure.");
            }

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(fixture.FactoryCalls, Is.EqualTo(1));
            Assert.That(fixture.CheckDomain, Is.True);
            Assert.That(fixture.Activations, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(fixture.RefreshedDiscoveryUrl!.IdnHost, Is.EqualTo("requested.test"));
            Assert.That(fixture.Endpoint.Description.Server.DiscoveryUrls,
                Is.EqualTo(s_discoveryUrls));
        }

        /// <summary>
        /// Both client factories retain the configured DNS or IP identity through discovery refresh.
        /// </summary>
        [TestCase(false, "gds.test")]
        [TestCase(true, "push.test")]
        [TestCase(false, "192.0.2.10")]
        [TestCase(true, "192.0.2.10")]
        [Category("SecurityRegression")]
        public async Task SecurityF03GdsAndPushBindConfiguredDnsAndIpBeforeActivation(bool push, string host)
        {
            await using var fixture = new ClientContext(host, host);
            await fixture.AssertTrustedAsync().ConfigureAwait(false);

            if (push)
            {
                await using ServerPushConfigurationClient client = CreatePushClient(fixture);
                await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
                await ClientContext.AssertMarkerReadAsync(client.Session!).ConfigureAwait(false);
            }
            else
            {
                await using var client = new GlobalDiscoveryServerClient(
                    fixture.Configuration, fixture.Administrator, fixture.Factory);
                await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
                await ClientContext.AssertMarkerReadAsync(client.Session!).ConfigureAwait(false);
            }

            Assert.That(fixture.FactoryCalls, Is.EqualTo(1));
            Assert.That(fixture.Activations, Is.EqualTo(1));
            Assert.That(fixture.CheckDomain, Is.True);
            Assert.That(fixture.RefreshedDiscoveryUrl!.IdnHost, Is.EqualTo(host));
            Assert.That(fixture.RefreshedDiscoveryUrl.Port, Is.EqualTo(4840));
            Assert.That(fixture.ActivatedIdentity, Is.SameAs(fixture.Administrator));
            Assert.That(fixture.Requests.OfType<ReadRequest>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// A selected discovery endpoint cannot replace the host passed to either string connection workflow.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF03DiscoveryCannotReplaceRequestedHost(bool push, bool matchingHost)
        {
            await using var fixture = new ClientContext(
                "requested.test", matchingHost ? "requested.test" : "other.test");
            await fixture.AssertTrustedAsync().ConfigureAwait(false);
            fixture.Endpoint.Description.EndpointUrl = "opc.tcp://other.test:62553/advertised";
            var requestedEndpoint = new Uri("opc.tcp://requested.test:4840/server");
            await using GlobalDiscoveryServerClient? gds = push
                ? null
                : new GlobalDiscoveryServerClient(fixture.Configuration, fixture.Administrator, fixture.Factory);
            await using ServerPushConfigurationClient? pusher = push
                ? CreatePushClient(fixture)
                : null;

            Task ConnectAsync()
            {
                return push
                    ? pusher!.ConnectInternalAsync(fixture.Endpoint, false, requestedEndpoint, CancellationToken.None)
                    : gds!.ConnectInternalAsync(fixture.Endpoint, false, requestedEndpoint, CancellationToken.None);
            }

            if (matchingHost)
            {
                await ConnectAsync().ConfigureAwait(false);
                await ClientContext.AssertMarkerReadAsync(push ? pusher!.Session! : gds!.Session!)
                    .ConfigureAwait(false);
                Assert.That(fixture.Activations, Is.EqualTo(1));
            }
            else
            {
                ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(ConnectAsync)
                    ?? throw new AssertionException("Expected a service failure.");
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
                Assert.That(fixture.Activations, Is.Zero);
                Assert.That(fixture.Requests, Is.Empty);
            }
            Assert.That(fixture.RefreshedDiscoveryUrl, Is.EqualTo(requestedEndpoint));
            Assert.That(fixture.Endpoint.Description.EndpointUrl, Is.EqualTo("opc.tcp://other.test:62553/advertised"));
            Assert.That(fixture.CheckDomain, Is.True);
        }

        /// <summary>
        /// Discovery cannot change transport protocols and thereby substitute another authenticated host.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF03GdsAndPushRejectDiscoveryTransportChanges(bool push)
        {
            await using var fixture = new ClientContext("requested.test", "requested.test");
            fixture.Endpoint.Description.EndpointUrl = "https://other.test:443/advertised";
            var requestedEndpoint = new Uri("opc.tcp://requested.test:4840/server");
            await using GlobalDiscoveryServerClient? gds = push
                ? null
                : new GlobalDiscoveryServerClient(fixture.Configuration, fixture.Administrator, fixture.Factory);
            await using ServerPushConfigurationClient? pusher = push
                ? CreatePushClient(fixture)
                : null;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await (push
                    ? pusher!.ConnectInternalAsync(fixture.Endpoint, false, requestedEndpoint, CancellationToken.None)
                    : gds!.ConnectInternalAsync(fixture.Endpoint, false, requestedEndpoint, CancellationToken.None))
                    .ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(fixture.FactoryCalls, Is.Zero);
            Assert.That(fixture.Activations, Is.Zero);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Signing, including a genuine sign-only endpoint, never protects uploaded PEM or PFX key bytes.
        /// </summary>
        [TestCase(MessageSecurityMode.None, "PEM")]
        [TestCase(MessageSecurityMode.None, "PFX")]
        [TestCase(MessageSecurityMode.Sign, "PEM")]
        [TestCase(MessageSecurityMode.Sign, "PFX")]
        [Category("SecurityRegression")]
        public async Task SecurityF04PrivateKeyNeverUsesSignOrNoneChannel(
            MessageSecurityMode mode,
            string format)
        {
            await using var fixture = new ClientContext("push.test", "push.test", mode);
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await UploadAsync(client, format, s_privateKey).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Exact certificate, issuer and key arguments use one captured transport and elevated identity is restored.
        /// </summary>
        [TestCase(false, "PEM")]
        [TestCase(true, "PEM")]
        [TestCase(false, "PFX")]
        [TestCase(true, "PFX")]
        [Category("SecurityRegression")]
        public async Task SecurityF04EncryptedUploadUsesCapturedTransportAndRestoresIdentity(
            bool elevate,
            string format)
        {
            await using var fixture = new ClientContext("push.test", "push.test");
            if (elevate)
            {
                fixture.InitialIdentity = new UserIdentity();
            }
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
            IUserIdentity original = fixture.InnerSession.Identity;

            bool applyChanges = await UploadAsync(client, format, s_privateKey).ConfigureAwait(false);

            Assert.That(applyChanges, Is.True);
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            var request = (CallRequest)fixture.Requests[0];
            Assert.That(request.RequestHeader.AuthenticationToken, Is.EqualTo(s_originalToken));
            Assert.That(request.MethodsToCall.Count, Is.EqualTo(1));
            Assert.That(request.MethodsToCall[0].ObjectId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(
                    Ua.ObjectIds.ServerConfiguration, fixture.MessageContext.NamespaceUris)));
            Assert.That(request.MethodsToCall[0].InputArguments, Is.EqualTo(UploadArguments(format, s_privateKey)));
            Assert.That(fixture.InnerSession.Identity, Is.SameAs(original));
            Assert.That(fixture.IdentityAtCall, Is.SameAs(fixture.Administrator));
            Assert.That(fixture.InnerSession.IdentityUpdates,
                Is.EqualTo(elevate ? [fixture.Administrator, original] : Array.Empty<IUserIdentity>()));
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Empty and null key values retain the certificate-only workflow on nonconfidential channels.
        /// </summary>
        [TestCase(MessageSecurityMode.None, false)]
        [TestCase(MessageSecurityMode.None, true)]
        [TestCase(MessageSecurityMode.Sign, false)]
        [TestCase(MessageSecurityMode.Sign, true)]
        [Category("SecurityRegression")]
        public async Task SecurityF04CertificateOnlyUpdatePreservesSignChannelAndElevation(
            MessageSecurityMode mode,
            bool nullKey)
        {
            await using var fixture = new ClientContext("push.test", "push.test", mode)
            {
                InitialIdentity = new UserIdentity()
            };
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
            IUserIdentity original = fixture.InnerSession.Identity;
            ByteString key = nullKey ? default : ByteString.From(Array.Empty<byte>());

            bool applyChanges = await UploadAsync(client, string.Empty, key).ConfigureAwait(false);

            Assert.That(applyChanges, Is.True);
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            var request = (CallRequest)fixture.Requests[0];
            Assert.That(request.MethodsToCall[0].InputArguments, Is.EqualTo(UploadArguments(string.Empty, key)));
            Assert.That(fixture.InnerSession.IdentityUpdates, Is.EqualTo([fixture.Administrator, original]));
            Assert.That(fixture.InnerSession.Identity, Is.SameAs(original));
        }

        /// <summary>
        /// A mutable session endpoint cannot disguise the physical channel that receives the key.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF04PhysicalSignChannelCannotBeDisguisedBySessionMetadata()
        {
            await using var fixture = new ClientContext("push.test", "push.test");
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
            fixture.WireEndpoint.SecurityMode = MessageSecurityMode.Sign;
            Assert.That(fixture.InnerSession.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await UploadAsync(client, "PFX", s_privateKey).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A failed post-elevation capture still restores the original identity without transmitting key bytes.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF04ConfidentialityFailureRestoresElevatedIdentity()
        {
            await using var fixture = new ClientContext("push.test", "push.test", MessageSecurityMode.Sign)
            {
                InitialIdentity = new UserIdentity()
            };
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
            IUserIdentity original = fixture.InnerSession.Identity;

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await UploadAsync(client, "PEM", s_privateKey).ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.InnerSession.IdentityUpdates, Is.EqualTo([fixture.Administrator, original]));
            Assert.That(fixture.InnerSession.Identity, Is.SameAs(original));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Recovery between capture and service dispatch cannot redirect a key to a replacement generation.
        /// </summary>
        [TestCase("Recreate")]
        [TestCase("Transport")]
        [TestCase("Downgrade")]
        [Category("SecurityRegression")]
        public async Task SecurityF04ReplacementCannotReceivePrivateKey(string replacementKind)
        {
            await using var fixture = new ClientContext("push.test", "push.test", managed: true);
            await using ServerPushConfigurationClient client = CreatePushClient(fixture);
            await client.ConnectAsync(fixture.Endpoint).ConfigureAwait(false);
            Task<IDisposable>? writer = null;
            fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(() =>
            {
                writer ??= fixture.PauseDispatchAsync();
                return fixture.WireEndpoint;
            });
            var replacement = new Mock<ITransportChannel>();
            replacement.SetupGet(channel => channel.MessageContext).Returns(fixture.MessageContext);
            replacement.SetupGet(channel => channel.EndpointDescription).Returns(new EndpointDescription
            {
                EndpointUrl = fixture.Endpoint.Description.EndpointUrl,
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            Task<bool> operation = UploadAsync(client, "PFX", s_privateKey).AsTask();
            Assert.That(writer, Is.Not.Null, "The actual transport must be classified before dispatch.");
            using (await writer!.ConfigureAwait(false))
            {
                Assert.That(operation.IsCompleted, Is.False);
                switch (replacementKind)
                {
                    case "Recreate":
                        fixture.InnerSession.SessionCreated(
                            NodeId.Parse("s=replacement-session"), NodeId.Parse("s=replacement-token"));
                        break;
                    case "Transport":
                        SetField(fixture.InnerSession, typeof(ClientBase), "m_channel", replacement.Object);
                        break;
                    default:
                        fixture.WireEndpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                }
            }

            ServiceResultException failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await operation.ConfigureAwait(false))
                ?? throw new AssertionException("Expected a service failure.");

            Assert.That(failure.StatusCode, Is.EqualTo(replacementKind == "Downgrade"
                ? StatusCodes.BadSecurityModeInsufficient
                : StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
            replacement.Verify(channel => channel.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Supplies administrator credentials through the push client's existing property contract.
        /// </summary>
        private static ServerPushConfigurationClient CreatePushClient(ClientContext fixture)
        {
            return new ServerPushConfigurationClient(fixture.Configuration, fixture.Factory)
            {
                AdminCredentials = fixture.Administrator
            };
        }

        /// <summary>
        /// Invokes the real push update with distinguishable synthetic wire values.
        /// </summary>
        private static ValueTask<bool> UploadAsync(
            ServerPushConfigurationClient client,
            string format,
            ByteString key)
        {
            return client.UpdateCertificateAsync(
                NodeId.Parse("s=group"), NodeId.Parse("s=type"), s_certificate, format, key, s_issuers);
        }

        /// <summary>
        /// Gets every expected source-generated method argument, in wire order.
        /// </summary>
        private static ArrayOf<Variant> UploadArguments(string format, ByteString key)
        {
            return
            [
                new(NodeId.Parse("s=group")), new(NodeId.Parse("s=type")), new(s_certificate),
                new(s_issuers), new(format), new(key)
            ];
        }

        /// <summary>
        /// Changes existing lifecycle state only to deterministically model transport recovery.
        /// </summary>
        private static void SetField(object target, Type type, string field, object value)
        {
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        }

        /// <summary>
        /// Queues recovery through the existing private service gate without extending Client's friend assemblies.
        /// </summary>
        private static Task<IDisposable> QueueRecoveryAsync(object gate)
        {
            object operation = gate.GetType()
                .GetMethod("WriterLockAsync", [typeof(CancellationToken)])!
                .Invoke(gate, [CancellationToken.None])!;
            MethodInfo awaitOperation = typeof(GdsClientSecurityRegressionTests)
                .GetMethod(nameof(AwaitRecoveryAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(operation.GetType().GenericTypeArguments[0]);
            return (Task<IDisposable>)awaitOperation.Invoke(null, [operation])!;
        }

        /// <summary>
        /// Awaits the actual recovery lease asynchronously, including when its type is internal to Client.
        /// </summary>
        /// <typeparam name="T">The disposable recovery lease type.</typeparam>
        private static async Task<IDisposable> AwaitRecoveryAsync<T>(ValueTask<T> operation)
            where T : IDisposable
        {
            return await operation.ConfigureAwait(false);
        }

        /// <summary>
        /// Owns real sessions, local certificate trust and recording transports without network listeners.
        /// </summary>
        private sealed class ClientContext : IAsyncDisposable
        {
            /// <summary>
            /// Creates the local trust chain and injected session-establishment boundary.
            /// </summary>
            internal ClientContext(
                string requestedHost,
                string certificateHost,
                MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
                bool managed = false)
            {
                m_managed = managed;
                Telemetry = NUnitTelemetryContext.Create();
                m_root = CertificateBuilder.Create("CN=GDS Security Regression CA")
                    .SetCAConstraint().CreateForRSA();
                m_serverCertificate = CertificateBuilder.Create("CN=GDS Security Regression Server")
                    .AddExtension(new X509SubjectAltNameExtension("urn:gds:security-server", [certificateHost]))
                    .SetIssuer(m_root).CreateForRSA();
                var security = new SecurityConfiguration
                {
                    RejectUnknownRevocationStatus = false,
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        TrustedCertificates = [new CertificateIdentifier { RawData = m_root.RawData }]
                    }
                };
                m_manager = CertificateManagerFactory.Create(security, Telemetry);
                Configuration = new ApplicationConfiguration(Telemetry)
                {
                    ApplicationName = "GdsSecurityRegression",
                    ApplicationUri = "urn:gds:security-client",
                    ApplicationType = ApplicationType.Client,
                    ClientConfiguration = new ClientConfiguration(),
                    SecurityConfiguration = security,
                    CertificateManager = m_manager
                };
                MessageContext = Configuration.CreateMessageContext();
                MessageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
                Endpoint = new ConfiguredEndpoint(null, new EndpointDescription
                {
                    EndpointUrl = $"opc.tcp://{requestedHost}:4840/server",
                    SecurityMode = mode,
                    SecurityPolicyUri = mode == MessageSecurityMode.None
                        ? SecurityPolicies.None
                        : SecurityPolicies.Basic256Sha256,
                    ServerCertificate = m_serverCertificate.RawData.ToByteString(),
                    Server = new ApplicationDescription
                    {
                        ApplicationUri = "urn:gds:security-server",
                        DiscoveryUrls = s_discoveryUrls
                    },
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy { PolicyId = "anonymous", TokenType = UserTokenType.Anonymous },
                        new UserTokenPolicy { PolicyId = "username", TokenType = UserTokenType.UserName }
                    ]
                }, EndpointConfiguration.Create());
                WireEndpoint = CoreUtils.Clone(Endpoint.Description)!;
                Channel = new Mock<ITransportChannel>();
                Channel.SetupGet(channel => channel.MessageContext).Returns(MessageContext);
                Channel.SetupGet(channel => channel.EndpointDescription).Returns(() => WireEndpoint);
                Channel.SetupGet(channel => channel.EndpointConfiguration).Returns(Endpoint.Configuration!);
                Channel.SetupGet(channel => channel.OperationTimeout).Returns(10_000);
                Channel.Setup(channel => channel.SendRequestAsync(
                        It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                    .Returns<IServiceRequest, CancellationToken>(DispatchAsync);
                var factory = new Mock<ISessionFactory>();
                factory.SetupGet(value => value.Telemetry).Returns(Telemetry);
                factory.SetupGet(value => value.SubscriptionEngineFactory)
                    .Returns(DefaultSubscriptionEngineFactory.Instance);
                factory.Setup(value => value.CreateAsync(
                        It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(),
                        It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<uint>(),
                        It.IsAny<IUserIdentity?>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .Returns((ApplicationConfiguration configuration, ConfiguredEndpoint endpoint,
                        bool _, bool checkDomain, string _, uint _, IUserIdentity? identity,
                        ArrayOf<string> _, CancellationToken ct) =>
                        CreateSessionAsync(configuration, endpoint, checkDomain, identity, ct));
                Factory = factory.Object;
            }

            /// <summary>
            /// Gets the fixture telemetry.
            /// </summary>
            internal ITelemetryContext Telemetry { get; }

            /// <summary>
            /// Gets the configuration with entirely in-memory certificate trust.
            /// </summary>
            internal ApplicationConfiguration Configuration { get; }

            /// <summary>
            /// Gets the message and namespace context used by the real session.
            /// </summary>
            internal ServiceMessageContext MessageContext { get; }

            /// <summary>
            /// Gets the operator-configured target.
            /// </summary>
            internal ConfiguredEndpoint Endpoint { get; }

            /// <summary>
            /// Gets the physical channel's independent security metadata.
            /// </summary>
            internal EndpointDescription WireEndpoint { get; }

            /// <summary>
            /// Gets the factory consumed by both actual GDS connection paths.
            /// </summary>
            internal ISessionFactory Factory { get; }

            /// <summary>
            /// Gets the non-networked wire transport.
            /// </summary>
            internal Mock<ITransportChannel> Channel { get; }

            /// <summary>
            /// Gets a distinct synthetic administrator identity.
            /// </summary>
            internal IUserIdentity Administrator { get; } =
                new UserIdentity("administrator-marker", "credential-marker"u8.ToArray());

            /// <summary>
            /// Gets or sets an initial lower-privilege identity for elevation tests.
            /// </summary>
            internal IUserIdentity? InitialIdentity { get; set; }

            /// <summary>
            /// Gets the real dispatch session.
            /// </summary>
            internal IdentitySession InnerSession { get; private set; } = null!;

            /// <summary>
            /// Gets the observed session-creation count.
            /// </summary>
            internal int FactoryCalls { get; private set; }

            /// <summary>
            /// Gets the number of identities admitted after real domain validation.
            /// </summary>
            internal int Activations { get; private set; }

            /// <summary>
            /// Gets whether the client enabled domain checking at its factory boundary.
            /// </summary>
            internal bool CheckDomain { get; private set; }

            /// <summary>
            /// Gets the discovery target selected from the client's endpoint.
            /// </summary>
            internal Uri? RefreshedDiscoveryUrl { get; private set; }

            /// <summary>
            /// Gets the identity admitted by the factory.
            /// </summary>
            internal IUserIdentity? ActivatedIdentity { get; private set; }

            /// <summary>
            /// Gets the identity in effect at the actual method dispatch.
            /// </summary>
            internal IUserIdentity? IdentityAtCall { get; private set; }

            /// <summary>
            /// Gets all service requests that actually reached the original transport.
            /// </summary>
            internal List<IServiceRequest> Requests { get; } = [];

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (InnerSession != null)
                    {
                        InnerSession.SessionCreated(NodeId.Null, NodeId.Null);
                        if (m_managedSession != null)
                        {
                            await m_managedSession.DisposeAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            await InnerSession.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                    m_serverCertificate.Dispose();
                    m_root.Dispose();
                }
            }

            /// <summary>
            /// Establishes that certificate trust and the advertised URI are otherwise valid.
            /// </summary>
            internal async Task AssertTrustedAsync()
            {
                CertificateValidationResult result = await m_manager.ValidateAsync(m_serverCertificate)
                    .ConfigureAwait(false);
                Assert.That(result.IsValid, Is.True);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Configuration.CertificateManager.ValidateApplicationUri(m_serverCertificate, Endpoint);
            }

            /// <summary>
            /// Performs a real service call after an authorized connection.
            /// </summary>
            internal static async Task AssertMarkerReadAsync(ISession session)
            {
                ReadResponse response = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = NodeId.Parse("s=marker"), AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(42));
            }

            /// <summary>
            /// Pauses the captured service dispatch behind the managed owner's recovery lease.
            /// </summary>
            internal Task<IDisposable> PauseDispatchAsync()
            {
                return m_pauseDispatch!();
            }

            /// <summary>
            /// Runs real local domain validation before admitting an identity, then creates a real dispatch session.
            /// </summary>
            private Task<ISession> CreateSessionAsync(
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                bool checkDomain,
                IUserIdentity? identity,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                FactoryCalls++;
                CheckDomain = checkDomain;
                RefreshedDiscoveryUrl = endpoint.GetDiscoveryUrl(endpoint.EndpointUrl!);
                using var certificate = Certificate.FromRawData(endpoint.Description.ServerCertificate);
                configuration.CertificateManager.ValidateApplicationUri(certificate, endpoint);
                if (checkDomain)
                {
                    configuration.CertificateManager.ValidateDomains(certificate, endpoint, serverValidation: true);
                }
                Activations++;
                ActivatedIdentity = identity;
                InnerSession = new IdentitySession(
                    Channel.Object, configuration, endpoint, InitialIdentity ?? identity!);
                if (!m_managed)
                {
                    return Task.FromResult<ISession>(InnerSession);
                }

                ConstructorInfo constructor = typeof(ManagedSession)
                    .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                m_managedSession = (ManagedSession)constructor.Invoke(
                [
                    configuration, endpoint, Factory, new ReconnectPolicy(), null,
                    Telemetry.CreateLogger<ManagedSession>(), identity, null, TimeProvider.System,
                    default(ArrayOf<string>), "GdsSecurityRegression", 60_000u,
                    true, false, false, false, null, null, null, null
                ]);
                SetField(m_managedSession, typeof(ManagedSession), "m_session", InnerSession);
                object stateMachine = typeof(ManagedSession)
                    .GetProperty("StateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(m_managedSession)!;
                Type stateMachineType = stateMachine.GetType();
                SetField(stateMachine, stateMachineType, "m_servicesAvailable", true);
                SetField(stateMachine, stateMachineType, "m_state", ConnectionState.Connected);
                foreach (string name in new[] { "m_servicesReady", "m_settled" })
                {
                    var ready = (AsyncManualResetEvent)stateMachineType
                        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(stateMachine)!;
                    ready.Set();
                }
                object serviceGate = typeof(ManagedSession)
                    .GetField("m_serviceLock", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(m_managedSession)!;
                m_pauseDispatch = () => QueueRecoveryAsync(serviceGate);
                return Task.FromResult<ISession>(m_managedSession);
            }

            /// <summary>
            /// Records actual service emissions and returns deterministic service responses.
            /// </summary>
            private ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Requests.Add(request);
                var header = new ResponseHeader { RequestHandle = request.RequestHeader.RequestHandle };
                IServiceResponse response;
                if (request is CallRequest)
                {
                    IdentityAtCall = InnerSession.Identity;
                    response = new CallResponse
                    {
                        ResponseHeader = header,
                        Results = [new CallMethodResult
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments = [new Variant(true)]
                        }]
                    };
                }
                else if (request is ReadRequest)
                {
                    response = new ReadResponse
                    {
                        ResponseHeader = header,
                        Results = [new DataValue(Variant.From(42))]
                    };
                }
                else
                {
                    response = new CloseSessionResponse { ResponseHeader = header };
                }
                return new ValueTask<IServiceResponse>(response);
            }

            /// <summary>Whether the injected factory supplies a managed owner.</summary>
            private readonly bool m_managed;

            /// <summary>The local CA certificate and signing key.</summary>
            private readonly Certificate m_root;

            /// <summary>The independently trusted peer certificate.</summary>
            private readonly Certificate m_serverCertificate;

            /// <summary>The in-memory trust authority.</summary>
            private readonly CertificateManager m_manager;

            /// <summary>The optional managed owner of the dispatch session.</summary>
            private ManagedSession? m_managedSession;

            /// <summary>Creates the test's asynchronous recovery pause without exposing the owner's gate.</summary>
            private Func<Task<IDisposable>>? m_pauseDispatch;
        }

        /// <summary>
        /// Keeps actual Session dispatch while scripting only the administrator identity transition.
        /// </summary>
        private sealed class IdentitySession : Session, ISession
        {
            /// <summary>
            /// Creates a real fixed-transport session without remote activation.
            /// </summary>
            internal IdentitySession(
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                IUserIdentity identity)
                : base(channel, configuration, endpoint, engineFactory: DefaultSubscriptionEngineFactory.Instance)
            {
                m_identity = identity;
                SessionCreated(NodeId.Parse("s=original-session"), s_originalToken);
            }

            /// <summary>
            /// Gets identity changes requested by the actual push client's elevation and restoration paths.
            /// </summary>
            internal List<IUserIdentity> IdentityUpdates { get; } = [];

            /// <inheritdoc/>
            Task ISession.UpdateSessionAsync(
                IUserIdentity? identity,
                ArrayOf<string> preferredLocales,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                m_identity = identity ?? new UserIdentity();
                IdentityUpdates.Add(m_identity);
                return Task.CompletedTask;
            }
        }

        /// <summary>A distinguishable synthetic application certificate payload.</summary>
        private static readonly ByteString s_certificate = [0x11, 0x12, 0x13];

        /// <summary>A distinguishable synthetic private-key payload, never a real credential.</summary>
        private static readonly ByteString s_privateKey = [0x21, 0x22, 0x23];

        /// <summary>Distinct issuer payloads that must retain their wire ordering.</summary>
        private static readonly ArrayOf<ByteString> s_issuers = [[0x31, 0x32], [0x41, 0x42]];

        /// <summary>The original session's wire authentication token.</summary>
        private static readonly NodeId s_originalToken = NodeId.Parse("s=original-token");

        /// <summary>Advertised discovery URLs that must not replace the configured host.</summary>
        private static readonly ArrayOf<string> s_discoveryUrls = ["opc.tcp://other.test:62553/discovery"];
    }
}
