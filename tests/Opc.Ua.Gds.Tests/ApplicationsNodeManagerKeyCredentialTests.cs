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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("KeyCredential")]
    [Category("ApplicationsNodeManagerCoverage")]
    [NonParallelizable]
    public sealed class ApplicationsNodeManagerKeyCredentialTests : GdsTestFixture
    {
        private const string OwnerApplicationUri =
            "urn:opcfoundation.org:tests:test:app:KeyCredentialOwner";

        private const string OtherApplicationUri =
            "urn:opcfoundation.org:tests:test:app:KeyCredentialOther";

        private static readonly byte[] s_passwordBytes = Encoding.UTF8.GetBytes("password");

        private TestableApplicationsNodeManager m_nodeManager = null!;
        private KeyCredentialServiceState m_service = null!;
        private LinqApplicationsDatabase m_database = null!;
        private NodeId m_ownerApplicationId;
        private NodeId m_otherApplicationId;

        [OneTimeSetUp]
        public async Task KeyCredentialSetUpAsync()
        {
            m_database = new LinqApplicationsDatabase();
            m_nodeManager = new TestableApplicationsNodeManager(
                ReferenceServer.CurrentInstance,
                ServerFixture.Config,
                m_database,
                Mock.Of<ICertificateGroup>());
            m_database.NamespaceIndex = m_nodeManager.ApplicationNamespaceIndex;

            ApplicationRecordDataType owner = CreateTestApplicationRecord("KeyCredentialOwner");
            ApplicationRecordDataType other = CreateTestApplicationRecord("KeyCredentialOther");
            m_ownerApplicationId = m_database.RegisterApplication(owner);
            m_otherApplicationId = m_database.RegisterApplication(other);

            m_service = await m_nodeManager.CreateKeyCredentialServiceAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public void KeyCredentialTearDown()
        {
            m_nodeManager?.Dispose();
        }

        [SetUp]
        public void ResetStore()
        {
            m_nodeManager.KeyCredentialRequestStore = new InMemoryKeyCredentialRequestStore();
        }

        [Test]
        public void KeyCredentialMethodsExposeApplicationPrivilegePermissions()
        {
            Assert.That(m_service.StartRequest!.OnReadRolePermissions, Is.Not.Null);
            Assert.That(m_service.StartRequest.OnReadUserRolePermissions, Is.Not.Null);
            Assert.That(m_service.FinishRequest!.OnReadRolePermissions, Is.Not.Null);
            Assert.That(m_service.FinishRequest.OnReadUserRolePermissions, Is.Not.Null);
            Assert.That(m_service.Revoke, Is.Not.Null);
            Assert.That(m_service.Revoke!.OnReadRolePermissions, Is.Not.Null);
            Assert.That(m_service.Revoke.OnReadUserRolePermissions, Is.Not.Null);
        }

        [Test]
        public async Task KeyCredentialAdminCanCompleteLifecycleAsync()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 1);

            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult finished =
                await FinishRequestAsync(context, requestId, cancelRequest: false)
                    .ConfigureAwait(false);
            KeyCredentialRevokeMethodStateResult revoked =
                await RevokeAsync(context, finished.CredentialId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(requestId.IsNull, Is.False);
                Assert.That(ServiceResult.IsGood(finished.ServiceResult), Is.True);
                Assert.That(finished.CredentialId, Is.Not.Empty);
                Assert.That(finished.CredentialSecret.IsEmpty, Is.False);
                Assert.That(ServiceResult.IsGood(revoked.ServiceResult), Is.True);
            });
        }

        [Test]
        public async Task ApplicationSelfAdminCanCompleteOwnApplicationLifecycleAsync()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(
                    GdsRole.ApplicationSelfAdmin,
                    applicationId: m_ownerApplicationId),
                certificateMarker: 2);

            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult finished =
                await FinishRequestAsync(context, requestId, cancelRequest: false)
                    .ConfigureAwait(false);
            KeyCredentialRevokeMethodStateResult revoked =
                await RevokeAsync(context, finished.CredentialId).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(finished.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(revoked.ServiceResult), Is.True);
        }

        [Test]
        public async Task ApplicationSelfAdminCannotManageAnotherApplicationAsync()
        {
            ISystemContext adminContext = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 3);
            NodeId requestId = await StartRequestAsync(adminContext, OtherApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult finished =
                await FinishRequestAsync(adminContext, requestId, cancelRequest: false)
                    .ConfigureAwait(false);

            ISystemContext selfAdminContext = CreateContext(
                CreateRoleIdentity(
                    GdsRole.ApplicationSelfAdmin,
                    applicationId: m_ownerApplicationId),
                certificateMarker: 3);

            AssertBadUserAccessDenied(
                async () => await StartRequestAsync(selfAdminContext, OtherApplicationUri)
                    .ConfigureAwait(false));
            AssertBadUserAccessDenied(
                async () => await FinishRequestAsync(
                    selfAdminContext,
                    requestId,
                    cancelRequest: false).ConfigureAwait(false));
            AssertBadUserAccessDenied(
                async () => await RevokeAsync(selfAdminContext, finished.CredentialId)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ApplicationAdminCanCompleteManagedApplicationLifecycleAsync()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(
                    GdsRole.ApplicationAdmin,
                    administeredApplicationIds: [m_ownerApplicationId]),
                certificateMarker: 4);

            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult finished =
                await FinishRequestAsync(context, requestId, cancelRequest: false)
                    .ConfigureAwait(false);
            KeyCredentialRevokeMethodStateResult revoked =
                await RevokeAsync(context, finished.CredentialId).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(finished.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(revoked.ServiceResult), Is.True);
        }

        [Test]
        public async Task ApplicationAdminCannotManageUnmanagedApplicationAsync()
        {
            ISystemContext adminContext = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 5);
            NodeId requestId = await StartRequestAsync(adminContext, OtherApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult finished =
                await FinishRequestAsync(adminContext, requestId, cancelRequest: false)
                    .ConfigureAwait(false);

            ISystemContext applicationAdminContext = CreateContext(
                CreateRoleIdentity(
                    GdsRole.ApplicationAdmin,
                    administeredApplicationIds: [m_ownerApplicationId]),
                certificateMarker: 5);

            AssertBadUserAccessDenied(
                async () => await StartRequestAsync(
                    applicationAdminContext,
                    OtherApplicationUri).ConfigureAwait(false));
            AssertBadUserAccessDenied(
                async () => await FinishRequestAsync(
                    applicationAdminContext,
                    requestId,
                    cancelRequest: false).ConfigureAwait(false));
            AssertBadUserAccessDenied(
                async () => await RevokeAsync(
                    applicationAdminContext,
                    finished.CredentialId).ConfigureAwait(false));
        }

        [Test]
        public async Task FinishRequestWithDifferentInitiatingCertificateFailsAsync()
        {
            ISystemContext startContext = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 6);
            NodeId requestId = await StartRequestAsync(startContext, OwnerApplicationUri)
                .ConfigureAwait(false);
            ISystemContext finishContext = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 7);

            Assert.That(
                async () => await FinishRequestAsync(
                    finishContext,
                    requestId,
                    cancelRequest: false).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task CancelRequestReturnsGoodWithoutCredentialMaterialAsync()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 8);
            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);

            KeyCredentialFinishRequestMethodStateResult result =
                await FinishRequestAsync(context, requestId, cancelRequest: true)
                    .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.CredentialId, Is.Null.Or.Empty);
                Assert.That(result.CredentialSecret.IsEmpty, Is.True);
            });
        }

        [Test]
        public async Task PendingRequestReturnsBadRequestNotCompleteAsync()
        {
            m_nodeManager.KeyCredentialRequestStore =
                new StateKeyCredentialRequestStore(
                    m_ownerApplicationId,
                    KeyCredentialRequestState.New);
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 9);

            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult result =
                await FinishRequestAsync(context, requestId, cancelRequest: false)
                    .ConfigureAwait(false);

            Assert.That(
                result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotComplete));
            Assert.That(result.CredentialSecret.IsEmpty, Is.True);
        }

        [Test]
        public async Task RejectedRequestReturnsBadRequestNotAllowedAsync()
        {
            m_nodeManager.KeyCredentialRequestStore =
                new StateKeyCredentialRequestStore(
                    m_ownerApplicationId,
                    KeyCredentialRequestState.Rejected);
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 13);

            NodeId requestId = await StartRequestAsync(context, OwnerApplicationUri)
                .ConfigureAwait(false);
            KeyCredentialFinishRequestMethodStateResult result =
                await FinishRequestAsync(context, requestId, cancelRequest: false)
                    .ConfigureAwait(false);

            Assert.That(
                result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(result.CredentialSecret.IsEmpty, Is.True);
        }

        [Test]
        public void UnknownApplicationFailureIsAuditedAndRethrown()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 10);

            Assert.That(
                async () => await StartRequestAsync(context, "urn:test:unknown")
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void UnknownCredentialFailureIsAuditedAndRethrown()
        {
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 11);

            Assert.That(
                async () => await RevokeAsync(context, "unknown-credential")
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void StoreWithoutApplicationOwnershipIsRejected()
        {
            m_nodeManager.KeyCredentialRequestStore = new LegacyKeyCredentialRequestStore();
            ISystemContext context = CreateContext(
                CreateRoleIdentity(GdsRole.KeyCredentialAdmin),
                certificateMarker: 12);

            Assert.That(
                async () => await StartRequestAsync(context, OwnerApplicationUri)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        private async Task<NodeId> StartRequestAsync(
            ISystemContext context,
            string applicationUri)
        {
            KeyCredentialStartRequestMethodStateResult result =
                await m_service.StartRequest!.OnCallAsync!(
                    context,
                    m_service.StartRequest,
                    m_service.NodeId,
                    applicationUri,
                    ByteString.From([1, 2, 3]),
                    SecurityPolicies.Basic256Sha256,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            return result.RequestId;
        }

        private ValueTask<KeyCredentialFinishRequestMethodStateResult> FinishRequestAsync(
            ISystemContext context,
            NodeId requestId,
            bool cancelRequest)
        {
            return m_service.FinishRequest!.OnCallAsync!(
                context,
                m_service.FinishRequest,
                m_service.NodeId,
                requestId,
                cancelRequest,
                CancellationToken.None);
        }

        private ValueTask<KeyCredentialRevokeMethodStateResult> RevokeAsync(
            ISystemContext context,
            string credentialId)
        {
            return m_service.Revoke!.OnCallAsync!(
                context,
                m_service.Revoke,
                m_service.NodeId,
                credentialId,
                CancellationToken.None);
        }

        private ServerSystemContext CreateContext(IUserIdentity identity, byte certificateMarker)
        {
            var endpoint = new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var channelContext = new SecureChannelContext(
                $"key-credential-{certificateMarker}",
                endpoint,
                RequestEncoding.Binary,
                [certificateMarker, 1, 2, 3]);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity);
            return ReferenceServer.CurrentInstance.DefaultSystemContext.Copy(operationContext);
        }

        private GdsRoleBasedIdentity CreateRoleIdentity(
            Role role,
            NodeId applicationId = default,
            IEnumerable<NodeId>? administeredApplicationIds = null)
        {
            return new GdsRoleBasedIdentity(
                new UserIdentity(role.Name, s_passwordBytes),
                [role],
                applicationId,
                administeredApplicationIds,
                ReferenceServer.CurrentInstance.NamespaceUris);
        }

        private static void AssertBadUserAccessDenied(Func<Task> action)
        {
            Assert.That(
                action,
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        private sealed class TestableApplicationsNodeManager : ApplicationsNodeManager
        {
            public TestableApplicationsNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                LinqApplicationsDatabase database,
                ICertificateGroup certificateGroup)
                : base(
                    server,
                    configuration,
                    database,
                    database,
                    certificateGroup,
                    autoApprove: true)
            {
            }

            public ushort ApplicationNamespaceIndex => NamespaceIndexes[0];

            public async ValueTask<KeyCredentialServiceState> CreateKeyCredentialServiceAsync()
            {
                ushort namespaceIndex = NamespaceIndexes[1];
                var service = new KeyCredentialServiceState(null);
                service.Create(
                    SystemContext,
                    new NodeId("KeyCredentialManagement/Test", namespaceIndex),
                    new QualifiedName("Test", namespaceIndex),
                    new LocalizedText("Test"),
                    false);
                service.AddRevoke(SystemContext);

                NodeState active = await AddBehaviourToPredefinedNodeAsync(
                    SystemContext,
                    service).ConfigureAwait(false);
                return (KeyCredentialServiceState)active;
            }
        }

        private sealed class StateKeyCredentialRequestStore :
            IApplicationOwnedKeyCredentialRequestStore
        {
            private readonly NodeId m_applicationId;
            private readonly KeyCredentialRequestState m_state;
            private readonly NodeId m_requestId = new(1);

            public StateKeyCredentialRequestStore(
                NodeId applicationId,
                KeyCredentialRequestState state)
            {
                m_applicationId = applicationId;
                m_state = state;
            }

            public ValueTask<NodeId> StartRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(m_requestId);
            }

            public ValueTask<NodeId> StartBoundRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                ByteString clientCertificateFingerprint,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(m_requestId);
            }

            public ValueTask<NodeId> StartOwnedBoundRequestAsync(
                string applicationUri,
                NodeId applicationId,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                ByteString clientCertificateFingerprint,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(m_requestId);
            }

            public ValueTask<FinishKeyCredentialRequestResult> FinishRequestAsync(
                NodeId requestId,
                bool cancelRequest,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<FinishKeyCredentialRequestResult>(
                    new FinishKeyCredentialRequestResult
                    {
                        State = m_state
                    });
            }

            public ValueTask<FinishKeyCredentialRequestResult> FinishBoundRequestAsync(
                NodeId requestId,
                bool cancelRequest,
                ByteString clientCertificateFingerprint,
                CancellationToken cancellationToken = default)
            {
                return FinishRequestAsync(requestId, cancelRequest, cancellationToken);
            }

            public ValueTask<NodeId> GetRequestApplicationIdAsync(
                NodeId requestId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(m_applicationId);
            }

            public ValueTask<FinishKeyCredentialRequestResult> FinishOwnedBoundRequestAsync(
                NodeId requestId,
                bool cancelRequest,
                NodeId applicationId,
                ByteString clientCertificateFingerprint,
                CancellationToken cancellationToken = default)
            {
                return FinishRequestAsync(requestId, cancelRequest, cancellationToken);
            }

            public ValueTask<NodeId> GetCredentialApplicationIdAsync(
                string credentialId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(m_applicationId);
            }

            public ValueTask RevokeAsync(
                string credentialId,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask RevokeOwnedAsync(
                string credentialId,
                NodeId applicationId,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class LegacyKeyCredentialRequestStore : IKeyCredentialRequestStore
        {
            public ValueTask<NodeId> StartRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(new NodeId(1));
            }

            public ValueTask<FinishKeyCredentialRequestResult> FinishRequestAsync(
                NodeId requestId,
                bool cancelRequest,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<FinishKeyCredentialRequestResult>(
                    new FinishKeyCredentialRequestResult
                    {
                        State = KeyCredentialRequestState.Completed
                    });
            }

            public ValueTask RevokeAsync(
                string credentialId,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
