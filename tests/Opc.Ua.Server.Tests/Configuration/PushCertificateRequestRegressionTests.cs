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
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    public sealed class PushCertificateRequestRegressionTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_path = Path.Combine(Path.GetTempPath(), "push-request-" + Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry));
            await m_fixture.LoadConfigurationAsync(m_path).ConfigureAwait(false);
            SecurityConfiguration security = m_fixture.Config.SecurityConfiguration;
            string ownPath = Path.Combine(m_path, "https-own");
            security.ApplicationCertificates += new CertificateIdentifier
            {
                StorePath = ownPath,
                StoreType = CertificateStoreType.Directory,
                SubjectName = "CN=localhost",
                CertificateType = ObjectTypeIds.HttpsCertificateType
            };
            security.ApplicationCertificates += new CertificateIdentifier
            {
                StorePath = Path.Combine(m_path, "ecc-own"),
                StoreType = CertificateStoreType.Directory,
                SubjectName = "CN=Regression ECC",
                CertificateType = ObjectTypeIds.EccNistP256ApplicationCertificateType
            };
            security.HttpsIssuerCertificates = new CertificateTrustList { StorePath = Path.Combine(m_path, "https-issuers") };
            security.TrustedHttpsCertificates = new CertificateTrustList { StorePath = Path.Combine(m_path, "https-trusted") };
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = (ConfigurationNodeManager)m_fixture.Server.CurrentInstance.ConfigurationNodeManager;
            m_node = m_manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
            m_context = CreateAdminContext();
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
            if (Directory.Exists(m_path))
            {
                Directory.Delete(m_path, recursive: true);
            }
        }

        [TearDown]
        public async Task CancelPendingAsync()
        {
            await m_node.CancelChanges.OnCallMethod2Async(
                m_context, m_node.CancelChanges, m_node.NodeId, [], [], CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task SelfSignedHttpsSlotUsesRsaAndAppliesSuccessfullyAsync()
        {
            NodeId group = ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup;
            NodeId type = ObjectTypeIds.HttpsCertificateType;
            await m_node.DeleteCertificate.OnCallAsync(
                m_context, m_node.DeleteCertificate, m_node.NodeId, group, type, CancellationToken.None)
                .ConfigureAwait(false);
            CreateSelfSignedCertificateMethodStateResult created = await m_node.CreateSelfSignedCertificate.OnCallAsync(
                m_context, m_node.CreateSelfSignedCertificate, m_node.NodeId, group, type,
                "CN=localhost", ["localhost"], ["127.0.0.1"], 30, 2048, CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            using Certificate certificate = Certificate.FromRawData(created.Certificate);
            using RSA key = certificate.GetRSAPublicKey();
            Assert.That(key, Is.Not.Null);
            Assert.That(key.KeySize, Is.EqualTo(2048));
            Assert.That(certificate.Subject, Is.EqualTo("CN=localhost"));
            Assert.That(X509Utils.GetDomainsFromCertificate(certificate).ToArray(),
                Is.EquivalentTo(s_domains).IgnoreCase);

            ServiceResult applied = await m_node.ApplyChanges.OnCallMethod2Async(
                m_context, m_node.ApplyChanges, m_node.NodeId, [], [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(applied.StatusCode, Is.EqualTo(StatusCodes.Good));
            await m_manager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);
            using CertificateEntry entry = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
            Assert.That(entry.Certificate.RawData, Is.EqualTo(certificate.RawData));
            Assert.That(entry.Certificate.HasPrivateKey, Is.True);
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("CN=Requested Subject, O=Regression", false)]
        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("CN=Requested Subject, O=Regression", true)]
        public async Task ExistingKeySigningRequestUsesRequestedSubjectWithoutChangingTheActiveCertificateAsync(
            string subjectName, bool ecc)
        {
            NodeId type = ecc
                ? ObjectTypeIds.EccNistP256ApplicationCertificateType
                : ObjectTypeIds.RsaSha256ApplicationCertificateType;
            using CertificateEntry before = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
            var baseline = new Pkcs10CertificationRequest(
                DefaultCertificateFactory.Instance.CreateSigningRequest(
                    before.Certificate, X509Utils.GetDomainsFromCertificate(before.Certificate).ToArray()));
            CreateSigningRequestMethodStateResult created = await m_node.CreateSigningRequest.OnCallAsync(
                m_context, m_node.CreateSigningRequest, m_node.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup, type,
                subjectName, false, ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
            Assert.That(VerifySigningRequest(request, created.CertificateRequest), Is.True);
            Assert.That(request.Subject.Name, Is.EqualTo(string.IsNullOrEmpty(subjectName)
                ? before.Certificate.Subject
                : subjectName));
            Assert.That(request.SubjectPublicKeyInfo, Is.EqualTo(baseline.SubjectPublicKeyInfo));
            Assert.That(request.Attributes, Is.EqualTo(baseline.Attributes));
            using CertificateEntry after = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
            Assert.That(after.Certificate.RawData, Is.EqualTo(before.Certificate.RawData));
        }

        [Test]
        public async Task NonmatchingUploadCannotConsumeTheRegeneratedSigningKeyAsync(
            [Values(false, true)] bool reuseActiveCertificate)
        {
            NodeId group = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            NodeId type = ObjectTypeIds.RsaSha256ApplicationCertificateType;
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
            string[] domains = X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray();
            using var entropy = RandomNumberGenerator.Create();
            byte[] nonce = new byte[32];
            entropy.GetBytes(nonce);
            CreateSigningRequestMethodStateResult created = await m_node.CreateSigningRequest.OnCallAsync(
                m_context, m_node.CreateSigningRequest, m_node.NodeId,
                group, type, active.Certificate.Subject, true, new ByteString(nonce), CancellationToken.None).ConfigureAwait(false);
            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
            Assert.That(request.Verify(), Is.True);

            using Certificate wrong = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                m_fixture.Config.ApplicationUri, m_fixture.Config.ApplicationName, active.Certificate.Subject, domains)
                .CreateForRSA();
            ByteString wrongUpload = new(reuseActiveCertificate ? active.Certificate.RawData : wrong.RawData);
            if (reuseActiveCertificate)
            {
                UpdateCertificateMethodStateResult reused = await m_node.UpdateCertificate.OnCallAsync(
                    m_context, m_node.UpdateCertificate, m_node.NodeId,
                    group, type, wrongUpload, [], string.Empty, ByteString.Empty, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(reused.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() =>
                    m_node.UpdateCertificate.OnCallAsync(
                        m_context, m_node.UpdateCertificate, m_node.NodeId,
                        group, type, wrongUpload, [], string.Empty, ByteString.Empty, CancellationToken.None).AsTask());
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
            await CancelPendingAsync().ConfigureAwait(false);

            using Certificate issuer = CertificateBuilder.Create("CN=Pending Signing Regression Issuer")
                .SetCAConstraint().CreateForRSA();
            using Certificate signed = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                m_fixture.Config.ApplicationUri, m_fixture.Config.ApplicationName, active.Certificate.Subject, domains)
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            UpdateCertificateMethodStateResult accepted = await m_node.UpdateCertificate.OnCallAsync(
                m_context, m_node.UpdateCertificate, m_node.NodeId,
                group, type, new ByteString(signed.RawData), [new ByteString(issuer.RawData)],
                string.Empty, ByteString.Empty, CancellationToken.None).ConfigureAwait(false);
            Assert.That(accepted.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult applied = await m_node.ApplyChanges.OnCallMethod2Async(
                m_context, m_node.ApplyChanges, m_node.NodeId, [], [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(applied.StatusCode, Is.EqualTo(StatusCodes.Good));
            await m_manager.DrainPendingApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);
            using CertificateEntry after = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
            Assert.That(after.Certificate.RawData, Is.EqualTo(signed.RawData));
            Assert.That(after.Certificate.HasPrivateKey, Is.True);
            using RSA privateKey = after.Certificate.GetRSAPrivateKey();
            using RSA publicKey = signed.GetRSAPublicKey();
            byte[] hash = new byte[32];
            byte[] signature = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(publicKey.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        }

        [Test]
        public async Task CancelledMatchingUploadRestoresOnlyAnUnreplacedPendingKeyAsync(
            [Values(false, true)] bool replaceDuringUpload)
        {
            FieldInfo field = typeof(ConfigurationNodeManager).GetField(
                "m_pendingKeyStore", BindingFlags.Instance | BindingFlags.NonPublic);
            var originalStore = (IMatchingPendingCertificateKeyStore)field.GetValue(m_manager);
            using var cancellation = new CancellationTokenSource();
            using Certificate replacement = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=Newer Pending Signing Request").CreateForRSA();
            var store = new CancelAfterMatchingClaimStore(
                originalStore, cancellation, replaceDuringUpload ? replacement : null);
            field.SetValue(m_manager, store);
            try
            {
                NodeId group = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
                NodeId type = ObjectTypeIds.RsaSha256ApplicationCertificateType;
                using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
                using var entropy = RandomNumberGenerator.Create();
                byte[] nonce = new byte[32];
                entropy.GetBytes(nonce);
                CreateSigningRequestMethodStateResult created = await m_node.CreateSigningRequest.OnCallAsync(
                    m_context, m_node.CreateSigningRequest, m_node.NodeId,
                    group, type, active.Certificate.Subject, true, new ByteString(nonce), CancellationToken.None)
                    .ConfigureAwait(false);
                var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
                using Certificate issuer = CertificateBuilder.Create("CN=Cancellation Signing Regression Issuer")
                    .SetCAConstraint().CreateForRSA();
                using Certificate signed = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                    m_fixture.Config.ApplicationUri, m_fixture.Config.ApplicationName, active.Certificate.Subject,
                    X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray())
                    .SetIssuer(issuer)
                    .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                    .CreateForRSA();
                Assert.That(() => m_node.UpdateCertificate.OnCallAsync(
                    m_context, m_node.UpdateCertificate, m_node.NodeId,
                    group, type, new ByteString(signed.RawData), [new ByteString(issuer.RawData)],
                    string.Empty, ByteString.Empty, cancellation.Token).AsTask(),
                    Throws.InstanceOf<OperationCanceledException>());
                Assert.That(store.RestoreCalled, Is.True);
                Assert.That(store.RestoreUsedCancelledToken, Is.False);
                Assert.That(store.RestoreSucceeded, Is.EqualTo(!replaceDuringUpload));
                using Certificate recovered = await originalStore.TryTakeAsync(store.ClaimContext).ConfigureAwait(false);
                Assert.That(recovered, Is.Not.Null);
                Assert.That(recovered.Thumbprint,
                    Is.EqualTo(replaceDuringUpload ? replacement.Thumbprint : store.ClaimedThumbprint));
                Assert.That(recovered.HasPrivateKey, Is.True);
                using CertificateEntry current = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(type);
                Assert.That(current.Certificate.RawData, Is.EqualTo(active.Certificate.RawData));
            }
            finally
            {
                field.SetValue(m_manager, originalStore);
            }
        }

        [Test]
        public async Task BoundKeyCredentialSubjectUsesTheServersActiveCertificateRegistryAsync()
        {
            using var store = new InMemoryKeyCredentialStore();
            var subject = new KeyCredentialPushSubject(store);
            await m_manager.BindKeyCredentialPushAsync(subject).ConfigureAwait(false);
            KeyCredentialConfigurationFolderState folder = m_manager.FindPredefinedNode<KeyCredentialConfigurationFolderState>(
                KeyCredentialPushSubject.StandardConfigurationFolderNodeId);
            CreateCredentialMethodStateResult created = await folder.CreateCredential.OnCallAsync(
                m_context, folder.CreateCredential, folder.NodeId, "BoundCredential",
                "urn:bound-credential", KeyCredentialBridgeOptions.DefaultProfileUri, [], CancellationToken.None)
                .ConfigureAwait(false);
            KeyCredentialConfigurationState node = m_manager.FindPredefinedNode<KeyCredentialConfigurationState>(
                created.CredentialNodeId);
            GetEncryptingKeyMethodStateResult key = await node.GetEncryptingKey.OnCallAsync(
                m_context, node.GetEncryptingKey, node.NodeId,
                "bound-secret", SecurityPolicies.Basic256Sha256, CancellationToken.None).ConfigureAwait(false);
            Assert.That(key.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            using Certificate receiver = Certificate.FromRawData(key.PublicKey);
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            Assert.That(receiver.RawData, Is.EqualTo(active.Certificate.RawData));
            byte[] encrypted = EncryptedSecret.CreateForRsa(
                m_context.AsMessageContext(), key.RevisedSecurityPolicyUri, receiver).Encrypt([63, 64, 65], []);
            KeyCredentialUpdateMethodStateResult result = await node.UpdateCredential.OnCallAsync(
                m_context, node.UpdateCredential, node.NodeId, "bound-secret",
                new ByteString(encrypted), receiver.Thumbprint, key.RevisedSecurityPolicyUri, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Server.KeyCredential credential = await store.GetAsync("bound-secret", CancellationToken.None).ConfigureAwait(false);
            Assert.That(credential.Secret, Is.EqualTo(new byte[] { 63, 64, 65 }));
        }

        private static bool VerifySigningRequest(Pkcs10CertificationRequest request, ByteString encoded)
        {
#if NETFRAMEWORK
            return new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(encoded.ToArray()).Verify();
#else
            return request.Verify();
#endif
        }

        private static SessionSystemContext CreateAdminContext()
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_SecurityAdmin]);
            var channel = new SecureChannelContext("push", new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            }, RequestEncoding.Binary);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            return new SessionSystemContext(operation, NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private sealed class CancelAfterMatchingClaimStore(
            IMatchingPendingCertificateKeyStore inner,
            CancellationTokenSource cancellation,
            Certificate replacement) : IMatchingPendingCertificateKeyStore
        {
            public PendingCertificateKeyContext ClaimContext { get; private set; }
            public string ClaimedThumbprint { get; private set; }
            public bool RestoreCalled { get; private set; }
            public bool RestoreUsedCancelledToken { get; private set; }
            public bool RestoreSucceeded { get; private set; }

            public ValueTask<bool> SaveAsync(
                PendingCertificateKeyContext context,
                Certificate certificateWithPrivateKey,
                CancellationToken cancellationToken = default)
            {
                return inner.SaveAsync(context, certificateWithPrivateKey, cancellationToken);
            }

            public ValueTask<Certificate> TryTakeAsync(
                PendingCertificateKeyContext context,
                CancellationToken cancellationToken = default)
            {
                return inner.TryTakeAsync(context, cancellationToken);
            }

            public async ValueTask<Certificate> TryTakeMatchingAsync(
                PendingCertificateKeyContext context,
                Certificate certificate,
                CancellationToken cancellationToken = default)
            {
                Certificate pending = await inner.TryTakeMatchingAsync(context, certificate, cancellationToken)
                    .ConfigureAwait(false);
                if (pending != null)
                {
                    ClaimContext = context;
                    ClaimedThumbprint = pending.Thumbprint;
                    if (replacement != null)
                    {
                        Assert.That(await inner.SaveAsync(context, replacement, CancellationToken.None).ConfigureAwait(false), Is.True);
                    }
                    cancellation.Cancel();
                }
                return pending;
            }

            public async ValueTask<bool> TryRestoreAsync(
                PendingCertificateKeyContext context,
                Certificate certificateWithPrivateKey,
                CancellationToken cancellationToken = default)
            {
                RestoreCalled = true;
                RestoreUsedCancelledToken = cancellationToken.IsCancellationRequested;
                RestoreSucceeded = await inner.TryRestoreAsync(context, certificateWithPrivateKey, cancellationToken)
                    .ConfigureAwait(false);
                return RestoreSucceeded;
            }

            public ValueTask RemoveAsync(
                PendingCertificateKeyContext context,
                CancellationToken cancellationToken = default)
            {
                return inner.RemoveAsync(context, cancellationToken);
            }
        }

        private ServerFixture<ReferenceServer> m_fixture;
        private ConfigurationNodeManager m_manager;
        private ServerConfigurationState m_node;
        private SessionSystemContext m_context;
        private string m_path;
        private static readonly string[] s_domains = ["localhost", "127.0.0.1"];
    }
}
