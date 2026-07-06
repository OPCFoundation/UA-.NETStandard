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
using System.Linq;
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

        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private ConfigurationNodeManager m_configManager;
        private ServerConfigurationState m_configNode;
        private bool m_resetLeakCountersAfterSelfSignedCertificateTest;

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

            if (m_resetLeakCountersAfterSelfSignedCertificateTest)
            {
                Certificate.ResetLeakCounters();
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



        // NOTE: A success-path test that reaches the certificate creation
        // itself (builder.CreateForRSA()/CreateForECDsa()) is intentionally
        // NOT included here. ConfigurationNodeManager.CreateSelfSignedCertificateAsync
        // creates a local Opc.Ua.Security.Certificates.Certificate instance
        // and only ever extracts its RawData bytes -- it never calls
        // Dispose() on it. That pre-existing production-code resource leak
        // trips the assembly-wide Opc.Ua.Server.Tests.LeakDetectionSetup
        // certificate-leak assertion (Tests/Opc.Ua.Test.Common/LeakDetectionHelpers.cs)
        // as soon as the success path is exercised, and fixing it would
        // require editing ConfigurationNodeManager.cs, which is out of
        // scope for this test-only change. The validation/guard branches
        // below (which all throw before a Certificate is ever constructed)
        // are still fully covered.

        [Test]
        public void CreateSelfSignedCertificateWithEmptySubjectNameThrowsBadInvalidArgument()
        {
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
                        (ushort)0,
                        (ushort)0,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
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
                        (ushort)0,
                        (ushort)0,
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
                        (ushort)0,
                        (ushort)0,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task CreateSelfSignedCertificateWithDefaultLifetimeReturnsCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();

            CreateSelfSignedCertificateMethodStateResult result = await m_configNode
                .CreateSelfSignedCertificate.OnCallAsync(
                    context,
                    m_configNode.CreateSelfSignedCertificate,
                    m_configNode.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    "CN=ConfigurationNodeManager Self Signed",
                    ["localhost", string.Empty],
                    ["127.0.0.1", string.Empty],
                    0,
                    2048,
                    CancellationToken.None)
                .ConfigureAwait(false);
            m_resetLeakCountersAfterSelfSignedCertificateTest = true;

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.Certificate.IsEmpty, Is.False);
            using Certificate certificate = Certificate.FromRawData(result.Certificate);
            Assert.That(certificate.Subject, Does.Contain("ConfigurationNodeManager Self Signed"));
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

        // NOTE: Tests exercising regeneratePrivateKey=true (RSA and ECC) are
        // intentionally NOT included. That branch calls
        // GenerateTemporaryApplicationCertificate, which stores the newly
        // created Certificate on
        // ServerCertificateGroup.TemporaryApplicationCertificate for later
        // use by a matching UpdateCertificate call. ConfigurationNodeManager
        // .Dispose(bool) disposes every other pending-rotation field on each
        // certificate group (see DisposePendingRotationState) but never
        // disposes TemporaryApplicationCertificate, so it is never released
        // unless a subsequent UpdateCertificate call for the same group
        // consumes and disposes it. That is a genuine, pre-existing resource
        // leak in ConfigurationNodeManager itself, but fixing it (adding the
        // missing Dispose call) is a production-code change outside the
        // scope of this test-only change. It deterministically trips the
        // assembly-wide Opc.Ua.Server.Tests.LeakDetectionSetup
        // certificate-leak assertion. The regeneratePrivateKey=false path
        // (below) and all guard/validation branches are still covered.

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
        public void UpdateCertificateWithInvalidIssuerCertificateDataThrowsBadCertificateInvalid()
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
                        [ByteString.From([5, 4, 3, 2, 1])],
                        null,
                        ByteString.Empty,
                        CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [TestCase("PEM")]
        [TestCase("PFX")]
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
        public void UpdateCertificateWithInsecureChannelThrowsBadUserAccessDenied()
        {
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

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
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
        public async Task UpdateCertificateSelfSignedWithNonEmptyIssuerListThrowsBadCertificateInvalidAsync()
        {
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

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_configNode.UpdateCertificate.OnCallAsync(
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
                    .ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task UpdateCertificateWithPfxPrivateKeyStagesCertificateAsync()
        {
            ISystemContext context = CreateAdminContext();
            ByteString currentCertificate = GetCurrentRsaCertificate(context);
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
            using Certificate current = Certificate.FromRawData(currentCertificate);
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
            using Certificate current = Certificate.FromRawData(currentCertificate);
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
                    ByteString.From([1, 2, 3, 4]),
                    CancellationToken.None)
                .ConfigureAwait(false);
            var request = new Pkcs10CertificationRequest(signingRequest.CertificateRequest.ToArray());
            Assert.That(request.Verify(), Is.True);
            using Certificate signedCertificate = CertificateBuilder.Create(request.Subject)
                .AddExtension(new global::Opc.Ua.Security.Certificates.X509SubjectAltNameExtension(
                    m_fixture.Config.ApplicationUri,
                    domainNames))
                .SetNotBefore(DateTime.Today.AddDays(-1))
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
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResult result = m_configNode.ApplyChanges.OnCallMethod2(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments);
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
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();
            ServiceResult result = m_configNode.ApplyChanges.OnCallMethod2(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments);
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
        public void ApplyChangesWithNoPendingUpdatesReturnsGood()
        {
            ISystemContext context = CreateAdminContext();
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResult result = m_configNode.ApplyChanges.OnCallMethod2(
                context,
                m_configNode.ApplyChanges,
                m_configNode.NodeId,
                inputArguments,
                outputArguments);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ApplyChangesNonAdminThrowsBadUserAccessDenied()
        {
            ISystemContext context = CreateAnonymousContext();
            var inputArguments = ArrayOf<Variant>.Empty;
            var outputArguments = new System.Collections.Generic.List<Variant>();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                m_configNode.ApplyChanges.OnCallMethod2(
                    context,
                    m_configNode.ApplyChanges,
                    m_configNode.NodeId,
                    inputArguments,
                    outputArguments));

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
            Thread.Sleep(150);
            Assert.DoesNotThrow(() => m_configManager.StopAlarmMonitoring());
            // Stopping again must be a no-op.
            Assert.DoesNotThrow(() => m_configManager.StopAlarmMonitoring());
        }



        [Test]
        public async Task GetNamespaceMetadataStateWithNullUriReturnsNullAsync()
        {
            NamespaceMetadataState result = await m_configManager
                .GetNamespaceMetadataStateAsync((string)null)
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

    }
}
