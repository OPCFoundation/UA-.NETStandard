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
    /// Tests selected identity candidates independently of ticket and certificate validation implementations.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvideIdentitiesRuntimeTests
    {
        /// <summary>
        /// The external certificate handoff enters the same candidate, software and Part 12 issuance gates.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalHandoffUsesActualChannelAndSharedLifecycleWithoutTicketSelection(bool update)
        {
            var harness = new Harness(update);
            var external = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
            external.Setup(value => value.EvaluateAsync(SelectedCertificate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeviceRegistrarExternalAuthenticationResult(Device,
                    new DeviceRegistrarOnboardingDecision(harness.Application, update, harness.Manager)));
            var authorization = new DeviceRegistrarAuthorization(
                harness.Lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>(),
                harness.Lifecycle, harness.Database.Object, external.Object);
            SessionSystemContext context = CreateContext(SelectedCertificate);

            Assert.That(await authorization.RequireDcaAsync(context).ConfigureAwait(false), Is.EqualTo(Device));
            DeviceRegistrarIdentityCandidate? candidate = await harness.Lifecycle
                .FindCandidateByCertificateAsync(SelectedCertificate).ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(SharedId));
            Assert.That(candidate.CertificateIssuanceAllowed, Is.EqualTo(!update));
            Assert.That(await harness.Lifecycle.FindDeviceByCertificateAsync(SelectedCertificate)
                .ConfigureAwait(false), Is.Null, "The external certificate is evidence, not a forged issuance.");
            if (update)
            {
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await authorization.RequireDcaCertificateIssuanceAsync(context, SharedId).ConfigureAwait(false));
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
                await harness.Lifecycle.UpdateSoftwareStatusAsync(Device, true, "approved").ConfigureAwait(false);
            }
            await authorization.RequireDcaCertificateIssuanceAsync(context, SharedId).ConfigureAwait(false);
            ArrayOf<DeviceRegistrarApplicationBinding> applications =
                await authorization.GetChannelApplicationsAsync(context).ConfigureAwait(false);
            Assert.That(applications.Count, Is.EqualTo(1));
            Assert.That(applications[0].ApplicationId, Is.EqualTo(SharedId));
            harness.Selector.VerifyNoOtherCalls();
            external.Verify(value => value.EvaluateAsync(
                SelectedCertificate, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// No unprotected, untrusted or explicitly rejected external peer can create registrar state.
        /// </summary>
        [TestCase("unprotected")]
        [TestCase("untrusted")]
        [TestCase("denied")]
        public async Task ExternalHandoffRejectsBeforeRegistrationWhenEvidenceOrApprovalIsMissing(string failure)
        {
            var harness = new Harness();
            var external = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
            if (failure == "denied")
            {
                external.Setup(value => value.EvaluateAsync(SelectedCertificate, It.IsAny<CancellationToken>()))
                    .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            }
            else if (failure == "untrusted")
            {
                external.Setup(value => value.EvaluateAsync(SelectedCertificate, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((DeviceRegistrarExternalAuthenticationResult?)null);
            }
            var authorization = new DeviceRegistrarAuthorization(
                harness.Lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>(),
                harness.Lifecycle, harness.Database.Object, external.Object);
            SessionSystemContext context = CreateContext(SelectedCertificate,
                failure == "unprotected" ? MessageSecurityMode.None : MessageSecurityMode.SignAndEncrypt);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireDcaAsync(context).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(failure == "unprotected"
                ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadUserAccessDenied));
            Assert.That(await harness.Lifecycle.FindCandidateByCertificateAsync(SelectedCertificate)
                .ConfigureAwait(false), Is.Null);
            harness.Database.Verify(value => value.RegisterApplication(It.IsAny<ApplicationRecordDataType>()),
                Times.Never);
            if (failure == "unprotected")
            {
                external.VerifyNoOtherCalls();
            }
        }

        /// <summary>
        /// An external approval cannot adopt an unowned or differently owned existing application record.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalHandoffCannotTakeOverExistingSharedApplication(bool boundToAnotherDevice)
        {
            var harness = new Harness();
            if (boundToAnotherDevice)
            {
                await harness.Lifecycle.RecordCandidateAsync(
                    "urn:device:other-owner", SharedId, OtherCertificate, false).ConfigureAwait(false);
            }
            harness.Database.Setup(value => value.FindApplications(harness.Application.ApplicationUri!))
                .Returns([new ApplicationRecordDataType { ApplicationId = SharedId }]);
            var external = new Mock<IDeviceRegistrarExternalAuthenticationProvider>(MockBehavior.Strict);
            external.Setup(value => value.EvaluateAsync(SelectedCertificate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeviceRegistrarExternalAuthenticationResult(Device,
                    new DeviceRegistrarOnboardingDecision(harness.Application, false, null)));
            var authorization = new DeviceRegistrarAuthorization(
                harness.Lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>(),
                harness.Lifecycle, harness.Database.Object, external.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireDcaAsync(CreateContext(SelectedCertificate)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(await harness.Lifecycle.FindCandidateByCertificateAsync(SelectedCertificate)
                .ConfigureAwait(false), Is.Null);
            harness.Database.Verify(value => value.RegisterApplication(It.IsAny<ApplicationRecordDataType>()),
                Times.Never);
            harness.Database.Verify(value => value.UpdateApplication(It.IsAny<ApplicationRecordDataType>()),
                Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectionReturnsSharedIdAndExplicitSoftwarePolicyWithoutGrantingCallerPrivilege(bool update)
        {
            var harness = new Harness(update);
            SessionSystemContext caller = CreateContext(OtherCertificate);
            DeviceRegistrarIdentityResult result = await harness.Runtime.ProvideIdentitiesAsync(
                caller, [SelectedCertificate], [], ["ticket"]).ConfigureAwait(false);
            Assert.That(result.SelectedIdentity, Is.EqualTo(SelectedCertificate));
            Assert.That(result.ApplicationId, Is.EqualTo(SharedId));
            Assert.That(result.MatchingTicket, Is.TypeOf<DeviceIdentityTicketType>());
            Assert.That(result.SoftwareUpdateManager, Is.SameAs(harness.Manager));
            harness.Database.Verify(d => d.RegisterApplication(harness.Application), Times.Once);
            harness.Audit.Verify(a => a.ReportIdentityAcceptedAsync(
                caller,
                It.Is<OnboardingIdentitySelection>(s => s.SelectedIdentity == SelectedCertificate),
                CancellationToken.None), Times.Once);
            Assert.That(
                await harness.Lifecycle.FindDeviceByCertificateAsync(SelectedCertificate).ConfigureAwait(false),
                Is.Null, "Selecting a certificate must not install an approved DCA binding.");
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaAsync(caller).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            harness.Selector.Verify(s => s.SelectAsync(
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Once candidate persistence succeeds, an audit failure is visible and retry can use the same shared ID.
        /// </summary>
        [Test]
        public async Task IdentityAuditFailurePropagatesWithoutLosingCandidateOrChangingApplicationId()
        {
            var harness = new Harness();
            var failure = new InvalidOperationException("audit unavailable");
            harness.Audit.Setup(a => a.ReportIdentityAcceptedAsync(
                It.IsAny<ISystemContext>(), It.IsAny<OnboardingIdentitySelection>(), CancellationToken.None))
                .Throws(failure);

            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            DeviceRegistrarIdentityCandidate? candidate = await harness.Lifecycle.FindCandidateByCertificateAsync(
                SelectedCertificate).ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(SharedId));
            harness.Audit.Reset();
            DeviceRegistrarIdentityResult retried = await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            Assert.That(retried.ApplicationId, Is.EqualTo(SharedId));
            harness.Database.Verify(d => d.RegisterApplication(harness.Application), Times.Once);
            harness.Database.Verify(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()), Times.Once);
            harness.Audit.Verify(a => a.ReportIdentityAcceptedAsync(
                It.IsAny<ISystemContext>(), It.IsAny<OnboardingIdentitySelection>(), CancellationToken.None),
                Times.Once);
        }

        [Test]
        public async Task MatchingAuthenticatedChannelProvesSelectedKeyWithoutRequiringWellKnownRole()
        {
            var harness = new Harness();
            await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            SessionSystemContext provenDevice = CreateContext(SelectedCertificate);
            string device = await harness.Authorization.RequireDcaAsync(provenDevice).ConfigureAwait(false);
            Assert.That(device, Is.EqualTo(Device));
            await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                provenDevice, SharedId).ConfigureAwait(false);
        }

        [Test]
        public async Task CertificateIssuanceIndependentlyRequiresExactIdProofAndCurrentSoftwareApproval()
        {
            var harness = new Harness(requiresUpdate: true);
            await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            ServiceResultException? noProof = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                    CreateContext(OtherCertificate), SharedId).ConfigureAwait(false));
            ServiceResultException? wrongId = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                    CreateContext(SelectedCertificate), new NodeId("other", 2)).ConfigureAwait(false));
            ServiceResultException? oldSoftware = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                    CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false));
            Assert.That(noProof!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(wrongId!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(oldSoftware!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            await harness.Lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false);
            await harness.Lifecycle.UpdateSoftwareStatusAsync(Device, false, "new-required").ConfigureAwait(false);
            ServiceResultException? revokedSoftware = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                    CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false));
            Assert.That(revokedSoftware!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [Test]
        public async Task SubsequentlyApprovedDcaCertificateCanProvePossessionOfSameDevice()
        {
            var harness = new Harness();
            await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            await harness.Lifecycle.RegisterDeviceAsync(Device, SharedId, OtherCertificate).ConfigureAwait(false);
            await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                CreateContext(OtherCertificate), SharedId).ConfigureAwait(false);
            Assert.That(
                await harness.Authorization.RequireDcaAsync(CreateContext(OtherCertificate)).ConfigureAwait(false),
                Is.EqualTo(Device));
        }

        [Test]
        public async Task ManagedCertificateIssuanceRechecksPerApplicationPermissionAfterRegistration()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            var application = new ApplicationRecordDataType
            {
                ApplicationId = new NodeId("managed", 2),
                ApplicationUri = "urn:managed:application"
            };
            await lifecycle.AssociateApplicationAsync(
                Device, application.ApplicationId, string.Empty).ConfigureAwait(false);
            var policy = new Mock<IDeviceRegistrarApplicationAuthorization>();
            policy.SetupSequence(p => p.CanManageAsync(
                Device, application, string.Empty, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true).ReturnsAsync(false);
            var authorization = new DeviceRegistrarAuthorization(lifecycle, policy.Object);
            SessionSystemContext context = CreateContext(SelectedCertificate);
            await authorization.RequireManagedCertificateIssuanceAsync(
                context, application, string.Empty).ConfigureAwait(false);
            ServiceResultException? revoked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireManagedCertificateIssuanceAsync(
                    context, application, string.Empty).ConfigureAwait(false));
            Assert.That(revoked!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            policy.Verify(p => p.CanManageAsync(
                Device, application, string.Empty, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// An allow-all host policy cannot replace a registered managed relation, its owner, or its protocol.
        /// </summary>
        [TestCase("unregistered")]
        [TestCase("otherDevice")]
        [TestCase("dca")]
        [TestCase("protocol")]
        public async Task ManagedIssuanceRequiresExactPersistedApplicationRelation(string relation)
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            var application = new ApplicationRecordDataType
            {
                ApplicationId = relation == "dca" ? SharedId : new NodeId("managed", 2),
                ApplicationUri = "urn:managed:application"
            };
            if (relation == "otherDevice")
            {
                await lifecycle.RecordCandidateAsync(
                    "urn:device:other", new NodeId("other-dca", 2), OtherCertificate, false).ConfigureAwait(false);
                await lifecycle.AssociateApplicationAsync(
                    "urn:device:other", application.ApplicationId, "urn:protocol:expected").ConfigureAwait(false);
            }
            else if (relation == "protocol")
            {
                await lifecycle.AssociateApplicationAsync(
                    Device, application.ApplicationId, "urn:protocol:original").ConfigureAwait(false);
            }
            var policy = new Mock<IDeviceRegistrarApplicationAuthorization>(MockBehavior.Strict);
            var authorization = new DeviceRegistrarAuthorization(lifecycle, policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireManagedCertificateIssuanceAsync(
                    CreateContext(SelectedCertificate), application, "urn:protocol:expected").ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Only applications outside the onboarding lifecycle may use the host's existing GDS-role checks.
        /// </summary>
        [Test]
        public async Task KnownApplicationWithWrongChannelCannotUseStandardGdsAuthorizationFallback()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            var policy = new Mock<IDeviceRegistrarApplicationAuthorization>(MockBehavior.Strict);
            var authorization = new DeviceRegistrarAuthorization(lifecycle, policy.Object);
            var application = new ApplicationRecordDataType { ApplicationId = SharedId };

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.AuthorizeApplicationAsync(
                    CreateContext(OtherCertificate), application).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await authorization.AuthorizeApplicationAsync(
                CreateContext(SelectedCertificate), application).ConfigureAwait(false), Is.True);

            application.ApplicationId = new NodeId("outside-onboarding", 2);
            Assert.That(await authorization.AuthorizeApplicationAsync(
                CreateContext(OtherCertificate), application).ConfigureAwait(false), Is.False);
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A channel sees only its device's current relations, and software policy remains authoritative.
        /// </summary>
        [Test]
        public async Task ChannelApplicationLookupIsDeviceScopedAndRechecksSoftwareApproval()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, true).ConfigureAwait(false);
            var appId = new NodeId("managed", 2);
            await lifecycle.AssociateApplicationAsync(Device, appId, "urn:protocol:expected").ConfigureAwait(false);
            await lifecycle.RecordCandidateAsync(
                "urn:device:other", new NodeId("other-dca", 2), OtherCertificate, false).ConfigureAwait(false);
            var authorization = new DeviceRegistrarAuthorization(
                lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>());

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.GetChannelApplicationsAsync(CreateContext(SelectedCertificate))
                    .ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            await lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            ArrayOf<DeviceRegistrarApplicationBinding> relations = await authorization.GetChannelApplicationsAsync(
                CreateContext(SelectedCertificate)).ConfigureAwait(false);
            Assert.That(relations.ToArray(), Is.EqualTo(new[]
            {
                new DeviceRegistrarApplicationBinding(Device, SharedId, true, string.Empty),
                new DeviceRegistrarApplicationBinding(Device, appId, false, "urn:protocol:expected")
            }));
            await lifecycle.UpdateSoftwareStatusAsync(Device, false, "rejected").ConfigureAwait(false);
            ServiceResultException? revoked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.GetChannelApplicationsAsync(CreateContext(SelectedCertificate))
                    .ConfigureAwait(false));
            Assert.That(revoked!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            ArrayOf<DeviceRegistrarApplicationBinding> unknown = await authorization.GetChannelApplicationsAsync(
                CreateContext(ByteString.From(new byte[] { 9, 9, 9 }))).ConfigureAwait(false);
            Assert.That(unknown.IsEmpty, Is.True);
        }

        /// <summary>
        /// Issued certificate material cannot gain DCA privilege until a later authenticated channel proves its key.
        /// </summary>
        [Test]
        public async Task InitialDcaIssuanceBindsOnlyTheAuthorizedDeviceAndRequiresSubsequentChannelProof()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            var authorization = new DeviceRegistrarAuthorization(
                lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>());
            var application = new ApplicationRecordDataType { ApplicationId = SharedId };

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RecordCertificateIssuedAsync(
                    CreateContext(OtherCertificate), application, OtherCertificate).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(OtherCertificate).ConfigureAwait(false), Is.Null);

            await authorization.RecordCertificateIssuedAsync(
                CreateContext(SelectedCertificate), application, OtherCertificate).ConfigureAwait(false);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(OtherCertificate).ConfigureAwait(false), Is.Null);
            Assert.That((await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!.PendingDcaCertificate,
                Is.EqualTo(OtherCertificate));
            Assert.That(await authorization.RequireDcaAsync(CreateContext(OtherCertificate)).ConfigureAwait(false),
                Is.EqualTo(Device));
            Assert.That(await authorization.AuthorizeApplicationAsync(
                CreateContext(OtherCertificate), application).ConfigureAwait(false), Is.True);
            Assert.That((await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!.DcaApplicationId,
                Is.EqualTo(SharedId));
        }

        /// <summary>
        /// A new delivery cannot retire the active key until a subsequent authenticated channel proves installation.
        /// </summary>
        [Test]
        public async Task ReplacementDcaIssuanceRetiresActiveCertificateOnlyAfterAuthenticatedInstallationProof()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            await lifecycle.RegisterDeviceAsync(Device, SharedId, OtherCertificate).ConfigureAwait(false);
            var authorization = new DeviceRegistrarAuthorization(
                lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>());
            var application = new ApplicationRecordDataType { ApplicationId = SharedId };
            ByteString replacement = ByteString.From(new byte[] { 7, 8, 9 });

            await authorization.RecordCertificateIssuedAsync(
                CreateContext(OtherCertificate), application, replacement).ConfigureAwait(false);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false), Is.Null);
            Assert.That(await authorization.RequireDcaAsync(CreateContext(OtherCertificate)).ConfigureAwait(false),
                Is.EqualTo(Device));
            await authorization.RequireDcaCertificateIssuanceAsync(CreateContext(OtherCertificate), SharedId)
                .ConfigureAwait(false);
            DeviceRegistrarDevice? unconfirmed = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(unconfirmed!.DcaCertificate, Is.EqualTo(OtherCertificate));
            Assert.That(unconfirmed.PendingDcaCertificate, Is.EqualTo(replacement));
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(OtherCertificate).ConfigureAwait(false),
                Is.EqualTo(Device));
            await authorization.RecordCertificateIssuedAsync(
                CreateContext(OtherCertificate), application, replacement).ConfigureAwait(false);
            ServiceResultException? pending = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RecordCertificateIssuedAsync(
                    CreateContext(OtherCertificate), application, ByteString.From(new byte[] { 8, 8, 8 }))
                    .ConfigureAwait(false));
            Assert.That(pending!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(await authorization.RequireDcaAsync(CreateContext(replacement)).ConfigureAwait(false),
                Is.EqualTo(Device));
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(OtherCertificate).ConfigureAwait(false), Is.Null);
            Assert.That((await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!.PendingDcaCertificate.IsEmpty,
                Is.True);
            ServiceResultException? retired = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireDcaAsync(CreateContext(OtherCertificate)).ConfigureAwait(false));
            Assert.That(retired!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Pending certificate bytes alone cannot confirm installation without a secure, authenticated session.
        /// </summary>
        [TestCase(MessageSecurityMode.None, false, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, true, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, false, true)]
        public async Task PendingCredentialCannotBeConfirmedWithoutAuthenticatedSecureChannelEvidence(
            MessageSecurityMode mode,
            bool missingSession,
            bool missingCertificate)
        {
            StatusCode expectedStatus = missingSession
                ? StatusCodes.BadUserAccessDenied
                : StatusCodes.BadSecurityModeInsufficient;
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            await lifecycle.RegisterDeviceAsync(Device, SharedId, OtherCertificate).ConfigureAwait(false);
            ByteString replacement = ByteString.From(new byte[] { 7, 8, 9 });
            await lifecycle.RecordIssuedDcaCertificateAsync(Device, SharedId, replacement).ConfigureAwait(false);
            var authorization = new DeviceRegistrarAuthorization(
                lifecycle, Mock.Of<IDeviceRegistrarApplicationAuthorization>());
            SessionSystemContext context = CreateContext(
                missingCertificate ? ByteString.Empty : replacement, mode, missingSession);

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await authorization.RequireDcaAsync(context).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(expectedStatus));
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.DcaCertificate, Is.EqualTo(OtherCertificate));
            Assert.That(state.PendingDcaCertificate, Is.EqualTo(replacement));
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task RepeatedSelectionPreservesSharedIdSoftwareStatusAndApplicationRelations()
        {
            var harness = new Harness(requiresUpdate: true);
            DeviceRegistrarIdentityResult first = await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            await harness.Lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            var managedId = new NodeId("managed", 2);
            await harness.Lifecycle.AssociateApplicationAsync(Device, managedId, string.Empty).ConfigureAwait(false);
            DeviceRegistrarIdentityResult second = await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            Assert.That(first.ApplicationId, Is.EqualTo(second.ApplicationId));
            harness.Database.Verify(d => d.RegisterApplication(harness.Application), Times.Once);
            harness.Database.Verify(d => d.UpdateApplication(
                It.Is<ApplicationRecordDataType>(a => a.ApplicationId == SharedId)), Times.Once);
            DeviceRegistrarDevice? state = await harness.Lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.SoftwareStatus, Is.True);
            Assert.That(state.SoftwareRevision, Is.EqualTo("installed"));
            Assert.That(state.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Applications[0].ApplicationId, Is.EqualTo(managedId));
            await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejecting another device's shared DCA identifier cannot modify that application's registration.
        /// </summary>
        [Test]
        public async Task ConflictingCandidateCannotUpdateAnotherDevicesSharedApplication()
        {
            var harness = new Harness();
            const string owner = "urn:device:existing-owner";
            await harness.Lifecycle.RecordCandidateAsync(
                owner, SharedId, OtherCertificate, false).ConfigureAwait(false);
            var registered = new ApplicationRecordDataType
            {
                ApplicationId = SharedId,
                ApplicationUri = harness.Application.ApplicationUri
            };
            harness.Database.Setup(d => d.FindApplications(
                It.Is<string>(uri => uri == harness.Application.ApplicationUri)))
                .Returns([registered]);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(SelectedCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            harness.Database.Verify(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
            harness.Database.Verify(d => d.RegisterApplication(It.IsAny<ApplicationRecordDataType>()), Times.Never);
            harness.Audit.VerifyNoOtherCalls();
            Assert.That(await harness.Lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
            DeviceRegistrarIdentityCandidate? retained = await harness.Lifecycle.FindCandidateByCertificateAsync(
                OtherCertificate).ConfigureAwait(false);
            Assert.That(retained!.ProductInstanceUri, Is.EqualTo(owner));
            Assert.That(retained.ApplicationId, Is.EqualTo(SharedId));
        }

        [TestCase(MessageSecurityMode.None, true, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, false, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, true, true)]
        public async Task SelectionRejectsUnauthenticatedChannelOrMissingSessionBeforeCallingSelector(
            MessageSecurityMode mode, bool certificate, bool missingSession)
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(
                certificate ? OtherCertificate : ByteString.Empty, mode, missingSession);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    context, [SelectedCertificate], [], []).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(missingSession
                ? StatusCodes.BadUserAccessDenied : StatusCodes.BadSecurityModeInsufficient));
            harness.Selector.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
        }

        [Test]
        public async Task CompatibilityConstructorFailsClosedWhenSelectorIsNotConfigured()
        {
            var harness = new Harness();
            var runtime = new DeviceRegistrarRuntime(
                harness.Database.Object, harness.Lifecycle, harness.Authorization,
                new Mock<IDeviceRegistrarPushScheduler>().Object,
                new Mock<IDeviceRegistrarManagerProvider>().Object,
                new Mock<IDeviceRegistrarAuditReporter>().Object);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            harness.Database.VerifyNoOtherCalls();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void IdentityConstructorRequiresEveryExplicitProvider(int missing)
        {
            var harness = new Harness();
            Assert.Throws<ArgumentNullException>(() => new DeviceRegistrarRuntime(
                harness.Database.Object, harness.Lifecycle, harness.Authorization,
                new Mock<IDeviceRegistrarPushScheduler>().Object,
                new Mock<IDeviceRegistrarManagerProvider>().Object,
                new Mock<IDeviceRegistrarAuditReporter>().Object,
                missing == 0 ? null! : harness.Selector.Object,
                missing == 1 ? null! : harness.Lifecycle,
                missing == 2 ? null! : harness.Policy.Object));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NoUsableIdentitiesReturnsSelectorBadCertificateInvalidWithoutPolicyOrDatabaseWork(
            bool emptyCertificate)
        {
            var harness = new Harness();
            ArrayOf<ByteString> identities = emptyCertificate ? [ByteString.Empty] : [];
            harness.Selector.Setup(s => s.SelectAsync(
                identities, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadCertificateInvalid));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), identities, [], []).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            harness.Selector.Verify(s => s.SelectAsync(
                identities, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            harness.Policy.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
        }

        [Test]
        public async Task ValidSelectedIdentitySurvivesAdjacentEmptyAndMalformedEntries()
        {
            var harness = new Harness();
            ByteString malformed = ByteString.From(new byte[] { 0x30, 0xFF });
            ArrayOf<ByteString> identities =
            [
                ByteString.Empty,
                malformed,
                SelectedCertificate,
                ByteString.Empty
            ];
            DeviceRegistrarIdentityResult result = await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), identities, [], []).ConfigureAwait(false);
            Assert.That(result.SelectedIdentity, Is.EqualTo(SelectedCertificate));
            Assert.That(result.ApplicationId, Is.EqualTo(SharedId));
            harness.Selector.Verify(s => s.SelectAsync(
                identities, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<string>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            harness.Policy.Verify(p => p.EvaluateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()), Times.Once);
            harness.Database.Verify(d => d.RegisterApplication(harness.Application), Times.Once);
        }

        [Test]
        public async Task SelectorCannotSubstituteUnsubmittedIdentity()
        {
            var harness = new Harness();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [OtherCertificate], [], []).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            harness.Policy.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
        }

        [Test]
        public async Task OnboardingPolicyDenialPreventsSharedRegistrationAndCandidateCreation()
        {
            var harness = new Harness();
            harness.Policy.Setup(p => p.EvaluateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadRequestNotAllowed));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            harness.Database.VerifyNoOtherCalls();
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task SoftwarePolicyMustExplicitlySupplyManagerOnlyWhenUpdateIsRequired(bool update, bool manager)
        {
            var harness = new Harness();
            harness.Policy.Setup(p => p.EvaluateAsync(
                It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeviceRegistrarOnboardingDecision(
                    harness.Application, update, manager ? new ManagerDescription() : null));
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            harness.Database.VerifyNoOtherCalls();
        }

        [TestCase("selector")]
        [TestCase("policy")]
        [TestCase("database")]
        [TestCase("candidate")]
        public async Task ProviderFailuresPropagateWithoutGrantingDcaPrivilege(string provider)
        {
            var harness = new Harness();
            var failure = new InvalidOperationException("Provider unavailable.");
            var candidates = new Mock<IDeviceRegistrarIdentityLifecycle>();
            switch (provider)
            {
                case "selector":
                    harness.Selector.Setup(s => s.SelectAsync(
                        It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                        It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>())).Throws(failure);
                    break;
                case "policy":
                    harness.Policy.Setup(p => p.EvaluateAsync(
                        It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>())).Throws(failure);
                    break;
                case "database":
                    harness.Database.Setup(d => d.RegisterApplication(
                        It.IsAny<ApplicationRecordDataType>())).Throws(failure);
                    break;
                default:
                    candidates.Setup(c => c.RecordCandidateAsync(
                        It.IsAny<string>(), It.IsAny<NodeId>(), It.IsAny<ByteString>(),
                        It.IsAny<bool>(), It.IsAny<CancellationToken>())).Throws(failure);
                    harness.Runtime = harness.CreateRuntime(candidates.Object);
                    break;
            }
            InvalidOperationException? actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaAsync(
                    CreateContext(SelectedCertificate)).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task CancelledSelectionDoesNotCallAnyProvider()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await harness.Runtime.ProvideIdentitiesAsync(
                    CreateContext(OtherCertificate), [SelectedCertificate], [], [], cancellation.Token)
                    .ConfigureAwait(false));
            harness.Selector.VerifyNoOtherCalls();
            harness.Policy.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
        }

        [Test]
        public async Task SelectorReceivesCancellationAndCannotCommitCandidateAfterCancellation()
        {
            var harness = new Harness();
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Selector.Setup(s => s.SelectAsync(
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<ArrayOf<string>>(), cancellation.Token))
                .Returns(async () =>
                {
                    started.SetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellation.Token).ConfigureAwait(false);
                    throw new InvalidOperationException("Cancellation was ignored.");
                });
            Task operation = harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], [], cancellation.Token).AsTask();
            await started.Task.ConfigureAwait(false);
            cancellation.Cancel();
            try
            {
                await operation.ConfigureAwait(false);
                Assert.Fail("Selection ignored cancellation.");
            }
            catch (OperationCanceledException error)
            {
                Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            harness.Policy.VerifyNoOtherCalls();
            harness.Database.VerifyNoOtherCalls();
            Assert.That(await harness.Lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task CandidateRefreshDoesNotGrantPrivilegeToOldSelectedCertificate()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(
                Device, SharedId, SelectedCertificate, true).ConfigureAwait(false);
            await lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            await lifecycle.RecordCandidateAsync(Device, SharedId, OtherCertificate, true).ConfigureAwait(false);
            Assert.That(
                await lifecycle.FindCandidateByCertificateAsync(SelectedCertificate).ConfigureAwait(false), Is.Null);
            DeviceRegistrarIdentityCandidate? candidate = await lifecycle.FindCandidateByCertificateAsync(
                OtherCertificate).ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(SharedId));
            Assert.That(candidate.CertificateIssuanceAllowed, Is.True);
            Assert.That(
                await lifecycle.FindDeviceByCertificateAsync(OtherCertificate).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task CandidateCannotStealCertificateOrSharedIdentifierFromAnotherDevice()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            await lifecycle.RecordCandidateAsync(
                Device, SharedId, SelectedCertificate, false).ConfigureAwait(false);
            ServiceResultException? certificate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RecordCandidateAsync(
                    "urn:other:device", new NodeId("other", 2), SelectedCertificate, false).ConfigureAwait(false));
            ServiceResultException? id = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RecordCandidateAsync(
                    "urn:other:device", SharedId, OtherCertificate, false).ConfigureAwait(false));
            Assert.That(certificate!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(id!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(await lifecycle.FindDeviceAsync("urn:other:device").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task CancelledCandidateRegistrationCannotCreateDeviceOrPrivilege()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await lifecycle.RecordCandidateAsync(
                    Device, SharedId, SelectedCertificate, false, cancellation.Token).ConfigureAwait(false));
            Assert.That(await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
            Assert.That(
                await lifecycle.FindCandidateByCertificateAsync(SelectedCertificate).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task SoftwareUpdateRequirementAfterInitiallyApprovedPolicyRevokesIssuance()
        {
            var harness = new Harness();
            await harness.Runtime.ProvideIdentitiesAsync(
                CreateContext(OtherCertificate), [SelectedCertificate], [], []).ConfigureAwait(false);
            await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false);
            await harness.Lifecycle.UpdateSoftwareStatusAsync(Device, false, "new-required").ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Authorization.RequireDcaCertificateIssuanceAsync(
                    CreateContext(SelectedCertificate), SharedId).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GeneratedBindingReturnsNativeCertificateTicketSharedIdAndOptionalManager(bool update)
        {
            var harness = new Harness(update);
            SessionSystemContext context = CreateContext(OtherCertificate);
            DeviceRegistrarState registrar = new NodeStateCollection().AddOpcUaOnboarding(context)
                .OfType<DeviceRegistrarState>().Single();
            registrar.BindToRuntime(harness.Runtime);
            MethodState method = registrar.ProvideIdentities!;
            var outputs = new List<Variant> { default, default, default, default };
            ArrayOf<string> tickets = ["signed-ticket"];
            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, registrar.NodeId,
                [
                    Variant.From(new ByteString[] { SelectedCertificate }.ToArrayOf()),
                    Variant.From(ArrayOf<ByteString>.Empty),
                    Variant.From(tickets)
                ], outputs, default).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(outputs[0].TryGetValue(out ByteString certificate), Is.True);
            Assert.That(certificate, Is.EqualTo(SelectedCertificate));
            Assert.That(outputs[1].TryGetStructure<BaseTicketType>(out BaseTicketType? ticket), Is.True);
            Assert.That(ticket, Is.TypeOf<DeviceIdentityTicketType>());
            Assert.That(outputs[2].TryGetValue(out NodeId applicationId), Is.True);
            Assert.That(applicationId, Is.EqualTo(SharedId));
            if (update)
            {
                Assert.That(outputs[3].TryGetStructure<ManagerDescription>(out ManagerDescription? manager), Is.True);
                ManagerDescription selectedManager = manager ?? throw new AssertionException("Missing manager.");
                Assert.That(selectedManager.PurposeUri, Is.EqualTo(harness.Manager!.PurposeUri));
            }
            else
            {
                Assert.That(outputs[3].TryGetValue(out ExtensionObject noManager), Is.True);
                Assert.That(noManager.IsNull, Is.True);
            }
        }

        [TestCase(0, 4)]
        [TestCase(4, 4)]
        [TestCase(3, 4)]
        public async Task GeneratedBindingRejectsMalformedIdentityArguments(int count, int outputCount)
        {
            StatusCode expected = count switch
            {
                0 => StatusCodes.BadArgumentsMissing,
                4 => StatusCodes.BadTooManyArguments,
                3 => StatusCodes.BadTypeMismatch,
                _ => throw new ArgumentOutOfRangeException(nameof(count))
            };
            SessionSystemContext context = CreateContext(OtherCertificate);
            DeviceRegistrarState registrar = new NodeStateCollection().AddOpcUaOnboarding(context)
                .OfType<DeviceRegistrarState>().Single();
            var runtime = new Mock<IDeviceRegistrarRuntime>(MockBehavior.Strict);
            registrar.BindToRuntime(runtime.Object);
            MethodState method = registrar.ProvideIdentities!;
            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, registrar.NodeId,
                Enumerable.Repeat(Variant.From(42), count).ToArray().ToArrayOf(),
                Enumerable.Repeat(default(Variant), outputCount).ToList(), default).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(expected));
            runtime.VerifyNoOtherCalls();
        }

        [Test]
        public async Task GeneratedBindingRejectsMissingOutputSlotsBeforeSelection()
        {
            SessionSystemContext context = CreateContext(OtherCertificate);
            DeviceRegistrarState registrar = new NodeStateCollection().AddOpcUaOnboarding(context)
                .OfType<DeviceRegistrarState>().Single();
            var runtime = new Mock<IDeviceRegistrarRuntime>(MockBehavior.Strict);
            registrar.BindToRuntime(runtime.Object);
            MethodState method = registrar.ProvideIdentities!;
            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, registrar.NodeId,
                [
                    Variant.From(new ByteString[] { SelectedCertificate }.ToArrayOf()),
                    Variant.From(ArrayOf<ByteString>.Empty),
                    Variant.From(ArrayOf<string>.Empty)
                ], [], default).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            runtime.VerifyNoOtherCalls();
        }

        [Test]
        public async Task GeneratedBindingAllowsNullOptionalTicketsAndIssuers()
        {
            var harness = new Harness();
            SessionSystemContext context = CreateContext(OtherCertificate);
            DeviceRegistrarState registrar = new NodeStateCollection().AddOpcUaOnboarding(context)
                .OfType<DeviceRegistrarState>().Single();
            registrar.BindToRuntime(harness.Runtime);
            MethodState method = registrar.ProvideIdentities!;
            ServiceResult result = await method.OnCallMethod2Async!(
                context, method, registrar.NodeId,
                [
                    Variant.From(new ByteString[] { SelectedCertificate }.ToArrayOf()),
                    default,
                    default
                ], [default, default, default, default], default).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            harness.Selector.Verify(s => s.SelectAsync(
                It.IsAny<ArrayOf<ByteString>>(),
                It.Is<ArrayOf<ByteString>>(values => values.IsNull),
                It.Is<ArrayOf<string>>(values => values.IsNull),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private static SessionSystemContext CreateContext(
            ByteString certificate,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            bool missingSession = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messages = ServiceMessageContext.Create(telemetry);
            messages.NamespaceUris.GetIndexOrAppend(Opc.Ua.Gds.Namespaces.OpcUaGds);
            messages.NamespaceUris.GetIndexOrAppend(Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(i => i.TokenType).Returns(UserTokenType.Anonymous);
            var session = new Mock<Opc.Ua.Server.ISession>();
            session.SetupGet(s => s.Id).Returns(missingSession ? NodeId.Null : new NodeId("session", 0));
            session.SetupGet(s => s.EffectiveIdentity).Returns(identity.Object);
            var channel = new SecureChannelContext(
                "channel", new EndpointDescription
                {
                    SecurityMode = mode,
                    SecurityPolicyUri = mode == MessageSecurityMode.None
                        ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256
                }, RequestEncoding.Binary, certificate.IsEmpty ? null : certificate.ToArray());
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object);
            return new SessionSystemContext(operation, telemetry)
            {
                NamespaceUris = messages.NamespaceUris,
                ServerUris = messages.ServerUris,
                EncodeableFactory = messages.Factory,
                TypeTable = new TypeTable(messages.NamespaceUris)
            };
        }

        private sealed class Harness
        {
            public Harness(bool requiresUpdate = false)
            {
                Application = new ApplicationRecordDataType
                {
                    ApplicationUri = "urn:device:dca",
                    ApplicationType = ApplicationType.Client,
                    ApplicationNames = [new LocalizedText("DCA")],
                    ProductUri = "urn:product:test"
                };
                Manager = requiresUpdate ? new ManagerDescription
                {
                    PurposeUri = "urn:explicit-software-manager",
                    EndpointUrls = ["https://updates/"]
                } : null;
                Selector.Setup(s => s.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new OnboardingIdentitySelection(
                        SelectedCertificate, new DeviceIdentityTicketType { ProductInstanceUri = Device }, Device));
                Policy.Setup(p => p.EvaluateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeviceRegistrarOnboardingDecision(Application, requiresUpdate, Manager));
                ApplicationRecordDataType? registered = null;
                Database.Setup(d => d.FindApplications(Application.ApplicationUri))
                    .Returns(() => registered == null ? Array.Empty<ApplicationRecordDataType>() : [registered]);
                Database.Setup(d => d.RegisterApplication(Application)).Callback(() =>
                {
                    registered = new ApplicationRecordDataType
                    {
                        ApplicationId = SharedId,
                        ApplicationUri = Application.ApplicationUri
                    };
                }).Returns(SharedId);
                Database.Setup(d => d.UpdateApplication(It.IsAny<ApplicationRecordDataType>())).Returns(SharedId);
                Authorization = new DeviceRegistrarAuthorization(
                    Lifecycle, new Mock<IDeviceRegistrarApplicationAuthorization>().Object);
                Runtime = CreateRuntime(Lifecycle);
            }

            public DeviceRegistrarRuntime CreateRuntime(IDeviceRegistrarIdentityLifecycle identities)
            {
                return new DeviceRegistrarRuntime(
                    Database.Object, Lifecycle, Authorization,
                    new Mock<IDeviceRegistrarPushScheduler>().Object,
                    new Mock<IDeviceRegistrarManagerProvider>().Object,
                    Audit.Object,
                    Selector.Object, identities, Policy.Object);
            }

            public Mock<IApplicationsDatabase> Database { get; } = new();
            public Mock<IOnboardingIdentitySelector> Selector { get; } = new();
            public Mock<IDeviceRegistrarOnboardingPolicy> Policy { get; } = new();
            public Mock<IDeviceRegistrarAuditReporter> Audit { get; } = new();
            public MemoryDeviceRegistrarLifecycle Lifecycle { get; } = new();
            public DeviceRegistrarAuthorization Authorization { get; }
            public ApplicationRecordDataType Application { get; }
            public ManagerDescription? Manager { get; }
            public DeviceRegistrarRuntime Runtime { get; set; }
        }

        private const string Device = "urn:device:test";
        private static readonly NodeId SharedId = new("shared-dca", 2);
        private static readonly ByteString SelectedCertificate = ByteString.From(new byte[] { 1, 2, 3 });
        private static readonly ByteString OtherCertificate = ByteString.From(new byte[] { 4, 5, 6 });
    }
}
