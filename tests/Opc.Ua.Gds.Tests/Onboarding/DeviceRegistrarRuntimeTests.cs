/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises registrar authorization, shared registration, provider failures, and generated wire binding.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DeviceRegistrarRuntimeTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void ConstructorRejectsEachMissingProvider(int provider)
        {
            var harness = new Harness();
            Assert.Throws<ArgumentNullException>(() => new DeviceRegistrarRuntime(
                provider == 0 ? null! : harness.Database.Object,
                provider == 1 ? null! : harness.Lifecycle.Object,
                provider == 2 ? null! : harness.Authorization,
                provider == 3 ? null! : harness.Push.Object,
                provider == 4 ? null! : harness.Managers.Object,
                provider == 5 ? null! : harness.Audit.Object));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AuthorizationConstructorRequiresLifecycleAndPerApplicationPolicy(bool lifecycleMissing)
        {
            Assert.Throws<ArgumentNullException>(() => new DeviceRegistrarAuthorization(
                lifecycleMissing ? null! : new Mock<IDeviceRegistrarLifecycle>().Object,
                lifecycleMissing ? new Mock<IDeviceRegistrarApplicationAuthorization>().Object : null!));
        }

        /// <summary>
        /// Missing or index-only role configuration cannot authorize a software state change.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task UpdateSoftwareStatusRejectsMissingNamespaceQualifiedRole(bool indexOnly)
        {
            var harness = new Harness();
            var runtime = new DeviceRegistrarRuntime(
                harness.Database.Object, harness.Lifecycle.Object, harness.Authorization,
                harness.Push.Object, harness.Managers.Object, harness.Audit.Object)
            {
                SoftwareUpdateAdminRoleId = indexOnly ? new ExpandedNodeId(1234u) : ExpandedNodeId.Null
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.UpdateSoftwareStatusAsync(
                    CreateContext(softwareAdmin: true), Device, true, "2.0").ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            harness.Lifecycle.Verify(value => value.UpdateSoftwareStatusAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            harness.Audit.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UpdateSoftwareStatusPersistsStatusAndRevisionBeforeAuditing(bool status)
        {
            var harness = new Harness();
            var order = new List<string>();
            harness.Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                Device, status, "2.0", It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("stored"))
                .ReturnsAsync(true);
            harness.Audit.Setup(a => a.ReportSoftwareStatusAsync(
                It.IsAny<ISystemContext>(), Device, status, "2.0", It.IsAny<CancellationToken>()))
                .Callback(() => order.Add("audited"))
                .Returns(default(ValueTask));
            await harness.Runtime.UpdateSoftwareStatusAsync(
                CreateContext(softwareAdmin: true), Device, status, "2.0").ConfigureAwait(false);
            string[] expectedOrder = ["stored", "audited"];
            Assert.That(order, Is.EqualTo(expectedOrder));
        }

        [Test]
        public async Task UpdateSoftwareStatusRejectsUnknownDeviceWithoutAudit()
        {
            var harness = new Harness();
            harness.Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                Device, true, "2.0", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.UpdateSoftwareStatusAsync(
                    CreateContext(softwareAdmin: true), Device, true, "2.0").ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            harness.Audit.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RepeatedSoftwareReportsReachLifecycleWithoutResettingDeviceIdentity()
        {
            var harness = new Harness();
            bool storedStatus = false;
            string storedRevision = string.Empty;
            harness.Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                Device, It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, bool, string, CancellationToken>((_, status, revision, _) =>
                {
                    storedStatus = status;
                    storedRevision = revision;
                })
                .ReturnsAsync(true);
            SessionSystemContext context = CreateContext(softwareAdmin: true);
            await harness.Runtime.UpdateSoftwareStatusAsync(context, Device, false, "required").ConfigureAwait(false);
            await harness.Runtime.UpdateSoftwareStatusAsync(context, Device, true, "installed").ConfigureAwait(false);
            Assert.That(storedStatus, Is.True);
            Assert.That(storedRevision, Is.EqualTo("installed"));
            harness.Database.VerifyNoOtherCalls();
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestCase("")]
        [TestCase("relative")]
        public async Task UpdateSoftwareStatusRejectsMalformedProductInstance(string product)
        {
            var harness = new Harness();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.UpdateSoftwareStatusAsync(
                    CreateContext(softwareAdmin: true), product, true, "2.0").ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Lifecycle.VerifyNoOtherCalls();
        }

        [Test]
        public async Task UpdateSoftwareStatusRejectsEmptyRevision()
        {
            var harness = new Harness();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.UpdateSoftwareStatusAsync(
                    CreateContext(softwareAdmin: true), Device, true, string.Empty).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Lifecycle.VerifyNoOtherCalls();
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        public async Task AdministrativeOperationsRejectWrongRole(string operation)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(
                registrarAdmin: operation == "UpdateSoftwareStatus",
                softwareAdmin: operation == "RegisterDeviceEndpoint");
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Lifecycle.VerifyNoOtherCalls();
            harness.Push.VerifyNoOtherCalls();
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task OperationsRejectMissingSession(string operation)
        {
            var harness = new Harness();
            var context = new SystemContext(NUnitTelemetryContext.Create());
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Lifecycle.VerifyNoOtherCalls();
            harness.Push.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
            harness.Managers.VerifyNoOtherCalls();
        }

        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task AnonymousUserTokenWithApprovedDcaChannelCertificateHasDcaPrivilege(string operation)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(anonymous: true);
            await InvokeAsync(harness, operation, context).ConfigureAwait(false);
            harness.Lifecycle.Verify(l => l.FindDeviceByCertificateAsync(
                It.Is<ByteString>(c => c == ByteString.From(new byte[] { 1, 2, 3 })),
                It.IsAny<CancellationToken>()), Times.Once);
            if (operation == "GetManagers")
            {
                harness.Managers.Verify(m => m.GetManagersAsync(
                    Device, It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                harness.Database.Verify(d => d.RegisterApplication(
                    It.IsAny<ApplicationRecordDataType>()), Times.Once);
                harness.Applications.Verify(a => a.CanManageAsync(
                    Device, It.IsAny<ApplicationRecordDataType>(), string.Empty, It.IsAny<CancellationToken>()),
                Times.Once);
            }
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        public async Task AnonymousUserTokenWithoutRequiredRoleCannotInvokeAdministrativeOperations(string operation)
        {
            var harness = new Harness();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, CreateContext(anonymous: true)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Lifecycle.VerifyNoOtherCalls();
            harness.Push.VerifyNoOtherCalls();
        }

        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task AnonymousUserTokenWithUnapprovedChannelCertificateCannotAcquireDcaPrivilege(string operation)
        {
            var harness = new Harness();
            harness.Lifecycle.Setup(l => l.FindDeviceByCertificateAsync(
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, CreateContext(anonymous: true)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Managers.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
            harness.Applications.VerifyNoOtherCalls();
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task AnonymousUserTokenStillRequiresAuthenticatedChannelAndCertificate(
            bool signed, bool certificate)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(
                anonymous: true,
                mode: signed ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None,
                certificate: certificate,
                policy: signed ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.GetManagersAsync(context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.Lifecycle.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AnonymousUserTokenStillRequiresRealSessionIdentifier()
        {
            var harness = new Harness();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.GetManagersAsync(
                    CreateContext(anonymous: true, missingSessionId: true)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Lifecycle.VerifyNoOtherCalls();
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task OperationsRejectMissingUser(string operation)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(missingUser: true);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [TestCase(MessageSecurityMode.None, true, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, false, SecurityPolicies.Basic256Sha256)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, true, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.Invalid, true, SecurityPolicies.Basic256Sha256)]
        public async Task AuthenticatedUserAndRolesDoNotReplaceAuthenticatedChannel(
            MessageSecurityMode mode, bool certificate, string policy)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(
                registrarAdmin: true, softwareAdmin: true, mode: mode,
                certificate: certificate, policy: policy);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.GetManagersAsync(context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            harness.Lifecycle.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SessionWithoutOperationContextCannotAuthorizeLocally()
        {
            var harness = new Harness();
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(i => i.TokenType).Returns(UserTokenType.UserName);
            var context = new SessionSystemContext(NUnitTelemetryContext.Create())
            {
                SessionId = new NodeId("session", 0),
                UserIdentity = identity.Object
            };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.GetManagersAsync(context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task SessionIdentifierIsRequiredEvenWhenAnOperationHasIdentityAndChannel()
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(missingSessionId: true);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.GetManagersAsync(context).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task RegisterDeviceEndpointAwaitsQueueAcceptanceAndForwardsCancellation()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ApplicationDescription application = CreateEndpoint();
            harness.Push.Setup(p => p.EnqueueAsync(application, cancellation.Token))
                .Returns(new ValueTask(accepted.Task));
            Task operation = harness.Runtime.RegisterDeviceEndpointAsync(
                CreateContext(registrarAdmin: true), application, cancellation.Token).AsTask();
            Assert.That(operation.IsCompleted, Is.False);
            accepted.SetResult(true);
            await operation.ConfigureAwait(false);
            harness.Push.Verify(p => p.EnqueueAsync(application, cancellation.Token), Times.Once);
        }

        [Test]
        public async Task RepeatedEndpointRegistrationSchedulesEachRealRequest()
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(registrarAdmin: true);
            ApplicationDescription endpoint = CreateEndpoint();
            await harness.Runtime.RegisterDeviceEndpointAsync(context, endpoint).ConfigureAwait(false);
            await harness.Runtime.RegisterDeviceEndpointAsync(context, endpoint).ConfigureAwait(false);
            harness.Push.Verify(p => p.EnqueueAsync(endpoint, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [TestCase(ApplicationType.Client, "opc.tcp://device:4840")]
        [TestCase(ApplicationType.Server, "relative")]
        [TestCase(ApplicationType.Server, "")]
        public async Task RegisterDeviceEndpointRejectsInvalidServerDescription(ApplicationType type, string endpoint)
        {
            var harness = new Harness();
            ApplicationDescription application = CreateEndpoint();
            application.ApplicationType = type;
            application.DiscoveryUrls = [endpoint];
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterDeviceEndpointAsync(
                    CreateContext(registrarAdmin: true), application).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Push.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegisterDeviceEndpointRejectsMissingDiscoveryUrls()
        {
            var harness = new Harness();
            ApplicationDescription application = CreateEndpoint();
            application.DiscoveryUrls = [];
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterDeviceEndpointAsync(
                    CreateContext(registrarAdmin: true), application).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Push.VerifyNoOtherCalls();
        }

        [TestCase(MessageSecurityMode.Sign)]
        [TestCase(MessageSecurityMode.SignAndEncrypt)]
        public async Task GetManagersUsesProvenDeviceAndReturnsNativeDescriptions(MessageSecurityMode mode)
        {
            var harness = new Harness();
            ArrayOf<ManagerDescription> expected =
            [
                new ManagerDescription
                {
                    PurposeUri = "http://opcfoundation.org/UA/Onboarding/CertificateManager",
                    EndpointUrls = ["opc.tcp://certificate-manager:4840"]
                }
            ];
            harness.Managers.Setup(m => m.GetManagersAsync(Device, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);
            ArrayOf<ManagerDescription> managers = await harness.Runtime.GetManagersAsync(
                CreateContext(mode: mode)).ConfigureAwait(false);
            Assert.That(managers, Is.EqualTo(expected));
            harness.Lifecycle.Verify(l => l.FindDeviceByCertificateAsync(
                It.Is<ByteString>(c => c == ByteString.From(new byte[] { 1, 2, 3 })),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task AdministrativeRoleWithoutApprovedDcaCertificateCannotAcquireDcaPrivilege(string operation)
        {
            var harness = new Harness();
            harness.Lifecycle.Setup(l => l.FindDeviceByCertificateAsync(
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await InvokeAsync(harness, operation, CreateContext(
                    registrarAdmin: true, softwareAdmin: true)).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Managers.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
            harness.Applications.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegisterManagedApplicationUsesSharedCertificateManagerIdentifierAndDeviceRelation()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            NodeId actual = await harness.Runtime.RegisterManagedApplicationAsync(
                CreateContext(), application, "urn:protocol:example").ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(ApplicationId));
            harness.Database.Verify(d => d.RegisterApplication(application), Times.Once);
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                Device, ApplicationId, "urn:protocol:example", It.IsAny<CancellationToken>()), Times.Once);
            harness.Applications.Verify(a => a.CanManageAsync(
                Device, application, "urn:protocol:example", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RepeatedRegistrationRetainsSharedIdAndAuthorizesEveryCall()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            ApplicationRecordDataType registered = CreateApplication();
            registered.ApplicationId = ApplicationId;
            harness.Database.SetupSequence(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri)))
                .Returns(Array.Empty<ApplicationRecordDataType>())
                .Returns(new[] { registered });
            harness.Database.Setup(d => d.UpdateApplication(
                It.Is<ApplicationRecordDataType>(a => a.ApplicationId == ApplicationId)))
                .Returns(ApplicationId);
            SessionSystemContext context = CreateContext();
            NodeId first = await harness.Runtime.RegisterManagedApplicationAsync(
                context, application, string.Empty).ConfigureAwait(false);
            NodeId second = await harness.Runtime.RegisterManagedApplicationAsync(
                context, application, string.Empty).ConfigureAwait(false);
            Assert.That(first, Is.EqualTo(ApplicationId));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(application.ApplicationId.IsNull, Is.True, "Do not mutate the caller's input.");
            harness.Database.Verify(d => d.RegisterApplication(application), Times.Once);
            harness.Database.Verify(d => d.UpdateApplication(
                It.Is<ApplicationRecordDataType>(a => a.ApplicationId == ApplicationId &&
                    a.ApplicationUri == application.ApplicationUri)), Times.Once);
            harness.Applications.Verify(a => a.CanManageAsync(
                Device, application, string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public async Task PerApplicationDenialCannotReuseEarlierPermissionOrExistingRegistryId()
        {
            var harness = new Harness();
            ApplicationRecordDataType allowed = CreateApplication();
            ApplicationRecordDataType denied = CreateApplication();
            denied.ApplicationUri = "urn:other-device:application";
            harness.Applications.Setup(a => a.CanManageAsync(
                Device, denied, string.Empty, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            await harness.Runtime.RegisterManagedApplicationAsync(
                CreateContext(), allowed, string.Empty).ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), denied, string.Empty).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            harness.Database.Verify(d => d.FindApplications(
                It.Is<string>(uri => uri == denied.ApplicationUri)), Times.Never);
            harness.Database.Verify(d => d.RegisterApplication(denied), Times.Never);
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RevokedApplicationPermissionDeniesRepeatedRegistrationBeforeDatabaseAccess()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            harness.Applications.SetupSequence(a => a.CanManageAsync(
                Device, application, string.Empty, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            await harness.Runtime.RegisterManagedApplicationAsync(
                CreateContext(), application, string.Empty).ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            harness.Database.Verify(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri)), Times.Once);
            harness.Database.Verify(d => d.RegisterApplication(application), Times.Once);
            harness.Database.Verify(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
        }

        [Test]
        public async Task FailedAssociationCanRetryUsingExistingSharedRegistryIdentifier()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            ApplicationRecordDataType registered = CreateApplication();
            registered.ApplicationId = ApplicationId;
            harness.Database.SetupSequence(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri)))
                .Returns(Array.Empty<ApplicationRecordDataType>())
                .Returns(new[] { registered });
            harness.Database.Setup(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()))
                .Returns(ApplicationId);
            harness.Lifecycle.SetupSequence(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Relation store unavailable."))
                .Returns(default(ValueTask));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));
            NodeId retried = await harness.Runtime.RegisterManagedApplicationAsync(
                CreateContext(), application, string.Empty).ConfigureAwait(false);
            Assert.That(retried, Is.EqualTo(ApplicationId));
            harness.Database.Verify(d => d.RegisterApplication(application), Times.Once);
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// An existing application's metadata is not changed when its device relation is denied.
        /// </summary>
        [Test]
        public async Task ConflictingApplicationOwnershipRejectsBeforeUpdatingSharedMetadata()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            ApplicationRecordDataType registered = CreateApplication();
            registered.ApplicationId = ApplicationId;
            harness.Database.Setup(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri))).Returns([registered]);
            harness.Lifecycle.Setup(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadRequestNotAllowed));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, It.IsAny<CancellationToken>()), Times.Once);
            harness.Database.Verify(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
            harness.Database.Verify(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
        }

        /// <summary>
        /// Cancellation observed after recording an existing relation does not change application metadata.
        /// </summary>
        [Test]
        public async Task CancellationDuringExistingAssociationPreventsSharedMetadataUpdate()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            ApplicationRecordDataType application = CreateApplication();
            ApplicationRecordDataType registered = CreateApplication();
            registered.ApplicationId = ApplicationId;
            harness.Database.Setup(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri))).Returns([registered]);
            harness.Lifecycle.Setup(l => l.AssociateApplicationAsync(
                Device, ApplicationId, string.Empty, cancellation.Token))
                .Callback(cancellation.Cancel)
                .Returns(default(ValueTask));

            OperationCanceledException? error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty, cancellation.Token).ConfigureAwait(false));

            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            harness.Database.Verify(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
            harness.Database.Verify(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
        }

        /// <summary>
        /// Standard application validation precedes any lifecycle relation or shared database write.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidApplicationMetadataDoesNotCreateDeviceRelationOrMutateDatabase(bool existing)
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            application.ApplicationNames = [];
            ApplicationRecordDataType registered = CreateApplication();
            registered.ApplicationId = ApplicationId;
            harness.Database.Setup(d => d.FindApplications(
                It.Is<string>(uri => uri == application.ApplicationUri)))
                .Returns(existing ? [registered] : []);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            harness.Database.VerifyNoOtherCalls();
        }

        [TestCase("", "")]
        [TestCase("relative", "")]
        [TestCase("urn:application:test", "relative")]
        public async Task RegisterManagedApplicationRejectsMalformedUrisBeforeApplicationAuthorization(
            string applicationUri, string protocolUri)
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            application.ApplicationUri = applicationUri;
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, protocolUri).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Applications.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegisterManagedApplicationRejectsCallerSuppliedUnrelatedId()
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            application.ApplicationId = new NodeId("unrelated", 2);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Database.Verify(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DatabaseValidationErrorsBecomeBadInvalidArgument(bool updating)
        {
            var harness = new Harness();
            ApplicationRecordDataType application = CreateApplication();
            if (updating)
            {
                ApplicationRecordDataType registered = CreateApplication();
                registered.ApplicationId = ApplicationId;
                harness.Database.Setup(d => d.FindApplications(
                    It.Is<string>(uri => uri == application.ApplicationUri)))
                    .Returns(new[] { registered });
                harness.Database.Setup(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Throws(new ArgumentException("Invalid registration data."));
            }
            else
            {
                harness.Database.Setup(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Throws(new ArgumentException("Invalid registration data."));
            }
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.RegisterManagedApplicationAsync(
                    CreateContext(), application, string.Empty).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            harness.Lifecycle.Verify(l => l.AssociateApplicationAsync(
                It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                updating ? Times.Once() : Times.Never());
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task CancellationBeforeCallPerformsNoProviderWork(string operation)
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await InvokeAsync(
                    harness, operation, CreateContext(registrarAdmin: true, softwareAdmin: true),
                    cancellation.Token).ConfigureAwait(false));
            harness.Lifecycle.VerifyNoOtherCalls();
            harness.Push.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
            harness.Managers.VerifyNoOtherCalls();
        }

        [TestCase("UpdateSoftwareStatus")]
        [TestCase("RegisterDeviceEndpoint")]
        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task ProviderFailuresPropagateWithoutReportingSuccess(string operation)
        {
            var harness = new Harness();
            var failure = new InvalidOperationException("Provider unavailable.");
            harness.Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            harness.Push.Setup(p => p.EnqueueAsync(
                It.IsAny<ApplicationDescription>(), It.IsAny<CancellationToken>())).Throws(failure);
            harness.Managers.Setup(m => m.GetManagersAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(failure);
            harness.Database.Setup(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>())).Throws(failure);
            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await InvokeAsync(harness, operation, CreateContext(
                    registrarAdmin: true, softwareAdmin: true)).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
            harness.Audit.VerifyNoOtherCalls();
        }

        [Test]
        public async Task AuditFailureIsNotSilentlyIgnored()
        {
            var harness = new Harness();
            var failure = new InvalidOperationException("Audit unavailable.");
            harness.Audit.Setup(a => a.ReportSoftwareStatusAsync(
                It.IsAny<ISystemContext>(), Device, true, "2.0", It.IsAny<CancellationToken>()))
                .Throws(failure);
            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Runtime.UpdateSoftwareStatusAsync(
                    CreateContext(softwareAdmin: true), Device, true, "2.0").ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
            harness.Lifecycle.Verify(l => l.UpdateSoftwareStatusAsync(
                Device, true, "2.0", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task CancellationAfterStoredStatusCannotSuppressRequiredAuditEvent()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            harness.Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                Device, true, "2.0", cancellation.Token))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync(true);
            await harness.Runtime.UpdateSoftwareStatusAsync(
                CreateContext(softwareAdmin: true), Device, true, "2.0", cancellation.Token).ConfigureAwait(false);
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            harness.Audit.Verify(a => a.ReportSoftwareStatusAsync(
                It.IsAny<ISystemContext>(), Device, true, "2.0", CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task CancellationWhileWaitingForPushAcceptanceIsObservedByCaller()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            harness.Push.Setup(p => p.EnqueueAsync(
                It.IsAny<ApplicationDescription>(), cancellation.Token))
                .Returns(() => new ValueTask(Task.Delay(Timeout.Infinite, cancellation.Token)));
            Task operation = harness.Runtime.RegisterDeviceEndpointAsync(
                CreateContext(registrarAdmin: true), CreateEndpoint(), cancellation.Token).AsTask();
            Assert.That(operation.IsCompleted, Is.False);
            cancellation.Cancel();
            try
            {
                await operation.ConfigureAwait(false);
                Assert.Fail("Push scheduling ignored cancellation.");
            }
            catch (OperationCanceledException error)
            {
                Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            harness.Push.Verify(p => p.EnqueueAsync(
                It.IsAny<ApplicationDescription>(), cancellation.Token), Times.Once);
        }

        [Test]
        public async Task GeneratedBindingPreservesTypedValuesForAllFourMethods()
        {
            var harness = new Harness();
            (SessionSystemContext context, DeviceRegistrarState registrar) = CreateRegistrar();
            registrar.BindToRuntime(harness.Runtime);
            Assert.That(registrar.ProvideIdentities!.OnCallMethod2Async, Is.Not.Null);
            ArrayOf<ManagerDescription> expected =
            [
                new ManagerDescription { PurposeUri = "urn:manager:test", EndpointUrls = ["https://manager/"] }
            ];
            harness.Managers.Setup(m => m.GetManagersAsync(Device, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expected);

            var outputs = new List<Variant> { default };
            ServiceResult update = await CallAsync(
                registrar.UpdateSoftwareStatus!, context, [Device, true, "2.0"], []).ConfigureAwait(false);
            ServiceResult endpoint = await CallAsync(
                registrar.RegisterDeviceEndpoint!, context,
                [Variant.FromStructure(CreateEndpoint())], []).ConfigureAwait(false);
            ServiceResult managers = await CallAsync(
                registrar.GetManagers!, context, [], outputs).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(update), Is.True);
            Assert.That(ServiceResult.IsGood(endpoint), Is.True);
            Assert.That(ServiceResult.IsGood(managers), Is.True);
            Assert.That(outputs[0].TryGetStructure(out ArrayOf<ManagerDescription> actualManagers), Is.True);
            Assert.That(actualManagers, Is.EqualTo(expected));
            ServiceResult registration = await CallAsync(
                registrar.RegisterManagedApplication!, context,
                [Variant.FromStructure(CreateApplication()), string.Empty], outputs).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(registration), Is.True);
            Assert.That(outputs[0].TryGetValue(out NodeId actualId), Is.True);
            Assert.That(actualId, Is.EqualTo(ApplicationId));
            harness.Lifecycle.Verify(l => l.UpdateSoftwareStatusAsync(
                Device, true, "2.0", It.IsAny<CancellationToken>()), Times.Once);
            harness.Push.Verify(p => p.EnqueueAsync(
                It.IsAny<ApplicationDescription>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase("UpdateSoftwareStatus", 0, "missing")]
        [TestCase("UpdateSoftwareStatus", 4, "extra")]
        [TestCase("UpdateSoftwareStatus", 3, "type")]
        [TestCase("RegisterDeviceEndpoint", 0, "missing")]
        [TestCase("RegisterDeviceEndpoint", 2, "extra")]
        [TestCase("RegisterDeviceEndpoint", 1, "type")]
        [TestCase("GetManagers", 1, "extra")]
        [TestCase("RegisterManagedApplication", 1, "missing")]
        [TestCase("RegisterManagedApplication", 3, "extra")]
        [TestCase("RegisterManagedApplication", 2, "type")]
        public async Task GeneratedBindingRejectsMalformedWireArguments(
            string methodName, int inputCount, string failure)
        {
            StatusCode expected = failure switch
            {
                "missing" => StatusCodes.BadArgumentsMissing,
                "extra" => StatusCodes.BadTooManyArguments,
                "type" => StatusCodes.BadTypeMismatch,
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            (SessionSystemContext context, DeviceRegistrarState registrar) = CreateRegistrar();
            var runtime = new Mock<IDeviceRegistrarRuntime>(MockBehavior.Strict);
            registrar.BindToRuntime(runtime.Object);
            MethodState method = methodName switch
            {
                "UpdateSoftwareStatus" => registrar.UpdateSoftwareStatus!,
                "RegisterDeviceEndpoint" => registrar.RegisterDeviceEndpoint!,
                "GetManagers" => registrar.GetManagers!,
                _ => registrar.RegisterManagedApplication!
            };
            ArrayOf<Variant> inputs = Enumerable.Repeat(Variant.From(42), inputCount).ToArray().ToArrayOf();
            ServiceResult result = await CallAsync(method, context, inputs, [default]).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(expected));
            runtime.VerifyNoOtherCalls();
        }

        [TestCase("GetManagers")]
        [TestCase("RegisterManagedApplication")]
        public async Task GeneratedBindingRejectsMissingOutputSlotsBeforeDoingWork(string methodName)
        {
            (SessionSystemContext context, DeviceRegistrarState registrar) = CreateRegistrar();
            var runtime = new Mock<IDeviceRegistrarRuntime>(MockBehavior.Strict);
            registrar.BindToRuntime(runtime.Object);
            MethodState method;
            ArrayOf<Variant> inputs;
            if (methodName == "GetManagers")
            {
                method = registrar.GetManagers!;
                inputs = [];
            }
            else
            {
                method = registrar.RegisterManagedApplication!;
                inputs = [Variant.FromStructure(CreateApplication()), string.Empty];
            }
            ServiceResult result = await CallAsync(method, context, inputs, []).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            runtime.VerifyNoOtherCalls();
        }

        [Test]
        public void BindingRejectsNullRuntimeAndIncompleteGeneratedRegistrar()
        {
            var registrar = new DeviceRegistrarState(null);
            Assert.Throws<ArgumentNullException>(() => registrar.BindToRuntime(null!));
            ServiceResultException? missing = Assert.Throws<ServiceResultException>(() =>
                registrar.BindToRuntime(new Mock<IDeviceRegistrarRuntime>().Object));
            Assert.That(missing!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.Throws<ArgumentNullException>(() =>
                DeviceRegistrarRuntimeExtensions.BindToRuntime(null!, new Mock<IDeviceRegistrarRuntime>().Object));
        }

        /// <summary>
        /// Legacy lifecycle implementations cannot authorize managed applications through a permissive policy.
        /// </summary>
        [Test]
        public async Task ManagedRegistrationRejectsLifecycleWithoutPersistedScopeBeforeDatabaseMutation()
        {
            var lifecycle = new Mock<IDeviceRegistrarLifecycle>(MockBehavior.Strict);
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            var authorization = new Mock<IDeviceRegistrarAuthorization>(MockBehavior.Strict);
            authorization.Setup(value => value.RequireDcaAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(Device);
            var runtime = new DeviceRegistrarRuntime(database.Object, lifecycle.Object, authorization.Object,
                new Mock<IDeviceRegistrarPushScheduler>().Object,
                new Mock<IDeviceRegistrarManagerProvider>().Object,
                new Mock<IDeviceRegistrarAuditReporter>().Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RegisterManagedApplicationAsync(CreateContext(), CreateApplication(), string.Empty)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            database.VerifyNoOtherCalls();
            lifecycle.VerifyNoOtherCalls();
            authorization.Verify(value => value.RequireApplicationAsync(
                It.IsAny<string>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Legacy lifecycle implementations also cannot bypass scope through the direct application-policy gate.
        /// </summary>
        [Test]
        public async Task ApplicationAuthorizationRejectsLifecycleWithoutPersistedScope()
        {
            var lifecycle = new Mock<IDeviceRegistrarLifecycle>(MockBehavior.Strict);
            var policy = new Mock<IDeviceRegistrarApplicationAuthorization>(MockBehavior.Strict);
            var authorization = new DeviceRegistrarAuthorization(lifecycle.Object, policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireApplicationAsync(
                    Device, CreateApplication(), string.Empty).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            policy.VerifyNoOtherCalls();
            lifecycle.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Proved singleton DCA credentials keep their own issuance rights but cannot configure other applications.
        /// </summary>
        [Test]
        public async Task InstalledSingletonCannotRegisterOrAuthorizeManagedApplicationEvenWhenPolicyApproves()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("singleton-dca", 2);
            var managedId = new NodeId("existing-managed", 2);
            ByteString selected = ByteString.From(new byte[] { 4, 5, 6 });
            ByteString installed = ByteString.From(new byte[] { 1, 2, 3 });
            await lifecycle.RecordCandidateAsync(Device, dcaId, selected, false).ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, managedId, string.Empty).ConfigureAwait(false);
            await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);
            await lifecycle.RecordIssuedDcaCertificateAsync(Device, dcaId, installed).ConfigureAwait(false);
            var database = new Mock<IApplicationsDatabase>(MockBehavior.Strict);
            var policy = new Mock<IDeviceRegistrarApplicationAuthorization>(MockBehavior.Strict);
            policy.Setup(value => value.CanManageAsync(It.IsAny<string>(), It.IsAny<ApplicationRecordDataType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var authorization = new DeviceRegistrarAuthorization(lifecycle, policy.Object);
            var runtime = new DeviceRegistrarRuntime(database.Object, lifecycle, authorization,
                new Mock<IDeviceRegistrarPushScheduler>().Object,
                new Mock<IDeviceRegistrarManagerProvider>().Object,
                new Mock<IDeviceRegistrarAuditReporter>().Object);
            SessionSystemContext context = CreateContext();

            // This is the authenticated installed peer; RequireDcaAsync commits pending-credential promotion.
            Assert.That(await authorization.RequireDcaAsync(context).ConfigureAwait(false), Is.EqualTo(Device));
            DeviceRegistrarDevice? confirmed = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(confirmed!.DcaCertificate, Is.EqualTo(installed));
            Assert.That(confirmed.PendingDcaCertificate.IsEmpty, Is.True);
            Assert.That(confirmed.IsSingletonRestricted, Is.True);
            await authorization.RequireDcaCertificateIssuanceAsync(context, dcaId).ConfigureAwait(false);
            ServiceResultException? registration = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.RegisterManagedApplicationAsync(
                    context, CreateApplication(), string.Empty).ConfigureAwait(false));
            var managed = CreateApplication();
            managed.ApplicationId = managedId;
            ServiceResultException? issuance = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireManagedCertificateIssuanceAsync(context, managed, string.Empty)
                    .ConfigureAwait(false));
            ServiceResultException? certificateAccess = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.AuthorizeApplicationAsync(context, managed).ConfigureAwait(false));

            Assert.That(registration!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(issuance!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(certificateAccess!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            ArrayOf<DeviceRegistrarApplicationBinding> visible =
                await authorization.GetChannelApplicationsAsync(context).ConfigureAwait(false);
            Assert.That(visible.Count, Is.EqualTo(1));
            Assert.That(visible[0].ApplicationId, Is.EqualTo(dcaId));
            Assert.That(visible[0].IsDca, Is.True);
            database.VerifyNoOtherCalls();
            policy.Verify(value => value.CanManageAsync(It.IsAny<string>(), It.IsAny<ApplicationRecordDataType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static async ValueTask InvokeAsync(
            Harness harness,
            string operation,
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            switch (operation)
            {
                case "UpdateSoftwareStatus":
                    await harness.Runtime.UpdateSoftwareStatusAsync(
                        context, Device, true, "2.0", cancellationToken).ConfigureAwait(false);
                    break;
                case "RegisterDeviceEndpoint":
                    await harness.Runtime.RegisterDeviceEndpointAsync(
                        context, CreateEndpoint(), cancellationToken).ConfigureAwait(false);
                    break;
                case "GetManagers":
                    await harness.Runtime.GetManagersAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await harness.Runtime.RegisterManagedApplicationAsync(
                        context, CreateApplication(), string.Empty, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private static ValueTask<ServiceResult> CallAsync(
            MethodState method,
            ISystemContext context,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            return method.OnCallMethod2Async!(context, method, new NodeId("registrar", 0), inputs, outputs, default);
        }

        private static SessionSystemContext CreateContext(
            bool registrarAdmin = false,
            bool softwareAdmin = false,
            bool anonymous = false,
            bool missingUser = false,
            bool missingSessionId = false,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            bool certificate = true,
            string policy = SecurityPolicies.Basic256Sha256)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(Opc.Ua.Gds.Namespaces.OpcUaGds);
            namespaces.Append(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            namespaces.Append(RolesNamespace);
            var roles = new List<NodeId>();
            if (registrarAdmin)
            {
                roles.Add(ExpandedNodeId.ToNodeId(
                    Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, namespaces));
            }
            if (softwareAdmin)
            {
                roles.Add(ExpandedNodeId.ToNodeId(
                    SoftwareUpdateAdminRole, namespaces));
            }
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(i => i.TokenType).Returns(anonymous ? UserTokenType.Anonymous : UserTokenType.UserName);
            identity.SetupGet(i => i.GrantedRoleIds).Returns(roles.ToArray().ToArrayOf());
            var session = new Mock<Opc.Ua.Server.ISession>();
            session.SetupGet(s => s.Id).Returns(missingSessionId ? NodeId.Null : new NodeId("session", 0));
            session.SetupGet(s => s.EffectiveIdentity).Returns(missingUser ? null! : identity.Object);
            var channel = new SecureChannelContext(
                "channel",
                new EndpointDescription { SecurityMode = mode, SecurityPolicyUri = policy },
                RequestEncoding.Binary,
                certificate ? new byte[] { 1, 2, 3 } : null);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object);
            return new SessionSystemContext(operation, NUnitTelemetryContext.Create())
            {
                NamespaceUris = namespaces
            };
        }

        private static (SessionSystemContext Context, DeviceRegistrarState Registrar) CreateRegistrar()
        {
            SessionSystemContext context = CreateContext(registrarAdmin: true, softwareAdmin: true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(context.Telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            messageContext.NamespaceUris.GetIndexOrAppend(RolesNamespace);
            context.NamespaceUris = messageContext.NamespaceUris;
            context.ServerUris = messageContext.ServerUris;
            context.EncodeableFactory = messageContext.Factory;
            context.TypeTable = new TypeTable(messageContext.NamespaceUris);
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUaOnboarding(context);
            return (context, nodes.OfType<DeviceRegistrarState>().Single());
        }

        private static ApplicationDescription CreateEndpoint()
        {
            return new ApplicationDescription
            {
                ApplicationUri = "urn:device:dca",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://device:4840"]
            };
        }

        private static ApplicationRecordDataType CreateApplication()
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = "urn:device:application",
                ApplicationNames = [new LocalizedText("Application")],
                ProductUri = "urn:product:test",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://device:4841"]
            };
        }

        private sealed class Harness
        {
            public Harness()
            {
                Lifecycle.As<IDeviceRegistrarApplicationScopeLifecycle>()
                    .Setup(scope => scope.RequireManagedApplicationAllowedAsync(
                        It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                Lifecycle.Setup(l => l.FindDeviceByCertificateAsync(
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>())).ReturnsAsync(Device);
                Lifecycle.Setup(l => l.UpdateSoftwareStatusAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                Applications.Setup(a => a.CanManageAsync(
                    It.IsAny<string>(), It.IsAny<ApplicationRecordDataType>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(true);
                Database.Setup(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()))
                    .Returns(ApplicationId);
                Authorization = new DeviceRegistrarAuthorization(Lifecycle.Object, Applications.Object);
                Runtime = new DeviceRegistrarRuntime(
                    Database.Object, Lifecycle.Object, Authorization, Push.Object, Managers.Object, Audit.Object)
                {
                    SoftwareUpdateAdminRoleId = SoftwareUpdateAdminRole
                };
            }

            public Mock<IApplicationsDatabase> Database { get; } = new();
            public Mock<IDeviceRegistrarLifecycle> Lifecycle { get; } = new();
            public Mock<IDeviceRegistrarApplicationAuthorization> Applications { get; } = new();
            public Mock<IDeviceRegistrarPushScheduler> Push { get; } = new();
            public Mock<IDeviceRegistrarManagerProvider> Managers { get; } = new();
            public Mock<IDeviceRegistrarAuditReporter> Audit { get; } = new();
            public DeviceRegistrarAuthorization Authorization { get; }
            public DeviceRegistrarRuntime Runtime { get; }
        }

        private const string Device = "urn:device:test";
        private const string RolesNamespace = "urn:opcfoundation:tests:registrar-roles";
        private static readonly ExpandedNodeId SoftwareUpdateAdminRole =
            new("SoftwareUpdateAdmin", RolesNamespace);
        private static readonly NodeId ApplicationId = new("shared-application", 2);
    }
}
