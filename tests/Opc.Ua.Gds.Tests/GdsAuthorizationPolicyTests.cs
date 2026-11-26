// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// The GDS directory, certificate manager and TrustList checks ask the server's
    /// <see cref="IServerAuthorizationPolicy"/>, which applies the GDS rule by default and
    /// can replace it.
    /// </summary>
    [TestFixture]
    [Category("Authorization")]
    [Parallelizable]
    public class GdsAuthorizationPolicyTests
    {
        private static readonly byte[] s_passwordBytes = Encoding.UTF8.GetBytes("password");
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        [Test]
        public void DefaultPolicyAppliesTheGdsRule()
        {
            ServerSystemContext admin = CreateContext(ServerAuthorizationPolicy.Default, GdsRole.DiscoveryAdmin);
            ServerSystemContext user = CreateContext(ServerAuthorizationPolicy.Default);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => AuthorizationHelper.HasAuthorization(
                        admin, ServerAuthorizationDecision.ApplicationRegistration, AuthorizationHelper.DiscoveryAdmin),
                    Throws.Nothing);
                Assert.That(
                    () => AuthorizationHelper.HasAuthorization(
                        user, ServerAuthorizationDecision.ApplicationRegistration, AuthorizationHelper.DiscoveryAdmin),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public void HostPolicyDeniesAGdsDecisionTheGdsRuleWouldGrant()
        {
            var policy = new RecordingPolicy(deny: ServerAuthorizationDecision.ApplicationRegistration);
            ServerSystemContext admin = CreateContext(policy, GdsRole.DiscoveryAdmin);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasAuthorization(
                    admin,
                    ServerAuthorizationDecision.ApplicationRegistration,
                    AuthorizationHelper.DiscoveryAdmin));

            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(policy.Requests, Has.Count.EqualTo(1));
                Assert.That(policy.Requests[0].Decision, Is.EqualTo(ServerAuthorizationDecision.ApplicationRegistration));
            });
        }

        [Test]
        public void HostPolicySeesTheTargetApplication()
        {
            var policy = new RecordingPolicy(deny: null);
            ServerSystemContext admin = CreateContext(policy, GdsRole.CertificateAuthorityAdmin);
            var applicationId = new NodeId(7u, 1);

            AuthorizationHelper.HasAuthorization(
                admin,
                ServerAuthorizationDecision.CertificateManagement,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            Assert.That(policy.Requests[0].ApplicationId, Is.EqualTo(applicationId));
        }

        [Test]
        public void TrustListWriteAsksTheHostPolicy()
        {
            var policy = new RecordingPolicy(deny: ServerAuthorizationDecision.TrustListWrite);
            ServerSystemContext admin = CreateContext(policy, GdsRole.CertificateAuthorityAdmin);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                AuthorizationHelper.HasTrustListWriteAccess(admin));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        private static ServerSystemContext CreateContext(IServerAuthorizationPolicy policy, params Role[] roles)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(Namespaces.OpcUaGds);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaces);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaces));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(s_telemetry);
            server.As<IServerAuthorizationPolicyProvider>()
                .Setup(p => p.AuthorizationPolicy)
                .Returns(policy);

            IUserIdentity identity = new UserIdentity("user", s_passwordBytes);
            if (roles.Length > 0)
            {
                identity = new GdsRoleBasedIdentity(identity, roles, namespaces);
            }
            return new ServerSystemContext(server.Object)
            {
                UserIdentity = identity
            };
        }

        /// <summary>
        /// A host policy implemented directly on the interface: it records each request, denies
        /// one decision and otherwise defers to the request's standard rule.
        /// </summary>
        private sealed class RecordingPolicy : IServerAuthorizationPolicy
        {
            public RecordingPolicy(ServerAuthorizationDecision? deny)
            {
                m_deny = deny;
            }

            public List<ServerAuthorizationRequest> Requests { get; } = [];

            public ServiceResult Authorize(ISystemContext context, ServerAuthorizationRequest request)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                }
                return request.Decision == m_deny
                    ? new ServiceResult(StatusCodes.BadUserAccessDenied)
                    : ServerAuthorizationPolicy.Default.Authorize(context, request);
            }

            public ValueTask<ServiceResult> AuthorizeAsync(
                ISystemContext context,
                ServerAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(Authorize(context, request));
            }

            private readonly ServerAuthorizationDecision? m_deny;
        }
    }
}
