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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises restart, indivisible checkpoint publication, and rejection of corrupt authorization state.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class FileSystemDeviceRegistrarLifecycleTests
    {
        /// <summary>
        /// Singleton scope survives namespace reordering, candidate refresh, approval and installed-peer promotion.
        /// </summary>
        [Test]
        public async Task SingletonScopeSurvivesRestartAndInstalledCredentialPromotion()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable original = NamespacesWithApplications(false);
            var dca = new NodeId("singleton-dca", (ushort)original.GetIndex(ApplicationNamespace));
            using (var first = new FileSystemDeviceRegistrarLifecycle(Path, original, files))
            {
                await first.RecordCandidateAsync(Device, dca, Identity, false).ConfigureAwait(false);
                await first.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false);
                await first.RecordIssuedDcaCertificateAsync(Device, dca, DcaCertificate).ConfigureAwait(false);
            }
            NamespaceTable reordered = NamespacesWithApplications(true);
            var restoredDca = new NodeId("singleton-dca", (ushort)reordered.GetIndex(ApplicationNamespace));
            using (var restarted = new FileSystemDeviceRegistrarLifecycle(Path, reordered, files))
            {
                await restarted.RecordCandidateAsync(Device, restoredDca, Identity, false).ConfigureAwait(false);
                await restarted.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
                Assert.That(await restarted.ConfirmDcaCertificateAsync(DcaCertificate).ConfigureAwait(false), Is.True);
            }
            using var confirmed = new FileSystemDeviceRegistrarLifecycle(Path, reordered, files);
            DeviceRegistrarDevice? state = await confirmed.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.IsSingletonRestricted, Is.True);
            Assert.That(state.DcaApplicationId, Is.EqualTo(restoredDca));
            Assert.That(state.DcaCertificate, Is.EqualTo(DcaCertificate));
            byte[] checkpoint = files.Get(Path);
            ServiceResultException? permission = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await confirmed.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false));
            ServiceResultException? mutation = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await confirmed.AssociateApplicationAsync(Device, new NodeId("forbidden", 1), string.Empty)
                    .ConfigureAwait(false));
            Assert.That(permission!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(mutation!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(files.Get(Path), Is.EqualTo(checkpoint));
        }

        /// <summary>
        /// Old checkpoints cannot silently grant application-management privilege without retained scope evidence.
        /// </summary>
        [Test]
        public async Task LegacyCheckpointWithoutApplicationScopeFailsClosed()
        {
            using var files = new VirtualFileSystem();
            const string checkpoint =
                "{\"version\":1,\"candidates\":[],\"devices\":[{\"product\":\"urn:test:device:7\"," +
                "\"application\":\"i=101\",\"certificate\":\"AQID\",\"softwareStatus\":false," +
                "\"softwareRevision\":\"\",\"applications\":[]}]}";
            files.Add(Path, Encoding.UTF8.GetBytes(checkpoint));
            using var lifecycle = new FileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false), files);

            DeviceRegistrarDevice? device = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false));

            Assert.That(device!.IsSingletonRestricted, Is.True);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(Encoding.UTF8.GetString(files.Get(Path)), Is.EqualTo(checkpoint));
        }

        /// <summary>
        /// A failed scope publication is reported, not treated as an installed singleton credential restriction.
        /// </summary>
        [Test]
        public async Task SingletonScopePublicationFailurePreservesPriorCheckpointAndReportsFailure()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(Device, new NodeId("dca", 1), Identity, false)
                    .ConfigureAwait(false);
            }
            byte[] previous = files.Get(Path).ToArray();
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            var failure = new IOException("scope publication unavailable");
            provider.Setup(value => value.Replace(It.IsAny<string>(), Path)).Throws(failure);
            using var lifecycle = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object);

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(files.Get(Path), Is.EqualTo(previous));
            DeviceRegistrarDevice? state = await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.IsSingletonRestricted, Is.False);
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
        }

        /// <summary>
        /// Corrupt scope evidence must not be interpreted as unrestricted application-management permission.
        /// </summary>
        [Test]
        public async Task MalformedSingletonScopeRejectsCheckpointWithoutOverwritingIt()
        {
            using var files = new VirtualFileSystem();
            const string checkpoint =
                "{\"version\":2,\"candidates\":[],\"devices\":[{\"product\":\"urn:test:device:7\"," +
                "\"application\":\"i=101\",\"certificate\":\"AQID\",\"softwareStatus\":false," +
                "\"softwareRevision\":\"\",\"singletonRestricted\":\"false\",\"applications\":[]}]}";
            files.Add(Path, Encoding.UTF8.GetBytes(checkpoint));
            using var lifecycle = new FileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false), files);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await lifecycle.RequireManagedApplicationAllowedAsync(Device).ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await lifecycle.RestrictToDcaApplicationAsync(Device).ConfigureAwait(false));

            Assert.That(Encoding.UTF8.GetString(files.Get(Path)), Is.EqualTo(checkpoint));
        }

        /// <summary>
        /// Hosted persistence cannot read or change authorization state before the actual model binds its table.
        /// </summary>
        [Test]
        public async Task HostedLifecycleRejectsRequestsUntilModelNamespaceBinding()
        {
            using var files = new VirtualFileSystem();
            using var lifecycle = new FileSystemDeviceRegistrarLifecycle(files, Path);
            NamespaceTable namespaces = NamespacesWithApplications(true);
            var applicationId = new NodeId("dca", (ushort)namespaces.GetIndex(ApplicationNamespace));

            ServiceResultException? read = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.FindDeviceAsync(Device).ConfigureAwait(false));
            ServiceResultException? write = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await lifecycle.RecordCandidateAsync(Device, applicationId, Identity, true).ConfigureAwait(false));

            Assert.That(read!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(write!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(files.Files, Is.Empty);
            lifecycle.BindNamespaceTable(namespaces);
            await lifecycle.RecordCandidateAsync(Device, applicationId, Identity, true).ConfigureAwait(false);
            using var restarted = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            DeviceRegistrarIdentityCandidate? candidate = await restarted.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(candidate.CertificateIssuanceAllowed, Is.False);
        }

        /// <summary>
        /// Repeated binding to the same table is harmless; a second host cannot reinterpret existing identifiers.
        /// </summary>
        [Test]
        public async Task HostedLifecycleRejectsAnotherNamespaceTableWithoutChangingItsBinding()
        {
            using var files = new VirtualFileSystem();
            using var lifecycle = new FileSystemDeviceRegistrarLifecycle(files, Path);
            NamespaceTable namespaces = NamespacesWithApplications(false);
            lifecycle.BindNamespaceTable(namespaces);
            lifecycle.BindNamespaceTable(namespaces);
            await lifecycle.RecordCandidateAsync(Device, new NodeId("dca", 1), Identity, false)
                .ConfigureAwait(false);

            Assert.Throws<InvalidOperationException>(() =>
                lifecycle.BindNamespaceTable(NamespacesWithApplications(true)));
            Assert.Throws<ArgumentNullException>(() => lifecycle.BindNamespaceTable(null!));

            DeviceRegistrarIdentityCandidate? candidate = await lifecycle.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(new NodeId("dca", 1)));
        }

        /// <summary>
        /// Namespace ordering may change while device identity, approval and application relations survive restart.
        /// </summary>
        [Test]
        public async Task RestartPreservesCandidateDcaSoftwareAndManagedApplicationsAcrossNamespaceReordering()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable originalNamespaces = NamespacesWithApplications(false);
            ushort originalIndex = (ushort)originalNamespaces.GetIndex(ApplicationNamespace);
            var dcaId = new NodeId("dca", originalIndex);
            var appId = new NodeId("application", originalIndex);
            using (var first = new FileSystemDeviceRegistrarLifecycle(Path, originalNamespaces, files))
            {
                await first.RecordCandidateAsync(Device, dcaId, Identity, true).ConfigureAwait(false);
                await first.RegisterDeviceAsync(Device, dcaId, DcaCertificate).ConfigureAwait(false);
                await first.UpdateSoftwareStatusAsync(Device, true, "installed-7").ConfigureAwait(false);
                await first.AssociateApplicationAsync(Device, appId, "urn:protocol:example").ConfigureAwait(false);
            }
            NamespaceTable reordered = NamespacesWithApplications(true);
            ushort newIndex = (ushort)reordered.GetIndex(ApplicationNamespace);
            Assert.That(newIndex, Is.Not.EqualTo(originalIndex));
            using var restarted = new FileSystemDeviceRegistrarLifecycle(Path, reordered, files);
            DeviceRegistrarDevice? state = await restarted.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.DcaApplicationId, Is.EqualTo(new NodeId("dca", newIndex)));
            Assert.That(state.DcaCertificate, Is.EqualTo(DcaCertificate));
            Assert.That(state.SoftwareStatus, Is.True);
            Assert.That(state.SoftwareRevision, Is.EqualTo("installed-7"));
            Assert.That(state.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Applications[0].ApplicationId, Is.EqualTo(new NodeId("application", newIndex)));
            Assert.That(state.Applications[0].ProtocolUri, Is.EqualTo("urn:protocol:example"));
            DeviceRegistrarApplicationBinding? binding = await restarted.FindApplicationAsync(
                new NodeId("application", newIndex)).ConfigureAwait(false);
            Assert.That(binding, Is.EqualTo(new DeviceRegistrarApplicationBinding(
                Device, new NodeId("application", newIndex), false, "urn:protocol:example")));
            DeviceRegistrarApplicationBinding? dcaBinding = await restarted.FindApplicationAsync(
                new NodeId("dca", newIndex)).ConfigureAwait(false);
            Assert.That(dcaBinding, Is.EqualTo(new DeviceRegistrarApplicationBinding(
                Device, new NodeId("dca", newIndex), true, string.Empty)));
            DeviceRegistrarIdentityCandidate? candidate = await restarted.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false);
            Assert.That(candidate!.ApplicationId, Is.EqualTo(state.DcaApplicationId));
            Assert.That(candidate.SelectedIdentity, Is.EqualTo(Identity));
            Assert.That(candidate.CertificateIssuanceAllowed, Is.True);
            Assert.That(await restarted.FindDeviceByCertificateAsync(DcaCertificate).ConfigureAwait(false),
                Is.EqualTo(Device));
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
        }

        /// <summary>
        /// A device requiring software approval cannot become authorized merely because the registrar restarts.
        /// </summary>
        [Test]
        public async Task RestartRetainsSoftwareGateAndDoesNotPromoteCandidateToApprovedDca()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var first = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await first.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, true).ConfigureAwait(false);
            }
            using var restarted = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            DeviceRegistrarIdentityCandidate? candidate = await restarted.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.False);
            Assert.That(await restarted.FindDeviceByCertificateAsync(Identity).ConfigureAwait(false), Is.Null);
            await restarted.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false);
            using var afterApproval = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            Assert.That((await afterApproval.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false))!.CertificateIssuanceAllowed, Is.True);
        }

        /// <summary>
        /// Restart preserves both the active credential and pending issuance until installation is confirmed.
        /// </summary>
        [Test]
        public async Task RestartPreservesPendingRotationAndPublishesConfirmationBeforeRetiringActiveCredential()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            var applicationId = new NodeId("dca", 1);
            ByteString active = ByteString.From(new byte[] { 7, 7, 7 });
            byte[] replacementBytes = { 8, 8, 8 };
            ByteString replacement = ByteString.From(replacementBytes.AsSpan());
            using (var first = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await first.RecordCandidateAsync(Device, applicationId, Identity, false).ConfigureAwait(false);
                await first.RegisterDeviceAsync(Device, applicationId, active).ConfigureAwait(false);
                await first.RecordIssuedDcaCertificateAsync(
                    Device, applicationId, new ByteString(replacementBytes)).ConfigureAwait(false);
                replacementBytes[0] = 9;
            }
            using (var restarted = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                DeviceRegistrarDevice? state = await restarted.FindDeviceAsync(Device).ConfigureAwait(false);
                Assert.That(state!.DcaCertificate, Is.EqualTo(active));
                Assert.That(state.PendingDcaCertificate, Is.EqualTo(replacement));
                Assert.That(await restarted.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false), Is.Null);
                Assert.That(await restarted.ConfirmDcaCertificateAsync(replacement).ConfigureAwait(false), Is.True);
            }
            using var confirmed = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            Assert.That(await confirmed.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false),
                Is.EqualTo(Device));
            Assert.That(await confirmed.FindDeviceByCertificateAsync(active).ConfigureAwait(false), Is.Null);
            Assert.That((await confirmed.FindDeviceAsync(Device).ConfigureAwait(false))!.PendingDcaCertificate.IsEmpty,
                Is.True);
            Assert.That(await confirmed.ConfirmDcaCertificateAsync(replacement).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Failed confirmation publication must preserve the active key and pending installation for retry.
        /// </summary>
        [Test]
        public async Task FailedInstallationConfirmationCannotRetireActiveCredentialOrLosePendingIssuance()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            var applicationId = new NodeId("dca", 1);
            ByteString active = ByteString.From(new byte[] { 7, 7, 7 });
            ByteString replacement = ByteString.From(new byte[] { 8, 8, 8 });
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(Device, applicationId, Identity, false).ConfigureAwait(false);
                await original.RegisterDeviceAsync(Device, applicationId, active).ConfigureAwait(false);
                await original.RecordIssuedDcaCertificateAsync(Device, applicationId, replacement)
                    .ConfigureAwait(false);
            }
            byte[] previous = files.Get(Path).ToArray();
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            var failure = new IOException("confirmation publication unavailable");
            provider.Setup(f => f.Replace(It.IsAny<string>(), Path)).Throws(failure);
            using (var failing = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object))
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await failing.ConfirmDcaCertificateAsync(replacement).ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(files.Get(Path), Is.EqualTo(previous));
            }
            using var restarted = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            DeviceRegistrarDevice? state = await restarted.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(state!.DcaCertificate, Is.EqualTo(active));
            Assert.That(state.PendingDcaCertificate, Is.EqualTo(replacement));
            Assert.That(await restarted.FindDeviceByCertificateAsync(active).ConfigureAwait(false), Is.EqualTo(Device));
            Assert.That(await restarted.FindDeviceByCertificateAsync(replacement).ConfigureAwait(false), Is.Null);
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
        }

        /// <summary>
        /// A failed replacement leaves the complete prior checkpoint and no in-memory approval to leak later.
        /// </summary>
        [Test]
        public async Task FailedCheckpointPublicationPreservesPreviousAuthorizationAndRemovesStagingFile()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, true).ConfigureAwait(false);
            }
            byte[] previous = files.Get(Path).ToArray();
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            var failure = new IOException("checkpoint publication unavailable");
            provider.Setup(f => f.Replace(It.IsAny<string>(), Path)).Throws(failure);
            using var store = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object);
            IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                await store.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false));
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(files.Get(Path), Is.EqualTo(previous));
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
            DeviceRegistrarIdentityCandidate? candidate = await store.FindCandidateByCertificateAsync(Identity)
                .ConfigureAwait(false);
            Assert.That(candidate!.CertificateIssuanceAllowed, Is.False);
            provider.Verify(f => f.Replace(It.IsAny<string>(), Path), Times.Once);
        }

        /// <summary>
        /// Staging cleanup must not hide the failure that prevented publication or change prior authorization.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPublicationAndCleanupReportBothErrorsWithoutChangingAuthorization(bool existsFails)
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, true).ConfigureAwait(false);
            }
            byte[] previous = files.Get(Path).ToArray();
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            var publication = new IOException("checkpoint publication unavailable");
            var cleanup = new IOException("staging cleanup unavailable");
            provider.Setup(f => f.Replace(It.IsAny<string>(), Path)).Throws(publication);
            if (existsFails)
            {
                provider.Setup(f => f.Exists(
                    It.Is<string>(path => path.EndsWith(".pending", StringComparison.Ordinal))))
                    .Throws(cleanup);
            }
            else
            {
                provider.Setup(f => f.Delete(It.IsAny<string>())).Throws(cleanup);
            }
            using var store = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object);

            AggregateException? actual = await Assert.ThrowsAsync<AggregateException>(async () =>
                await store.UpdateSoftwareStatusAsync(Device, true, "installed").ConfigureAwait(false));

            Assert.That(actual!.InnerExceptions, Is.EqualTo(new[] { publication, cleanup }));
            Assert.That(files.Get(Path), Is.EqualTo(previous));
            Assert.That((await store.FindCandidateByCertificateAsync(Identity).ConfigureAwait(false))!
                .CertificateIssuanceAllowed, Is.False);
            Assert.That(files.Files.Count(), Is.EqualTo(2));
            provider.Verify(f => f.Replace(It.IsAny<string>(), Path), Times.Once);
        }

        /// <summary>
        /// Cancellation before publication leaves the existing checkpoint unchanged.
        /// </summary>
        [Test]
        public async Task CancellationDuringStagingDoesNotPublishApproval()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, true).ConfigureAwait(false);
            }
            byte[] previous = files.Get(Path).ToArray();
            using var cancellation = new CancellationTokenSource();
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            provider.Setup(f => f.OpenWrite(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    cancellation.Cancel();
                    return files.OpenWrite(path);
                });
            using var store = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object);
            await Assert.ThatAsync(async () =>
                await store.UpdateSoftwareStatusAsync(
                    Device, true, "installed", cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(files.Get(Path), Is.EqualTo(previous));
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
            provider.Verify(f => f.Replace(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Invalid checkpoints cannot silently create an empty store and accept new authorization state.
        /// </summary>
        [TestCase("")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("{\"version\":3,\"devices\":[],\"candidates\":[]}")]
        [TestCase("{\"version\":1,\"version\":1,\"devices\":[],\"candidates\":[]}")]
        public async Task CorruptOrIncompatibleCheckpointRejectsReadsAndWritesWithoutReplacingData(string checkpoint)
        {
            using var files = new VirtualFileSystem();
            byte[] bytes = Encoding.UTF8.GetBytes(checkpoint);
            files.Add(Path, bytes);
            using var store = new FileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false), files);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.FindDeviceAsync(Device).ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, false).ConfigureAwait(false));
            Assert.That(files.Get(Path), Is.EqualTo(bytes));
            Assert.That(files.Files, Is.EquivalentTo(new[] { Path }));
        }

        /// <summary>
        /// Reconstructed state is subject to live ownership rules and unambiguous checkpoint identities.
        /// </summary>
        [TestCase("missingDevice")]
        [TestCase("duplicateCandidate")]
        [TestCase("duplicateDevice")]
        [TestCase("duplicateApplication")]
        [TestCase("mismatchedDca")]
        [TestCase("crossDeviceApplication")]
        [TestCase("unstableNamespace")]
        [TestCase("emptyIdentity")]
        [TestCase("pendingEqualsActive")]
        [TestCase("pendingInvalidBase64")]
        [TestCase("pendingOwnedByAnotherIdentity")]
        [TestCase("pendingOwnedByAnotherActiveCredential")]
        public async Task InconsistentCheckpointCannotAuthorizeOrBeOverwritten(string corruption)
        {
            const string candidate =
                "{\"product\":\"urn:test:device:7\",\"application\":\"i=101\"," +
                "\"certificate\":\"AQID\",\"issuanceAllowed\":false}";
            const string application = "{\"id\":\"i=102\",\"protocol\":\"\"}";
            const string device =
                "{\"product\":\"urn:test:device:7\",\"application\":\"i=101\",\"certificate\":\"\"," +
                "\"softwareStatus\":false,\"softwareRevision\":\"\",\"applications\":[" + application + "]}";
            string candidates = candidate;
            string devices = device;
            switch (corruption)
            {
                case "missingDevice":
                    devices = string.Empty;
                    break;
                case "duplicateCandidate":
                    candidates += "," + candidate;
                    break;
                case "duplicateDevice":
                    devices += "," + device;
                    break;
                case "duplicateApplication":
                    devices = device.Replace(application, application + "," + application, StringComparison.Ordinal);
                    break;
                case "mismatchedDca":
                    devices = device.Replace("i=101", "i=999", StringComparison.Ordinal);
                    break;
                case "crossDeviceApplication":
                    candidates += "," + candidate.Replace("device:7", "device:8", StringComparison.Ordinal)
                        .Replace("i=101", "i=102", StringComparison.Ordinal)
                        .Replace("AQID", "BAUG", StringComparison.Ordinal);
                    devices += "," + device.Replace("device:7", "device:8", StringComparison.Ordinal)
                        .Replace("i=101", "i=102", StringComparison.Ordinal)
                        .Replace(application, string.Empty, StringComparison.Ordinal);
                    break;
                case "unstableNamespace":
                    candidates = candidate.Replace("i=101", "ns=2;i=101", StringComparison.Ordinal);
                    break;
                case "emptyIdentity":
                    candidates = candidate.Replace("AQID", string.Empty, StringComparison.Ordinal);
                    break;
                case "pendingEqualsActive":
                    devices = device.Replace("\"certificate\":\"\"",
                        "\"certificate\":\"BwgJ\",\"pendingCertificate\":\"BwgJ\"", StringComparison.Ordinal);
                    break;
                case "pendingInvalidBase64":
                    devices = device.Replace("\"certificate\":\"\"",
                        "\"certificate\":\"\",\"pendingCertificate\":\"not-base64\"", StringComparison.Ordinal);
                    break;
                case "pendingOwnedByAnotherIdentity":
                    candidates += "," + candidate.Replace("device:7", "device:8", StringComparison.Ordinal)
                        .Replace("i=101", "i=103", StringComparison.Ordinal)
                        .Replace("AQID", "BAUG", StringComparison.Ordinal);
                    devices = device.Replace("\"certificate\":\"\"",
                        "\"certificate\":\"\",\"pendingCertificate\":\"BAUG\"", StringComparison.Ordinal);
                    devices += "," + device.Replace("device:7", "device:8", StringComparison.Ordinal)
                        .Replace("i=101", "i=103", StringComparison.Ordinal)
                        .Replace(application, string.Empty, StringComparison.Ordinal);
                    break;
                case "pendingOwnedByAnotherActiveCredential":
                    devices = device.Replace("\"certificate\":\"\"",
                        "\"certificate\":\"\",\"pendingCertificate\":\"BAUG\"", StringComparison.Ordinal);
                    devices += "," + device.Replace("device:7", "device:8", StringComparison.Ordinal)
                        .Replace("i=101", "i=103", StringComparison.Ordinal)
                        .Replace(application, string.Empty, StringComparison.Ordinal)
                        .Replace("\"certificate\":\"\"", "\"certificate\":\"BAUG\"", StringComparison.Ordinal);
                    break;
            }
            string checkpoint = "{\"version\":1,\"candidates\":[" + candidates + "],\"devices\":[" + devices + "]}";
            byte[] bytes = Encoding.UTF8.GetBytes(checkpoint);
            using var files = new VirtualFileSystem();
            files.Add(Path, bytes);
            using var store = new FileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false), files);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.FindCandidateByCertificateAsync(Identity).ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.UpdateSoftwareStatusAsync(Device, true, "approved").ConfigureAwait(false));
            Assert.That(files.Get(Path), Is.EqualTo(bytes));
        }

        /// <summary>
        /// Serialized checkpoint updates retain every concurrently added managed relation.
        /// </summary>
        [Test]
        public async Task ConcurrentApplicationAssociationsDoNotLoseCommittedRelations()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var original = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await original.RecordCandidateAsync(
                    Device, new NodeId("dca", 1), Identity, false).ConfigureAwait(false);
            }
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IFileSystem> provider = ForwardingFileSystem(files);
            int reads = 0;
            provider.Setup(f => f.OpenRead(Path)).Returns(() =>
                Interlocked.Increment(ref reads) == 1
                    ? new DelayedReadStream(files.Get(Path).ToArray(), entered, release)
                    : files.OpenRead(Path));
            provider.Setup(f => f.Replace(It.IsAny<string>(), Path))
                .Callback((string source, string destination) => files.Replace(source, destination));
            using var store = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, provider.Object);
            Task first = store.AssociateApplicationAsync(Device, new NodeId("app-0", 1), string.Empty).AsTask();
            Task[] pending = [];
            try
            {
                await Task.WhenAny(entered.Task, first, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
                Assert.That(entered.Task.IsCompleted, Is.True, "The first operation did not enter checkpoint loading.");
                pending = Enumerable.Range(1, 11).Select(async index =>
                    await store.AssociateApplicationAsync(Device, new NodeId("app-" + index, 1), string.Empty)
                        .ConfigureAwait(false)).ToArray();
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(pending.All(task => !task.IsCompleted), Is.True);
                Assert.That(Volatile.Read(ref reads), Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                await Task.WhenAll(pending.Append(first)).ConfigureAwait(false);
            }
            DeviceRegistrarDevice? device = await store.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(device!.Applications.Count, Is.EqualTo(12));
            DeviceRegistrarApplication[] applications = device.Applications.ToArray() ??
                throw new AssertionException("Missing device applications.");
            Assert.That(applications.Select(a => a.ApplicationId),
                Is.EquivalentTo(Enumerable.Range(0, 12).Select(index => new NodeId("app-" + index, 1))));
        }

        /// <summary>
        /// A restart retains identifier ownership across DCA and managed-application roles.
        /// </summary>
        [Test]
        public async Task RestartCannotRebindAnotherDevicesSharedApplicationId()
        {
            using var files = new VirtualFileSystem();
            NamespaceTable namespaces = NamespacesWithApplications(false);
            using (var first = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files))
            {
                await first.RecordCandidateAsync(Device, new NodeId("dca", 1), Identity, false).ConfigureAwait(false);
                await first.AssociateApplicationAsync(
                    Device, new NodeId("managed", 1), string.Empty).ConfigureAwait(false);
            }
            using var restarted = new FileSystemDeviceRegistrarLifecycle(Path, namespaces, files);
            byte[] before = files.Get(Path).ToArray();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RecordCandidateAsync(
                    "urn:device:other", new NodeId("managed", 1), DcaCertificate, false).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
            Assert.That(files.Get(Path), Is.EqualTo(before));
            Assert.That(await restarted.FindDeviceAsync("urn:device:other").ConfigureAwait(false), Is.Null);
        }

        /// <summary>
        /// Caller byte buffers cannot change committed identity or installed certificate bindings.
        /// </summary>
        [Test]
        public async Task CommittedCertificatesDoNotAliasCallerBuffersOrEarlierSnapshots()
        {
            using var files = new VirtualFileSystem();
            using var store = new FileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false), files);
            byte[] identity = [7, 8, 9];
            byte[] dca = [10, 11, 12];
            await store.RecordCandidateAsync(
                Device, new NodeId("dca", 1), ByteString.From(identity), false).ConfigureAwait(false);
            await store.RegisterDeviceAsync(
                Device, new NodeId("dca", 1), ByteString.From(dca)).ConfigureAwait(false);
            DeviceRegistrarDevice? first = await store.FindDeviceAsync(Device).ConfigureAwait(false);
            identity[0] = 99;
            dca[0] = 99;
            await store.AssociateApplicationAsync(Device, new NodeId("managed", 1), string.Empty).ConfigureAwait(false);
            DeviceRegistrarDevice? current = await store.FindDeviceAsync(Device).ConfigureAwait(false);
            Assert.That(first!.Applications.Count, Is.Zero);
            Assert.That(current!.Applications.Count, Is.EqualTo(1));
            Assert.That(current.DcaCertificate, Is.EqualTo(ByteString.From(new byte[] { 10, 11, 12 })));
            DeviceRegistrarIdentityCandidate? candidate = await store.FindCandidateByCertificateAsync(
                ByteString.From(new byte[] { 7, 8, 9 })).ConfigureAwait(false);
            Assert.That(candidate!.SelectedIdentity, Is.EqualTo(ByteString.From(new byte[] { 7, 8, 9 })));
        }

        /// <summary>
        /// Fluent registration shares one persistent instance across all lifecycle capabilities.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FluentRegistrationUsesOneContainerOwnedLifecycleWithoutOpeningFiles(bool hosted)
        {
            var files = new Mock<IFileSystem>(MockBehavior.Strict);
            var services = new ServiceCollection();
            IServiceCollection result = hosted ?
                services.AddHostedFileSystemDeviceRegistrarLifecycle(Path, files.Object) :
                services.AddFileSystemDeviceRegistrarLifecycle(Path, NamespacesWithApplications(false), files.Object);
            Assert.That(result, Is.SameAs(services));
            using ServiceProvider provider = services.BuildServiceProvider();
            FileSystemDeviceRegistrarLifecycle concrete =
                provider.GetRequiredService<FileSystemDeviceRegistrarLifecycle>();
            Assert.That(provider.GetRequiredService<IDeviceRegistrarLifecycle>(), Is.SameAs(concrete));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarIdentityLifecycle>(), Is.SameAs(concrete));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarCertificateLifecycle>(), Is.SameAs(concrete));
            Assert.That(provider.GetRequiredService<IDeviceRegistrarApplicationScopeLifecycle>(), Is.SameAs(concrete));
            files.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Conflicting service registrations cannot split identity candidates from approved device relations.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void FluentRegistrationRejectsConflictingLifecycleProvidersWithoutChangingServices(int existing)
        {
            var services = new ServiceCollection();
            if (existing == 0)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarLifecycle>().Object);
            }
            else if (existing == 1)
            {
                services.AddSingleton(new Mock<IDeviceRegistrarIdentityLifecycle>().Object);
            }
            else if (existing == 2)
            {
                services.AddFileSystemDeviceRegistrarLifecycle(Path, NamespacesWithApplications(false));
            }
            else
            {
                services.AddSingleton(new Mock<IDeviceRegistrarCertificateLifecycle>().Object);
            }
            ServiceDescriptor[] before = services.ToArray();
            Assert.Throws<InvalidOperationException>(() => services.AddFileSystemDeviceRegistrarLifecycle(
                Path, NamespacesWithApplications(false)));
            Assert.That(services.ToArray(), Is.EqualTo(before));
        }

        /// <summary>
        /// A host-registered file system is used when no explicit instance is supplied.
        /// </summary>
        [Test]
        public async Task FluentRegistrationResolvesTheInjectedFileSystem()
        {
            using var files = new VirtualFileSystem();
            var services = new ServiceCollection();
            services.AddSingleton<IFileSystem>(files);
            services.AddFileSystemDeviceRegistrarLifecycle(Path, NamespacesWithApplications(false));
            using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDeviceRegistrarIdentityLifecycle>().RecordCandidateAsync(
                Device, new NodeId("dca", 1), Identity, false).ConfigureAwait(false);
            Assert.That(files.Exists(Path), Is.True);
        }

        /// <summary>
        /// Invalid configuration cannot create a provider with an implicit checkpoint location.
        /// </summary>
        [Test]
        public void ConstructorAndFluentRegistrationRejectInvalidArguments()
        {
            NamespaceTable namespaces = NamespacesWithApplications(false);
            Assert.Throws<ArgumentException>(() => new FileSystemDeviceRegistrarLifecycle(string.Empty, namespaces));
            Assert.Throws<ArgumentNullException>(() => new FileSystemDeviceRegistrarLifecycle(Path, null!));
            Assert.Throws<ArgumentNullException>(() =>
                OpcUaDeviceRegistrarBuilderExtensions.AddFileSystemDeviceRegistrarLifecycle(null!, Path, namespaces));
            Assert.Throws<ArgumentException>(() =>
                new ServiceCollection().AddFileSystemDeviceRegistrarLifecycle(string.Empty, namespaces));
            Assert.Throws<ArgumentNullException>(() =>
                new ServiceCollection().AddFileSystemDeviceRegistrarLifecycle(Path, null!));
        }

        /// <summary>
        /// Disposed providers cannot continue processing while the borrowed file system remains usable.
        /// </summary>
        [Test]
        public async Task DisposalRejectsFurtherOperationsWithoutDisposingBorrowedFileSystem()
        {
            using var files = new VirtualFileSystem();
            var store = new FileSystemDeviceRegistrarLifecycle(Path, NamespacesWithApplications(false), files);
            store.Dispose();
            store.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.FindDeviceAsync(Device).ConfigureAwait(false));
            files.Add("still-usable", []);
            Assert.That(files.Exists("still-usable"), Is.True);
        }

        /// <summary>
        /// Creates deterministic namespace tables with deliberately different application namespace indexes.
        /// </summary>
        private static NamespaceTable NamespacesWithApplications(bool reordered)
        {
            var namespaces = new NamespaceTable();
            if (reordered)
            {
                namespaces.GetIndexOrAppend("urn:test:unrelated");
            }
            namespaces.GetIndexOrAppend(ApplicationNamespace);
            return namespaces;
        }

        /// <summary>
        /// Forwards storage except for the publication operation a test replaces.
        /// </summary>
        private static Mock<IFileSystem> ForwardingFileSystem(VirtualFileSystem files)
        {
            var provider = new Mock<IFileSystem>(MockBehavior.Strict);
            provider.Setup(f => f.Exists(It.IsAny<string>(), false))
                .Returns((string path, bool _) => files.Exists(path));
            provider.Setup(f => f.OpenRead(It.IsAny<string>())).Returns((string path) => files.OpenRead(path));
            provider.Setup(f => f.OpenWrite(It.IsAny<string>())).Returns((string path) => files.OpenWrite(path));
            provider.Setup(f => f.Delete(It.IsAny<string>(), false))
                .Callback((string path, bool _) => files.Delete(path));
            return provider;
        }

        /// <summary>
        /// Suspends a checkpoint read so other lifecycle operations demonstrably contend for the provider gate.
        /// </summary>
        private sealed class DelayedReadStream : MemoryStream
        {
            /// <summary>
            /// Creates a read-only checkpoint whose first read waits for a test-controlled release.
            /// </summary>
            public DelayedReadStream(
                byte[] content,
                TaskCompletionSource<bool> entered,
                TaskCompletionSource<bool> release)
                : base(content, writable: false)
            {
                m_entered = entered;
                m_release = release;
            }

            /// <inheritdoc/>
#if NETFRAMEWORK
            public override async Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await base.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }
#else
            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                m_entered.TrySetResult(true);
                await m_release.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
#endif

            /// <summary>Signals that the first lifecycle operation owns the gate.</summary>
            private readonly TaskCompletionSource<bool> m_entered;

            /// <summary>Releases the checkpoint read after competing operations have entered.</summary>
            private readonly TaskCompletionSource<bool> m_release;
        }

        /// <summary>The virtual checkpoint location, never a physical test artifact.</summary>
        private const string Path = "onboarding-lifecycle.json";

        /// <summary>The stable namespace owning the test's GDS identifiers.</summary>
        private const string ApplicationNamespace = "urn:test:gds:applications";

        /// <summary>The device represented by the persisted identity candidate.</summary>
        private const string Device = "urn:test:device:7";

        /// <summary>The validated identity certificate bytes supplied by the test.</summary>
        private static readonly ByteString Identity = ByteString.From(new byte[] { 1, 2, 3 });

        /// <summary>The separately installed DCA certificate bytes supplied by the test.</summary>
        private static readonly ByteString DcaCertificate = ByteString.From(new byte[] { 4, 5, 6 });
    }
}
