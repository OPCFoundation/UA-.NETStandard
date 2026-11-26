// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using OnboardingClientSample;
using OnboardingRegistrar;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;
using CoreDataTypeIds = Opc.Ua.DataTypeIds;
using CoreNamespaces = Opc.Ua.Namespaces;
using CoreObjectIds = Opc.Ua.ObjectIds;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using CoreReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using ISession = Opc.Ua.Client.ISession;
using OnboardingObjectIds = Opc.Ua.Onboarding.ObjectIds;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the hosted pull registrar over encrypted local sessions with store-backed device keys.
    /// Transport auto-accept is confined to this bootstrap fixture; ticket and identity validation remain real.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    [Category("GdsHosting")]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [NonParallelizable]
    public sealed partial class PullRegistrarIntegrationTests : IAsyncDisposable
    {
        /// <summary>
        /// Starts the existing GDS host and client fixtures on an allocated port with isolated directory stores.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            string tempRoot = Path.GetTempPath();
            m_root = Path.Combine(tempRoot, nameof(PullRegistrarIntegrationTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            m_telemetry = NUnitTelemetryContext.Create();
            m_device = new ClientFixture(telemetry: m_telemetry);
            m_other = new ClientFixture(telemetry: m_telemetry);
            m_signer = new ClientFixture(telemetry: m_telemetry);
            await m_device.LoadClientConfigurationAsync(Path.Combine(m_root, "device"), "PullDca")
                .ConfigureAwait(false);
            await m_other.LoadClientConfigurationAsync(Path.Combine(m_root, "other"), "PullDca")
                .ConfigureAwait(false);
            await m_signer.LoadClientConfigurationAsync(Path.Combine(m_root, "signer"), "PullTicketSigner")
                .ConfigureAwait(false);
            m_product = m_device.Config.ApplicationUri ??
                throw new AssertionException("The selected device must have an application URI.");
            m_identity = GetIdentity(m_device);
            m_otherIdentity = GetIdentity(m_other);
            Assert.That(m_device.Config.ApplicationUri, Is.EqualTo(m_other.Config.ApplicationUri));
            Assert.That(m_otherIdentity, Is.Not.EqualTo(m_identity), "The two generations must have distinct keys.");

            var policy = new PullRegistrarPolicy(m_product, m_product);
            m_policy = new Mock<IDeviceRegistrarOnboardingPolicy>(MockBehavior.Strict);
            m_policy.Setup(value => value.EvaluateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                .Returns(async (OnboardingIdentitySelection selection, CancellationToken ct) =>
                {
                    DeviceRegistrarOnboardingDecision approved =
                        await policy.EvaluateAsync(selection, ct).ConfigureAwait(false);
                    return approved with
                    {
                        RequiresSoftwareUpdate = true,
                        SoftwareUpdateManager = new ManagerDescription
                        {
                            PurposeUri = "urn:test:software-approval",
                            EndpointUrls = ["opc.tcp://localhost/software-manager"]
                        }
                    };
                });
            m_lifecycle = new MemoryDeviceRegistrarLifecycle();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(m_telemetry);
            services.AddSingleton<ICertificateManager>(provider =>
                new CertificateManager(provider.GetRequiredService<ITelemetryContext>()));
            services.AddSingleton<IDeviceRegistrarLifecycle>(m_lifecycle);
            services.AddSingleton<IDeviceRegistrarIdentityLifecycle>(m_lifecycle);
            services.AddSingleton<IDeviceRegistrarCertificateLifecycle>(m_lifecycle);
            services.AddSingleton<ITicketStore>(new MemoryTicketStore());
            services.AddSingleton(m_policy.Object);
            m_externalAuthentication = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
            m_externalAuthentication.Setup(value => value.EvaluateAsync(
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DeviceRegistrarExternalAuthenticationResult?)null);
            services.AddDeviceRegistrarExternalAuthenticationProvider(_ => m_externalAuthentication.Object);
            services.AddSingleton<IDeviceRegistrarApplicationAuthorization>(policy);
            services.AddSingleton<IDeviceRegistrarPushScheduler>(policy);
            services.AddSingleton<IDeviceRegistrarManagerProvider>(policy);
            services.AddHostedDeviceRegistrarAuditing();
            services.AddDeviceRegistrarSoftwareUpdateRole(
                new ExpandedNodeId(CoreObjectIds.WellKnownRole_SecurityAdmin, CoreNamespaces.OpcUa));
            m_endpoint = new Uri("opc.tcp://localhost:" +
                ServerFixtureUtils.GetNextFreeIPPort().ToString(CultureInfo.InvariantCulture) + "/PullRegistrar");
            var serverStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            string trustedTickets = Path.Combine(m_root, "ticket-trusted");
            await TrustSignerAsync(trustedTickets).ConfigureAwait(false);
            services.AddOpcUa()
                .AddGdsServer(options =>
                {
                    options.ApplicationName = "PullRegistrarIntegration";
                    options.ApplicationUri = "urn:localhost:pull-registrar-integration";
                    options.ProductUri = "urn:test:pull-registrar";
                    options.PkiRoot = Path.Combine(m_root, "registrar");
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeSignAndEncryptPolicies = true;
                    options.IncludeUnsecurePolicyNone = false;
                    options.AutoApprove = true;
                    options.ConfigureBuilder = server => server.SetAuditingEnabled(true);
                    options.EndpointUrls.Add(m_endpoint.AbsoluteUri);
                })
                .AddStartupTask((_, _, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    serverStarted.TrySetResult(true);
                    return default;
                })
                .AddInMemoryStores()
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = true;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                    options.EnableGdsApplicationSelfAdminProvider = false;
                })
                .ConfigureRoles(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["OpcUa:Roles:Roles:0:Name"] = "SecurityAdmin",
                        ["OpcUa:Roles:Roles:0:Identities:0:CriteriaType"] = "UserName",
                        ["OpcUa:Roles:Roles:0:Identities:0:Criteria"] = "software-admin"
                    }).Build())
                .AddIdentityAugmenter(_ => new OnboardingRegistrarAdminAugmenter("ticket-admin"))
                .AddOnboardingTickets(trustedTickets, Path.Combine(m_root, "ticket-issuers"))
                .AddOnboardingIdentities(
                    Path.Combine(m_root, "identity-trusted"), Path.Combine(m_root, "identity-issuers"))
                .AddSecretStoreCertificateRequestIssuanceStore(new InMemorySecretStore(), "pull-integration")
                .AddDeviceRegistrar(_ => { });
            m_provider = services.BuildServiceProvider();
            m_password = Guid.NewGuid().ToString("N");
            m_provider.GetRequiredService<IUserDatabase>().CreateUser(
                "software-admin", Encoding.UTF8.GetBytes(m_password), [Role.SecurityAdmin, Role.AuthenticatedUser]);
            GdsServerHostedService host = m_provider.GetServices<IHostedService>()
                .OfType<GdsServerHostedService>()
                .Single();
            await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Task serverStartedTask = serverStarted.Task;
            Task hostExecution = host.ExecuteTask ??
                throw new InvalidOperationException("The GDS hosted service did not start.");
            Task startupResult = await Task.WhenAny(
                serverStartedTask,
                hostExecution,
                Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            if (startupResult == hostExecution)
            {
                await hostExecution.ConfigureAwait(false);
            }
            Assert.That(startupResult, Is.SameAs(serverStartedTask),
                "The GDS server must finish startup before the test connects.");
            m_session = await ConnectAsync(m_device).ConfigureAwait(false);
            m_otherSession = await ConnectAsync(m_other).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes sessions before stopping the host and removing the private-key stores.
        /// </summary>
        [TearDown]
        public Task TearDownAsync()
        {
            return DisposeAsync().AsTask();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            var errors = new List<Exception>();
            await CleanupAsync(errors, () => CloseSessionAsync(m_session)).ConfigureAwait(false);
            await CleanupAsync(errors, () => CloseSessionAsync(m_otherSession)).ConfigureAwait(false);
            await CleanupAsync(errors, () => DisposeFixtureAsync(m_device)).ConfigureAwait(false);
            await CleanupAsync(errors, () => DisposeFixtureAsync(m_other)).ConfigureAwait(false);
            await CleanupAsync(errors, () => DisposeFixtureAsync(m_signer)).ConfigureAwait(false);
            await CleanupAsync(errors, StopHostAsync).ConfigureAwait(false);
            if (m_provider != null)
            {
                try
                {
                    await m_provider.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
            await CleanupAsync(errors, () =>
            {
                if (m_root != null && Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, recursive: true);
                }
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            if (errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            }
            if (errors.Count > 1)
            {
                throw new AggregateException("Pull registrar integration cleanup failed.", errors);
            }
        }

        /// <summary>
        /// The hosted model advertises EncodedTicket as String and accepts String arrays over a real session,
        /// reporting each ticket's status while retaining the signed JSON exactly as supplied.
        /// </summary>
        [Test]
        public async Task HostedRegistrarRegistersStringTicketsAndAdvertisesStringSupertype()
        {
            string first = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            string secondProduct = m_product + ":second";
            string second = await WriteTicketAsync(m_signer, secondProduct, m_otherIdentity).ConfigureAwait(false);
            Assert.That(m_provider.GetRequiredService<IUserDatabase>().CreateUser(
                "ticket-admin", Encoding.UTF8.GetBytes(m_password),
                [
                    Role.AuthenticatedUser,
                    new Role(OnboardingObjectIds.WellKnownRole_RegistrarAdmin, "RegistrarAdmin")
                ]), Is.True);
            ISession session = await ConnectAsync(
                m_device, new UserIdentity("ticket-admin", Encoding.UTF8.GetBytes(m_password))).ConfigureAwait(false);
            try
            {
                BrowseResponse supertypes = await session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = CoreDataTypeIds.EncodedTicket,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = CoreReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.DataType,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(supertypes.Results.Count, Is.EqualTo(1));
                Assert.That(supertypes.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(supertypes.Results[0].ContinuationPoint.IsEmpty, Is.True);
                Assert.That(supertypes.Results[0].References.Count, Is.EqualTo(1));
                ReferenceDescription supertype = supertypes.Results[0].References[0];
                Assert.That(supertype.IsForward, Is.False);
                Assert.That(ExpandedNodeId.ToNodeId(supertype.NodeId, session.NamespaceUris),
                    Is.EqualTo(CoreDataTypeIds.String));

                BrowseResponse children = await session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = ExpandedNodeId.ToNodeId(OnboardingObjectIds.DeviceRegistrar, session.NamespaceUris),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = CoreReferenceTypeIds.HasComponent,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(children.Results.Count, Is.EqualTo(1));
                Assert.That(children.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(children.Results[0].ContinuationPoint.IsEmpty, Is.True);
                ReferenceDescription administration = children.Results[0].References.ToList().Single(
                    reference => reference.BrowseName.Name == "Administration");
                var client = new OnboardingClient(session,
                    ExpandedNodeId.ToNodeId(administration.NodeId, session.NamespaceUris), m_telemetry);

                ArrayOf<StatusCode> statuses = await client.RegisterTicketsAsync(
                    [first, "not a JSON ticket", second]).ConfigureAwait(false);

                Assert.That(statuses.ToList(), Is.EqualTo(new StatusCode[]
                {
                    StatusCodes.Good, StatusCodes.BadTicketInvalid, StatusCodes.Good
                }));
                ITicketStore store = m_provider.GetRequiredService<ITicketStore>();
                TicketRecord? firstStored = await store.FindByProductInstanceUriAsync(m_product).ConfigureAwait(false);
                TicketRecord? secondStored = await store.FindByProductInstanceUriAsync(secondProduct)
                    .ConfigureAwait(false);
                Assert.That(firstStored, Is.Not.Null);
                Assert.That(secondStored, Is.Not.Null);
                Assert.That(firstStored!.EncodedTicket, Is.EqualTo(Encoding.UTF8.GetBytes(first)));
                Assert.That(secondStored!.EncodedTicket, Is.EqualTo(Encoding.UTF8.GetBytes(second)));
                Assert.That(Encoding.UTF8.GetString(firstStored.EncodedTicket), Is.EqualTo(first));
                Assert.That(Encoding.UTF8.GetString(secondStored.EncodedTicket), Is.EqualTo(second));
                int count = 0;
                await foreach (TicketRecord _ in store.ListAsync().ConfigureAwait(false))
                {
                    count++;
                }
                Assert.That(count, Is.EqualTo(2));
            }
            finally
            {
                await CloseSessionAsync(session).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An external provider sees the actual channel key and enters the normal software/Part 12 gates.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalHandoffOnLiveChannelRequiresDeviceApprovalBeforePart12Issuance(bool update)
        {
            var policy = new PullRegistrarPolicy(m_product, m_product);
            DeviceRegistrarExternalAuthenticationResult external = await policy.EvaluateAsync(
                m_identity, [m_product]).ConfigureAwait(false);
            if (update)
            {
                external = external with
                {
                    Decision = external.Decision with
                    {
                        RequiresSoftwareUpdate = true,
                        SoftwareUpdateManager = new ManagerDescription
                        {
                            PurposeUri = "urn:test:software-approval",
                            EndpointUrls = ["opc.tcp://localhost/software-manager"]
                        }
                    }
                };
            }
            m_externalAuthentication.Setup(value => value.EvaluateAsync(
                m_identity, It.IsAny<CancellationToken>())).ReturnsAsync(external);

            Assert.That(await Registrar(m_session).GetManagersAsync().ConfigureAwait(false), Is.Empty);
            DeviceRegistrarIdentityCandidate? candidate = await m_lifecycle.FindCandidateByCertificateAsync(
                m_identity).ConfigureAwait(false);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.EqualTo(!update));
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await Registrar(m_otherSession).GetManagersAsync().ConfigureAwait(false)).ConfigureAwait(false);
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    m_session.NamespaceUris),
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            var credentials = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
            ByteString csr = await credentials.CreateSigningRequestAsync(candidate.ApplicationId, profile)
                .ConfigureAwait(false);
            var directory = DirectoryClient(m_session);
            if (update)
            {
                await AssertStatusAsync(StatusCodes.BadRequestNotAllowed, async () =>
                    await directory.StartSigningRequestAsync(
                        candidate.ApplicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                await m_otherSession.CloseAsync().ConfigureAwait(false);
                m_otherSession.Dispose();
                m_otherSession = await ConnectAsync(
                    m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password)))
                    .ConfigureAwait(false);
                await Registrar(m_otherSession).UpdateSoftwareStatusAsync(m_product, true, "external-approved")
                    .ConfigureAwait(false);
            }
            NodeId requestId = await directory.StartSigningRequestAsync(
                candidate.ApplicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                .ConfigureAwait(false);
            Assert.That(requestId.IsNull, Is.False);
            m_policy.VerifyNoOtherCalls();
            m_externalAuthentication.Verify(value => value.EvaluateAsync(
                m_identity, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A genuine signed ticket registers one candidate, but only its exact channel key can act as the DCA.
        /// </summary>
        [Test]
        public async Task ProvideIdentitiesRegistersCandidateWithoutAuthorizingAnotherKeyWithTheSameUri()
        {
            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            var registrar = Registrar(m_session);
            var (identity, matchingTicket, applicationId, manager) = await registrar.ProvideIdentitiesAsync(
                [m_identity], [], [ticket]).ConfigureAwait(false);

            Assert.That(identity, Is.EqualTo(m_identity));
            Assert.That(matchingTicket, Is.TypeOf<DeviceIdentityTicketType>());
            Assert.That(((DeviceIdentityTicketType)matchingTicket).ProductInstanceUri, Is.EqualTo(m_product));
            Assert.That(applicationId.IsNull, Is.False);
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager!.PurposeUri, Is.EqualTo("urn:test:software-approval"));
            ApplicationRecordDataType? application =
                m_provider.GetRequiredService<IApplicationsDatabase>().GetApplication(applicationId);
            Assert.That(application, Is.Not.Null);
            Assert.That(application!.ApplicationUri, Is.EqualTo(m_product));
            DeviceRegistrarIdentityCandidate? candidate = await m_lifecycle.FindCandidateByCertificateAsync(
                m_identity).ConfigureAwait(false);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(candidate.CertificateIssuanceAllowed, Is.False);
            DeviceRegistrarDevice? device = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(device!.DcaCertificate.IsEmpty, Is.True, "Selection is not installation proof.");
            Assert.That(await registrar.GetManagersAsync().ConfigureAwait(false), Is.Empty);
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await Registrar(m_otherSession).GetManagersAsync().ConfigureAwait(false)).ConfigureAwait(false);
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await registrar.UpdateSoftwareStatusAsync(m_product, true, "unprivileged")
                    .ConfigureAwait(false)).ConfigureAwait(false);
            candidate = await m_lifecycle.FindCandidateByCertificateAsync(m_identity).ConfigureAwait(false);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.False);

            var (_, _, repeatedId, _) = await registrar.ProvideIdentitiesAsync(
                [m_identity], [], [ticket]).ConfigureAwait(false);
            Assert.That(repeatedId, Is.EqualTo(applicationId));
            Assert.That(m_provider.GetRequiredService<IApplicationsDatabase>().FindApplications(m_product),
                Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Invalid or untrusted tickets and unmatched identity generations cannot create database entries.
        /// </summary>
        [TestCase("missing")]
        [TestCase("malformed")]
        [TestCase("tampered")]
        [TestCase("untrusted")]
        [TestCase("product")]
        [TestCase("identity")]
        public async Task ProvideIdentitiesRejectsInvalidTicketOrIdentityWithoutRegistration(string failure)
        {
            ClientFixture signer = failure == "untrusted" ? m_other : m_signer;
            string product = failure == "product" ? "urn:test:another-product" : m_product;
            ByteString authority = failure == "identity" ? m_otherIdentity : m_identity;
            string ticket = failure == "malformed"
                ? "not-a-signed-ticket"
                : await WriteTicketAsync(signer, product, authority).ConfigureAwait(false);
            if (failure == "tampered")
            {
                using JsonDocument envelope = JsonDocument.Parse(ticket);
                string payload = envelope.RootElement.GetProperty("payload").GetString()!;
                ticket = ticket.Replace(payload, "dGFtcGVyZWQ", StringComparison.Ordinal);
            }
            ArrayOf<string> tickets = failure == "missing" ? [] : [ticket];
            StatusCode expected = failure == "missing" ? StatusCodes.BadTicketRequired :
                failure == "identity" ? StatusCodes.BadCertificateInvalid : StatusCodes.BadNotFound;

            await AssertStatusAsync(expected, async () =>
                await Registrar(m_session).ProvideIdentitiesAsync([m_identity], [], tickets)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(await m_lifecycle.FindCandidateByCertificateAsync(m_identity).ConfigureAwait(false), Is.Null);
            Assert.That(m_provider.GetRequiredService<IApplicationsDatabase>().FindApplications(m_product), Is.Empty);
            m_policy.Verify(value => value.EvaluateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Software approval is rechecked at issuance, and the issued key becomes active only after reconnection.
        /// </summary>
        [Test]
        public async Task SoftwareApprovalGatesIssuanceAndInstalledCertificateIsProvenOnANewChannel()
        {
            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            var (_, _, applicationId, _) = await Registrar(m_session).ProvideIdentitiesAsync(
                [m_identity], [], [ticket]).ConfigureAwait(false);
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    m_session.NamespaceUris),
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            var credentials = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
            ByteString csr = await credentials.CreateSigningRequestAsync(applicationId, profile).ConfigureAwait(false);
            var directory = DirectoryClient(m_session);
            await AssertStatusAsync(StatusCodes.BadRequestNotAllowed, async () =>
                await directory.StartSigningRequestAsync(
                    applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await DirectoryClient(m_otherSession).StartSigningRequestAsync(
                    applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            await m_otherSession.CloseAsync().ConfigureAwait(false);
            m_otherSession.Dispose();
            m_otherSession = await ConnectAsync(
                m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password))).ConfigureAwait(false);
            await Registrar(m_otherSession).UpdateSoftwareStatusAsync(m_product, true, "approved-1")
                .ConfigureAwait(false);
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await DirectoryClient(m_otherSession).StartSigningRequestAsync(
                    applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            NodeId requestId = await directory.StartSigningRequestAsync(
                applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr).ConfigureAwait(false);
            Assert.That(requestId.IsNull, Is.False);
            var (issued, privateKey, issuers) = await directory.FinishRequestAsync(applicationId, requestId)
                .ConfigureAwait(false);
            Assert.That(issued.IsEmpty, Is.False);
            Assert.That(privateKey.IsEmpty, Is.True);
            Assert.That(issued, Is.Not.EqualTo(m_identity));
            DeviceRegistrarDevice? pending = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(pending!.DcaCertificate.IsEmpty, Is.True);
            Assert.That(pending.PendingDcaCertificate, Is.EqualTo(issued));
            Assert.That(await Registrar(m_session).GetManagersAsync().ConfigureAwait(false), Is.Empty);
            pending = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(pending!.DcaCertificate.IsEmpty, Is.True,
                "The old channel cannot prove the issued certificate.");

            NodeId trustListId = await directory.GetTrustListAsync(applicationId, profile.CertificateGroupId)
                .ConfigureAwait(false);
            var file = new FileTypeClient(m_session, trustListId, m_telemetry);
            uint handle = await file.OpenAsync((byte)OpenFileMode.Read).ConfigureAwait(false);
            TrustListDataType trustList = await TrustListFileTransferHelper.ReadAndCloseAsync(
                file, handle, m_session.MessageContext, 1024 * 1024, 4096, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(trustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            await credentials.InstallAsync(applicationId, new PullDcaCredentials(issued, issuers, trustList))
                .ConfigureAwait(false);
            Assert.That(GetIdentity(m_device), Is.EqualTo(issued));
            pending = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(pending!.DcaCertificate.IsEmpty, Is.True, "A local store update is not channel proof.");
            await m_session.CloseAsync().ConfigureAwait(false);
            m_session.Dispose();
            m_session = await ConnectAsync(m_device).ConfigureAwait(false);
            Assert.That(await Registrar(m_session).GetManagersAsync().ConfigureAwait(false), Is.Empty);
            DeviceRegistrarDevice? installed = await m_lifecycle.FindDeviceAsync(m_product).ConfigureAwait(false);
            Assert.That(installed!.DcaCertificate, Is.EqualTo(issued));
            Assert.That(installed.PendingDcaCertificate.IsEmpty, Is.True);
            await AssertStatusAsync(StatusCodes.BadUserAccessDenied, async () =>
                await Registrar(m_otherSession).GetManagersAsync().ConfigureAwait(false)).ConfigureAwait(false);

            await Registrar(m_otherSession).UpdateSoftwareStatusAsync(m_product, false, "revoked-2")
                .ConfigureAwait(false);
            await AssertStatusAsync(StatusCodes.BadRequestNotAllowed, async () =>
                await DirectoryClient(m_session).StartSigningRequestAsync(
                    applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr)
                    .ConfigureAwait(false)).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(120_000)]
        public async Task KeyPairFinishRequiresOriginalClientCertificateAcrossReauthentication(
            bool rotate,
            CancellationToken ct)
        {
            await using var channelManager = new ClientChannelManager(m_device.Config, m_telemetry);
            await using ManagedSession session = await new ManagedSessionBuilder(m_device.Config, m_telemetry)
                .UseEndpoint(m_endpoint.AbsoluteUri)
                .WithChannelManager(channelManager)
                .ConnectAsync(ct).ConfigureAwait(false);
            session.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            Assert.That(session.TransportChannel.ClientChannelCertificate, Is.EqualTo(m_identity.ToArray()));

            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            var (_, _, applicationId, _) = await Registrar(session).ProvideIdentitiesAsync(
                [m_identity], [], [ticket], ct).ConfigureAwait(false);
            await m_otherSession.CloseAsync(ct).ConfigureAwait(false);
            m_otherSession.Dispose();
            m_otherSession = await ConnectAsync(
                m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password)))
                .ConfigureAwait(false);
            await Registrar(m_otherSession).UpdateSoftwareStatusAsync(m_product, true, "approved-keypair", ct)
                .ConfigureAwait(false);

            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    session.NamespaceUris),
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            var registrar = new PullDcaRegistrar(
                m_endpoint,
                ExpandedNodeId.ToNodeId(OnboardingObjectIds.DeviceRegistrar, session.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris));
            var adapter = new PullDcaConnection(session, registrar, m_telemetry, 1024 * 1024);
            NodeId requestId = await adapter.StartNewKeyPairAsync(applicationId, profile, ct).ConfigureAwait(false);
            Assert.That(requestId.IsNull, Is.False);

            if (rotate)
            {
                NodeId previousSession = session.SessionId;
                using CertificateEntry replacement = m_other.Config.CertificateManager!
                    .AcquireApplicationCertificateByType(CoreObjectTypeIds.RsaSha256ApplicationCertificateType)!;
                await m_device.Config.CertificateManager!.UpdateApplicationCertificateAsync(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType, replacement.Certificate, ct: ct)
                    .ConfigureAwait(false);
                while (session.SessionId == previousSession ||
                    !session.TransportChannel.ClientChannelCertificate.AsSpan().SequenceEqual(m_otherIdentity.Span))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
                }
                Assert.That(session.TransportChannel.ClientChannelCertificate, Is.EqualTo(m_otherIdentity.ToArray()));
                Assert.That(m_otherIdentity, Is.Not.EqualTo(m_identity));
            }
            else
            {
                await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(session.TransportChannel.ClientChannelCertificate, Is.EqualTo(m_identity.ToArray()));
                });
            }

            string sourceName = m_telemetry.GetActivitySource(typeof(UaSCUaBinaryClientChannel).Assembly).Name;
            ActivityTraceId traceId = ActivityTraceId.CreateRandom();
            int sendEntries = 0;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    if (activity.TraceId == traceId && activity.OperationName == "SendRequestAsync")
                    {
                        Interlocked.Increment(ref sendEntries);
                    }
                }
            };
            ActivitySource.AddActivityListener(listener);
            using var parent = new Activity("keypair-finish");
            parent.SetParentId(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
            parent.Start();
            if (rotate)
            {
                ByteString unexpectedKey = default;
                try
                {
                    ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    {
                        (_, unexpectedKey, _) = await adapter.FinishNewKeyPairAsync(applicationId, requestId, ct)
                            .ConfigureAwait(false);
                    });
                    Assert.Multiple(() =>
                    {
                        Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                        Assert.That(Volatile.Read(ref sendEntries), Is.Zero);
                    });
                }
                finally
                {
                    ClearReturnedPrivateKey(unexpectedKey);
                    TestContext.Out.WriteLine(
                        $"Rotation plus recreation Finish native send entries: {Volatile.Read(ref sendEntries)}");
                }
            }
            else
            {
                var (certificate, privateKey, _) = await adapter.FinishNewKeyPairAsync(
                    applicationId, requestId, ct).ConfigureAwait(false);
                try
                {
                    Assert.That(certificate.IsEmpty, Is.False);
                    Assert.That(privateKey.IsEmpty, Is.False);
                    Assert.That(Volatile.Read(ref sendEntries), Is.GreaterThan(0));
                }
                finally
                {
                    ClearReturnedPrivateKey(privateKey);
                }
            }
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task KeyPairRecoveryInstallsRetainedCredentialsOnARecreatedSession(CancellationToken ct)
        {
            string ticket = await WriteTicketAsync(m_signer, m_product, m_identity).ConfigureAwait(false);
            await m_otherSession.CloseAsync(ct).ConfigureAwait(false);
            m_otherSession.Dispose();
            m_otherSession = await ConnectAsync(
                m_other, new UserIdentity("software-admin", Encoding.UTF8.GetBytes(m_password)))
                .ConfigureAwait(false);
            var software = new Mock<IPullDcaSoftwareUpdateProvider>(MockBehavior.Strict);
            software.Setup(provider => provider.CompleteAsync(
                    It.IsAny<PullDcaRegistrar>(), It.IsAny<PullDcaIdentitySelection>(),
                    It.IsAny<ManagerDescription>(), It.IsAny<CancellationToken>()))
                .Returns((
                    PullDcaRegistrar _, PullDcaIdentitySelection _, ManagerDescription _, CancellationToken token) =>
                    Registrar(m_otherSession).UpdateSoftwareStatusAsync(
                        m_product, true, "approved-keypair-recovery", token));

            var registrar = new PullDcaRegistrar(
                m_endpoint,
                ExpandedNodeId.ToNodeId(OnboardingObjectIds.DeviceRegistrar, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, m_session.NamespaceUris));
            var profile = new PullDcaCertificateProfile(
                ExpandedNodeId.ToNodeId(ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    m_session.NamespaceUris),
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            var request = new PullDcaRequest(new PullDcaIdentityOffer([m_identity], [], [ticket]), profile, [])
            {
                DcaCertificateRequestMode = PullDcaCertificateRequestMode.NewKeyPairRequest
            };
            const string slot = "connected-keypair-recovery";
            using var store = new TestRecoverySecret();
            var backend = new Mock<IProtectedPullDcaRecoveryStore>(MockBehavior.Strict);
            backend.SetupGet(value => value.StoreType).Returns("Test");
            backend.Setup(value => value.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ISecret?>(store.IsEmpty ? null : new TestRecoverySecret(store.Bytes)));
            backend.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Callback((SecretIdentifier _, ReadOnlyMemory<byte> data, CancellationToken _) =>
                    store.Replace(data.Span))
                .Returns(default(ValueTask));
            backend.Setup(value => value.RemoveAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    bool found = !store.IsEmpty;
                    store.Dispose();
                    return new ValueTask<bool>(found);
                });

            var interrupted = new Mock<IPullDcaNewKeyPairCredentialProvider>(MockBehavior.Strict);
            var interruption = new InvalidOperationException("Installation interrupted before storing credentials.");
            interrupted.Setup(provider => provider.InstallNewKeyPairAsync(
                    It.IsAny<NodeId>(), It.IsAny<PullDcaCertificateProfile>(), It.IsAny<PullDcaCredentials>(),
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Throws(interruption);
            var progress = new Mock<IProgress<PullDcaProgress>>();
            var stages = new List<PullDcaStage>();
            NodeId applicationId = NodeId.Null;
            NodeId requestId = NodeId.Null;
            progress.Setup(value => value.Report(It.IsAny<PullDcaProgress>()))
                .Callback((PullDcaProgress report) =>
                {
                    stages.Add(report.Stage);
                    if (report.Stage == PullDcaStage.FinishingCertificate)
                    {
                        applicationId = report.ApplicationId;
                        requestId = report.RequestId;
                    }
                });

            PullDcaNewKeyPairRecoveryScope scope;
            ByteString retainedCertificate;
            NodeId priorSessionId;
            await using (var channels = new ClientChannelManager(m_device.Config, m_telemetry))
            await using (ManagedSession first = await ConnectRecoverySessionAsync(channels).ConfigureAwait(false))
            using (var recovery = new SecretStorePullDcaNewKeyPairRecoveryProvider(
                backend.Object, slot, m_telemetry))
            {
                PullDcaWorkflow workflow = CreateWorkflow(first, interrupted.Object, recovery);
                Assert.That(await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await workflow.RunAsync(registrar, request, progress.Object, ct).ConfigureAwait(false)),
                    Is.SameAs(interruption));
                Assert.That(applicationId.IsNull, Is.False);
                Assert.That(requestId.IsNull, Is.False);
                Assert.That(stages.Count(stage => stage == PullDcaStage.RequestingCertificate), Is.EqualTo(1));
                Assert.That(stages[^1], Is.EqualTo(PullDcaStage.InstallingCredentials));

                var connection = new PullDcaConnection(first, registrar, m_telemetry, 1024 * 1024);
                scope = await connection.CaptureNewKeyPairRecoveryScopeAsync(applicationId, profile, ct)
                    .ConfigureAwait(false);
                PullDcaNewKeyPairRecoveryAttempt pending = await recovery.GetPendingAsync(scope, ct)
                    .ConfigureAwait(false) ?? throw new AssertionException("The received checkpoint was not saved.");
                try
                {
                    Assert.That(pending.Binding.RequestId, Is.EqualTo(requestId));
                    Assert.That(pending.State, Is.EqualTo(PullDcaNewKeyPairRecoveryState.Received));
                    Assert.That(pending.PrivateKeyPfx.IsEmpty, Is.False);
                    retainedCertificate = pending.Certificate;
                    Assert.That(retainedCertificate.IsEmpty, Is.False);
                    Assert.That(GetIdentity(m_device), Is.EqualTo(m_identity));
                }
                finally
                {
                    ClearReturnedPrivateKey(pending.PrivateKeyPfx);
                }
                priorSessionId = first.SessionId;
            }

            stages.Clear();
            await using var resumedChannels = new ClientChannelManager(m_device.Config, m_telemetry);
            await using ManagedSession resumed = await ConnectRecoverySessionAsync(resumedChannels)
                .ConfigureAwait(false);
            Assert.That(resumed.SessionId, Is.Not.EqualTo(priorSessionId));
            Assert.That(resumed.TransportChannel.ClientChannelCertificate.AsSpan().SequenceEqual(m_identity.Span),
                Is.True);
            using var resumedRecovery = new SecretStorePullDcaNewKeyPairRecoveryProvider(
                backend.Object, slot, m_telemetry);
            var installer = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
            PullDcaResult result = await CreateWorkflow(resumed, installer, resumedRecovery)
                .RunAsync(registrar, request, progress.Object, ct).ConfigureAwait(false);
            Assert.That(stages, Does.Not.Contain(PullDcaStage.RequestingCertificate));
            Assert.That(stages, Does.Not.Contain(PullDcaStage.FinishingCertificate));
            Assert.That(stages[^1], Is.EqualTo(PullDcaStage.Completed));
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(retainedCertificate));
            Assert.That(result.DcaCredentials.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            Assert.That(resumed.TransportChannel.ClientChannelCertificate.AsSpan()
                .SequenceEqual(retainedCertificate.Span), Is.True);
            DeviceRegistrarDevice? installed = await m_lifecycle.FindDeviceAsync(m_product, ct).ConfigureAwait(false);
            Assert.That(installed?.DcaCertificate, Is.EqualTo(retainedCertificate));
            Assert.That(await resumedRecovery.GetPendingAsync(scope, ct).ConfigureAwait(false), Is.Null);
            backend.Verify(value => value.RemoveAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()),
                Times.Once);

            async Task<ManagedSession> ConnectRecoverySessionAsync(ClientChannelManager channels)
            {
                ManagedSession session = await new ManagedSessionBuilder(m_device.Config, m_telemetry)
                    .UseEndpoint(m_endpoint.AbsoluteUri)
                    .WithChannelManager(channels)
                    .ConnectAsync(ct).ConfigureAwait(false);
                session.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
                session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
                return session;
            }

            PullDcaWorkflow CreateWorkflow(
                ManagedSession session,
                IPullDcaNewKeyPairCredentialProvider keyPairInstaller,
                IPullDcaNewKeyPairRecoveryProvider recovery)
            {
                var credentials = new OnboardingDeviceCredentials(m_device.Config, m_telemetry);
                return new PullDcaWorkflow(
                    new PullDcaOptions(), (_, _) => new ValueTask<ManagedSession>(session), credentials, credentials,
                    m_telemetry, software.Object, discovery: null,
                    receipts: Mock.Of<IPullDcaCertificateReceiptStore>(MockBehavior.Strict),
                    signingRecovery: null, signingJournal: null, newKeyPairCredentialProvider: keyPairInstaller,
                    newKeyPairRecoveryProvider: recovery);
            }
        }

        private sealed class TestRecoverySecret : ISecret
        {
            public TestRecoverySecret(ReadOnlySpan<byte> bytes = default)
            {
                m_bytes = bytes.ToArray();
            }

            public bool IsEmpty => m_bytes.Length == 0;

            public ReadOnlySpan<byte> Bytes => m_bytes;

            public void Replace(ReadOnlySpan<byte> bytes)
            {
                Dispose();
                m_bytes = bytes.ToArray();
            }

            public void Dispose()
            {
                Array.Clear(m_bytes, 0, m_bytes.Length);
                m_bytes = [];
            }

            private byte[] m_bytes;
        }

        private static void ClearReturnedPrivateKey(ByteString privateKey)
        {
            if (MemoryMarshal.TryGetArray(privateKey.Memory, out ArraySegment<byte> bytes) && bytes.Array != null)
            {
                Array.Clear(bytes.Array, bytes.Offset, bytes.Count);
            }
        }

        /// <summary>
        /// Uses the existing real client connection lifecycle and registers the generated model codecs.
        /// </summary>
        private async Task<ISession> ConnectAsync(ClientFixture client, IUserIdentity? identity = null)
        {
            ISession session = await client.ConnectAsync(
                m_endpoint, SecurityPolicies.Basic256Sha256, default, identity).ConfigureAwait(false);
            session.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            session.MessageContext.Factory.Builder.AddOpcUaGds().AddOpcUaOnboarding().Commit();
            return session;
        }

        /// <summary>
        /// Runs one independent cleanup step and records its failure without skipping later steps.
        /// </summary>
        private static async Task CleanupAsync(List<Exception> errors, Func<Task> cleanup)
        {
            try
            {
                await cleanup().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        /// <summary>
        /// Disposes a client fixture and its certificate manager when setup created it.
        /// </summary>
        private static async Task DisposeFixtureAsync(ClientFixture? fixture)
        {
            if (fixture != null)
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops the hosted registrar with a bounded cancellation token.
        /// </summary>
        private async Task StopHostAsync()
        {
            if (m_provider != null)
            {
                GdsServerHostedService? host = m_provider.GetServices<IHostedService>()
                    .OfType<GdsServerHostedService>()
                    .SingleOrDefault();
                if (host != null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await host.StopAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Releases the session independently of the certificate-store fixture.
        /// </summary>
        private static async Task CloseSessionAsync(ISession? session)
        {
            if (session != null)
            {
                try
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                }
                finally
                {
                    session.Dispose();
                }
            }
        }

        /// <summary>
        /// Creates the generated registrar proxy using the session's negotiated namespace table.
        /// </summary>
        private DeviceRegistrarTypeClient Registrar(ISession session)
        {
            return new DeviceRegistrarTypeClient(session,
                ExpandedNodeId.ToNodeId(OnboardingObjectIds.DeviceRegistrar, session.NamespaceUris), m_telemetry);
        }

        /// <summary>
        /// Creates the generated certificate directory proxy.
        /// </summary>
        private CertificateDirectoryTypeClient DirectoryClient(ISession session)
        {
            return new CertificateDirectoryTypeClient(session,
                ExpandedNodeId.ToNodeId(ObjectIds.Directory, session.NamespaceUris), m_telemetry);
        }

        /// <summary>
        /// Borrows the RSA application key created in the client's own certificate store.
        /// </summary>
        private static ByteString GetIdentity(ClientFixture client)
        {
            using CertificateEntry entry = client.Config.CertificateManager!.AcquireApplicationCertificateByType(
                CoreObjectTypeIds.RsaSha256ApplicationCertificateType)!;
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Certificate.HasPrivateKey, Is.True);
            return ByteString.From(entry.Certificate.RawData);
        }

        /// <summary>
        /// Publishes only the ticket signer's public certificate to the registrar's ticket trust store.
        /// </summary>
        private async Task TrustSignerAsync(string path)
        {
            var identifier = new CertificateStoreIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = path
            };
            using ICertificateStore store = identifier.OpenStore(m_telemetry);
            using Certificate signer = Certificate.FromRawData(GetIdentity(m_signer));
            await store.AddAsync(signer).ConfigureAwait(false);
        }

        /// <summary>
        /// Signs an actual Part 21 ticket using a key resolved by the existing certificate-store provider.
        /// </summary>
        private async Task<string> WriteTicketAsync(ClientFixture signer, string product, ByteString authority)
        {
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(m_telemetry);
            context.Factory.Builder.AddOpcUaOnboarding().Commit();
            var writer = new OnboardingTicketWriter(
                signer.Config.CertificateManager!.CertificateProvider, context);
            CertificateIdentifier identifier = signer.Config.SecurityConfiguration.ApplicationCertificates
                .ToList().First(value =>
                    value.CertificateType == CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            return await writer.WriteAsync(new DeviceIdentityTicketType
            {
                ManufacturerName = "Integration manufacturer",
                ModelName = "Pull device",
                SerialNumber = "Integration-1",
                ProductInstanceUri = product,
                Authorities = [new CertificateAuthorityType { AuthorityCertificate = authority }]
            }, identifier).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the method's exact wire status without accepting unrelated transport or decoding failures.
        /// </summary>
        private static async Task AssertStatusAsync(StatusCode expected, Func<Task> action)
        {
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(action);
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
        }

        /// <summary>The isolated certificate-store root.</summary>
        private string m_root = null!;

        /// <summary>The runtime-only administrator password.</summary>
        private string m_password = null!;

        /// <summary>The product URI also carried by both channel certificate generations.</summary>
        private string m_product = null!;

        /// <summary>The local endpoint selected by the existing server fixture utility.</summary>
        private Uri m_endpoint = null!;

        /// <summary>The fixture telemetry shared with the host and clients.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The independently persisted selected identity.</summary>
        private ByteString m_identity;

        /// <summary>A different key generation with identical discovery identity.</summary>
        private ByteString m_otherIdentity;

        /// <summary>The real selected-device client fixture.</summary>
        private ClientFixture m_device = null!;

        /// <summary>The independently keyed client used for negative proof and administrative calls.</summary>
        private ClientFixture m_other = null!;

        /// <summary>The real ticket-signing certificate-store owner.</summary>
        private ClientFixture m_signer = null!;

        /// <summary>The connected device session.</summary>
        private ISession m_session = null!;

        /// <summary>The connected independent-key session.</summary>
        private ISession m_otherSession = null!;

        /// <summary>The host's actual transient lifecycle provider.</summary>
        private MemoryDeviceRegistrarLifecycle m_lifecycle = null!;

        /// <summary>The explicit host admission and software-update decision.</summary>
        private Mock<IDeviceRegistrarOnboardingPolicy> m_policy = null!;

        /// <summary>The optional handoff boundary, rejected unless a test explicitly approves a channel key.</summary>
        private Mock<IDeviceRegistrarExternalAuthenticationProvider> m_externalAuthentication = null!;

        /// <summary>The owning dependency-injection container.</summary>
        private ServiceProvider m_provider = null!;

        /// <summary>Prevents repeated disposal after NUnit teardown.</summary>
        private int m_disposed;
    }
}
