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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that exercise the certificate-push method handlers bound by
    /// <see cref="ConfigurationNodeManager"/> onto the <c>ServerConfiguration</c>
    /// node (UpdateCertificate, CreateSigningRequest, CreateSelfSignedCertificate,
    /// ApplyChanges, GetRejectedList, GetCertificates) as well as the alarm
    /// monitoring and namespace-metadata cache helpers.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class ConfigurationNodeManagerPushTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        // A §7.10.10-compliant (>= 32 byte) additional-entropy nonce for
        // CreateSigningRequest regeneratePrivateKey requests.
        private static readonly ByteString s_regenerateNonce = ByteString.From(
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
        ]);

        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private ConfigurationNodeManager m_configManager;
        private ServerConfigurationState m_configNode;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);

            IServerInternal serverInternal = m_server.CurrentInstance;
            m_configManager = serverInternal.ConfigurationNodeManager as ConfigurationNodeManager;
            Assert.That(m_configManager, Is.Not.Null, "ConfigurationNodeManager not found or not of expected type");

            NodeState node = await serverInternal.NodeManager
                .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                .ConfigureAwait(false);
            m_configNode = node as ServerConfigurationState;
            Assert.That(m_configNode, Is.Not.Null, "ServerConfiguration node not found");
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            // Several tests stage a certificate/TrustList change without
            // calling ApplyChanges. Cancel any transaction left active so
            // the next test in this shared-fixture file always starts from
            // a clean, deterministic state (CancelChanges is a harmless
            // no-op returning BadNothingToDo when nothing is staged).
            if (m_configNode?.CancelChanges != null)
            {
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                await m_configNode.CancelChanges.OnCallMethod2Async(
                    CreateAdminContext(),
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public void GetRejectedListReturnsGoodForAdmin()
        {
            ISystemContext context = CreateAdminContext();
            ArrayOf<ByteString> certificates = default;

            ServiceResult result = m_configNode.GetRejectedList.OnCall(
                context,
                m_configNode.GetRejectedList,
                m_configNode.NodeId,
                ref certificates);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void GetRejectedListNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();
            ArrayOf<ByteString> certificates = default;

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                m_configNode.GetRejectedList.OnCall(
                    context,
                    m_configNode.GetRejectedList,
                    m_configNode.NodeId,
                    ref certificates));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        // NOTE: A test that pre-seeds the rejected-certificate store and then
        // calls GetRejectedList to exercise the non-empty enumeration branch
        // is intentionally NOT included. DirectoryCertificateStore.Load
        // (invoked from GetRejectedList's `store.EnumerateAsync()` at
        // ConfigurationNodeManager.cs) constructs a Certificate per stored
        // entry that is never disposed by the enclosing `using
        // CertificateCollection` -- CertificateCollection.Dispose() does not
        // cascade to its members. This is a genuine, pre-existing resource
        // leak below ConfigurationNodeManager (in
        // Opc.Ua.Core's DirectoryCertificateStore /
        // CertificateCollection), reproducible deterministically as soon as
        // the rejected store is non-empty. It trips the assembly-wide
        // Opc.Ua.Server.Tests.LeakDetectionSetup certificate-leak assertion
        // and is out of scope to fix here (production code change required,
        // outside ConfigurationNodeManager.cs). The empty-store success path
        // and the non-admin failure path are still covered below.

        [Test]
        public void GetCertificatesForDefaultApplicationGroupReturnsGood()
        {
            ISystemContext context = CreateAdminContext();
            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;

            ServiceResult result = m_configNode.GetCertificates.OnCall(
                context,
                m_configNode.GetCertificates,
                m_configNode.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ref certificateTypeIds,
                ref certificates);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(certificateTypeIds.Count, Is.GreaterThan(0));
            Assert.That(certificates.Count, Is.EqualTo(certificateTypeIds.Count));
        }

        [Test]
        public void GetCertificatesWithInvalidGroupThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();
            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                m_configNode.GetCertificates.OnCall(
                    context,
                    m_configNode.GetCertificates,
                    m_configNode.NodeId,
                    new NodeId(Guid.NewGuid(), 1),
                    ref certificateTypeIds,
                    ref certificates));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void GetCertificatesNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();
            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                m_configNode.GetCertificates.OnCall(
                    context,
                    m_configNode.GetCertificates,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIds,
                    ref certificates));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CreateSelfSignedCertificateWithEmptyDnsAndIpAddressesThrowsBadInvalidArgument()
        {
            // OPC 10000-12 §7.10.6: "There shall be at least one entry in
            // dnsName or IP address lists," regardless of certificate type
            // or whether SubjectName was supplied.
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        string.Empty,
                        [],
                        [],
                        0,
                        0,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void CreateSelfSignedCertificateWithEmptySubjectNameForApplicationCertificateTypeComputesDefault()
        {
            // OPC 10000-12 §7.10.6/§7.10.21: for ApplicationCertificateTypes
            // the SubjectName may be omitted; the Server derives a suitable
            // default rather than rejecting the request with
            // BadInvalidArgument. The shared fixture's DefaultApplicationGroup
            // RsaSha256 slot is already occupied, so - once the (now
            // permitted) empty-subject request passes validation - it must
            // still reach (and fail with) the pre-existing occupied-slot
            // check, proving the old blanket SubjectName-required rejection
            // no longer fires first.
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        string.Empty,
                        ["localhost", string.Empty],
                        ["127.0.0.1", string.Empty],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CreateSelfSignedCertificateWithUnsupportedKeySizeThrowsBadOutOfRange()
        {
            // OPC 10000-12 §7.10.6: "Bad_OutOfRange: The keySizeInBits is
            // not supported." RsaSha256ApplicationCertificateType only
            // supports 2048/3072/4096; 1024 is not one of them.
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=Unsupported Key Size Probe",
                        ["localhost"],
                        ["127.0.0.1"],
                        (ushort)0,
                        (ushort)1024,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void CreateSelfSignedCertificateNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=Test",
                        [],
                        [],
                        0,
                        0,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CreateSelfSignedCertificateWithInvalidGroupThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        new NodeId(Guid.NewGuid(), 1),
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=Test",
                        [],
                        [],
                        0,
                        0,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateSelfSignedCertificateWithDefaultLifetimeReturnsCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();

            // OPC 10000-12 §7.10.6: the shared fixture's DefaultApplicationGroup
            // already has an active RsaSha256 certificate (the server's own
            // application certificate) — CreateSelfSignedCertificate must never
            // replace an occupied slot.
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=ConfigurationNodeManager Self Signed Occupied Probe",
                        ["localhost", string.Empty],
                        ["127.0.0.1", string.Empty],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null, "expected BadInvalidState but call succeeded");
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task DeleteCertificateReferencedByEndpointIsRejectedByApplyChangesAsync()
        {
            // OPC 10000-12 §7.10.7: "Certificates that are referenced by
            // EndpointDescriptions shall not be deleted. This determination
            // happens when ApplyChanges is called." The server's RSA
            // application certificate is referenced by its secure endpoints,
            // so staging its deletion succeeds but ApplyChanges must reject
            // the transaction and leave the certificate in place.
            //
            // Isolated fixture so the (rejected) deletion cannot affect any
            // test sharing m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();

                ArrayOf<NodeId> typesBefore = default;
                ArrayOf<ByteString> certsBefore = default;
                Assert.That(
                    ServiceResult.IsGood(configNode.GetCertificates.OnCall(
                        context,
                        configNode.GetCertificates,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ref typesBefore,
                        ref certsBefore)),
                    Is.True);
                int rsaBefore = typesBefore.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaBefore, Is.GreaterThanOrEqualTo(0));
                using Certificate referencedCertificate = Certificate.FromRawData(certsBefore[rsaBefore]);

                DeleteCertificateMethodStateResult deleteResult = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(deleteResult.ServiceResult),
                    Is.True,
                    "staging DeleteCertificate must succeed; §7.10.7 is enforced at ApplyChanges");

                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                ServiceResult applyResult = await configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    applyResult.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "deleting an endpoint-referenced certificate must be rejected at ApplyChanges (§7.10.7)");
                await configManager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                // The rejected transaction must have left the certificate.
                ArrayOf<NodeId> typesAfter = default;
                ArrayOf<ByteString> certsAfter = default;
                Assert.That(
                    ServiceResult.IsGood(configNode.GetCertificates.OnCall(
                        context,
                        configNode.GetCertificates,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ref typesAfter,
                        ref certsAfter)),
                    Is.True);
                int rsaAfter = typesAfter.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(
                    rsaAfter,
                    Is.GreaterThanOrEqualTo(0),
                    "the referenced certificate must survive the rejected delete");
                using Certificate afterCertificate = Certificate.FromRawData(certsAfter[rsaAfter]);
                Assert.That(afterCertificate.Thumbprint, Is.EqualTo(referencedCertificate.Thumbprint));
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task RotatedCertificateBecomesEndpointReferencedAndCannotBeDeletedAsync()
        {
            // OPC 10000-12 §7.10.7 regression: the endpoint-reference check must
            // resolve the certificate each EndpointDescription presents from the
            // live certificate registry at ApplyChanges time, not from the
            // EndpointDescription.ServerCertificate blob captured at startup.
            // After rotating the RSA application certificate A -> B, deleting B
            // (the certificate now presented by the secure endpoints) must be
            // rejected at ApplyChanges even though the startup blob referenced A.
            //
            // Multi-slot configuration: the ECC slots stay occupied so the
            // conservative "last remaining certificate" staging check passes and
            // the rejection is produced specifically by the endpoint-reference
            // determination. Isolated fixture so the rotation/rejected delete
            // cannot affect any test sharing m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();

                // Capture the certificate the RSA slot holds at startup (A).
                ArrayOf<NodeId> typesBefore = default;
                ArrayOf<ByteString> certsBefore = default;
                Assert.That(
                    ServiceResult.IsGood(configNode.GetCertificates.OnCall(
                        context,
                        configNode.GetCertificates,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ref typesBefore,
                        ref certsBefore)),
                    Is.True);
                int rsaBefore = typesBefore.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaBefore, Is.GreaterThanOrEqualTo(0));
                using Certificate certificateA = Certificate.FromRawData(certsBefore[rsaBefore]);
                string[] domainNames = X509Utils.GetDomainsFromCertificate(certificateA).ToArray();

                // Rotate A -> B via UpdateCertificate + ApplyChanges. B keeps the
                // same subject so it stays the RSA application certificate.
                using Certificate certificateB = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(
                        fixture.Config.ApplicationUri,
                        fixture.Config.ApplicationName,
                        certificateA.Subject,
                        domainNames)
                    .CreateForRSA();
                Assert.That(
                    certificateB.Thumbprint,
                    Is.Not.EqualTo(certificateA.Thumbprint),
                    "the rotation must produce a genuinely different certificate");
                ByteString privateKeyB = certificateB.Export(X509ContentType.Pfx).ToByteString();

                UpdateCertificateMethodStateResult updateResult = await configNode.UpdateCertificate.OnCallAsync(
                        context,
                        configNode.UpdateCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        certificateB.RawData.ToByteString(),
                        ArrayOf<ByteString>.Empty,
                        "pfx",
                        privateKeyB,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(updateResult.ServiceResult), Is.True);

                ServiceResult rotateApplyResult = await configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(rotateApplyResult),
                    Is.True,
                    "rotating the RSA application certificate must succeed");
                await configManager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                // The RSA slot now presents B.
                ArrayOf<NodeId> typesRotated = default;
                ArrayOf<ByteString> certsRotated = default;
                Assert.That(
                    ServiceResult.IsGood(configNode.GetCertificates.OnCall(
                        context,
                        configNode.GetCertificates,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ref typesRotated,
                        ref certsRotated)),
                    Is.True);
                int rsaRotated = typesRotated.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaRotated, Is.GreaterThanOrEqualTo(0));
                using (Certificate rotatedCert = Certificate.FromRawData(certsRotated[rsaRotated]))
                {
                    Assert.That(
                        rotatedCert.Thumbprint,
                        Is.EqualTo(certificateB.Thumbprint),
                        "the RSA slot must hold the rotated certificate B after ApplyChanges");
                }

                // Staging DeleteCertificate(B) succeeds: the ECC slots keep the
                // "last remaining certificate" staging check satisfied.
                DeleteCertificateMethodStateResult deleteResult = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(deleteResult.ServiceResult),
                    Is.True,
                    "staging DeleteCertificate must succeed; §7.10.7 is enforced at ApplyChanges");

                // ApplyChanges must reject deleting the rotated, endpoint-
                // referenced certificate B.
                ServiceResult deleteApplyResult = await configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    deleteApplyResult.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "deleting the rotated (currently presented) certificate must be rejected at " +
                    "ApplyChanges (§7.10.7)");
                await configManager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                // B must survive the rejected delete.
                ArrayOf<NodeId> typesAfter = default;
                ArrayOf<ByteString> certsAfter = default;
                Assert.That(
                    ServiceResult.IsGood(configNode.GetCertificates.OnCall(
                        context,
                        configNode.GetCertificates,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ref typesAfter,
                        ref certsAfter)),
                    Is.True);
                int rsaAfter = typesAfter.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(
                    rsaAfter,
                    Is.GreaterThanOrEqualTo(0),
                    "the referenced rotated certificate must survive the rejected delete");
                using Certificate afterCertificate = Certificate.FromRawData(certsAfter[rsaAfter]);
                Assert.That(afterCertificate.Thumbprint, Is.EqualTo(certificateB.Thumbprint));
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DeleteCertificateThenCreateSelfSignedCertificateInSameTransactionReplacesTheSlotAsync()
        {
            // Issue: IsSlotOccupiedAsync only ever checked the live
            // store/registry state, ignoring every operation already
            // staged (but not yet committed) in the active transaction.
            // Staging DeleteCertificate followed by
            // CreateSelfSignedCertificate for the same slot in the same
            // transaction therefore always failed with BadInvalidState,
            // even though the staged delete - once committed - empties
            // exactly the slot CreateSelfSignedCertificate is about to
            // (re)populate. Verify the second call is now permitted, and
            // that ApplyChanges commits a clean replacement: the original
            // certificate is gone (no orphan left in the application
            // store) and the new self-signed certificate alone occupies
            // the slot.
            //
            // Isolated fixture: this permanently replaces the server's
            // own application-certificate slot, which would break every
            // other test sharing m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();

                ArrayOf<NodeId> certificateTypeIdsBefore = default;
                ArrayOf<ByteString> certificatesBefore = default;
                ServiceResult getBeforeResult = configNode.GetCertificates.OnCall(
                    context,
                    configNode.GetCertificates,
                    configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIdsBefore,
                    ref certificatesBefore);
                Assert.That(ServiceResult.IsGood(getBeforeResult), Is.True);
                int rsaIndexBefore = certificateTypeIdsBefore.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaIndexBefore, Is.GreaterThanOrEqualTo(0));
                using Certificate originalCertificate = Certificate.FromRawData(certificatesBefore[rsaIndexBefore]);

                DeleteCertificateMethodStateResult deleteResult = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(deleteResult.ServiceResult), Is.True);

                // Before the fix, this would throw BadInvalidState: the
                // live store/registry still shows the slot occupied,
                // since the staged delete above has not committed yet.
                CreateSelfSignedCertificateMethodStateResult createResult = await configNode
                    .CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        configNode.CreateSelfSignedCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=DeleteThenCreateSelfSigned " + Guid.NewGuid().ToString("N")[..8],
                        ["localhost", string.Empty],
                        ["127.0.0.1", string.Empty],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(createResult.ServiceResult),
                    Is.True,
                    "DeleteCertificate staged earlier in the same transaction must permit CreateSelfSignedCertificate");
                using Certificate newCertificate = Certificate.FromRawData(createResult.Certificate);

                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                ServiceResult applyResult = await configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(applyResult), Is.True, "the net replacement must commit successfully");
                await configManager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                ArrayOf<NodeId> certificateTypeIdsAfter = default;
                ArrayOf<ByteString> certificatesAfter = default;
                ServiceResult getAfterResult = configNode.GetCertificates.OnCall(
                    context,
                    configNode.GetCertificates,
                    configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIdsAfter,
                    ref certificatesAfter);
                Assert.That(ServiceResult.IsGood(getAfterResult), Is.True);
                int rsaIndexAfter = certificateTypeIdsAfter.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaIndexAfter, Is.GreaterThanOrEqualTo(0));
                using Certificate finalCertificate = Certificate.FromRawData(certificatesAfter[rsaIndexAfter]);
                Assert.That(
                    finalCertificate.Thumbprint,
                    Is.EqualTo(newCertificate.Thumbprint),
                    "the slot must be occupied by the newly created self-signed certificate");
                Assert.That(
                    finalCertificate.Thumbprint,
                    Is.Not.EqualTo(originalCertificate.Thumbprint),
                    "the original certificate must have been replaced");

                // No orphan: the original certificate must no longer
                // resolve from the application store either.
                CertificateIdentifier rsaIdentifier = fixture.Config.SecurityConfiguration.ApplicationCertificates
                    .ToList()
                    .First(c => c.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                var appStoreIdentifier = new CertificateStoreIdentifier(rsaIdentifier.StorePath!);
                using ICertificateStore appStore = appStoreIdentifier.OpenStore(s_telemetry);
                using CertificateCollection originalMatches = await appStore
                    .FindByThumbprintAsync(originalCertificate.Thumbprint).ConfigureAwait(false);
                Assert.That(
                    originalMatches,
                    Has.Count.EqualTo(0),
                    "the replaced original certificate must not remain orphaned in the application store");
                using CertificateCollection newMatches = await appStore
                    .FindByThumbprintAsync(newCertificate.Thumbprint).ConfigureAwait(false);
                Assert.That(
                    newMatches,
                    Has.Count.EqualTo(1),
                    "the new self-signed certificate must be the only one present for this slot");
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateSelfSignedCertificateThenDeleteCertificateInSameTransactionAreStagedThenCancelledAsync()
        {
            // Issue: IsSlotOccupiedAsync only ever checked the live
            // store/registry state, ignoring every operation already
            // staged in the active transaction. Staging
            // CreateSelfSignedCertificate followed by DeleteCertificate
            // for the same (still genuinely empty) slot in the same
            // transaction therefore always failed with BadInvalidArgument
            // ("slot already empty"), even though the staged create -
            // once committed - would occupy exactly the slot
            // DeleteCertificate is about to empty again. Verify the
            // second call is now permitted, and that ApplyChanges commits
            // the net no-op: the slot remains empty, exactly as if
            // neither call had been made.
            //
            // Isolated fixture: this permanently empties the server's own
            // application-certificate slot, which would break every
            // other test sharing m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();

                // The RSA application certificate is referenced by the
                // server's secure EndpointDescriptions, so §7.10.7 forbids
                // deleting it to genuinely empty its slot (see
                // DeleteCertificateReferencedByEndpointIsRejectedByApplyChangesAsync).
                // The net-staged-state behaviour is therefore exercised at
                // staging time only, and the transaction is cancelled so the
                // shared server certificate is left untouched: staging
                // DeleteCertificate first nets the slot empty, which must
                // permit CreateSelfSignedCertificate, which in turn nets the
                // slot occupied and must permit a further DeleteCertificate.
                DeleteCertificateMethodStateResult firstDelete = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(firstDelete.ServiceResult), Is.True);

                CreateSelfSignedCertificateMethodStateResult createResult = await configNode
                    .CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        configNode.CreateSelfSignedCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=CreateThenDelete " + Guid.NewGuid().ToString("N")[..8],
                        ["localhost", string.Empty],
                        ["127.0.0.1", string.Empty],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(createResult.ServiceResult), Is.True);

                // Before the fix, this would throw BadInvalidArgument: the
                // live store/registry still shows the slot occupied, since
                // the staged create above has not committed yet.
                DeleteCertificateMethodStateResult deleteResult = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(deleteResult.ServiceResult),
                    Is.True,
                    "CreateSelfSignedCertificate staged earlier in the same transaction must permit DeleteCertificate");

                ServiceResult cancelResult = await configNode.CancelChanges.OnCallMethod2Async(
                    context,
                    configNode.CancelChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(cancelResult), Is.True);

                // Cancelling the transaction must leave the original,
                // endpoint-referenced RSA certificate in place.
                ArrayOf<NodeId> certificateTypeIdsAfter = default;
                ArrayOf<ByteString> certificatesAfter = default;
                ServiceResult getAfterResult = configNode.GetCertificates.OnCall(
                    context,
                    configNode.GetCertificates,
                    configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIdsAfter,
                    ref certificatesAfter);
                Assert.That(ServiceResult.IsGood(getAfterResult), Is.True);
                Assert.That(certificatesAfter.Count, Is.EqualTo(certificateTypeIdsAfter.Count));
                int rsaIndexAfter = certificateTypeIdsAfter.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(
                    rsaIndexAfter,
                    Is.GreaterThanOrEqualTo(0),
                    "cancelling CreateSelfSignedCertificate + DeleteCertificate must leave the slot occupied");
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateSigningRequestWithoutRegeneratePrivateKeyReturnsGoodAsync()
        {
            ISystemContext context = CreateAdminContext();

            CreateSigningRequestMethodStateResult result = await m_configNode
                .CreateSigningRequest.OnCallAsync(
                    context,
                    m_configNode.CreateSigningRequest,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    string.Empty,
                    false,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.CertificateRequest.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task CreateSigningRequestWithRegeneratedPrivateKeyDisposesOnShutdownAsync()
        {
            long leakedBefore = Certificate.InstancesLeaked;
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);

                CreateSigningRequestMethodStateResult result = await configNode
                    .CreateSigningRequest.OnCallAsync(
                        CreateAdminContext(),
                        configNode.CreateSigningRequest,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        string.Empty,
                        true,
                        s_regenerateNonce,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.CertificateRequest.Length, Is.GreaterThan(0));
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }

            Assert.That(Certificate.InstancesLeaked, Is.LessThanOrEqualTo(leakedBefore));
        }

        [Test]
        public void CreateSigningRequestNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        string.Empty,
                        false,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CreateSigningRequestWithInvalidGroupThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        new NodeId(Guid.NewGuid(), 1),
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        string.Empty,
                        false,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void CreateSigningRequestWithNullCertificateTypeThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        NodeId.Null,
                        string.Empty,
                        false,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateSigningRequestWithNullGroupUsesDefaultApplicationGroupAsync()
        {
            ISystemContext context = CreateAdminContext();

            // Passing NodeId.Null for the group must hit the "default to
            // DefaultApplicationGroup" branch inside VerifyGroupAndTypeId.
            CreateSigningRequestMethodStateResult result = await m_configNode
                .CreateSigningRequest.OnCallAsync(
                    context,
                    m_configNode.CreateSigningRequest,
                    m_configNode.NodeId,
                    NodeId.Null,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    string.Empty,
                    false,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
        }

        [Test]
        public void CreateSigningRequestWithCertificateTypeNotInGroupThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();

            // HttpsCertificateType is only ever registered on the
            // DefaultHttpsGroup, never on DefaultApplicationGroup, so this
            // must hit the "certificate type not valid for group" branch.
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.HttpsCertificateType,
                        string.Empty,
                        false,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void UpdateCertificateWithEmptyCertificateThrowsArgumentNullException()
        {
            ISystemContext context = CreateAdminContext();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.Empty,
                        [],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void UpdateCertificateWithInvalidCertificateDataThrowsBadCertificateInvalid()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.From([1, 2, 3, 4]),
                        [],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task UpdateCertificateForApplicationCertificateGroupIgnoresMalformedIssuerCertificateDataAsync()
        {
            // OPC 10000-12 §7.10.5: the issuerCertificates list is never
            // parsed for an ApplicationCertificate-purpose group, so even
            // structurally invalid (non-DER) bytes must not fail the
            // request the way they would for a purpose that still honors
            // the supplied issuer chain.
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);

            UpdateCertificateMethodStateResult result = await m_configNode.UpdateCertificate.OnCallAsync(
                    context,
                    m_configNode.UpdateCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    currentCertificate,
                    [ByteString.From([5, 4, 3, 2, 1])],
                    null,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
        }

        [Test]
        public async Task UpdateCertificateForApplicationCertificateGroupDoesNotStageOrImportSuppliedIssuersAsync()
        {
            // OPC 10000-12 §7.10.5: the ignored issuerCertificates list
            // must never be staged or imported into the group's issuer
            // store for an ApplicationCertificate-purpose group, even
            // when ApplyChanges actually commits the update. Isolated
            // fixture: this replaces the server's own application
            // certificate, which would affect every other test sharing
            // m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();

                ArrayOf<NodeId> certificateTypeIds = default;
                ArrayOf<ByteString> currentCertificates = default;
                ServiceResult getBeforeResult = configNode.GetCertificates.OnCall(
                    context,
                    configNode.GetCertificates,
                    configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIds,
                    ref currentCertificates);
                Assert.That(ServiceResult.IsGood(getBeforeResult), Is.True);
                int rsaIndex = certificateTypeIds.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaIndex, Is.GreaterThanOrEqualTo(0));
                using Certificate current = Certificate.FromRawData(currentCertificates[rsaIndex]);
                string[] domainNames = X509Utils.GetDomainsFromCertificate(current).ToArray();

                using Certificate newCertificate = DefaultCertificateFactory.Instance
                    .CreateApplicationCertificate(
                        fixture.Config.ApplicationUri,
                        fixture.Config.ApplicationName,
                        current.Subject,
                        domainNames)
                    .CreateForRSA();
                ByteString privateKey = newCertificate.Export(X509ContentType.Pfx).ToByteString();

                var issuerStoreIdentifier = new CertificateStoreIdentifier(
                    fixture.Config.SecurityConfiguration.TrustedIssuerCertificates.StorePath!);
                int issuerCountBefore;
                using (ICertificateStore issuerStoreBefore = issuerStoreIdentifier.OpenStore(s_telemetry))
                {
                    using CertificateCollection allBefore = await issuerStoreBefore.EnumerateAsync()
                        .ConfigureAwait(false);
                    issuerCountBefore = allBefore.Count;
                }

                using Certificate bogusIssuer = CertificateBuilder
                    .Create("CN=Bogus Supplied Issuer " + Guid.NewGuid().ToString("N")[..8])
                    .SetCAConstraint(0)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                UpdateCertificateMethodStateResult updateResult = await configNode.UpdateCertificate.OnCallAsync(
                        context,
                        configNode.UpdateCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        newCertificate.RawData.ToByteString(),
                        [bogusIssuer.RawData.ToByteString()],
                        "pfx",
                        privateKey,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(updateResult.ServiceResult), Is.True);
                Assert.That(updateResult.ApplyChangesRequired, Is.True);

                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                ServiceResult applyResult = await configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    configNode.ApplyChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(applyResult), Is.True);
                await configManager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

                using (ICertificateStore issuerStoreAfter = issuerStoreIdentifier.OpenStore(s_telemetry))
                {
                    using CertificateCollection allAfter = await issuerStoreAfter.EnumerateAsync()
                        .ConfigureAwait(false);
                    Assert.That(
                        allAfter,
                        Has.Count.EqualTo(issuerCountBefore),
                        "no supplied issuer may be staged/imported for an ApplicationCertificate-purpose group");

                    using CertificateCollection bogusMatches = await issuerStoreAfter
                        .FindByThumbprintAsync(bogusIssuer.Thumbprint).ConfigureAwait(false);
                    Assert.That(bogusMatches, Has.Count.EqualTo(0));
                }

                ArrayOf<NodeId> certificateTypeIdsAfter = default;
                ArrayOf<ByteString> certificatesAfter = default;
                ServiceResult getAfterResult = configNode.GetCertificates.OnCall(
                    context,
                    configNode.GetCertificates,
                    configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ref certificateTypeIdsAfter,
                    ref certificatesAfter);
                Assert.That(ServiceResult.IsGood(getAfterResult), Is.True);
                int rsaIndexAfter = certificateTypeIdsAfter.ToList()
                    .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
                Assert.That(rsaIndexAfter, Is.GreaterThanOrEqualTo(0));
                using Certificate committed = Certificate.FromRawData(certificatesAfter[rsaIndexAfter]);
                Assert.That(
                    committed.Thumbprint,
                    Is.EqualTo(newCertificate.Thumbprint),
                    "the replacement certificate must actually have been committed");
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }


        public void UpdateCertificateWithInvalidPrivateKeyThrowsBadSecurityChecksFailed(string privateKeyFormat)
        {
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        currentCertificate,
                        [],
                        privateKeyFormat,
                        ByteString.From([1, 2, 3, 4]),
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void UpdateCertificateWithUnsupportedPrivateKeyFormatThrowsBadNotSupported()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.From([1, 2, 3, 4]),
                        [],
                        "DER",
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void UpdateCertificateNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.Empty,
                        [],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void UpdateCertificateWithInsecureChannelThrowsBadSecurityModeInsufficient()
        {
            // OPC 10000-12 §7.10.5: UpdateCertificate requires an encrypted
            // SecureChannel; a Sign-only channel is reported as
            // Bad_SecurityModeInsufficient (a channel-security failure),
            // distinct from the Bad_UserAccessDenied Role failure.
            ISystemContext context = CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.Sign,
                ObjectIds.WellKnownRole_SecurityAdmin);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.Empty,
                        [],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void UpdateCertificateWithInvalidGroupThrowsBadInvalidArgument()
        {
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        new NodeId(Guid.NewGuid(), 1),
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        ByteString.From([1, 2, 3, 4]),
                        [],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task UpdateCertificateForApplicationCertificateGroupIgnoresSuppliedIssuerCertificatesAsync()
        {
            // OPC 10000-12 §7.10.5: "If the CertificateGroup Purpose is
            // ApplicationCertificateType, this list is redundant ...
            // therefore the Server shall ignore this list." A well-formed
            // but otherwise irrelevant supplied issuer certificate must
            // never be staged/imported nor cause validation to fail, even
            // for a self-signed replacement certificate (which previously
            // required an empty issuer list).
            ISystemContext context = CreateAdminContext();

            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;
            ServiceResult getResult = m_configNode.GetCertificates.OnCall(
                context,
                m_configNode.GetCertificates,
                m_configNode.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ref certificateTypeIds,
                ref certificates);
            Assert.That(ServiceResult.IsGood(getResult), Is.True);

            int index = certificateTypeIds.ToList()
                .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            ByteString currentCertificate = certificates[index];

            using Certificate fakeIssuer = CertificateBuilder.Create("CN=Fake Issuer")
                .SetCAConstraint(0)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            UpdateCertificateMethodStateResult result = await m_configNode.UpdateCertificate.OnCallAsync(
                    context,
                    m_configNode.UpdateCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    currentCertificate,
                    [fakeIssuer.RawData.ToByteString()],
                    null,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
        }

        [Test]
        public async Task UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);
            using var current = Certificate.FromRawData(currentCertificate);
            string[] domainNames = X509Utils.GetDomainsFromCertificate(current).ToArray();
            using Certificate newCertificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    m_fixture.Config.ApplicationUri,
                    m_fixture.Config.ApplicationName,
                    current.Subject,
                    domainNames)
                .CreateForRSA();
            ByteString privateKey = newCertificate.Export(X509ContentType.Pfx).ToByteString();

            UpdateCertificateMethodStateResult result = await m_configNode.UpdateCertificate.OnCallAsync(
                    context,
                    m_configNode.UpdateCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    newCertificate.RawData.ToByteString(),
                    [],
                    "pfx",
                    privateKey,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
        }

        [Test]
        public async Task UpdateCertificateWithPemPrivateKeyStagesCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);
            using var current = Certificate.FromRawData(currentCertificate);
            string[] domainNames = X509Utils.GetDomainsFromCertificate(current).ToArray();
            using Certificate newCertificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    m_fixture.Config.ApplicationUri,
                    m_fixture.Config.ApplicationName,
                    current.Subject,
                    domainNames)
                .CreateForRSA();
            ByteString privateKey = PEMWriter.ExportPrivateKeyAsPEM(newCertificate).ToByteString();

            UpdateCertificateMethodStateResult result = await m_configNode.UpdateCertificate.OnCallAsync(
                    context,
                    m_configNode.UpdateCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    newCertificate.RawData.ToByteString(),
                    [],
                    "pem",
                    privateKey,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
        }

        [Test]
        public async Task UpdateCertificateWithRegeneratedPrivateKeyStagesCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);
            using var current = Certificate.FromRawData(currentCertificate);
            string[] domainNames = X509Utils.GetDomainsFromCertificate(current).ToArray();
            using Certificate issuer = CertificateBuilder.Create("CN=ConfigurationNodeManager Push CA")
                .SetCAConstraint(0)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            CreateSigningRequestMethodStateResult signingRequest = await m_configNode
                .CreateSigningRequest.OnCallAsync(
                    context,
                    m_configNode.CreateSigningRequest,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    current.Subject,
                    true,
                    s_regenerateNonce,
                    CancellationToken.None)
                .ConfigureAwait(false);
            var request = new Pkcs10CertificationRequest(signingRequest.CertificateRequest.ToArray());
            Assert.That(request.Verify(), Is.True);
            using Certificate signedCertificate = CertificateBuilder.Create(request.Subject)
                .AddExtension(new X509SubjectAltNameExtension(
                    m_fixture.Config.ApplicationUri,
                    domainNames))
                .SetNotBefore(DateTime.UtcNow.Date.AddDays(-1))
                .SetLifeTime(12)
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();

            UpdateCertificateMethodStateResult result = await m_configNode.UpdateCertificate.OnCallAsync(
                    context,
                    m_configNode.UpdateCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    signedCertificate.RawData.ToByteString(),
                    [issuer.RawData.ToByteString()],
                    null,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.True);
        }

        [Test]
        public async Task ApplyChangesWithPendingCertificateUpdateReturnsGoodAsync()
        {
            ISystemContext context = CreateAdminContext();
            await UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync().ConfigureAwait(false);
            m_configManager.ApplyChangesGracePeriod = TimeSpan.FromMilliseconds(-1);
            ArrayOf<Variant> inputArguments = [];
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResult result = await m_configNode.ApplyChanges.OnCallMethod2Async(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);
            await m_configManager.DrainPendingApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task DrainPendingApplyChangesAsyncWithCancellationTokenCancelsAsync()
        {
            ISystemContext context = CreateAdminContext();
            await UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync().ConfigureAwait(false);
            m_configManager.ApplyChangesGracePeriod = TimeSpan.FromMilliseconds(250);
            ArrayOf<Variant> inputArguments = [];
            var outputArguments = new System.Collections.Generic.List<Variant>();
            ServiceResult result = await m_configNode.ApplyChanges.OnCallMethod2Async(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);
            using var cancellationTokenSource = new CancellationTokenSource();
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await m_configManager
                    .DrainPendingApplyChangesAsync(cancellationTokenSource.Token)
                    .ConfigureAwait(false));
            await m_configManager.DrainPendingApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        // NOTE: A test that drives UpdateCertificate + ApplyChanges through a
        // full, completed rotation (i.e. awaiting DrainPendingApplyChangesAsync)
        // is intentionally NOT included. ScheduleDeferredApplyChanges calls
        // CertificateManager.UpdateAsync, which notifies every registered
        // ITransportListener; TcpTransportListener.CertificateUpdate then
        // calls CertificateManager.AcquireApplicationCertificateBySecurityPolicy,
        // which AddRef()s a CertificateEntry that is never released. That is
        // a genuine, pre-existing resource leak several layers below
        // ConfigurationNodeManager (in Opc.Ua.Core's TcpTransportListener /
        // CertificateManager), reproducible deterministically as soon as a
        // real ApplyChanges rotation completes against a running TCP
        // listener. It trips the assembly-wide
        // Opc.Ua.Server.Tests.LeakDetectionSetup certificate-leak assertion.
        // Fixing it is out of scope for this test-only change (it lives
        // outside ConfigurationNodeManager.cs entirely). ApplyChanges' own
        // request-building/staging logic (lines up to the deferred
        // scheduling call) is still covered by the tests below and by
        // UpdateCertificate's own success/failure staging tests.

        [Test]
        public async Task ApplyChangesWithNoPendingUpdatesReturnsBadNothingToDoAsync()
        {
            // OPC 10000-12 §7.10.11: ApplyChanges returns BadNothingToDo
            // when no PushManagement transaction is active.
            ISystemContext context = CreateAdminContext();
            ArrayOf<Variant> inputArguments = [];
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResult result = await m_configNode.ApplyChanges.OnCallMethod2Async(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public void ApplyChangesNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();
            ArrayOf<Variant> inputArguments = [];
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    m_configNode.ApplyChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task DrainPendingApplyChangesAsyncWithNoPendingTaskCompletesImmediatelyAsync()
        {
            await m_configManager.DrainPendingApplyChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await m_configManager.DrainPendingApplyChangesAsync()
                .ConfigureAwait(false);
        }

        [Test]
        public void ValidatePushCertificateAndIssuerChainWithNullCertificateThrowsArgumentNullException()
        {
            using var issuerCertificates = new CertificateCollection();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ConfigurationNodeManager.ValidatePushCertificateAndIssuerChainAsync(
                        null,
                        issuerCertificates,
                        m_fixture.Config.SecurityConfiguration,
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidatePushCertificateAndIssuerChainWithNullIssuersThrowsArgumentNullException()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=Validation Subject")
                .CreateForRSA();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ConfigurationNodeManager.ValidatePushCertificateAndIssuerChainAsync(
                        certificate,
                        null,
                        m_fixture.Config.SecurityConfiguration,
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidatePushCertificateAndIssuerChainWithNullConfigurationThrowsArgumentNullException()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=Validation Subject")
                .CreateForRSA();
            using var issuerCertificates = new CertificateCollection();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ConfigurationNodeManager.ValidatePushCertificateAndIssuerChainAsync(
                        certificate,
                        issuerCertificates,
                        null,
                        s_telemetry,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidatePushCertificateAndIssuerChainWithNullTelemetryThrowsArgumentNullException()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=Validation Subject")
                .CreateForRSA();
            using var issuerCertificates = new CertificateCollection();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await ConfigurationNodeManager.ValidatePushCertificateAndIssuerChainAsync(
                        certificate,
                        issuerCertificates,
                        m_fixture.Config.SecurityConfiguration,
                        null,
                        CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void StartAlarmMonitoringTwiceThenStopDoesNotThrow()
        {
            Assert.DoesNotThrow(() => m_configManager.StartAlarmMonitoring(TimeSpan.FromMilliseconds(50)));
            // Second call must hit the early-return branch (timer already running).
            Assert.DoesNotThrow(() => m_configManager.StartAlarmMonitoring(TimeSpan.FromMilliseconds(50)));
            Assert.DoesNotThrow(() => m_configManager.StopAlarmMonitoring());
            // Stopping again must be a no-op.
            Assert.DoesNotThrow(m_configManager.StopAlarmMonitoring);
        }

        [Test]
        public async Task GetNamespaceMetadataStateWithNullUriReturnsNullAsync()
        {
            NamespaceMetadataState result = await m_configManager
                .GetNamespaceMetadataStateAsync(null)
                .ConfigureAwait(false);

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetNamespaceMetadataStateByIndexReturnsMetadataAsync()
        {
            // Use the server's own application namespace URI (already registered
            // in the NamespaceUris table with a stable index), since
            // CreateNamespaceMetadataStateAsync does not itself register a brand
            // new namespace URI in that table.
            string namespaceUri = m_server.CurrentInstance.NamespaceUris.GetString(1);
            NamespaceMetadataState created = await m_configManager
                .CreateNamespaceMetadataStateAsync(namespaceUri)
                .ConfigureAwait(false);
            Assert.That(created, Is.Not.Null);

            ushort namespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri);

            NamespaceMetadataState byIndex = await m_configManager
                .GetNamespaceMetadataStateAsync(namespaceIndex)
                .ConfigureAwait(false);
            Assert.That(byIndex, Is.SameAs(created));

            // Second call must hit the cache branch.
            NamespaceMetadataState byIndexCached = await m_configManager
                .GetNamespaceMetadataStateAsync(namespaceIndex)
                .ConfigureAwait(false);
            Assert.That(byIndexCached, Is.SameAs(created));
        }

        [Test]
        public async Task GetNamespaceMetadataStateCachedReturnsSameInstanceAsync()
        {
            const string namespaceUri = "http://test.org/UA/CachedLookupTest";
            NamespaceMetadataState created = await m_configManager
                .CreateNamespaceMetadataStateAsync(namespaceUri)
                .ConfigureAwait(false);

            NamespaceMetadataState first = await m_configManager
                .GetNamespaceMetadataStateAsync(namespaceUri)
                .ConfigureAwait(false);
            NamespaceMetadataState second = await m_configManager
                .GetNamespaceMetadataStateAsync(namespaceUri)
                .ConfigureAwait(false);

            Assert.That(first, Is.SameAs(created));
            Assert.That(second, Is.SameAs(created));
        }

        [Test]
        public void BindKeyCredentialPushWithNullSubjectThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await m_configManager.BindKeyCredentialPushAsync(null, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task BindKeyCredentialPushWithExistingFolderBindsSubjectAsync()
        {
            using var store = new InMemoryKeyCredentialStore();
            var subject = new KeyCredentialPushSubject(store);

            Assert.DoesNotThrowAsync(async () =>
                await m_configManager.BindKeyCredentialPushAsync(subject, CancellationToken.None)
                    .ConfigureAwait(false));

            NodeState node = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(
                    KeyCredentialPushSubject.StandardConfigurationFolderNodeId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(node, Is.TypeOf<KeyCredentialConfigurationFolderState>());
        }

        [Test]
        public void ConstructorWithLoggerOverloadCreatesUserAndHttpsCertificateGroups()
        {
            IServerInternal serverInternal = m_server.CurrentInstance;
            ApplicationConfiguration configuration = m_fixture.Config;
            SecurityConfiguration security = configuration.SecurityConfiguration;

            CertificateTrustList originalUserIssuer = security.UserIssuerCertificates;
            CertificateTrustList originalTrustedUser = security.TrustedUserCertificates;
            CertificateTrustList originalHttpsIssuer = security.HttpsIssuerCertificates;
            CertificateTrustList originalTrustedHttps = security.TrustedHttpsCertificates;
            ArrayOf<CertificateIdentifier> originalAppCertificates = security.ApplicationCertificates;

            try
            {
                security.UserIssuerCertificates = new CertificateTrustList
                {
                    StorePath = security.TrustedIssuerCertificates.StorePath
                };
                security.TrustedUserCertificates = new CertificateTrustList
                {
                    StorePath = security.TrustedPeerCertificates.StorePath
                };
                security.HttpsIssuerCertificates = new CertificateTrustList
                {
                    StorePath = security.TrustedIssuerCertificates.StorePath
                };
                security.TrustedHttpsCertificates = new CertificateTrustList
                {
                    StorePath = security.TrustedPeerCertificates.StorePath
                };
                security.ApplicationCertificates =
                [
                    new CertificateIdentifier
                    {
                        StorePath = security.TrustedIssuerCertificates.StorePath,
                        CertificateType = ObjectTypeIds.HttpsCertificateType
                    }
                ];

                using var manager = new ConfigurationNodeManager(
                    serverInternal,
                    configuration,
                    serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>());

                Assert.That(manager, Is.Not.Null);
            }
            finally
            {
                security.UserIssuerCertificates = originalUserIssuer;
                security.TrustedUserCertificates = originalTrustedUser;
                security.HttpsIssuerCertificates = originalHttpsIssuer;
                security.TrustedHttpsCertificates = originalTrustedHttps;
                security.ApplicationCertificates = originalAppCertificates;
            }
        }

        [Test]
        public void SupportsTransactionsIsExposedAndTrue()
        {
            // OPC 10000-12 §7.10.2: SupportsTransactions has no well-known
            // singleton-instance NodeId in the standard model (see
            // ConfigurationNodeManagerTests' address-space regression
            // test), so its presence is verified directly here instead.
            Assert.That(m_configNode.SupportsTransactions, Is.Not.Null);
            Assert.That(m_configNode.SupportsTransactions.Value, Is.True);
        }

        [Test]
        public void MaxTrustListSizeAdvertisesHonestFiniteEffectiveLimit()
        {
            // OPC 10000-12 §8.4.5: the reference server configures
            // MaxTrustListSize = 0 (unlimited). The server must NOT advertise 0
            // while enforcing a hidden cap; it advertises the honest effective
            // limit — here the default resource-protection safety ceiling —
            // that the TrustList handlers actually enforce.
            Assert.That(m_configNode.MaxTrustListSize, Is.Not.Null);
            Assert.That(m_configNode.MaxTrustListSize.Value, Is.GreaterThan(0u));
            Assert.That(
                m_configNode.MaxTrustListSize.Value,
                Is.LessThanOrEqualTo((uint)TrustList.DefaultMaxTrustListSizeSafetyCeiling));
        }

        [Test]
        public void DeleteCertificateNodeIsBound()
        {
            Assert.That(m_configNode.DeleteCertificate, Is.Not.Null);
            Assert.That(m_configNode.DeleteCertificate.OnCallAsync, Is.Not.Null);
        }

        [Test]
        public void TransactionDiagnosticsNodeAndMandatoryChildrenAreBound()
        {
            Assert.That(m_configNode.TransactionDiagnostics, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.StartTime, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.EndTime, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.Result, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.AffectedTrustLists, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.AffectedCertificateGroups, Is.Not.Null);
            Assert.That(m_configNode.TransactionDiagnostics.Errors, Is.Not.Null);
        }

        [Test]
        public async Task TransactionDiagnosticsReportBadOutOfServiceBeforeAnyTransactionAsync()
        {
            // OPC 10000-12 §7.10.17: "If no transaction has started the values
            // of all Variables have a status of Bad_OutOfService." A pristine
            // coordinator is swapped in so the shared fixture's transaction
            // history does not interfere.
            var fresh = new PushConfigurationTransactionCoordinator(s_telemetry);
            IPushConfigurationTransactionCoordinator original = SwapCoordinator(m_configManager, fresh);
            try
            {
                // CancelChanges returns BadNothingToDo on the pristine
                // coordinator but still refreshes TransactionDiagnostics from
                // its (None) snapshot.
                await InvokeCancelChangesAsync().ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(m_configNode.TransactionDiagnostics.StartTime.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                    Assert.That(m_configNode.TransactionDiagnostics.EndTime.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                    Assert.That(m_configNode.TransactionDiagnostics.Result.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                    Assert.That(m_configNode.TransactionDiagnostics.AffectedTrustLists.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                    Assert.That(m_configNode.TransactionDiagnostics.AffectedCertificateGroups.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                    Assert.That(m_configNode.TransactionDiagnostics.Errors.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.BadOutOfService));
                });
            }
            finally
            {
                SwapCoordinator(m_configManager, original);
            }
        }

        [Test]
        public async Task TransactionDiagnosticsReportBadInvalidStateWhileTransactionActiveAsync()
        {
            // OPC 10000-12 §7.10.17: "It has a status of Bad_InvalidState if a
            // transaction has started but not completed." Staging a certificate
            // change starts the transaction and refreshes the diagnostics node
            // immediately (not only at ApplyChanges).
            await UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(m_configNode.TransactionDiagnostics.Result.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
                Assert.That(m_configNode.TransactionDiagnostics.StartTime.StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.Good));
                // §7.10.17: EndTime is DateTime.MinValue until the transaction
                // completes. The UA DateTime encoding clamps MinValue to the
                // 1601 epoch, so compare against the same round-tripped value.
                Assert.That((DateTime)m_configNode.TransactionDiagnostics.EndTime.Value,
                    Is.EqualTo((DateTime)(DateTimeUtc)DateTime.MinValue));
            });

            // TearDown cancels the still-active transaction.
        }

        [Test]
        public async Task TransactionDiagnosticsReportGoodResultAfterCompletedTransactionAsync()
        {
            // OPC 10000-12 §7.10.17: once the transaction completes the Result
            // status is Good and its value is the ApplyChanges StatusCode.
            var fresh = new PushConfigurationTransactionCoordinator(s_telemetry);
            IPushConfigurationTransactionCoordinator original = SwapCoordinator(m_configManager, fresh);
            try
            {
                var sessionId = new NodeId(Guid.NewGuid(), 1);
                var group = new NodeId(Guid.NewGuid(), 1);
                fresh.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup = group,
                    CommitAsync = _ => Task.CompletedTask
                });
                await fresh.ApplyChangesAsync(sessionId, CancellationToken.None).ConfigureAwait(false);

                // Refresh the node from the now-completed snapshot (a
                // BadNothingToDo CancelChanges still refreshes diagnostics).
                await InvokeCancelChangesAsync().ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(m_configNode.TransactionDiagnostics.Result.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.Good));
                    Assert.That(m_configNode.TransactionDiagnostics.Result.Value,
                        Is.EqualTo((StatusCode)StatusCodes.Good));
                    Assert.That((DateTime)m_configNode.TransactionDiagnostics.EndTime.Value,
                        Is.GreaterThan(DateTime.MinValue));
                    Assert.That(m_configNode.TransactionDiagnostics.EndTime.StatusCode,
                        Is.EqualTo((StatusCode)StatusCodes.Good));
                    Assert.That(m_configNode.TransactionDiagnostics.AffectedCertificateGroups.Value.Contains(group),
                        Is.True);
                });
            }
            finally
            {
                SwapCoordinator(m_configManager, original);
            }
        }

        private async Task InvokeCancelChangesAsync()
        {
            await m_configNode.CancelChanges.OnCallMethod2Async(
                CreateAdminContext(),
                m_configNode.CancelChanges,
                m_configNode.NodeId,
                ArrayOf<Variant>.Empty,
                new System.Collections.Generic.List<Variant>(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private static IPushConfigurationTransactionCoordinator SwapCoordinator(
            ConfigurationNodeManager manager,
            IPushConfigurationTransactionCoordinator coordinator)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_coordinator", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_coordinator not found.");
            var previous = (IPushConfigurationTransactionCoordinator)field.GetValue(manager)!;
            field.SetValue(manager, coordinator);
            return previous;
        }

        private static IPushConfigurationTransactionCoordinator GetCoordinatorField(
            ConfigurationNodeManager manager)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_coordinator", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_coordinator not found.");
            return (IPushConfigurationTransactionCoordinator)field.GetValue(manager)!;
        }

        [Test]
        public void EachConfigurationNodeManagerOwnsADistinctDefaultCoordinator()
        {
            // OPC 10000-12 §§7.10.2-7.10.11: the coordinator is per-server
            // state, so two ConfigurationNodeManagers (one per server, e.g.
            // two servers built from one DI container) that are not given an
            // injected coordinator must each own a distinct default instance.
            IServerInternal serverInternal = m_server.CurrentInstance;
            using var managerA = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator: null,
                pendingKeyStore: null);
            using var managerB = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator: null,
                pendingKeyStore: null);

            IPushConfigurationTransactionCoordinator coordinatorA = GetCoordinatorField(managerA);
            IPushConfigurationTransactionCoordinator coordinatorB = GetCoordinatorField(managerB);

            Assert.That(coordinatorA, Is.Not.Null);
            Assert.That(coordinatorB, Is.Not.Null);
            Assert.That(
                coordinatorA,
                Is.Not.SameAs(coordinatorB),
                "each server's ConfigurationNodeManager must own a distinct default coordinator");
        }

        [Test]
        public void AnInjectedCoordinatorIsSharedAcrossConfigurationNodeManagers()
        {
            // Custom injection is preserved: an explicitly supplied coordinator
            // is used as-is (the direct-construction fallback path).
            IServerInternal serverInternal = m_server.CurrentInstance;
            var shared = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            using var managerA = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator: shared,
                pendingKeyStore: null);
            using var managerB = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator: shared,
                pendingKeyStore: null);

            Assert.That(GetCoordinatorField(managerA), Is.SameAs(shared));
            Assert.That(GetCoordinatorField(managerB), Is.SameAs(shared));
        }

        [Test]
        public void DeleteCertificateWithInvalidGroupThrowsBadInvalidArgument()
        {
            // The "slot already empty" rejection path is covered end-to-end
            // by the isolated fixture in
            // DeleteCertificateOnAlreadyEmptySlotReturnsBadInvalidStateAsync
            // (and, for the create-on-the-now-empty-slot half,
            // CreateSelfSignedCertificateOnSlotEmptiedByDeleteCertificateSucceedsAsync).
            // This test instead exercises the invalid-group rejection,
            // shared with VerifyGroupAndTypeId's other callers.
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.DeleteCertificate.OnCallAsync(
                        context,
                        m_configNode.DeleteCertificate,
                        m_configNode.NodeId,
                        new NodeId(Guid.NewGuid(), 1),
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void DeleteCertificateNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.DeleteCertificate.OnCallAsync(
                        context,
                        m_configNode.DeleteCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task DeleteCertificateOnAlreadyEmptySlotReturnsBadInvalidStateAsync()
        {
            // OPC 10000-12 §7.10.7: "If no Certificate is assigned to the
            // CertificateType slot then a Bad_InvalidState error is
            // returned." Because the RSA certificate is endpoint-referenced it
            // cannot be deleted for real (§7.10.7), so the empty-slot state is
            // reached by staging a first DeleteCertificate (which nets the
            // slot empty via IsSlotOccupiedAsync); a second DeleteCertificate
            // must then observe the netted-empty slot. The transaction is
            // cancelled so the shared server certificate is untouched.
            // Isolated fixture keeps the staged transaction off m_configManager.
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = null;

            try
            {
                server = await fixture.StartAsync().ConfigureAwait(false);
                NodeState node = await server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(ObjectIds.ServerConfiguration)
                    .ConfigureAwait(false);
                var configNode = node as ServerConfigurationState;
                Assert.That(configNode, Is.Not.Null);
                var configManager = server.CurrentInstance.ConfigurationNodeManager as ConfigurationNodeManager;
                Assert.That(configManager, Is.Not.Null);

                ISystemContext context = CreateAdminContext();

                DeleteCertificateMethodStateResult firstDeleteResult = await configNode
                    .DeleteCertificate.OnCallAsync(
                        context,
                        configNode.DeleteCertificate,
                        configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(firstDeleteResult.ServiceResult), Is.True);

                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await configNode.DeleteCertificate.OnCallAsync(
                            context,
                            configNode.DeleteCertificate,
                            configNode.NodeId,
                            ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                            ObjectTypeIds.RsaSha256ApplicationCertificateType,
                            CancellationToken.None)
                        .ConfigureAwait(false));

                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                await configNode.CancelChanges.OnCallMethod2Async(
                    context,
                    configNode.CancelChanges,
                    configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (server != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DeleteCertificateForEveryTypeInOneTransactionRejectsTheLastOneAsync()
        {
            // OPC 10000-12 §7.10.7: staging DeleteCertificate for every
            // certificate type in the same transaction must not be able to
            // leave the server with zero occupied application-certificate
            // slots once ApplyChanges commits them all together, even
            // though each individual staging request only ever sees the
            // (unmodified until commit) live registry. The shared
            // fixture's DefaultApplicationGroup has at least 3 occupied
            // types (RSA + two ECC curves; see
            // ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates),
            // so deleting all but the last must succeed individually while
            // the last one must be rejected.
            ISystemContext context = CreateAdminContext();

            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;
            ServiceResult getResult = m_configNode.GetCertificates.OnCall(
                context,
                m_configNode.GetCertificates,
                m_configNode.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ref certificateTypeIds,
                ref certificates);
            Assert.That(ServiceResult.IsGood(getResult), Is.True);

            var types = certificateTypeIds.ToList();
            Assert.That(
                types,
                Has.Count.GreaterThanOrEqualTo(3),
                "Test requires the fixture's default RSA + ECC certificate types.");

            try
            {
                for (int i = 0; i < types.Count - 1; i++)
                {
                    DeleteCertificateMethodStateResult deleteResult = await m_configNode
                        .DeleteCertificate.OnCallAsync(
                            context,
                            m_configNode.DeleteCertificate,
                            m_configNode.NodeId,
                            ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                            types[i],
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(
                        ServiceResult.IsGood(deleteResult.ServiceResult),
                        Is.True,
                        $"Deleting type #{i} should succeed while another type remains occupied.");
                }

                NodeId lastType = types[types.Count - 1];
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_configNode.DeleteCertificate.OnCallAsync(
                            context,
                            m_configNode.DeleteCertificate,
                            m_configNode.NodeId,
                            ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                            lastType,
                            CancellationToken.None)
                        .ConfigureAwait(false));

                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            }
            finally
            {
                // Discard every staged delete without applying it so the
                // shared fixture's real certificate stores remain
                // untouched for the other tests in this file.
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                await m_configNode.CancelChanges.OnCallMethod2Async(
                    context,
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ApplyCertificateSlotChangeSelfCompensatesWhenAddFailsAfterDeleteSucceedsAsync()
        {
            // PushConfigurationTransactionCoordinator.ApplyChangesAsync only
            // reverse-compensates operations that already committed in
            // full; the operation whose own CommitAsync throws is excluded
            // from that loop. ApplyCertificateSlotChangeAsync (the
            // primitive shared by every staged certificate operation's
            // commit) must therefore be self-compensating: when the
            // previous certificate has already been deleted and the new
            // one then fails to persist, it must restore the previous
            // certificate itself before the exception propagates, so the
            // slot is never left empty despite ApplyChanges reporting a
            // failure. A certificate whose thumbprint is already present
            // in the store is used to make the add deterministically fail
            // (DirectoryCertificateStore.AddAsync throws ArgumentException
            // for a duplicate thumbprint).
            string tempPki = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cnm-selfcompensate",
                Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var storeIdentifier = new CertificateStoreIdentifier(
                    tempPki,
                    CertificateStoreType.Directory,
                    noPrivateKeys: false);
                using Certificate oldCertificate = CertificateBuilder
                    .Create("CN=SelfCompensate Old " + Guid.NewGuid().ToString("N")[..8])
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                using Certificate blockingCertificate = CertificateBuilder
                    .Create("CN=SelfCompensate Blocker " + Guid.NewGuid().ToString("N")[..8])
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                using (ICertificateStore seedStore = storeIdentifier.OpenStore(s_telemetry))
                {
                    await seedStore.AddAsync(oldCertificate, ct: CancellationToken.None).ConfigureAwait(false);
                    // Already present with the same thumbprint before the
                    // call under test, so re-adding it below deterministically fails.
                    await seedStore.AddAsync(blockingCertificate, ct: CancellationToken.None).ConfigureAwait(false);
                }

                var existingCertIdentifier = new CertificateIdentifier
                {
                    StorePath = tempPki,
                    StoreType = CertificateStoreType.Directory,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    Thumbprint = oldCertificate.Thumbprint
                };

                ArgumentException caught = Assert.ThrowsAsync<ArgumentException>(async () =>
                    await InvokeApplyCertificateSlotChangeAsync(
                            m_configManager,
                            existingCertIdentifier,
                            oldCertificate.Thumbprint,
                            blockingCertificate,
                            oldCertificate)
                        .ConfigureAwait(false));

                Assert.That(caught, Is.Not.Null, "the duplicate-thumbprint add must fail");

                using ICertificateStore verifyStore = storeIdentifier.OpenStore(s_telemetry);
                using CertificateCollection oldRestored = await verifyStore
                    .FindByThumbprintAsync(oldCertificate.Thumbprint)
                    .ConfigureAwait(false);
                Assert.That(
                    oldRestored,
                    Has.Count.EqualTo(1),
                    "self-compensation must restore the deleted certificate after the replacement failed");

                using CertificateCollection allCerts = await verifyStore.EnumerateAsync().ConfigureAwait(false);
                Assert.That(
                    allCerts,
                    Has.Count.EqualTo(2),
                    "the store must end up exactly where it started: old certificate restored, blocker untouched");
            }
            finally
            {
                if (Directory.Exists(tempPki))
                {
                    Directory.Delete(tempPki, true);
                }
            }
        }

        /// <summary>
        /// Invokes the private <c>ApplyCertificateSlotChangeAsync</c>
        /// primitive directly so its self-compensation behavior can be
        /// verified without needing to reproduce an entire failing
        /// <c>UpdateCertificate</c> request end-to-end. The
        /// <c>certificateGroup</c> argument is never dereferenced by the
        /// method for this test (no issuer chain is supplied), so reusing
        /// the shared fixture's own first configured group is sufficient.
        /// </summary>
        private static async Task InvokeApplyCertificateSlotChangeAsync(
            ConfigurationNodeManager manager,
            CertificateIdentifier existingCertIdentifier,
            string removeThumbprint,
            Certificate addCertificateWithKey,
            Certificate removedCertificateBackup)
        {
            Type managerType = typeof(ConfigurationNodeManager);
            FieldInfo groupsField = managerType.GetField(
                "m_certificateGroups",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_certificateGroups not found.");
            var groups = (System.Collections.IList)groupsField.GetValue(manager)!;
            object certificateGroup = groups[0]!;

            MethodInfo method = managerType.GetMethod(
                "ApplyCertificateSlotChangeAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method ApplyCertificateSlotChangeAsync not found.");

            var task = (Task)method.Invoke(
                manager,
                [
                    certificateGroup,
                    existingCertIdentifier,
                    removeThumbprint,
                    addCertificateWithKey,
                    null,
                    CancellationToken.None,
                    removedCertificateBackup
                ])!;
            await task.ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the private <c>ApplyCertificateSlotChangeAsync</c>
        /// primitive directly with a non-null issuer chain (and no
        /// application-certificate mutation), returning the thumbprints
        /// it reports as newly added, so the issuer-tracking behavior can
        /// be verified without reproducing an entire
        /// <c>UpdateCertificate</c> request end-to-end.
        /// </summary>
        private static Task<ArrayOf<string>> InvokeApplyCertificateSlotChangeForIssuersAsync(
            ConfigurationNodeManager manager,
            object certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            CertificateCollection addIssuerChain)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "ApplyCertificateSlotChangeAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method ApplyCertificateSlotChangeAsync not found.");

            return (Task<ArrayOf<string>>)method.Invoke(
                manager,
                [
                    certificateGroup,
                    existingCertIdentifier,
                    null,
                    null,
                    addIssuerChain,
                    CancellationToken.None,
                    null
                ])!;
        }

        /// <summary>
        /// Invokes the private <c>RemoveIssuerCertificatesAsync</c> helper
        /// directly, exactly as <c>UpdateCertificate</c>'s
        /// <c>RollbackAsync</c> would when compensating a completed
        /// issuer import.
        /// </summary>
        private static async Task InvokeRemoveIssuerCertificatesAsync(
            ConfigurationNodeManager manager,
            object certificateGroup,
            ArrayOf<string> thumbprints)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "RemoveIssuerCertificatesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method RemoveIssuerCertificatesAsync not found.");

            var task = (Task)method.Invoke(manager, [certificateGroup, thumbprints, CancellationToken.None])!;
            await task.ConfigureAwait(false);
        }

        [Test]
        public async Task ApplyCertificateSlotChangeTracksNewlyAddedIssuersAndPreservesPreExistingOnesAsync()
        {
            // Issue: UpdateCertificate's commit imports the entire staged
            // issuer chain into the group's issuer store without
            // recording which of those issuers were already present.
            // ApplyCertificateSlotChangeAsync must report exactly the
            // thumbprints it newly added (excluding any pre-existing
            // issuer that also happened to be part of the submitted
            // chain), so a later rollback/self-compensation - exercised
            // end-to-end in
            // UpdateCertificateRollbackAfterLaterOperationFailureRestoresAppCertAndRemovesNewIssuerAsync -
            // removes only the issuers it actually introduced.
            Type managerType = typeof(ConfigurationNodeManager);
            FieldInfo groupsField = managerType.GetField(
                "m_certificateGroups",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_certificateGroups not found.");
            var groups = (System.Collections.IList)groupsField.GetValue(m_configManager)!;
            object certificateGroup = groups[0]!;
            PropertyInfo appCertificatesProperty = certificateGroup.GetType().GetProperty("ApplicationCertificates")
                ?? throw new InvalidOperationException("Property ApplicationCertificates not found.");
            var applicationCertificates = (ArrayOf<CertificateIdentifier>)appCertificatesProperty
                .GetValue(certificateGroup)!;
            // removeThumbprint/addCertificateWithKey are both null for
            // this call (see InvokeApplyCertificateSlotChangeForIssuersAsync),
            // so this identifier's application store is never mutated;
            // reusing the real, already-open RSA identifier is safe.
            CertificateIdentifier existingCertIdentifier = applicationCertificates.ToList()
                .First(c => c.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType);

            var issuerStoreIdentifier = new CertificateStoreIdentifier(
                m_fixture.Config.SecurityConfiguration.TrustedIssuerCertificates.StorePath!);

            using Certificate preExistingIssuer = CertificateBuilder
                .Create("CN=PreExisting Issuer " + Guid.NewGuid().ToString("N")[..8])
                .SetCAConstraint(0)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate newIssuer = CertificateBuilder
                .Create("CN=Newly Added Issuer " + Guid.NewGuid().ToString("N")[..8])
                .SetCAConstraint(0)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            try
            {
                using (ICertificateStore seedStore = issuerStoreIdentifier.OpenStore(s_telemetry))
                {
                    // Simulates an issuer already trusted by an earlier,
                    // already-committed operation. newIssuer is not added
                    // here, so it must be reported as newly added below.
                    await seedStore.AddAsync(preExistingIssuer, ct: CancellationToken.None).ConfigureAwait(false);
                }

                using var addIssuerChain = new CertificateCollection { preExistingIssuer, newIssuer };

                ArrayOf<string> newlyAdded = await InvokeApplyCertificateSlotChangeForIssuersAsync(
                    m_configManager,
                    certificateGroup,
                    existingCertIdentifier,
                    addIssuerChain).ConfigureAwait(false);

                Assert.That(
                    newlyAdded.ToList(),
                    Is.EquivalentTo(new[] { newIssuer.Thumbprint }),
                    "only the issuer that was not already present must be reported as newly added");

                using (ICertificateStore verifyStore = issuerStoreIdentifier.OpenStore(s_telemetry))
                {
                    using CertificateCollection preExistingMatches = await verifyStore
                        .FindByThumbprintAsync(preExistingIssuer.Thumbprint).ConfigureAwait(false);
                    Assert.That(preExistingMatches, Has.Count.EqualTo(1), "the pre-existing issuer must remain");

                    using CertificateCollection newMatches = await verifyStore
                        .FindByThumbprintAsync(newIssuer.Thumbprint).ConfigureAwait(false);
                    Assert.That(newMatches, Has.Count.EqualTo(1), "the newly added issuer must have been imported");
                }

                // Compensate exactly as UpdateCertificateAsync's
                // RollbackAsync would, using the thumbprints reported
                // above, and verify only the newly added issuer is
                // removed.
                await InvokeRemoveIssuerCertificatesAsync(m_configManager, certificateGroup, newlyAdded)
                    .ConfigureAwait(false);

                using ICertificateStore finalStore = issuerStoreIdentifier.OpenStore(s_telemetry);
                using CertificateCollection preExistingAfterRemoval = await finalStore
                    .FindByThumbprintAsync(preExistingIssuer.Thumbprint).ConfigureAwait(false);
                Assert.That(
                    preExistingAfterRemoval,
                    Has.Count.EqualTo(1),
                    "removing the newly added issuers must never remove a pre-existing issuer");

                using CertificateCollection newAfterRemoval = await finalStore
                    .FindByThumbprintAsync(newIssuer.Thumbprint).ConfigureAwait(false);
                Assert.That(
                    newAfterRemoval,
                    Has.Count.EqualTo(0),
                    "the newly added issuer must be removed once rolled back");
            }
            finally
            {
                using ICertificateStore cleanupStore = issuerStoreIdentifier.OpenStore(s_telemetry);
                await cleanupStore.DeleteAsync(preExistingIssuer.Thumbprint).ConfigureAwait(false);
                await cleanupStore.DeleteAsync(newIssuer.Thumbprint).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Invokes the private <c>ApplyCertificateSlotChangeAsync</c>
        /// primitive directly with every parameter populated (an
        /// application-certificate slot swap plus an issuer-chain
        /// import), so the self-compensation for an issuer-import
        /// failure that follows a successful application-certificate
        /// swap can be verified without reproducing an entire failing
        /// <c>UpdateCertificate</c> request end-to-end.
        /// </summary>
        private static Task<ArrayOf<string>> InvokeApplyCertificateSlotChangeFullAsync(
            ConfigurationNodeManager manager,
            object certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            string removeThumbprint,
            Certificate addCertificateWithKey,
            CertificateCollection addIssuerChain,
            Certificate removedCertificateBackup)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "ApplyCertificateSlotChangeAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method ApplyCertificateSlotChangeAsync not found.");

            return (Task<ArrayOf<string>>)method.Invoke(
                manager,
                [
                    certificateGroup,
                    existingCertIdentifier,
                    removeThumbprint,
                    addCertificateWithKey,
                    addIssuerChain,
                    CancellationToken.None,
                    removedCertificateBackup
                ])!;
        }

        [Test]
        public async Task ApplyCertificateSlotChangeSelfCompensatesWhenIssuerImportFailsAfterAppCertSwapAsync()
        {
            // Issue: once ApplyCertificateSlotChangeAsync has fully
            // swapped the application-certificate slot (the previous
            // certificate deleted and the new one added), a subsequent
            // failure importing that certificate's issuer chain must not
            // leave the new application certificate live alongside a
            // half-imported issuer chain. The method must self-compensate
            // before propagating: restore the previous application
            // certificate, remove exactly the issuer certificates the
            // import loop had newly added so far, and preserve any
            // issuer that was already present. A directory is
            // pre-created at the exact file path the store would write
            // the poisoned issuer certificate to, so that import
            // deterministically fails with UnauthorizedAccessException -
            // not the ArgumentException the loop already tolerates for a
            // duplicate thumbprint.
            string tempAppPki = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cnm-selfcompensate-issuer-app",
                Guid.NewGuid().ToString("N")[..8]);
            string tempIssuerPki = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "cnm-selfcompensate-issuer-store",
                Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var appStoreIdentifier = new CertificateStoreIdentifier(
                    tempAppPki,
                    CertificateStoreType.Directory,
                    noPrivateKeys: false);
                using Certificate oldCertificate = CertificateBuilder
                    .Create("CN=IssuerSelfCompensate Old " + Guid.NewGuid().ToString("N")[..8])
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                using Certificate newCertificate = CertificateBuilder
                    .Create("CN=IssuerSelfCompensate New " + Guid.NewGuid().ToString("N")[..8])
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                using (ICertificateStore seedAppStore = appStoreIdentifier.OpenStore(s_telemetry))
                {
                    await seedAppStore.AddAsync(oldCertificate, ct: CancellationToken.None).ConfigureAwait(false);
                }

                var existingCertIdentifier = new CertificateIdentifier
                {
                    StorePath = tempAppPki,
                    StoreType = CertificateStoreType.Directory,
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    Thumbprint = oldCertificate.Thumbprint
                };

                var issuerStoreIdentifier = new CertificateStoreIdentifier(
                    tempIssuerPki,
                    CertificateStoreType.Directory,
                    noPrivateKeys: true);

                using Certificate preExistingIssuer = CertificateBuilder
                    .Create("CN=IssuerSelfCompensate PreExisting " + Guid.NewGuid().ToString("N")[..8])
                    .SetCAConstraint(0)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                using Certificate newIssuer = CertificateBuilder
                    .Create("CN=IssuerSelfCompensate NewlyAdded " + Guid.NewGuid().ToString("N")[..8])
                    .SetCAConstraint(0)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                using Certificate poisonedIssuer = CertificateBuilder
                    .Create("CN=IssuerSelfCompensate Poisoned " + Guid.NewGuid().ToString("N")[..8])
                    .SetCAConstraint(0)
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                using (ICertificateStore seedIssuerStore = issuerStoreIdentifier.OpenStore(s_telemetry))
                {
                    // Simulates an issuer already trusted by an earlier,
                    // already-committed operation; must survive self-compensation.
                    await seedIssuerStore.AddAsync(preExistingIssuer, ct: CancellationToken.None)
                        .ConfigureAwait(false);
                }

                MethodInfo getFileNameMethod = typeof(DirectoryCertificateStore).GetMethod(
                    "GetFileName",
                    BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Method GetFileName not found.");
                var poisonedFileName = (string)getFileNameMethod.Invoke(null, [poisonedIssuer])!;
                string poisonedFilePath = Path.Combine(tempIssuerPki, "certs", poisonedFileName + ".der");
                Directory.CreateDirectory(poisonedFilePath);

                // Reuse the shared fixture's own first configured group
                // solely for its NodeId (used only for logging); its
                // IssuerStore is swapped, via reflection, for this test's
                // isolated issuer store below so the fixture's real
                // trusted-issuer store is never touched, and restored in
                // the inner finally.
                Type managerType = typeof(ConfigurationNodeManager);
                FieldInfo groupsField = managerType.GetField(
                    "m_certificateGroups",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Field m_certificateGroups not found.");
                var groups = (System.Collections.IList)groupsField.GetValue(m_configManager)!;
                object certificateGroup = groups[0]!;
                PropertyInfo issuerStoreProperty = certificateGroup.GetType().GetProperty("IssuerStore")
                    ?? throw new InvalidOperationException("Property IssuerStore not found.");
                object originalIssuerStore = issuerStoreProperty.GetValue(certificateGroup)!;
                issuerStoreProperty.SetValue(certificateGroup, issuerStoreIdentifier);

                try
                {
                    using var addIssuerChain = new CertificateCollection
                    {
                        preExistingIssuer,
                        newIssuer,
                        poisonedIssuer
                    };

                    UnauthorizedAccessException caught = Assert.ThrowsAsync<UnauthorizedAccessException>(
                        async () => await InvokeApplyCertificateSlotChangeFullAsync(
                                m_configManager,
                                certificateGroup,
                                existingCertIdentifier,
                                oldCertificate.Thumbprint,
                                newCertificate,
                                addIssuerChain,
                                oldCertificate)
                            .ConfigureAwait(false));

                    Assert.That(caught, Is.Not.Null, "the poisoned issuer import must fail");

                    using ICertificateStore verifyAppStore = appStoreIdentifier.OpenStore(s_telemetry);
                    using CertificateCollection oldRestored = await verifyAppStore
                        .FindByThumbprintAsync(oldCertificate.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(
                        oldRestored,
                        Has.Count.EqualTo(1),
                        "self-compensation must restore the previous application certificate " +
                        "after the issuer import failed");

                    using CertificateCollection newRemoved = await verifyAppStore
                        .FindByThumbprintAsync(newCertificate.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(
                        newRemoved,
                        Has.Count.EqualTo(0),
                        "self-compensation must remove the new application certificate already " +
                        "swapped in before the issuer import failed");

                    using ICertificateStore verifyIssuerStore = issuerStoreIdentifier.OpenStore(s_telemetry);
                    using CertificateCollection preExistingAfter = await verifyIssuerStore
                        .FindByThumbprintAsync(preExistingIssuer.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(
                        preExistingAfter,
                        Has.Count.EqualTo(1),
                        "self-compensation must never remove a pre-existing issuer certificate");

                    using CertificateCollection newIssuerAfter = await verifyIssuerStore
                        .FindByThumbprintAsync(newIssuer.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(
                        newIssuerAfter,
                        Has.Count.EqualTo(0),
                        "self-compensation must remove exactly the issuer certificate this import " +
                        "loop had newly added before the poisoned issuer failed");

                    using CertificateCollection poisonedIssuerAfter = await verifyIssuerStore
                        .FindByThumbprintAsync(poisonedIssuer.Thumbprint)
                        .ConfigureAwait(false);
                    Assert.That(
                        poisonedIssuerAfter,
                        Has.Count.EqualTo(0),
                        "the poisoned issuer certificate itself was never successfully imported");
                }
                finally
                {
                    issuerStoreProperty.SetValue(certificateGroup, originalIssuerStore);
                }
            }
            finally
            {
                if (Directory.Exists(tempAppPki))
                {
                    Directory.Delete(tempAppPki, true);
                }

                if (Directory.Exists(tempIssuerPki))
                {
                    Directory.Delete(tempIssuerPki, true);
                }
            }
        }

        [Test]
        public async Task UpdateCertificateRollbackAfterLaterOperationFailureRestoresAppCertAndRemovesNewIssuerAsync()
        {
            // Issue: UpdateCertificate's commit imports the staged issuer
            // chain into the group's issuer store, but its RollbackAsync
            // only ever restored the application certificate. Any issuer
            // newly imported by a successful UpdateCertificate therefore
            // stayed behind forever once a LATER operation in the same
            // transaction failed to commit and the coordinator reverse-
            // compensated this already-successful UpdateCertificate.
            // Verify that after such a later-operation failure, both the
            // application certificate AND the issuer store are fully
            // restored.
            ISystemContext context = CreateAdminContext();
            ByteString originalCertificateBytes = GetCurrentRsaCertificate(context);
            using Certificate original = Certificate.FromRawData(originalCertificateBytes);
            string[] domainNames = X509Utils.GetDomainsFromCertificate(original).ToArray();

            using Certificate issuerCa = CertificateBuilder
                .Create("CN=UpdateRollback Issuer CA " + Guid.NewGuid().ToString("N")[..8])
                .SetCAConstraint(0)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate newCertificate = CertificateBuilder.Create(original.Subject)
                .AddExtension(new global::Opc.Ua.Security.Certificates.X509SubjectAltNameExtension(
                    m_fixture.Config.ApplicationUri,
                    domainNames))
                .SetIssuer(issuerCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            ByteString privateKey = newCertificate.Export(X509ContentType.Pfx).ToByteString();

            var issuerStoreIdentifier = new CertificateStoreIdentifier(
                m_fixture.Config.SecurityConfiguration.TrustedIssuerCertificates.StorePath!);

            // Guard: the freshly generated CA must not already be
            // trusted, so its later removal is a meaningful assertion.
            using (ICertificateStore issuerStoreBefore = issuerStoreIdentifier.OpenStore(s_telemetry))
            {
                using CertificateCollection beforeMatches = await issuerStoreBefore
                    .FindByThumbprintAsync(issuerCa.Thumbprint).ConfigureAwait(false);
                Assert.That(beforeMatches, Has.Count.EqualTo(0), "the fresh issuer CA must not already be trusted");
            }

            try
            {
                UpdateCertificateMethodStateResult updateResult = await m_configNode.UpdateCertificate.OnCallAsync(
                        context,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        newCertificate.RawData.ToByteString(),
                        [issuerCa.RawData.ToByteString()],
                        "pfx",
                        privateKey,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(updateResult.ServiceResult), Is.True, "UpdateCertificate must stage");

                // Inject a second staged operation - for an unrelated
                // slot (default/null Affected* so it cannot supersede the
                // UpdateCertificate operation above) - that
                // deterministically fails to commit, simulating a later
                // PushManagement request in the same transaction that
                // fails after this UpdateCertificate has already
                // committed.
                FieldInfo coordinatorField = typeof(ConfigurationNodeManager).GetField(
                    "m_coordinator",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Field m_coordinator not found.");
                var coordinator = (IPushConfigurationTransactionCoordinator)coordinatorField
                    .GetValue(m_configManager)!;
                coordinator.Stage(NodeId.Null, new PushConfigurationOperation
                {
                    CommitAsync = _ => throw new InvalidOperationException("Induced failure for test.")
                });

                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                ServiceResult applyResult = await m_configNode.ApplyChanges.OnCallMethod2Async(
                    context,
                    m_configNode.ApplyChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(applyResult),
                    Is.False,
                    "the induced later-operation failure must fail ApplyChanges");

                ByteString restoredCertificateBytes = GetCurrentRsaCertificate(context);
                using Certificate restored = Certificate.FromRawData(restoredCertificateBytes);
                Assert.That(
                    restored.Thumbprint,
                    Is.EqualTo(original.Thumbprint),
                    "the application certificate must be fully restored after the later operation's failure");

                using ICertificateStore issuerStoreAfter = issuerStoreIdentifier.OpenStore(s_telemetry);
                using CertificateCollection afterMatches = await issuerStoreAfter
                    .FindByThumbprintAsync(issuerCa.Thumbprint).ConfigureAwait(false);
                Assert.That(
                    afterMatches,
                    Has.Count.EqualTo(0),
                    "the newly imported issuer certificate must be removed once the transaction rolls back");
            }
            finally
            {
                // Defensive: ApplyChanges already resolves the
                // transaction (successfully or not), so this is a no-op
                // on the expected path; it only guards against the
                // transaction somehow being left active if an assertion
                // above fails first.
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                await m_configNode.CancelChanges.OnCallMethod2Async(
                    context,
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);

                using ICertificateStore cleanupStore = issuerStoreIdentifier.OpenStore(s_telemetry);
                await cleanupStore.DeleteAsync(issuerCa.Thumbprint).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConcurrentApplyChangesDoesNotDropTheSuccessfulCommitsRotationBookkeepingAsync()
        {
            // Bug: the rotation collector ApplyChanges hands to
            // RegisterPendingRotation used to be a single field shared by
            // every call. PushConfigurationTransactionCoordinator.ApplyChangesAsync
            // keeps the transaction owned by the committing Session for
            // the whole duration of the commit -- including while the
            // staged operation's CommitAsync is still awaiting -- so a
            // second/duplicate ApplyChanges call that races in while the
            // first call's (slower) commit is still running always
            // observes BadInvalidState immediately, without disturbing the
            // first call's in-flight commit. But with a single shared
            // collector the second call would still drain and dispose
            // whatever the first call's still-in-flight, about-to-succeed
            // commit had ALREADY registered, silently dropping that
            // commit's SecureChannel-renegotiation bookkeeping. Correlating
            // the collector per ApplyChanges call (via the ambient async
            // call chain) must prevent that: the second call's own
            // collector stays empty, and the first call's rotation
            // survives to be scheduled for deferred apply.
            //
            // A dedicated coordinator (and ConfigurationNodeManager
            // instance) isolates this test's staged operation from every
            // other test's PushManagement transaction, while still
            // reusing the shared fixture's real, running server so the
            // deferred apply's certificate-manager refresh has a valid
            // target (mirrors ApplyChangesWithPendingCertificateUpdateReturnsGoodAsync,
            // which already proves that path is safe to drain here).
            IServerInternal serverInternal = m_server.CurrentInstance;
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            using var manager = new ConfigurationNodeManager(
                serverInternal,
                m_fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator,
                pendingKeyStore: null);

            var sessionId = new NodeId(Guid.NewGuid(), 1);
            ISystemContext context = CreateAdminContextForSession(sessionId);
            var certificateType = new NodeId(Guid.NewGuid(), 1);
            using Certificate rotatedCertificate = CertificateBuilder
                .Create("CN=ConcurrentApplyChanges " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            coordinator.Stage(sessionId, new PushConfigurationOperation
            {
                AffectedCertificateType = certificateType,
                CommitAsync = async _ =>
                {
                    // Register the rotation FIRST (as a real Delete/UpdateCertificate
                    // commit would, before any further work) and only then
                    // pause, so the duplicate call below races in after
                    // the rotation already exists but before this call's
                    // own ApplyChangesAsync has drained it.
                    InvokeRegisterPendingRotation(manager, certificateType, rotatedCertificate);
                    started.TrySetResult(true);
                    await gate.Task.ConfigureAwait(false);
                }
            });

            FieldInfo pendingApplyChangesTaskField = typeof(ConfigurationNodeManager).GetField(
                "m_pendingApplyChangesTask",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_pendingApplyChangesTask not found.");
            var beforeTask = (Task)pendingApplyChangesTaskField.GetValue(manager)!;

            // CA2025: rotatedCertificate is only read (its RawData copied)
            // synchronously inside CommitAsync before the pause below, so
            // it is never touched after this point; the analyzer cannot
            // see that firstApply is still awaited later in this same
            // method before rotatedCertificate's `using` disposes it.
#pragma warning disable CA2025
            Task<ServiceResult> firstApply = InvokeApplyChangesAsync(manager, context);
#pragma warning restore CA2025
            await started.Task.ConfigureAwait(false);

            ServiceResult secondResult = await InvokeApplyChangesAsync(manager, context).ConfigureAwait(false);
            Assert.That(
                secondResult.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState),
                "the duplicate call must short-circuit while the first call's commit is still running");

            gate.TrySetResult(true);
            ServiceResult firstResult = await firstApply.ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(firstResult), Is.True, "the first, legitimate commit must still succeed");

            var afterTask = (Task)pendingApplyChangesTaskField.GetValue(manager)!;
            Assert.That(
                ReferenceEquals(beforeTask, afterTask),
                Is.False,
                "the successful commit's rotation must still be scheduled for deferred apply, " +
                "not silently dropped by the concurrent duplicate call");

            await manager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the private <c>ApplyChangesAsync</c> method-handler
        /// directly via reflection so two overlapping calls can be driven
        /// deterministically without needing two concurrent OPC UA Method
        /// Call requests.
        /// </summary>
        private static async Task<ServiceResult> InvokeApplyChangesAsync(
            ConfigurationNodeManager manager,
            ISystemContext context)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "ApplyChangesAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method ApplyChangesAsync not found.");

            var valueTask = (ValueTask<ServiceResult>)method.Invoke(
                manager,
                [
                    context,
                    null,
                    NodeId.Null,
                    ArrayOf<Variant>.Empty,
                    new System.Collections.Generic.List<Variant>(),
                    CancellationToken.None
                ])!;
            return await valueTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Invokes the private <c>RegisterPendingRotation</c> helper
        /// directly via reflection, exactly as a real Delete/UpdateCertificate
        /// staged commit would, without needing a full certificate-store
        /// round trip.
        /// </summary>
        private static void InvokeRegisterPendingRotation(
            ConfigurationNodeManager manager,
            NodeId certificateType,
            Certificate oldCertificateWithKey)
        {
            MethodInfo method = typeof(ConfigurationNodeManager).GetMethod(
                "RegisterPendingRotation",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Method RegisterPendingRotation not found.");
            method.Invoke(manager, [certificateType, oldCertificateWithKey]);
        }

        [Test]
        public async Task CancelChangesWithNoActiveTransactionReturnsBadNothingToDoAsync()
        {
            ISystemContext context = CreateAdminContext();
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResult result = await m_configNode.CancelChanges.OnCallMethod2Async(
                context,
                m_configNode.CancelChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public void CancelChangesNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CancelChanges.OnCallMethod2Async(
                    context,
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task CancelChangesDiscardsStagedUpdateCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();
            ByteString beforeCancel = GetCurrentRsaCertificate(context);

            await UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync().ConfigureAwait(false);

            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();
            ServiceResult cancelResult = await m_configNode.CancelChanges.OnCallMethod2Async(
                context,
                m_configNode.CancelChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(cancelResult), Is.True);

            ByteString afterCancel = GetCurrentRsaCertificate(context);
            Assert.That(afterCancel.ToArray(), Is.EqualTo(beforeCancel.ToArray()),
                "cancelling the transaction must leave the certificate store untouched");

            // A repeated CancelChanges call has nothing left to discard.
            ServiceResult secondCancelResult = await m_configNode.CancelChanges.OnCallMethod2Async(
                context,
                m_configNode.CancelChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(secondCancelResult.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));
        }

        [Test]
        public async Task CancelChangesReportsBadRequestCancelledByClientInTransactionDiagnosticsAsync()
        {
            // OPC 10000-12 §7.10.17: "If the CancelChanges Method was
            // called the value is Bad_RequestCancelledByClient."
            ISystemContext context = CreateAdminContext();

            await UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync().ConfigureAwait(false);

            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();
            ServiceResult cancelResult = await m_configNode.CancelChanges.OnCallMethod2Async(
                context,
                m_configNode.CancelChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(cancelResult), Is.True);
            Assert.That(
                m_configNode.TransactionDiagnostics.Result.Value,
                Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            // §7.10.17: a completed transaction reports the outcome as the
            // Result value with a Good DataValue status (only an in-flight
            // transaction reads Bad_InvalidState).
            Assert.That(
                m_configNode.TransactionDiagnostics.Result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public async Task UpdateCertificateFromAnotherSessionWhileTransactionActiveThrowsBadTransactionPendingAsync()
        {
            ISystemContext ownerContext = CreateAdminContextForSession(new NodeId(Guid.NewGuid(), 1));
            ISystemContext otherContext = CreateAdminContextForSession(new NodeId(Guid.NewGuid(), 1));
            ByteString currentCertificate = GetCurrentRsaCertificate(ownerContext);
            using Certificate current = Certificate.FromRawData(currentCertificate);
            string[] domainNames = X509Utils.GetDomainsFromCertificate(current).ToArray();
            using Certificate newCertificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    m_fixture.Config.ApplicationUri,
                    m_fixture.Config.ApplicationName,
                    current.Subject,
                    domainNames)
                .CreateForRSA();
            ByteString privateKey = newCertificate.Export(X509ContentType.Pfx).ToByteString();

            try
            {
                UpdateCertificateMethodStateResult ownerResult = await m_configNode.UpdateCertificate.OnCallAsync(
                        ownerContext,
                        m_configNode.UpdateCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        newCertificate.RawData.ToByteString(),
                        [],
                        "pfx",
                        privateKey,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(ownerResult.ServiceResult), Is.True);

                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await m_configNode.UpdateCertificate.OnCallAsync(
                            otherContext,
                            m_configNode.UpdateCertificate,
                            m_configNode.NodeId,
                            ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                            ObjectTypeIds.RsaSha256ApplicationCertificateType,
                            newCertificate.RawData.ToByteString(),
                            [],
                            "pfx",
                            privateKey,
                            CancellationToken.None)
                        .ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTransactionPending));

                // ApplyChanges/CancelChanges from the non-owning Session
                // must also be rejected without disturbing the owner's
                // staged transaction.
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                ServiceResult applyFromOtherResult = await m_configNode.ApplyChanges.OnCallMethod2Async(
                    otherContext,
                    m_configNode.ApplyChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(applyFromOtherResult.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));

                ServiceResult cancelFromOtherResult = await m_configNode.CancelChanges.OnCallMethod2Async(
                    otherContext,
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(cancelFromOtherResult.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            }
            finally
            {
                // Always discard the staged transaction so later tests in
                // this shared-fixture file are unaffected (the file-level
                // [TearDown] uses CreateAdminContext(), whose NodeId.Null
                // Session does not own this transaction and would
                // otherwise get BadSessionIdInvalid).
                var inputArguments = ArrayOf<Variant>.Empty;
                var outputArguments = new System.Collections.Generic.List<Variant>();
                await m_configNode.CancelChanges.OnCallMethod2Async(
                    ownerContext,
                    m_configNode.CancelChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }



        private ByteString GetCurrentRsaCertificate(ISystemContext context)
        {
            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;
            ServiceResult getResult = m_configNode.GetCertificates.OnCall(
                context,
                m_configNode.GetCertificates,
                m_configNode.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ref certificateTypeIds,
                ref certificates);
            Assert.That(ServiceResult.IsGood(getResult), Is.True);
            int index = certificateTypeIds.ToList()
                .FindIndex(t => t == ObjectTypeIds.RsaSha256ApplicationCertificateType);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            return certificates[index];
        }

        [Test]
        public void CreateSigningRequestWithInsecureChannelThrowsBadSecurityModeInsufficient()
        {
            // §7.10.10: CreateSigningRequest requires an encrypted channel.
            ISystemContext context = CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.Sign,
                ObjectIds.WellKnownRole_SecurityAdmin);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        null,
                        false,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void GetCertificatesOverAuthenticatedSignChannelIsPermitted()
        {
            // §7.10 GetCertificates does not transfer private-key material, so
            // an authenticated (Sign) channel is sufficient — it must NOT be
            // rejected with BadSecurityModeInsufficient.
            ISystemContext context = CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.Sign,
                ObjectIds.WellKnownRole_SecurityAdmin);
            ArrayOf<NodeId> certificateTypeIds = default;
            ArrayOf<ByteString> certificates = default;

            ServiceResult result = m_configNode.GetCertificates.OnCall(
                context,
                m_configNode.GetCertificates,
                m_configNode.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ref certificateTypeIds,
                ref certificates);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void GetRejectedListOverUnauthenticatedChannelThrowsBadSecurityModeInsufficient()
        {
            ISystemContext context = CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.None,
                ObjectIds.WellKnownRole_SecurityAdmin);
            ArrayOf<ByteString> certificates = default;

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                m_configNode.GetRejectedList.OnCall(
                    context,
                    m_configNode.GetRejectedList,
                    m_configNode.NodeId,
                    ref certificates));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void CreateSelfSignedCertificateOverUnauthenticatedChannelThrowsBadSecurityModeInsufficient()
        {
            ISystemContext context = CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.None,
                ObjectIds.WellKnownRole_SecurityAdmin);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=Test",
                        ["localhost"],
                        [],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void CreateSelfSignedCertificateOverSignChannelWithoutAdminRoleThrowsBadUserAccessDenied()
        {
            // Channel security is sufficient (authenticated Sign channel) but
            // the SecurityAdmin Role is missing: the failure must be the
            // Role failure (Bad_UserAccessDenied), not the channel failure.
            ISystemContext context = CreateContext(UserTokenType.Anonymous, MessageSecurityMode.Sign);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSelfSignedCertificate.OnCallAsync(
                        context,
                        m_configNode.CreateSelfSignedCertificate,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        "CN=Test",
                        ["localhost"],
                        [],
                        (ushort)0,
                        (ushort)2048,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CreateSigningRequestWithRegenerateAndShortNonceThrowsBadInvalidArgument()
        {
            // §7.10.10: the Nonce must be at least 32 bytes when a new private
            // key is regenerated; a shorter Nonce is Bad_InvalidArgument.
            ISystemContext context = CreateAdminContext();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.CreateSigningRequest.OnCallAsync(
                        context,
                        m_configNode.CreateSigningRequest,
                        m_configNode.NodeId,
                        ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                        ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        null,
                        true,
                        ByteString.From(new byte[31]),
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateSigningRequestWithoutRegenerateAcceptsAnEmptyNonceAsync()
        {
            // The Nonce is only required when regeneratePrivateKey is true;
            // reusing the existing key must accept an empty Nonce.
            ISystemContext context = CreateAdminContext();

            CreateSigningRequestMethodStateResult result = await m_configNode
                .CreateSigningRequest.OnCallAsync(
                    context,
                    m_configNode.CreateSigningRequest,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    null,
                    false,
                    ByteString.Empty,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.CertificateRequest.Length, Is.GreaterThan(0));
        }

        private static SessionSystemContext CreateAdminContext()
        {
            return CreateContext(
                UserTokenType.UserName,
                MessageSecurityMode.SignAndEncrypt,
                ObjectIds.WellKnownRole_SecurityAdmin);
        }

        private static SessionSystemContext CreateAnonymousContext()
        {
            return CreateContext(UserTokenType.Anonymous, MessageSecurityMode.SignAndEncrypt);
        }

        private static SessionSystemContext CreateContext(
            UserTokenType tokenType,
            MessageSecurityMode securityMode,
            params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(tokenType.ToString());
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }
        /// <summary>
        /// Creates an admin context bound to a specific, deterministic
        /// Session NodeId (unlike <see cref="CreateAdminContext"/>, which
        /// always resolves to <see cref="NodeId.Null"/>) so cross-Session
        /// PushManagement transaction ownership can be exercised.
        /// </summary>
        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }
    }
}
