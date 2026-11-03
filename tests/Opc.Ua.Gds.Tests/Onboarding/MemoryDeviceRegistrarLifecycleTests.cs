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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies transient lifecycle state and identity-refresh invariants.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class MemoryDeviceRegistrarLifecycleTests
    {
        /// <summary>
        /// Singleton scope is monotonic across identity refresh, software approval and credential replacement.
        /// </summary>
        [Test]
        public async Task SingletonScopeSurvivesRefreshApprovalRotationAndInstalledCredentialProof()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var applicationId = new NodeId("dca", 2);
            ByteString replacement = ByteString.From(new byte[] { 4, 5, 6 });
            await lifecycle.RecordCandidateAsync(Device, applicationId, Certificate, false).ConfigureAwait(false);
            await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);
            await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);
            await lifecycle.RecordCandidateAsync(Device, applicationId, Certificate, false).ConfigureAwait(false);
            await lifecycle.UpdateSoftwareStatusAsync(Device, true, "approved").ConfigureAwait(false);
            await lifecycle.RegisterDeviceAsync(Device, applicationId, Certificate).ConfigureAwait(false);
            await lifecycle.RecordIssuedDcaCertificateAsync(Device, applicationId, replacement).ConfigureAwait(false);
            Assert.That(await lifecycle.ConfirmDcaCertificateAsync(replacement).ConfigureAwait(false), Is.True);

            ServiceResultException? permission = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false));
            ServiceResultException? mutation = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.AssociateApplicationAsync(
                    Device, new NodeId("other", 2), string.Empty).ConfigureAwait(false));

            Assert.That(permission!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(mutation!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.IsSingletonRestricted, Is.True);
            Assert.That(state.DcaCertificate, Is.EqualTo(replacement));
            Assert.That(state.Applications.IsEmpty, Is.True);
            Assert.That(state.SoftwareRevision, Is.EqualTo("approved"));
        }

        /// <summary>
        /// Tightening scope retains prior relations as history but rejects reuse and further associations.
        /// </summary>
        [Test]
        public async Task SingletonRestrictionRevokesExistingManagedRelationsWithoutErasingOwnership()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var applicationId = new NodeId("dca", 2);
            var managedId = new NodeId("managed", 2);
            await lifecycle.RegisterDeviceAsync(Device, applicationId, Certificate).ConfigureAwait(false);
            await lifecycle.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, managedId, string.Empty).ConfigureAwait(false);
            await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.AssociateApplicationAsync(Device, managedId, "urn:updated").ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            DeviceRegistrarApplicationBinding? binding = await lifecycle.FindApplicationAsync(managedId)
                .ConfigureAwait(false);
            Assert.That(binding!.ProductInstanceUri, Is.EqualTo(Device));
            Assert.That(binding.ProtocolUri, Is.Empty);
        }

        /// <summary>
        /// Missing identity state or cancellation never creates a new unrestricted authorization record.
        /// </summary>
        [TestCase("restrict")]
        [TestCase("require")]
        public async Task ScopeOperationsRejectUnknownDeviceAndCancellation(string operation)
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (operation == "restrict")
                {
                    await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);
                }
                else
                {
                    await lifecycle.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false);
                }
            });
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                if (operation == "restrict")
                {
                    await lifecycle.RestrictToDcaApplicationAsync(Device, cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    await lifecycle.RequireManagedApplicationAllowedAsync(Device, cancellation.Token)
                        .ConfigureAwait(false);
                }
            });
            Assert.That(await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task UnknownDeviceDoesNotAcquireSoftwareStatusOrDcaPrivilege()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            Assert.That(await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(Certificate).ConfigureAwait(false), Is.Null);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(ByteString.Empty).ConfigureAwait(false), Is.Null);
            Assert.That(await lifecycle.UpdateSoftwareStatusAsync(Device, true, "2.0").ConfigureAwait(false), Is.False);
            Assert.That(await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task IdentityRefreshPreservesSoftwareStatusAndApplicationRelations()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            var appId = new NodeId("app", 2);
            await lifecycle.RegisterDeviceAsync(Device, dcaId, Certificate).ConfigureAwait(false);
            await lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, appId, "urn:protocol:test").ConfigureAwait(false);
            ByteString replacement = ByteString.From(new byte[] { 4, 5, 6 });
            await lifecycle.RegisterDeviceAsync(Device, dcaId, replacement).ConfigureAwait(false);
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.DcaApplicationId, Is.EqualTo(dcaId));
            Assert.That(state.SoftwareStatus, Is.True);
            Assert.That(state.SoftwareRevision, Is.EqualTo("installed"));
            Assert.That(state.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Applications[0].ApplicationId, Is.EqualTo(appId));
            Assert.That(state.Applications[0].ProtocolUri, Is.EqualTo("urn:protocol:test"));
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(Certificate).ConfigureAwait(false), Is.Null);
            Assert.That(
                await lifecycle.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false),
                Is.EqualTo(Device));
        }

        [Test]
        public async Task RepeatedAssociationPreservesStatusAndDoesNotDuplicateApplication()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var appId = new NodeId("app", 2);
            await lifecycle.RegisterDeviceAsync(Device, new NodeId("dca", 2), Certificate).ConfigureAwait(false);
            await lifecycle.UpdateSoftwareStatusAsync(Device, false, "required").ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, appId, "urn:first").ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, appId, "urn:second").ConfigureAwait(false);
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.SoftwareStatus, Is.False);
            Assert.That(state.SoftwareRevision, Is.EqualTo("required"));
            Assert.That(state.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Applications[0].ProtocolUri, Is.EqualTo("urn:second"));
        }

        [Test]
        public async Task RegisterDeviceDetachesApprovedCertificateFromCallerOwnedBuffer()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            byte[] buffer = { 1, 2, 3 };
            await lifecycle.RegisterDeviceAsync(
                Device, new NodeId("dca", 2), new ByteString(buffer)).ConfigureAwait(false);
            buffer[0] = 9;
            Assert.That(
                await lifecycle.FindDeviceByCertificateAsync(Certificate).ConfigureAwait(false), Is.EqualTo(Device));
            Assert.That(
                await lifecycle.FindDeviceByCertificateAsync(new ByteString(buffer)).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ReturnedDeviceAndCandidateSnapshotsCannotMutateStoredAuthorization()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            var appId = new NodeId("app", 2);
            await lifecycle.RegisterDeviceAsync(Device, dcaId, Certificate).ConfigureAwait(false);
            await lifecycle.RecordCandidateAsync(Device, dcaId, Certificate, false).ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, appId, "urn:original").ConfigureAwait(false);
            DeviceRegistrarDevice state = (await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!;
            DeviceRegistrarIdentityCandidate candidate =
                (await lifecycle.FindCandidateByCertificateAsync(Certificate).ConfigureAwait(false))!;
            Assert.That(MemoryMarshal.TryGetArray(state.DcaCertificate.Memory,
                out ArraySegment<byte> certificateBytes), Is.True);
            Assert.That(MemoryMarshal.TryGetArray(state.Applications.Memory,
                out ArraySegment<DeviceRegistrarApplication> applications), Is.True);
            Assert.That(MemoryMarshal.TryGetArray(candidate.SelectedIdentity.Memory,
                out ArraySegment<byte> identityBytes), Is.True);

            certificateBytes.Array![certificateBytes.Offset] = 9;
            identityBytes.Array![identityBytes.Offset] = 8;
            applications.Array![applications.Offset] =
                new DeviceRegistrarApplication(new NodeId("replacement", 2), "urn:replacement");

            DeviceRegistrarDevice preserved = (await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!;
            DeviceRegistrarIdentityCandidate preservedCandidate =
                (await lifecycle.FindCandidateByCertificateAsync(Certificate).ConfigureAwait(false))!;
            Assert.That(preserved.DcaCertificate, Is.EqualTo(Certificate));
            Assert.That(preserved.Applications.Count, Is.EqualTo(1));
            Assert.That(preserved.Applications[0].ApplicationId, Is.EqualTo(appId));
            Assert.That(preserved.Applications[0].ProtocolUri, Is.EqualTo("urn:original"));
            Assert.That(preservedCandidate.SelectedIdentity, Is.EqualTo(Certificate));
            Assert.That(preservedCandidate.CertificateIssuanceAllowed, Is.True);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(Certificate).ConfigureAwait(false),
                Is.EqualTo(Device));
        }

        [Test]
        public async Task CertificateAndSharedApplicationIdentifiersCannotBeReboundToAnotherDevice()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            await lifecycle.RegisterDeviceAsync(Device, dcaId, Certificate).ConfigureAwait(false);
            ServiceResultException? duplicateCertificate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    "urn:device:other", new NodeId("other", 2), Certificate).ConfigureAwait(false));
            ServiceResultException? duplicateId = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    "urn:device:other", dcaId, ByteString.From(new byte[] { 9 })).ConfigureAwait(false));
            Assert.That(duplicateCertificate!.StatusCode, Is.EqualTo(StatusCodes.BadEntryExists));
            Assert.That(duplicateId!.StatusCode, Is.EqualTo(StatusCodes.BadEntryExists));
            Assert.That(await lifecycle.FindDeviceAsync("urn:device:other").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ManagedApplicationCannotBeStolenFromAnotherDevice()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var appId = new NodeId("app", 2);
            await lifecycle.RegisterDeviceAsync(Device, new NodeId("dca", 2), Certificate).ConfigureAwait(false);
            await lifecycle.RegisterDeviceAsync(
                "urn:device:other", new NodeId("other", 2), ByteString.From(new byte[] { 9 })).ConfigureAwait(false);
            await lifecycle.AssociateApplicationAsync(Device, appId, string.Empty).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.AssociateApplicationAsync(
                    "urn:device:other", appId, string.Empty).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            DeviceRegistrarDevice? other = await lifecycle.FindDeviceAsync("urn:device:other").ConfigureAwait(false);
            Assert.That(other!.Applications.IsEmpty, Is.True);
        }

        /// <summary>
        /// Pending issuance owns its certificate independently and cannot overlap another device's credentials.
        /// </summary>
        [Test]
        public async Task PendingCredentialHasExclusiveOwnershipAndDetachedSnapshots()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            var otherId = new NodeId("other", 2);
            ByteString otherIdentity = ByteString.From(new byte[] { 9, 9, 9 });
            byte[] pendingBytes = { 7, 7, 7 };
            ByteString pending = ByteString.From(pendingBytes.AsSpan());
            await lifecycle.RecordCandidateAsync(Device, dcaId, Certificate, false).ConfigureAwait(false);
            await lifecycle.RecordCandidateAsync("urn:device:other", otherId, otherIdentity, false)
                .ConfigureAwait(false);
            await lifecycle.RecordIssuedDcaCertificateAsync(Device, dcaId, new ByteString(pendingBytes))
                .ConfigureAwait(false);
            pendingBytes[0] = 8;
            DeviceRegistrarDevice snapshot = (await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!;
            Assert.That(snapshot.PendingDcaCertificate, Is.EqualTo(pending));
            Assert.That(MemoryMarshal.TryGetArray(snapshot.PendingDcaCertificate.Memory,
                out ArraySegment<byte> snapshotBytes), Is.True);
            snapshotBytes.Array![snapshotBytes.Offset] = 6;

            ServiceResultException? duplicate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RecordIssuedDcaCertificateAsync("urn:device:other", otherId, pending)
                    .ConfigureAwait(false));
            Assert.That(duplicate!.StatusCode, Is.EqualTo(StatusCodes.BadEntryExists));
            ServiceResultException? candidate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RecordCandidateAsync("urn:device:other", otherId, pending, false)
                    .ConfigureAwait(false));
            Assert.That(candidate!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That((await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!.PendingDcaCertificate,
                Is.EqualTo(pending));
            Assert.That(await lifecycle.ConfirmDcaCertificateAsync(otherIdentity).ConfigureAwait(false), Is.False);
            Assert.That((await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!.DcaCertificate.IsEmpty,
                Is.True);
            Assert.That(await lifecycle.ConfirmDcaCertificateAsync(pending).ConfigureAwait(false), Is.True);
            Assert.That(await lifecycle.FindDeviceByCertificateAsync(pending).ConfigureAwait(false),
                Is.EqualTo(Device));
            Assert.That((await lifecycle.FindDeviceAsync("urn:device:other").ConfigureAwait(false))!
                .DcaCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// DCA and managed-application identifiers share a database and cannot be rebound across device roles.
        /// </summary>
        [TestCase("Candidate", false)]
        [TestCase("Candidate", true)]
        [TestCase("Dca", false)]
        [TestCase("Dca", true)]
        [TestCase("Managed", false)]
        [TestCase("Managed", true)]
        public async Task SharedApplicationOwnershipCannotBeBypassedByChangingItsOnboardingRole(
            string operation, bool managedOwner)
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var sharedId = new NodeId("shared", 2);
            var ownerDca = managedOwner ? new NodeId("owner-dca", 2) : sharedId;
            const string other = "urn:device:other";
            ByteString otherCertificate = ByteString.From(new byte[] { 9, 8, 7 });
            await lifecycle.RegisterDeviceAsync(Device, ownerDca, Certificate).ConfigureAwait(false);
            if (managedOwner)
            {
                await lifecycle.AssociateApplicationAsync(Device, sharedId, "urn:protocol:owner").ConfigureAwait(false);
            }
            if (operation == "Managed")
            {
                await lifecycle.RegisterDeviceAsync(
                    other, new NodeId("other-dca", 2), otherCertificate).ConfigureAwait(false);
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                switch (operation)
                {
                    case "Candidate":
                        await lifecycle.RecordCandidateAsync(
                            other, sharedId, otherCertificate, false).ConfigureAwait(false);
                        break;
                    case "Dca":
                        await lifecycle.RegisterDeviceAsync(other, sharedId, otherCertificate).ConfigureAwait(false);
                        break;
                    default:
                        await lifecycle.AssociateApplicationAsync(other, sharedId, "urn:protocol:other")
                            .ConfigureAwait(false);
                        break;
                }
            });

            Assert.That(error!.StatusCode, Is.EqualTo(operation == "Dca"
                ? StatusCodes.BadEntryExists : StatusCodes.BadRequestNotAllowed));
            DeviceRegistrarDevice owner = (await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false))!;
            Assert.That(owner.DcaApplicationId, Is.EqualTo(ownerDca));
            Assert.That(owner.DcaCertificate, Is.EqualTo(Certificate));
            Assert.That(owner.Applications.Count, Is.EqualTo(managedOwner ? 1 : 0));
            if (managedOwner)
            {
                Assert.That(owner.Applications[0].ApplicationId, Is.EqualTo(sharedId));
                Assert.That(owner.Applications[0].ProtocolUri, Is.EqualTo("urn:protocol:owner"));
            }
            DeviceRegistrarDevice? denied = await lifecycle.FindDeviceAsync(other).ConfigureAwait(false);
            if (operation == "Managed")
            {
                Assert.That(denied!.Applications.IsEmpty, Is.True);
            }
            else
            {
                Assert.That(denied, Is.Null);
            }
            Assert.That(
                await lifecycle.FindCandidateByCertificateAsync(otherCertificate).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ExistingDeviceCannotBeAssignedAReplacementRegistryIdentifier()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            await lifecycle.RegisterDeviceAsync(Device, dcaId, Certificate).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    Device, new NodeId("replacement", 2), Certificate).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.DcaApplicationId, Is.EqualTo(dcaId));
        }

        [TestCase("")]
        [TestCase("relative")]
        public async Task InvalidProductInstanceCannotBeRegistered(string productInstance)
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    productInstance, new NodeId("dca", 2), Certificate).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task NullRegistryIdentifierAndEmptyCertificateCannotGrantDcaPrivilege()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ServiceResultException? idError = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(Device, NodeId.Null, Certificate).ConfigureAwait(false));
            ServiceResultException? certificateError = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    Device, new NodeId("dca", 2), ByteString.Empty).ConfigureAwait(false));
            Assert.That(idError!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(certificateError!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AssociationRequiresKnownDevice()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.AssociateApplicationAsync(
                    Device, new NodeId("app", 2), string.Empty).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task CancelledRefreshAndStatusUpdateLeaveStateUnchanged()
        {
            var lifecycle = new MemoryDeviceRegistrarLifecycle();
            var dcaId = new NodeId("dca", 2);
            await lifecycle.RegisterDeviceAsync(Device, dcaId, Certificate).ConfigureAwait(false);
            await lifecycle.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await lifecycle.RegisterDeviceAsync(
                    Device, dcaId, ByteString.From(new byte[] { 9 }), cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await lifecycle.UpdateSoftwareStatusAsync(
                    Device, false, "required", cancellation.Token).ConfigureAwait(false));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await lifecycle.AssociateApplicationAsync(
                    Device, new NodeId("app", 2), string.Empty, cancellation.Token).ConfigureAwait(false));
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.DcaCertificate, Is.EqualTo(Certificate));
            Assert.That(state.SoftwareStatus, Is.True);
            Assert.That(state.SoftwareRevision, Is.EqualTo("installed"));
            Assert.That(state.Applications.IsEmpty, Is.True);
        }

        private const string Device = "urn:device:test";
        private static readonly ByteString Certificate = ByteString.From(new byte[] { 1, 2, 3 });
    }
}
