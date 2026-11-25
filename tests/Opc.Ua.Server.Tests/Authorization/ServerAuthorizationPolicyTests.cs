// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Authorization
{
    /// <summary>
    /// Tests the decisions of the standard <see cref="ServerAuthorizationPolicy"/>, the resolution
    /// of the server's policy from a context, and that every routed check honours a host policy.
    /// </summary>
    [TestFixture]
    [Category("Authorization")]
    [Parallelizable]
    public class ServerAuthorizationPolicyTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        private static readonly string[] s_expectedSinkOrder = ["first", "third"];

        private static readonly ServerAuthorizationDecision[] s_allDecisions =
            Enum.GetValues(typeof(ServerAuthorizationDecision)).Cast<ServerAuthorizationDecision>().ToArray();

        private static ServerAuthorizationDecision[] AllDecisions() => s_allDecisions;

        [TestCaseSource(nameof(AllDecisions))]
        public void SecurityAdminOverSignAndEncryptIsGrantedEveryDecision(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void AnonymousSecurityAdminIsDeniedEveryDecision(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.Anonymous, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void ObserverIsDeniedEveryDecision(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_Observer);

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void ConfigureAdminIsGrantedOnlyConfigurationDecisions(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_ConfigureAdmin);
            bool expected = decision is
                ServerAuthorizationDecision.ConfigureAdmin or
                ServerAuthorizationDecision.DiagnosticsConfiguration;

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(expected ? StatusCodes.Good : StatusCodes.BadUserAccessDenied));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void SignOnlyChannelIsSufficientOnlyForCertificatePush(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.Sign, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);
            bool expected = decision == ServerAuthorizationDecision.CertificatePush;

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(expected ? StatusCodes.Good : StatusCodes.BadSecurityModeInsufficient));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void UnsecuredChannelIsDeniedBeforeTheIdentityIsChecked(ServerAuthorizationDecision decision)
        {
            ISystemContext context = CreateContext(MessageSecurityMode.None, UserTokenType.Anonymous);

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [TestCaseSource(nameof(AllDecisions))]
        public void ServerInternalCallIsGrantedOnlyCertificatePush(ServerAuthorizationDecision decision)
        {
            var context = new SessionSystemContext(s_telemetry);
            bool expected = decision is
                ServerAuthorizationDecision.CertificatePush or
                ServerAuthorizationDecision.CertificatePushEncrypted;

            Assert.That(ServerAuthorizationPolicy.Default.Authorize(context, decision).StatusCode,
                Is.EqualTo(expected ? StatusCodes.Good : StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void SecurityAdminInAnotherNodeIdRepresentationIsGranted()
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt,
                UserTokenType.UserName,
                new NodeId(Objects.WellKnownRole_SecurityAdmin));

            Assert.That(
                ServiceResult.IsGood(ServerAuthorizationPolicy.Default.Authorize(
                    context, ServerAuthorizationDecision.SecurityAdmin)),
                Is.True);
        }

        [Test]
        public async Task AuthorizeAsyncReturnsTheSynchronousDecisionAsync()
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.Sign, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);

            ServiceResult result = await ServerAuthorizationPolicy.Default
                .AuthorizeAsync(context, ServerAuthorizationDecision.KeyCredentialPush)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void ResolveReturnsTheServersPolicy()
        {
            var policy = new DenyingPolicy(ServerAuthorizationDecision.SecurityAdmin);
            ISystemContext context = CreateServerContext(policy, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.That(ServerAuthorizationPolicy.Resolve(context), Is.SameAs(policy));
        }

        [Test]
        public void ResolveFallsBackToTheDefaultPolicy()
        {
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);
            var server = new Mock<IServerInternal>();
            SetupServer(server);

            Assert.Multiple(() =>
            {
                Assert.That(ServerAuthorizationPolicy.Resolve(context), Is.SameAs(ServerAuthorizationPolicy.Default));
                Assert.That(ServerAuthorizationPolicy.Resolve(null), Is.SameAs(ServerAuthorizationPolicy.Default));
                Assert.That(
                    ServerAuthorizationPolicy.Resolve(new ServerSystemContext(server.Object)),
                    Is.SameAs(ServerAuthorizationPolicy.Default));
            });
        }

        [TestCase(ServerAuthorizationDecision.SecurityAdmin)]
        [TestCase(ServerAuthorizationDecision.RoleManagement)]
        [TestCase(ServerAuthorizationDecision.UserManagement)]
        [TestCase(ServerAuthorizationDecision.DiagnosticsRead)]
        [TestCase(ServerAuthorizationDecision.DiagnosticsConfiguration)]
        [TestCase(ServerAuthorizationDecision.AliasManagement)]
        public void RoleAuthorizationGateAsksTheHostPolicy(ServerAuthorizationDecision decision)
        {
            var policy = new DenyingPolicy(decision);
            ISystemContext context = CreateServerContext(policy, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.Multiple(() =>
            {
                Assert.That(RoleAuthorizationGate.Check(context, decision).StatusCode,
                    Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(policy.Decisions, Is.EqualTo(new[] { decision }));
            });
        }

        [Test]
        public void CheckAdminAsksTheHostPolicyForSecurityAdmin()
        {
            var policy = new DenyingPolicy(ServerAuthorizationDecision.SecurityAdmin);
            ISystemContext context = CreateServerContext(policy, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.That(RoleAuthorizationGate.CheckAdmin(context).StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void AliasMutationAsksTheHostPolicy()
        {
            var policy = new DenyingPolicy(ServerAuthorizationDecision.AliasManagement);
            ISystemContext context = CreateServerContext(policy, ObjectIds.WellKnownRole_SecurityAdmin);

            Assert.That(global::Opc.Ua.Server.AliasNames.AliasNameMethodDispatcher.HasSecureAdminAccess(context), Is.False);
            Assert.That(policy.Decisions, Is.EqualTo(new[] { ServerAuthorizationDecision.AliasManagement }));
        }

        [Test]
        public async Task KeyCredentialPushIsDeniedByAHostPolicyAsync()
        {
            using var store = new InMemoryKeyCredentialStore();
            var subject = new KeyCredentialPushSubject(store);
            KeyCredentialConfigurationFolderState folder = CreateKeyCredentialFolder();
            var policy = new DenyingPolicy(ServerAuthorizationDecision.KeyCredentialPush);
            ISystemContext context = CreateServerContext(policy, ObjectIds.WellKnownRole_SecurityAdmin);
            await subject.BindAsync(folder, context).ConfigureAwait(false);

            CreateCredentialMethodStateResult result = await CreateCredentialAsync(folder, context)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(policy.Decisions, Does.Contain(ServerAuthorizationDecision.KeyCredentialPush));
            });
        }

        [Test]
        public async Task KeyCredentialPushIsDeniedByTheHostPushAuthorizationAsync()
        {
            using var store = new InMemoryKeyCredentialStore();
            var pushAuthorization = new RecordingPushAuthorization(deny: true);
            var subject = new KeyCredentialPushSubject(
                store, new KeyCredentialPushOptions { PushAuthorization = pushAuthorization });
            KeyCredentialConfigurationFolderState folder = CreateKeyCredentialFolder();
            ISystemContext context = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);
            await subject.BindAsync(folder, context).ConfigureAwait(false);

            CreateCredentialMethodStateResult result = await CreateCredentialAsync(folder, context)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(pushAuthorization.Calls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task KeyCredentialPushRunsTheHostPushAuthorizationOnlyAfterThePolicyGrantsAsync()
        {
            using var store = new InMemoryKeyCredentialStore();
            var pushAuthorization = new RecordingPushAuthorization(deny: false);
            var subject = new KeyCredentialPushSubject(
                store, new KeyCredentialPushOptions { PushAuthorization = pushAuthorization });
            KeyCredentialConfigurationFolderState folder = CreateKeyCredentialFolder();
            ISystemContext admin = CreateContext(
                MessageSecurityMode.SignAndEncrypt, UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin);
            await subject.BindAsync(folder, admin).ConfigureAwait(false);

            CreateCredentialMethodStateResult denied = await CreateCredentialAsync(
                    folder, CreateContext(MessageSecurityMode.SignAndEncrypt, UserTokenType.Anonymous))
                .ConfigureAwait(false);
            Assert.That(pushAuthorization.Calls, Is.Zero, "The host hook must not run for a caller the policy denies.");

            CreateCredentialMethodStateResult granted = await CreateCredentialAsync(folder, admin)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(denied.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(ServiceResult.IsGood(granted.ServiceResult), Is.True);
                Assert.That(pushAuthorization.Calls, Is.EqualTo(1));
            });
        }

        [Test]
        public void CompositeAuditEventSinkForwardsInOrderAndIsolatesAFailingSink()
        {
            var received = new List<string>();
            var sink = new CompositeAuditEventSink(
                [
                    new DelegateSink(() => received.Add("first")),
                    new DelegateSink(() => throw new InvalidOperationException("sink failure")),
                    new DelegateSink(() => received.Add("third"))
                ],
                s_telemetry);

            sink.ReportAuditEvent(new SessionSystemContext(s_telemetry), new AuditEventState(null));

            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(s_expectedSinkOrder));
                Assert.That(sink.Sinks, Has.Count.EqualTo(3));
            });
        }

        private static Task<CreateCredentialMethodStateResult> CreateCredentialAsync(
            KeyCredentialConfigurationFolderState folder,
            ISystemContext context)
        {
            return folder.CreateCredential!.OnCallAsync!(
                    context,
                    folder.CreateCredential,
                    folder.NodeId,
                    "ServiceA",
                    "urn:test:resource",
                    KeyCredentialBridgeOptions.DefaultProfileUri,
                    [],
                    CancellationToken.None)
                .AsTask();
        }

        private static KeyCredentialConfigurationFolderState CreateKeyCredentialFolder()
        {
            return new KeyCredentialConfigurationFolderState(null)
            {
                NodeId = KeyCredentialPushSubject.StandardConfigurationFolderNodeId,
                BrowseName = new QualifiedName("KeyCredentialConfiguration"),
                DisplayName = LocalizedText.From("KeyCredentialConfiguration"),
                TypeDefinitionId = ObjectTypeIds.KeyCredentialConfigurationFolderType
            };
        }

        private static IUserIdentity CreateIdentity(UserTokenType tokenType, params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(tokenType.ToString());
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            return identity.Object;
        }

        private static OperationContext CreateOperationContext(
            MessageSecurityMode securityMode,
            IUserIdentity identity)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            return new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity);
        }

        private static SessionSystemContext CreateContext(
            MessageSecurityMode securityMode,
            UserTokenType tokenType,
            params NodeId[] grantedRoles)
        {
            return new SessionSystemContext(
                CreateOperationContext(securityMode, CreateIdentity(tokenType, grantedRoles)),
                s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private static ServerSystemContext CreateServerContext(
            IServerAuthorizationPolicy policy,
            params NodeId[] grantedRoles)
        {
            var server = new Mock<IServerInternal>();
            SetupServer(server);
            server.As<IServerAuthorizationPolicyProvider>()
                .Setup(provider => provider.AuthorizationPolicy)
                .Returns(policy);
            return new ServerSystemContext(
                server.Object,
                CreateOperationContext(
                    MessageSecurityMode.SignAndEncrypt,
                    CreateIdentity(UserTokenType.UserName, grantedRoles)));
        }

        private static void SetupServer(Mock<IServerInternal> server)
        {
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(s_telemetry);
        }

        /// <summary>
        /// A host policy that denies one decision and applies the standard rules to the others.
        /// </summary>
        private sealed class DenyingPolicy : ServerAuthorizationPolicy
        {
            public DenyingPolicy(ServerAuthorizationDecision denied)
            {
                m_denied = denied;
            }

            public List<ServerAuthorizationDecision> Decisions { get; } = [];

            public override ServiceResult Authorize(ISystemContext context, ServerAuthorizationDecision decision)
            {
                lock (Decisions)
                {
                    Decisions.Add(decision);
                }
                return decision == m_denied
                    ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                    : base.Authorize(context, decision);
            }

            private readonly ServerAuthorizationDecision m_denied;
        }

        private sealed class RecordingPushAuthorization : IPushConfigurationAuthorization
        {
            public RecordingPushAuthorization(bool deny)
            {
                m_deny = deny;
            }

            public int Calls => m_calls;

            public ValueTask AuthorizeAsync(ISystemContext context, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_calls);
                if (m_deny)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied, "Denied by host policy.");
                }
                return default;
            }

            private readonly bool m_deny;
            private int m_calls;
        }

        private sealed class DelegateSink : IAuditEventSink
        {
            public DelegateSink(Action action)
            {
                m_action = action;
            }

            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
            {
                m_action();
            }

            private readonly Action m_action;
        }
    }
}
