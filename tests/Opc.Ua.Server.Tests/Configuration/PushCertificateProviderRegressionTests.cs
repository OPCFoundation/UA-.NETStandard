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
    /// <summary>
    /// Verifies push-certificate behavior with injected pending-key providers and store-resolved certificate subjects.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    public sealed class PushCertificateProviderRegressionTests
    {
        /// <summary>
        /// Starts an isolated reference server and prepares a security-administrator call context.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_path = Path.Combine(Path.GetTempPath(), "push-provider-" + Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry));
            await m_fixture.LoadConfigurationAsync(m_path).ConfigureAwait(false);
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_context = CreateAdminContext();
        }

        /// <summary>
        /// Stops the reference server and removes its temporary certificate stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
            if (Directory.Exists(m_path))
            {
                Directory.Delete(m_path, recursive: true);
            }
        }

        /// <summary>
        /// Verifies that a provider without matching claims rejects key regeneration before any key or store mutation.
        /// </summary>
        [Test]
        public async Task LegacyPendingProviderRejectsRegenerationBeforeCreatingOrSavingAKeyAsync()
        {
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var pending = new Mock<IPendingCertificateKeyStore>(MockBehavior.Strict);
            pending.Setup(store => store.SaveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            var generator = new Mock<IPushCertificateKeyGenerator>(MockBehavior.Strict);
            generator.Setup(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() => active.Certificate.AddRef());
            using Harness harness = await CreateHarnessAsync(pending.Object, generator.Object).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() =>
                harness.Node.CreateSigningRequest.OnCallAsync(
                    m_context, harness.Node.CreateSigningRequest, harness.Node.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    active.Certificate.Subject, true, new ByteString(new byte[32]), CancellationToken.None).AsTask());

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            generator.Verify(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            pending.Verify(store => store.SaveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()),
                Times.Never);
            pending.Verify(store => store.TryTakeAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Never);
            pending.Verify(store => store.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Never);
            using Certificate unchanged = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                harness.Identifier, passwordProvider: null, harness.Configuration.ApplicationUri,
                m_fixture.Server.CurrentInstance.Telemetry).ConfigureAwait(false);
            Assert.That(unchanged.RawData, Is.EqualTo(active.Certificate.RawData));
            Assert.That(unchanged.HasPrivateKey, Is.True);
        }

        /// <summary>
        /// Verifies that legacy pending-key providers still support signing and installing with the existing private
        /// key.
        /// </summary>
        [Test]
        public async Task LegacyPendingProviderCompletesExistingKeySigningAndUpdateAsync()
        {
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var pending = new Mock<IPendingCertificateKeyStore>(MockBehavior.Strict);
            pending.Setup(store => store.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var generator = new Mock<IPushCertificateKeyGenerator>(MockBehavior.Strict);
            using Harness harness = await CreateHarnessAsync(pending.Object, generator.Object).ConfigureAwait(false);
            ServerConfigurationState node = harness.Node;
            NodeId group = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            NodeId type = ObjectTypeIds.RsaSha256ApplicationCertificateType;

            CreateSigningRequestMethodStateResult created = await node.CreateSigningRequest.OnCallAsync(
                m_context, node.CreateSigningRequest, node.NodeId,
                group, type, active.Certificate.Subject, false, ByteString.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
            Assert.That(request.Verify(), Is.True);
            var originalRequest = new Pkcs10CertificationRequest(
                DefaultCertificateFactory.Instance.CreateSigningRequest(
                    active.Certificate, X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray()));
            Assert.That(request.SubjectPublicKeyInfo, Is.EqualTo(originalRequest.SubjectPublicKeyInfo));
            pending.Verify(store => store.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Once);

            using Certificate issuer = CertificateBuilder.Create("CN=Legacy Pending Provider Issuer")
                .SetCAConstraint().CreateForRSA();
            using Certificate signed = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                harness.Configuration.ApplicationUri, harness.Configuration.ApplicationName,
                request.Subject.Name, X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray())
                .SetIssuer(issuer)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            using (ICertificateStore trusted = harness.Configuration.SecurityConfiguration.TrustedPeerCertificates
                .OpenStore(m_fixture.Server.CurrentInstance.Telemetry))
            {
                await trusted.AddAsync(issuer).ConfigureAwait(false);
            }

            UpdateCertificateMethodStateResult updated = await node.UpdateCertificate.OnCallAsync(
                m_context, node.UpdateCertificate, node.NodeId,
                group, type, new ByteString(signed.RawData), [], string.Empty, ByteString.Empty, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(updated.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult applied = await node.ApplyChanges.OnCallMethod2Async(
                m_context, node.ApplyChanges, node.NodeId, [], [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(applied.StatusCode, Is.EqualTo(StatusCodes.Good));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await harness.Manager.DrainPendingApplyChangesAsync(timeout.Token).ConfigureAwait(false);

            using Certificate installed = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                harness.Identifier, passwordProvider: null, harness.Configuration.ApplicationUri,
                m_fixture.Server.CurrentInstance.Telemetry).ConfigureAwait(false);
            Assert.That(installed.RawData, Is.EqualTo(signed.RawData));
            Assert.That(installed.HasPrivateKey, Is.True);
            using RSA privateKey = installed.GetRSAPrivateKey();
            using RSA publicKey = signed.GetRSAPublicKey();
            byte[] hash = new byte[32];
            byte[] signature = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(publicKey.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                Is.True);
            generator.Verify(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            pending.Verify(store => store.SaveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()),
                Times.Never);
            pending.Verify(store => store.TryTakeAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Verifies that an omitted signing-request subject is read from the stored certificate without replacing it.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task ExistingKeySigningRequestFallsBackToLoadedCertificateSubjectAsync(string subjectName)
        {
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var pending = new Mock<IPendingCertificateKeyStore>(MockBehavior.Strict);
            pending.Setup(store => store.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var generator = new Mock<IPushCertificateKeyGenerator>(MockBehavior.Strict);
            using Harness harness = await CreateHarnessAsync(pending.Object, generator.Object, includeSubject: false)
                .ConfigureAwait(false);
            Assert.That(harness.Configuration.CertificateManager, Is.Null);
            Assert.That(harness.Identifier.SubjectName, Is.Null);
            ServerConfigurationState node = harness.Node;

            CreateSigningRequestMethodStateResult created = await node.CreateSigningRequest.OnCallAsync(
                m_context, node.CreateSigningRequest, node.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                subjectName, false, ByteString.Empty, CancellationToken.None).ConfigureAwait(false);

            Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
            Assert.That(request.Verify(), Is.True);
            Assert.That(request.Subject.RawData, Is.EqualTo(active.Certificate.SubjectName.RawData));
            var originalRequest = new Pkcs10CertificationRequest(
                DefaultCertificateFactory.Instance.CreateSigningRequest(
                    active.Certificate, X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray()));
            Assert.That(request.SubjectPublicKeyInfo, Is.EqualTo(originalRequest.SubjectPublicKeyInfo));
            Assert.That(request.Attributes, Is.EqualTo(originalRequest.Attributes));
            await AssertStoredCertificateAsync(harness, active.Certificate).ConfigureAwait(false);
            generator.Verify(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            pending.Verify(store => store.SaveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that regeneration resolves an omitted subject from storage before creating a distinct pending key.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public async Task RegeneratedSigningRequestResolvesStoredSubjectBeforeCreatingKeyAsync(string subjectName)
        {
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var pending = new InMemoryPendingCertificateKeyStore();
            using Harness harness = await CreateHarnessAsync(
                pending, new AdditionalEntropyCertificateKeyGenerator(), includeSubject: false).ConfigureAwait(false);
            Assert.That(harness.Configuration.CertificateManager, Is.Null);
            Assert.That(harness.Identifier.SubjectName, Is.Null);
            var context = new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(
                    harness.Identifier.StorePath, harness.Identifier.StoreType, noPrivateKeys: false),
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                PasswordProvider: null,
                m_fixture.Server.CurrentInstance.Telemetry);
            try
            {
                ServerConfigurationState node = harness.Node;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                CreateSigningRequestMethodStateResult created = await node.CreateSigningRequest.OnCallAsync(
                    m_context, node.CreateSigningRequest, node.NodeId,
                    context.CertificateGroupId, context.CertificateTypeId,
                    subjectName, true, new ByteString(new byte[32]), timeout.Token).ConfigureAwait(false);

                Assert.That(created.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                var request = new Pkcs10CertificationRequest(created.CertificateRequest.ToArray());
                Assert.That(request.Verify(), Is.True);
                Assert.That(request.Subject.RawData, Is.EqualTo(active.Certificate.SubjectName.RawData));
                using Certificate saved = await pending.TryTakeAsync(context).ConfigureAwait(false);
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved.HasPrivateKey, Is.True);
                var savedRequest = new Pkcs10CertificationRequest(
                    DefaultCertificateFactory.Instance.CreateSigningRequest(saved));
                Assert.That(request.SubjectPublicKeyInfo, Is.EqualTo(savedRequest.SubjectPublicKeyInfo));
                var originalRequest = new Pkcs10CertificationRequest(
                    DefaultCertificateFactory.Instance.CreateSigningRequest(active.Certificate));
                Assert.That(request.SubjectPublicKeyInfo, Is.Not.EqualTo(originalRequest.SubjectPublicKeyInfo));
                await AssertStoredCertificateAsync(harness, active.Certificate).ConfigureAwait(false);
            }
            finally
            {
                await pending.RemoveAsync(context).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that regeneration without a resolvable subject fails before creating, saving, or removing keys.
        /// </summary>
        [Test]
        public async Task RegenerationWithoutResolvableSubjectRejectsBeforeCreatingOrSavingKeyAsync()
        {
            var pending = new Mock<IPeekablePendingCertificateKeyStore>(MockBehavior.Strict);
            var generator = new Mock<IPushCertificateKeyGenerator>(MockBehavior.Strict);
            using Harness harness = await CreateHarnessAsync(
                pending.Object, generator.Object, includeSubject: false).ConfigureAwait(false);
            using ICertificateStore store = new CertificateStoreIdentifier(
                harness.Identifier.StorePath, harness.Identifier.StoreType, noPrivateKeys: false)
                .OpenStore(m_fixture.Server.CurrentInstance.Telemetry);
            Assert.That(await store.DeleteAsync(harness.Identifier.Thumbprint).ConfigureAwait(false), Is.True);
            ServerConfigurationState node = harness.Node;

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() =>
                node.CreateSigningRequest.OnCallAsync(
                    m_context, node.CreateSigningRequest, node.NodeId,
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    string.Empty, true, new ByteString(new byte[32]), CancellationToken.None).AsTask());

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            generator.Verify(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            pending.Verify(value => value.SaveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(), It.IsAny<CancellationToken>()),
                Times.Never);
            pending.Verify(value => value.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Never);
            using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certificates, Is.Empty);
        }

        /// <summary>
        /// Verifies that malformed subjects report an argument error while preserving active and pending certificates.
        /// </summary>
        [TestCase("not-a-distinguished-name", false)]
        [TestCase("not-a-distinguished-name", true)]
        [TestCase("CN=\"unterminated", false)]
        [TestCase("CN=\"unterminated", true)]
        public async Task MalformedSigningRequestSubjectReturnsBadInvalidArgumentWithoutMutationAsync(
            string subjectName, bool regeneratePrivateKey)
        {
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            using Certificate previousPending = CertificateBuilder.Create("CN=Pending Subject Regression")
                .CreateForRSA();
            var pending = new InMemoryPendingCertificateKeyStore();
            var generator = new Mock<IPushCertificateKeyGenerator>(MockBehavior.Strict);
            generator.Setup(value => value.CreateApplicationCertificate(
                It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() => active.Certificate.AddRef());
            using Harness harness = await CreateHarnessAsync(pending, generator.Object).ConfigureAwait(false);
            var context = new PendingCertificateKeyContext(
                new CertificateStoreIdentifier(
                    harness.Identifier.StorePath, harness.Identifier.StoreType, noPrivateKeys: false),
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                PasswordProvider: null,
                m_fixture.Server.CurrentInstance.Telemetry);
            Assert.That(await pending.SaveAsync(context, previousPending).ConfigureAwait(false), Is.True);
            try
            {
                ServerConfigurationState node = harness.Node;
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() =>
                    node.CreateSigningRequest.OnCallAsync(
                        m_context, node.CreateSigningRequest, node.NodeId,
                        context.CertificateGroupId, context.CertificateTypeId,
                        subjectName, regeneratePrivateKey, new ByteString(new byte[32]), CancellationToken.None)
                    .AsTask());

                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(error.InnerException, Is.TypeOf<CryptographicException>());
                generator.Verify(value => value.CreateApplicationCertificate(
                    It.IsAny<PushCertificateKeyGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
                using Certificate retained = await pending.TryTakeMatchingAsync(context, previousPending)
                    .ConfigureAwait(false);
                Assert.That(retained, Is.Not.Null);
                Assert.That(retained.RawData, Is.EqualTo(previousPending.RawData));
                Assert.That(retained.HasPrivateKey, Is.True);
                await AssertStoredCertificateAsync(harness, active.Certificate).ConfigureAwait(false);
            }
            finally
            {
                await pending.RemoveAsync(context).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that the certificate store retains exactly the expected certificate and its private key.
        /// </summary>
        private async Task AssertStoredCertificateAsync(Harness harness, Certificate expected)
        {
            ITelemetryContext telemetry = m_fixture.Server.CurrentInstance.Telemetry;
            using Certificate unchanged = await CertificateIdentifierResolver.LoadPrivateKeyAsync(
                harness.Identifier, passwordProvider: null, harness.Configuration.ApplicationUri, telemetry)
                .ConfigureAwait(false);
            Assert.That(unchanged.RawData, Is.EqualTo(expected.RawData));
            Assert.That(unchanged.HasPrivateKey, Is.True);
            using ICertificateStore store = new CertificateStoreIdentifier(
                harness.Identifier.StorePath, harness.Identifier.StoreType).OpenStore(telemetry);
            using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certificates, Has.Count.EqualTo(1));
            Assert.That(certificates[0].RawData, Is.EqualTo(expected.RawData));
        }

        /// <summary>
        /// Creates a configuration manager with isolated stores and the requested pending-key and key-generation
        /// providers.
        /// </summary>
        private async Task<Harness> CreateHarnessAsync(
            IPendingCertificateKeyStore pendingStore,
            IPushCertificateKeyGenerator keyGenerator,
            bool includeSubject = true)
        {
            IServerInternal server = m_fixture.Server.CurrentInstance;
            using CertificateEntry active = m_fixture.Server.CertificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType);
            string path = Path.Combine(m_path, "case-" + Guid.NewGuid().ToString("N"));
            var identifier = new CertificateIdentifier
            {
                StorePath = Path.Combine(path, "own"),
                StoreType = CertificateStoreType.Directory,
                SubjectName = includeSubject ? active.Certificate.Subject : null,
                Thumbprint = active.Certificate.Thumbprint,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            using (ICertificateStore store = new CertificateStoreIdentifier(
                identifier.StorePath, identifier.StoreType, noPrivateKeys: false).OpenStore(server.Telemetry))
            {
                await store.AddAsync(active.Certificate).ConfigureAwait(false);
            }
            var configuration = new ApplicationConfiguration
            {
                ApplicationName = m_fixture.Config.ApplicationName,
                ApplicationUri = m_fixture.Config.ApplicationUri,
                ApplicationType = ApplicationType.Server,
                ProductUri = m_fixture.Config.ProductUri,
                ServerConfiguration = m_fixture.Config.ServerConfiguration,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates = [identifier],
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = Path.Combine(path, "trusted") },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = Path.Combine(path, "issuers") }
                }
            };
            var manager = new ConfigurationNodeManager(
                server, configuration, server.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null, coordinator: null, pendingStore, keyGenerator)
            {
                ApplyChangesGracePeriod = TimeSpan.Zero
            };
            try
            {
                await manager.CreateAddressSpaceAsync(
                    new Dictionary<NodeId, IList<IReference>>(), CancellationToken.None).ConfigureAwait(false);
                manager.CreateServerConfiguration(server.DefaultSystemContext, configuration);
                ServerConfigurationState node = manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
                Assert.That(node, Is.Not.Null);
                return new Harness(manager, node, configuration, identifier);
            }
            catch
            {
                manager.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates an encrypted call context with the security-administrator role required by push configuration.
        /// </summary>
        private static SessionSystemContext CreateAdminContext()
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_SecurityAdmin]);
            var channel = new SecureChannelContext("push-provider", new EndpointDescription
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

        /// <summary>
        /// Owns a configuration manager and exposes its certificate store and callable configuration node.
        /// </summary>
        private sealed class Harness(
            ConfigurationNodeManager manager,
            ServerConfigurationState node,
            ApplicationConfiguration configuration,
            CertificateIdentifier identifier) : IDisposable
        {
            /// <summary>
            /// Gets the manager configured with the providers under test.
            /// </summary>
            public ConfigurationNodeManager Manager { get; } = manager;

            /// <summary>
            /// Gets the server-configuration node used to invoke push-certificate methods.
            /// </summary>
            public ServerConfigurationState Node { get; } = node;

            /// <summary>
            /// Gets the isolated application configuration that resolves certificates from storage.
            /// </summary>
            public ApplicationConfiguration Configuration { get; } = configuration;

            /// <summary>
            /// Gets the identifier locating the active RSA certificate in the isolated store.
            /// </summary>
            public CertificateIdentifier Identifier { get; } = identifier;

            /// <summary>
            /// Releases the configuration manager and its owned resources.
            /// </summary>
            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        /// <summary>
        /// Stores the temporary root for the reference server and per-case certificate stores.
        /// </summary>
        private string m_path;

        /// <summary>
        /// Hosts the reference server supplying the active application certificate.
        /// </summary>
        private ServerFixture<ReferenceServer> m_fixture;

        /// <summary>
        /// Supplies the security-administrator identity for push-certificate method calls.
        /// </summary>
        private SessionSystemContext m_context;
    }
}
