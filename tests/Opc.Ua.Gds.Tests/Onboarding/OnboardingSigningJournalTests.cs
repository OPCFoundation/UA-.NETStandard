/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingClientSample;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using ManagerDescription = Opc.Ua.Onboarding.ManagerDescription;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the opt-in sample signing journal with real RSA keys, a real directory certificate store and a real
    /// journal file: restart, foreign bindings, stale assignments and lost FinishRequest responses.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingSigningJournalTests
    {
        /// <summary>
        /// Creates an isolated journal directory, a private identity, a registrar CA and a strict manager.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "signing-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_root);
            m_journalPath = Path.Combine(m_root, "signing-request.journal");
            m_keyStorePath = Path.Combine(m_root, "request-keys");
            Directory.CreateDirectory(m_keyStorePath);
            string applicationStorePath = Path.Combine(m_root, "application");
            Directory.CreateDirectory(applicationStorePath);
            m_keyStore = new CertificateStoreIdentifier(m_keyStorePath, CertificateStoreType.Directory, false);
            m_identity = CertificateBuilder.Create("CN=Sample DCA")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(2))
                .AddExtension(new X509SubjectAltNameExtension(kApplicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_authority = CertificateBuilder.Create("CN=Sample Registrar CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            m_validatedChains = [];
            m_installed = [];
            m_issued = [];
            m_manager = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_manager.Setup(m => m.AcquireApplicationCertificateByType(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType))
                .Returns(() =>
                {
                    using var issuers = new CertificateCollection();
                    return new CertificateEntry(
                        m_identity, issuers, CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
                });
            m_manager.Setup(m => m.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier?, ValidationOptions?, CancellationToken>(
                    (chain, _, options, _) =>
                    {
                        Assert.That(options!.RejectUnknownRevocationStatus, Is.True);
                        Assert.That(options.AutoAcceptUntrustedCertificates, Is.False);
                        var thumbprints = new List<string>();
                        for (int i = 0; i < chain.Count; i++)
                        {
                            thumbprints.Add(chain[i].Thumbprint);
                        }
                        m_validatedChains.Add(thumbprints);
                    })
                .ReturnsAsync(CertificateValidationResult.Success);
            m_manager.Setup(m => m.WriteTrustListAsync(
                    TrustListIdentifier.Peers, It.IsAny<TrustListData>(), TrustListMasks.All,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            m_manager.Setup(m => m.UpdateApplicationCertificateAsync(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    It.IsAny<Certificate>(), It.IsAny<CertificateCollection>(), It.IsAny<CancellationToken>()))
                .Callback<NodeId, Certificate, CertificateCollection?, CancellationToken>(
                    (_, certificate, _, _) =>
                    {
                        Assert.That(certificate.HasPrivateKey, Is.True);
                        m_installed.Add(certificate.Thumbprint);
                    })
                .Returns(Task.CompletedTask);
            m_identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = applicationStorePath,
                CertificateType = CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                RawData = m_identity.RawData
            };
            m_configuration = CreateConfiguration(kApplicationUri);
            m_namespaces = new NamespaceTable();
            m_binding = CreateBinding(m_namespaces.GetIndexOrAppend(kRegistrarNamespace));
        }

        /// <summary>
        /// Releases fixture-owned keys and removes the journal directory.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (Certificate issued in m_issued)
            {
                issued.Dispose();
            }
            m_identity.Dispose();
            m_authority.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, true);
            }
        }

        /// <summary>
        /// Each request uses a fresh key held only in the signing-key store; the journal carries public data only.
        /// </summary>
        [Test]
        public async Task FreshRequestKeyIsRetainedOnlyInSigningKeyStoreAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);

            ByteString csr = await credentials.CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile)
                .ConfigureAwait(false);

            Assert.That(new Pkcs10CertificationRequest(csr.ToArray()).Verify(), Is.True);
            Assert.That(await credentials.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.Prepared));
            using Certificate retained = await LoadRetainedKeyAsync().ConfigureAwait(false);
            Assert.That(retained.HasPrivateKey, Is.True);
            Assert.That(X509Utils.GetApplicationUrisFromCertificate(retained), Does.Contain(kApplicationUri));
            Assert.That(Modulus(retained), Is.Not.EqualTo(Modulus(m_identity)));
            Certificate issued = Issue(csr, kApplicationUri);
            Assert.That(Modulus(issued), Is.EqualTo(Modulus(retained)));
            byte[] journal = File.ReadAllBytes(m_journalPath);
            Assert.That(Contains(journal, csr.ToArray()), Is.True);
            string[] privateFiles = Directory.GetFiles(Path.Combine(m_keyStorePath, "private"));
            Assert.That(privateFiles, Has.Length.EqualTo(1));
            Assert.That(Contains(journal, File.ReadAllBytes(privateFiles[0])), Is.False);
#if !NETFRAMEWORK
            // .NET Framework CNG imports do not permit plaintext parameter export, so only modern runtimes check D.
            using RSA privateKey = retained.GetRSAPrivateKey()!;
            RSAParameters parameters = privateKey.ExportParameters(true);
            Assert.That(Contains(journal, parameters.D!), Is.False);
            Assert.That(Contains(journal, parameters.P!), Is.False);
#endif
        }

        /// <summary>
        /// After a restart with a reordered namespace table, only the certificate for the retained key is recovered
        /// and installed, and the request key and journal are removed afterwards.
        /// </summary>
        [Test]
        public async Task RestartRestoresStartedRequestAndRecoversOnlyItsCertificateAsync()
        {
            ByteString csr = await PrepareAndStartAsync(CreateJournaled(m_namespaces), m_binding)
                .ConfigureAwait(false);
            var reordered = new NamespaceTable();
            reordered.GetIndexOrAppend("urn:sample:unrelated");
            ushort index = reordered.GetIndexOrAppend(kRegistrarNamespace);
            Assert.That(index, Is.Not.EqualTo(m_binding.ApplicationId.NamespaceIndex));
            Binding moved = CreateBinding(index);
            OnboardingDeviceCredentials restarted = CreateJournaled(reordered);

            OnboardingJournaledSigningRequest restored = await restarted
                .RestoreJournaledSigningRequestAsync(moved.Registrar).ConfigureAwait(false);
            Certificate issued = Issue(csr, kApplicationUri);
            PullDcaCredentials recovered = CreateCredentials(issued);
            await restarted.ValidateAsync(
                moved.Registrar, moved.ApplicationId, moved.RequestId, moved.Profile, recovered)
                .ConfigureAwait(false);
            await restarted.InstallAsync(moved.ApplicationId, recovered).ConfigureAwait(false);

            Assert.That(restored.ApplicationId, Is.EqualTo(moved.ApplicationId));
            Assert.That(restored.RequestId, Is.EqualTo(moved.RequestId));
            Assert.That(restored.Profile, Is.EqualTo(moved.Profile));
            Assert.That(m_validatedChains[0][0], Is.EqualTo(issued.Thumbprint));
            Assert.That(m_installed, Is.EqualTo(new[] { issued.Thumbprint }));
            Assert.That(await restarted.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.None));
            Assert.That(File.Exists(m_journalPath), Is.False);
            Assert.That(await CountRetainedKeysAsync().ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// A request started at one registrar cannot be restored or recovered through another registrar binding.
        /// </summary>
        [TestCase("endpoint")]
        [TestCase("registrar")]
        [TestCase("directory")]
        public async Task ForeignRegistrarCannotRestoreOrRecoverStartedRequestAsync(string field)
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            ushort index = m_binding.ApplicationId.NamespaceIndex;
            PullDcaRegistrar foreign = field switch
            {
                "endpoint" => m_binding.Registrar with { EndpointUrl = new Uri("opc.tcp://other.example:4840/") },
                "registrar" => m_binding.Registrar with { RegistrarId = new NodeId("other-registrar", index) },
                _ => m_binding.Registrar with { CertificateDirectoryId = new NodeId("other-directory", index) }
            };
            PullDcaCredentials recovered = CreateCredentials(Issue(csr, kApplicationUri));

            ServiceResultException? restore = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.RestoreJournaledSigningRequestAsync(foreign).ConfigureAwait(false));
            ServiceResultException? recovery = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    foreign, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile, recovered)
                    .ConfigureAwait(false));

            Assert.That(restore!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(recovery!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// Recovery accepts only the exact journaled application, request and certificate group.
        /// </summary>
        [TestCase("application")]
        [TestCase("request")]
        [TestCase("group")]
        [TestCase("type")]
        public async Task RecoveryRejectsDifferentRequestBindingAsync(string field)
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            ushort index = m_binding.ApplicationId.NamespaceIndex;
            Binding other = field switch
            {
                "application" => m_binding with { ApplicationId = new NodeId("other-dca", index) },
                "request" => m_binding with { RequestId = new NodeId("other-request", index) },
                "group" => m_binding with
                {
                    Profile = m_binding.Profile with { CertificateGroupId = new NodeId("other-group", index) }
                },
                _ => m_binding with
                {
                    Profile = m_binding.Profile with { CertificateTypeId = new NodeId("other-type", index) }
                }
            };

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    other.Registrar, other.ApplicationId, other.RequestId, other.Profile,
                    CreateCredentials(Issue(csr, kApplicationUri))).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// A validly issued certificate for the long-lived identity key is a stale assignment and never matches.
        /// </summary>
        [Test]
        public async Task StaleCertificateForIdentityKeyIsRejectedBeforeTrustValidationAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            ByteString staleCsr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(m_identity));
            PullDcaCredentials stale = CreateCredentials(Issue(staleCsr, kApplicationUri));

            ServiceResultException? recovery = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile, stale)
                    .ConfigureAwait(false));
            ServiceResultException? install = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.InstallAsync(m_binding.ApplicationId, stale).ConfigureAwait(false));

            Assert.That(recovery!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(install!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// A certificate issued for an earlier, discarded request key cannot complete a newer request.
        /// </summary>
        [Test]
        public async Task CertificateForDiscardedEarlierRequestCannotCompleteNewRequestAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString earlier = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            await credentials.DiscardSigningRequestAsync().ConfigureAwait(false);
            ByteString current = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            Assert.That(current, Is.Not.EqualTo(earlier));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                    CreateCredentials(Issue(earlier, kApplicationUri))).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// A certificate for the retained key that names another application is rejected.
        /// </summary>
        [Test]
        public async Task CertificateForAnotherApplicationUriIsRejectedAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                    CreateCredentials(Issue(csr, "urn:sample:other"))).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// A matching key and URI cannot bypass trust validation or an incomplete trust-list rejection.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecoveryRejectsUntrustedOrIncompleteTrustContextAsync(bool incomplete)
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            TrustListDataType trust = CompleteTrustList();
            if (incomplete)
            {
                trust.SpecifiedLists = (uint)TrustListMasks.TrustedCertificates;
            }
            else
            {
                m_manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CertificateValidationResult(
                        false, StatusCodes.BadCertificateUntrusted, [], false));
            }
            var received = new PullDcaCredentials(
                Encoded(Issue(csr, kApplicationUri)), [Encoded(m_authority)], trust);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile, received)
                    .ConfigureAwait(false));

            StatusCode expected = incomplete
                ? StatusCodes.BadSecurityChecksFailed : StatusCodes.BadCertificateUntrusted;
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
        }

        /// <summary>
        /// A failure after writing the issued key pair leaves the request resumable without adding it twice.
        /// </summary>
        [Test]
        public async Task RestartRetriesPartialInstallationWithAlreadyStoredIssuedKeyAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await PrepareAndStartAsync(credentials, m_binding).ConfigureAwait(false);
            Certificate issued = Issue(csr, kApplicationUri);
            PullDcaCredentials received = CreateCredentials(issued);
            var failure = new IOException("Trust-store publication failed.");
            m_manager.Setup(value => value.WriteTrustListAsync(
                TrustListIdentifier.Peers, It.IsAny<TrustListData>(), TrustListMasks.All,
                It.IsAny<CancellationToken>())).Returns(Task.FromException(failure));

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await credentials.InstallAsync(m_binding.ApplicationId, received).ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            await AssertStartedWithoutInstallationAsync(credentials).ConfigureAwait(false);
            m_manager.Setup(value => value.WriteTrustListAsync(
                TrustListIdentifier.Peers, It.IsAny<TrustListData>(), TrustListMasks.All,
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            OnboardingDeviceCredentials restarted = CreateJournaled(m_namespaces);
            await restarted.RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false);
            await restarted.InstallAsync(m_binding.ApplicationId, received).ConfigureAwait(false);

            Assert.That(m_installed, Is.EqualTo(new[] { issued.Thumbprint }));
            Assert.That(await restarted.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.None));
            Assert.That(await CountRetainedKeysAsync().ConfigureAwait(false), Is.Zero);

        }

        /// <summary>
        /// A prepared request whose StartSigningRequest outcome is unknown is never restored, reused or restarted;
        /// only an explicit discard clears it, and the next request uses a new key.
        /// </summary>
        [Test]
        public async Task PreparedRequestWithUnknownStartOutcomeMustBeDiscardedAsync()
        {
            ByteString prepared = await CreateJournaled(m_namespaces)
                .CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile).ConfigureAwait(false);
            OnboardingDeviceCredentials restarted = CreateJournaled(m_namespaces);

            ServiceResultException? restore = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false));
            ServiceResultException? repeat = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile)
                    .ConfigureAwait(false));
            ServiceResultException? recovery = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                    CreateCredentials(Issue(prepared, kApplicationUri))).ConfigureAwait(false));

            Assert.That(restore!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(repeat!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(recovery!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await restarted.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.Prepared));

            await restarted.DiscardSigningRequestAsync().ConfigureAwait(false);

            Assert.That(File.Exists(m_journalPath), Is.False);
            Assert.That(await CountRetainedKeysAsync().ConfigureAwait(false), Is.Zero);
            ByteString next = await restarted.CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile)
                .ConfigureAwait(false);
            Assert.That(next, Is.Not.EqualTo(prepared));
            Assert.That(m_installed, Is.Empty);
        }

        /// <summary>
        /// The started-request hook binds only the journaled CSR, and only once.
        /// </summary>
        [Test]
        public async Task ObserverRejectsDifferentCsrAndSecondBindingAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            ByteString csr = await credentials.CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile)
                .ConfigureAwait(false);
            ByteString other = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(m_identity));

            ServiceResultException? mismatch = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.OnSigningRequestStartedAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile, other)
                    .ConfigureAwait(false));
            Assert.That(mismatch!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(await credentials.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.Prepared));

            await credentials.OnSigningRequestStartedAsync(
                m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile, csr)
                .ConfigureAwait(false);
            ServiceResultException? rebind = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await credentials.OnSigningRequestStartedAsync(
                    m_binding.Registrar, m_binding.ApplicationId,
                    new NodeId("second-request", m_binding.RequestId.NamespaceIndex), m_binding.Profile, csr)
                    .ConfigureAwait(false));

            Assert.That(rebind!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            OnboardingJournaledSigningRequest restored = await CreateJournaled(m_namespaces)
                .RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false);
            Assert.That(restored.RequestId, Is.EqualTo(m_binding.RequestId));
        }

        /// <summary>
        /// Without the retained private key a started request can be neither restored nor recovered.
        /// </summary>
        [Test]
        public async Task MissingRetainedKeyBlocksRestoreAndRecoveryAsync()
        {
            ByteString csr = await PrepareAndStartAsync(CreateJournaled(m_namespaces), m_binding)
                .ConfigureAwait(false);
            using (Certificate retained = await LoadRetainedKeyAsync().ConfigureAwait(false))
            using (ICertificateStore store = OpenKeyStore())
            {
                Assert.That(await store.DeleteAsync(retained.Thumbprint).ConfigureAwait(false), Is.True);
            }
            OnboardingDeviceCredentials restarted = CreateJournaled(m_namespaces);

            ServiceResultException? restore = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false));
            ServiceResultException? recovery = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.ValidateAsync(
                    m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                    CreateCredentials(Issue(csr, kApplicationUri))).ConfigureAwait(false));

            Assert.That(restore!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(recovery!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_validatedChains, Is.Empty);
            Assert.That(m_installed, Is.Empty);
        }

        /// <summary>
        /// A truncated, extended or unknown-version journal is rejected instead of being reinterpreted.
        /// </summary>
        [TestCase("truncated")]
        [TestCase("trailing")]
        [TestCase("version")]
        public async Task CorruptJournalIsRejectedAsync(string corruption)
        {
            await PrepareAndStartAsync(CreateJournaled(m_namespaces), m_binding).ConfigureAwait(false);
            byte[] journal = File.ReadAllBytes(m_journalPath);
            switch (corruption)
            {
                case "truncated":
                    Array.Resize(ref journal, journal.Length - 4);
                    break;
                case "trailing":
                    Array.Resize(ref journal, journal.Length + 1);
                    break;
                default:
                    journal[0] = 2;
                    break;
            }
            File.WriteAllBytes(m_journalPath, journal);
            OnboardingDeviceCredentials restarted = CreateJournaled(m_namespaces);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.GetSigningRequestStateAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false));
            Assert.That(m_installed, Is.Empty);
        }

        /// <summary>
        /// A journal written for another application URI cannot be used by this application.
        /// </summary>
        [Test]
        public async Task JournalForAnotherApplicationIsRejectedAsync()
        {
            await PrepareAndStartAsync(CreateJournaled(m_namespaces), m_binding).ConfigureAwait(false);
            OnboardingDeviceCredentials other = CreateJournaled(
                m_namespaces, CreateConfiguration("urn:sample:onboarding:other"));

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await other.GetSigningRequestStateAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            await AssertStartedWithoutInstallationAsync(CreateJournaled(m_namespaces)).ConfigureAwait(false);
        }

        /// <summary>
        /// Without an opted-in journal, journal operations and GetCertificates recovery fail explicitly.
        /// </summary>
        [Test]
        public async Task OptOutRejectsJournalAndRecoveryOperationsAsync()
        {
            var credentials = new OnboardingDeviceCredentials(m_configuration, m_telemetry);
            var received = new PullDcaCredentials(Encoded(m_identity), [], CompleteTrustList());

            ServiceResultException?[] errors =
            [
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await credentials.GetSigningRequestStateAsync().ConfigureAwait(false)),
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await credentials.RestoreJournaledSigningRequestAsync(m_binding.Registrar)
                        .ConfigureAwait(false)),
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await credentials.DiscardSigningRequestAsync().ConfigureAwait(false)),
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await credentials.OnSigningRequestStartedAsync(
                        m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                        Encoded(m_identity)).ConfigureAwait(false)),
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await credentials.ValidateAsync(
                        m_binding.Registrar, m_binding.ApplicationId, m_binding.RequestId, m_binding.Profile,
                        received).ConfigureAwait(false))
            ];

            foreach (ServiceResultException? error in errors)
            {
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
            Assert.That(File.Exists(m_journalPath), Is.False);
            Assert.That(m_validatedChains, Is.Empty);
        }

        /// <summary>
        /// The journal needs a dedicated key store and must be enabled before any request exists.
        /// </summary>
        [Test]
        public async Task UseSigningJournalRejectsUnsafeConfigurationAsync()
        {
            string storePath = m_identifier.StorePath ??
                throw new AssertionException("The application certificate store must have a path.");
            var shared = new CertificateStoreIdentifier(storePath, CertificateStoreType.Directory, false);
            var credentials = new OnboardingDeviceCredentials(m_configuration, m_telemetry);
            Assert.That(() => credentials.UseSigningJournal(m_journalPath, shared, m_namespaces),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadConfigurationError));

            credentials.UseSigningJournal(m_journalPath, m_keyStore, m_namespaces);
            Assert.That(() => credentials.UseSigningJournal(m_journalPath, m_keyStore, m_namespaces),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));

            var legacy = new OnboardingDeviceCredentials(m_configuration, m_telemetry);
            await legacy.CreateSigningRequestAsync(m_binding.ApplicationId, m_binding.Profile).ConfigureAwait(false);
            Assert.That(() => legacy.UseSigningJournal(m_journalPath, m_keyStore, m_namespaces),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));
            Assert.That(File.Exists(m_journalPath), Is.False);
        }

        /// <summary>
        /// Through the real workflow, an accepted FinishRequest whose response is lost is retried, answered with
        /// Bad_InvalidArgument, and recovered through GetCertificates for exactly the journaled request key.
        /// </summary>
        [Test]
        public async Task LostFinishResponseRecoversFreshCertificateThroughWorkflowAsync()
        {
            OnboardingDeviceCredentials credentials = CreateJournaled(m_namespaces);
            var connection = new RecoveringConnection(this, csr => Issue(csr, kApplicationUri));
            connection.FinishFailures.Enqueue(StatusCodes.BadTimeout);
            connection.FinishFailures.Enqueue(StatusCodes.BadInvalidArgument);

            PullDcaResult result = await CreateWorkflow(connection, credentials)
                .RunAsync(m_binding.Registrar, CreateRequest()).ConfigureAwait(false);

            Assert.That(connection.Starts, Is.EqualTo(1));
            Assert.That(connection.Finishes, Is.EqualTo(2));
            Assert.That(connection.CertificateReads, Is.EqualTo(1));
            Assert.That(connection.Assigned, Is.Not.Null);
            Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(Encoded(connection.Assigned!)));
            Assert.That(m_installed, Is.EqualTo(new[] { connection.Assigned!.Thumbprint }));
            Assert.That(await credentials.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.None));
            Assert.That(await CountRetainedKeysAsync().ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// A stale assignment returned after a lost FinishRequest response is rejected and leaves the request
        /// started; after a restart the same request completes with its fresh certificate and no new Start.
        /// </summary>
        [Test]
        public async Task StaleAssignmentAfterLostFinishResponseIsRejectedThenRecoveredAfterRestartAsync()
        {
            ByteString staleCsr = ByteString.From(DefaultCertificateFactory.Instance.CreateSigningRequest(m_identity));
            var first = new RecoveringConnection(this, _ => Issue(staleCsr, kApplicationUri));
            first.FinishFailures.Enqueue(StatusCodes.BadInvalidArgument);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateWorkflow(first, CreateJournaled(m_namespaces))
                    .RunAsync(m_binding.Registrar, CreateRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(first.Starts, Is.EqualTo(1));
            Assert.That(m_installed, Is.Empty);
            OnboardingDeviceCredentials restarted = CreateJournaled(m_namespaces);
            Assert.That(await restarted.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.Started));
            OnboardingJournaledSigningRequest restored = await restarted
                .RestoreJournaledSigningRequestAsync(m_binding.Registrar).ConfigureAwait(false);
            ByteString original = first.SigningRequest;
            var second = new RecoveringConnection(this, _ => Issue(original, kApplicationUri));
            second.FinishFailures.Enqueue(StatusCodes.BadInvalidArgument);

            PullDcaCredentials installed = await CreateWorkflow(second, restarted)
                .CompleteCertificateRequestAsync(
                    restored.Registrar, restored.ApplicationId, restored.RequestId, restored.Profile)
                .ConfigureAwait(false);

            Assert.That(second.Starts, Is.Zero);
            Assert.That(second.CertificateReads, Is.EqualTo(1));
            Assert.That(installed.Certificate, Is.EqualTo(Encoded(second.Assigned!)));
            Assert.That(m_installed, Is.EqualTo(new[] { second.Assigned!.Thumbprint }));
            Assert.That(await restarted.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.None));
        }

        /// <summary>
        /// Builds an application configuration with the shared manager and identity for an application URI.
        /// </summary>
        private ApplicationConfiguration CreateConfiguration(string applicationUri)
        {
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationUri = applicationUri,
                CertificateManager = m_manager.Object,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificates = [m_identifier]
                }
            };
        }

        /// <summary>
        /// Creates a provider instance, as after a process restart, using the shared journal and key store.
        /// </summary>
        private OnboardingDeviceCredentials CreateJournaled(
            NamespaceTable namespaces,
            ApplicationConfiguration? configuration = null)
        {
            var credentials = new OnboardingDeviceCredentials(configuration ?? m_configuration, m_telemetry);
            credentials.UseSigningJournal(m_journalPath, m_keyStore, namespaces);
            return credentials;
        }

        /// <summary>
        /// Creates the registrar, application, request and profile identifiers in one namespace.
        /// </summary>
        private static Binding CreateBinding(ushort namespaceIndex)
        {
            return new Binding(
                new PullDcaRegistrar(
                    new Uri(kRegistrarUrl),
                    new NodeId("registrar", namespaceIndex),
                    new NodeId("directory", namespaceIndex)),
                new NodeId("dca", namespaceIndex),
                new NodeId("request", namespaceIndex),
                new PullDcaCertificateProfile(
                    new NodeId("group", namespaceIndex), CoreObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        /// <summary>
        /// Prepares a journaled CSR and records it as started, as the workflow hook does after StartSigningRequest.
        /// </summary>
        private static async Task<ByteString> PrepareAndStartAsync(
            OnboardingDeviceCredentials credentials,
            Binding binding)
        {
            ByteString csr = await credentials.CreateSigningRequestAsync(binding.ApplicationId, binding.Profile)
                .ConfigureAwait(false);
            await credentials.OnSigningRequestStartedAsync(
                binding.Registrar, binding.ApplicationId, binding.RequestId, binding.Profile, csr)
                .ConfigureAwait(false);
            return csr;
        }

        /// <summary>
        /// Signs the CSR's public key with the registrar CA, as a certificate manager would.
        /// </summary>
        private Certificate Issue(ByteString csr, string applicationUri)
        {
            var request = new Pkcs10CertificationRequest(csr.ToArray());
            Certificate issued = CertificateBuilder.Create(request.Subject)
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(30))
                .SetIssuer(m_authority)
                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                .CreateForRSA();
            m_issued.Add(issued);
            return issued;
        }

        /// <summary>
        /// Wraps an issued certificate with the registrar chain and a complete trust list.
        /// </summary>
        private PullDcaCredentials CreateCredentials(Certificate issued)
        {
            return new PullDcaCredentials(Encoded(issued), [Encoded(m_authority)], CompleteTrustList());
        }

        /// <summary>
        /// Returns a complete trust list containing the registrar CA.
        /// </summary>
        private TrustListDataType CompleteTrustList()
        {
            return new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = [Encoded(m_authority)],
                IssuerCertificates = [Encoded(m_authority)]
            };
        }

        /// <summary>
        /// Creates the real workflow over the scripted connection, with the sample as credential and recovery
        /// provider.
        /// </summary>
        private static PullDcaWorkflow CreateWorkflow(
            RecoveringConnection connection,
            OnboardingDeviceCredentials credentials)
        {
            return new PullDcaWorkflow(
                new PullDcaOptions { MaximumFinishAttempts = 2, FinishRetryDelay = TimeSpan.Zero },
                (_, _) => new ValueTask<IPullDcaConnection>(connection),
                new SelectedIdentity(),
                credentials,
                signingRecovery: credentials);
        }

        /// <summary>
        /// Creates a pull request that offers the fixture identity and provisions only the DCA.
        /// </summary>
        private PullDcaRequest CreateRequest()
        {
            return new PullDcaRequest(
                new PullDcaIdentityOffer([Encoded(m_identity)], [], []), m_binding.Profile, []);
        }

        /// <summary>
        /// Opens the dedicated signing-key store with private-key access.
        /// </summary>
        private ICertificateStore OpenKeyStore()
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(
                CertificateStoreType.Directory, m_telemetry);
            store.Open(m_keyStorePath, noPrivateKeys: false);
            return store;
        }

        /// <summary>
        /// Loads the single retained request key from the signing-key store.
        /// </summary>
        private async Task<Certificate> LoadRetainedKeyAsync()
        {
            using ICertificateStore store = OpenKeyStore();
            using CertificateCollection keys = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(keys, Has.Count.EqualTo(1));
            Certificate? retained = await store.LoadPrivateKeyAsync(
                keys[0].Thumbprint, null, null, CoreObjectTypeIds.RsaSha256ApplicationCertificateType, null)
                .ConfigureAwait(false);
            Assert.That(retained, Is.Not.Null);
            return retained!;
        }

        /// <summary>
        /// Counts the certificates remaining in the signing-key store.
        /// </summary>
        private async Task<int> CountRetainedKeysAsync()
        {
            using ICertificateStore store = OpenKeyStore();
            using CertificateCollection keys = await store.EnumerateAsync().ConfigureAwait(false);
            return keys.Count;
        }

        /// <summary>
        /// Asserts that a rejected recovery left the started request, its key and the application untouched.
        /// </summary>
        private async Task AssertStartedWithoutInstallationAsync(OnboardingDeviceCredentials credentials)
        {
            Assert.That(await credentials.GetSigningRequestStateAsync().ConfigureAwait(false),
                Is.EqualTo(OnboardingSigningRequestState.Started));
            Assert.That(await CountRetainedKeysAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(m_installed, Is.Empty);
            Assert.That(m_identifier.RawData, Is.EqualTo(m_identity.RawData));
        }

        /// <summary>
        /// Copies the certificate bytes independently from the certificate handle.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>
        /// Returns the RSA modulus of a certificate's public key.
        /// </summary>
        private static byte[] Modulus(Certificate certificate)
        {
            using RSA key = certificate.GetRSAPublicKey()!;
            return key.ExportParameters(false).Modulus!;
        }

        /// <summary>
        /// Reports whether a byte sequence occurs in a buffer.
        /// </summary>
        private static bool Contains(byte[] buffer, byte[] value)
        {
            return buffer.AsSpan().IndexOf(value.AsSpan()) >= 0;
        }

        /// <summary>
        /// The registrar-bound identifiers of one request.
        /// </summary>
        private sealed record Binding(
            PullDcaRegistrar Registrar,
            NodeId ApplicationId,
            NodeId RequestId,
            PullDcaCertificateProfile Profile);

        /// <summary>
        /// Selects the offered identity without changing the application certificate.
        /// </summary>
        private sealed class SelectedIdentity : IPullDcaIdentityProvider
        {
            public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
        }

        /// <summary>
        /// A registrar whose FinishRequest responses are lost, then answered with scripted failures, and whose
        /// GetCertificates assignment is produced from the started CSR.
        /// </summary>
        private sealed class RecoveringConnection : IPullDcaConnection
        {
            public RecoveringConnection(OnboardingSigningJournalTests fixture, Func<ByteString, Certificate> assign)
            {
                m_fixture = fixture;
                m_assign = assign;
            }

            public Queue<StatusCode> FinishFailures { get; } = new();
            public ByteString SigningRequest { get; private set; }
            public Certificate? Assigned { get; private set; }
            public int Starts { get; private set; }
            public int Finishes { get; private set; }
            public int CertificateReads { get; private set; }

            public ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
                PullDcaIdentityOffer offer, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<PullDcaIdentitySelection>(new PullDcaIdentitySelection(
                    offer.Identities[0], new Opc.Ua.Onboarding.DeviceIdentityTicketType(),
                    m_fixture.m_binding.ApplicationId, null));
            }

            public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return default;
            }

            public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                ArrayOf<ManagerDescription> managers = [];
                return new ValueTask<ArrayOf<ManagerDescription>>(managers);
            }

            public ValueTask<ArrayOf<ManagerDescription>> GetManagersOnInstalledChannelAsync(
                PullDcaSigningBinding? binding,
                ByteString installedCertificate,
                CancellationToken ct)
            {
                Assert.That(binding, Is.Null);
                Assert.That(installedCertificate.IsEmpty, Is.False);
                return GetManagersAsync(ct);
            }

            public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
            {
                throw new NotSupportedException("Only the DCA is provisioned.");
            }

            public ValueTask<NodeId> StartAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(applicationId, Is.EqualTo(m_fixture.m_binding.ApplicationId));
                Assert.That(profile, Is.EqualTo(m_fixture.m_binding.Profile));
                Starts++;
                SigningRequest = csr;
                return new ValueTask<NodeId>(m_fixture.m_binding.RequestId);
            }

            /// <inheritdoc/>
            public ValueTask<NodeId> StartNewKeyPairAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                throw new AssertionException("A new key pair was not requested by this signing recovery fixture.");
            }

            /// <inheritdoc/>
            public ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
                FinishNewKeyPairAsync(NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                throw new AssertionException("A new key pair was not requested by this signing recovery fixture.");
            }

            public ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
                NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(requestId, Is.EqualTo(m_fixture.m_binding.RequestId));
                Finishes++;
                if (FinishFailures.Count == 0)
                {
                    throw new InvalidOperationException("No FinishRequest outcome is scripted.");
                }
                throw new ServiceResultException(FinishFailures.Dequeue());
            }

            public ValueTask<TrustListDataType> ReadTrustListAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(certificateGroupId, Is.EqualTo(m_fixture.m_binding.Profile.CertificateGroupId));
                return new ValueTask<TrustListDataType>(m_fixture.CompleteTrustList());
            }

            public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(applicationId, Is.EqualTo(m_fixture.m_binding.ApplicationId));
                Assert.That(certificateGroupId, Is.EqualTo(m_fixture.m_binding.Profile.CertificateGroupId));
                CertificateReads++;
                Assigned = m_assign(SigningRequest);
                ArrayOf<NodeId> types = [CoreObjectTypeIds.RsaSha256ApplicationCertificateType];
                ArrayOf<ByteString> certificates = [Encoded(Assigned)];
                return new ValueTask<(ArrayOf<NodeId>, ArrayOf<ByteString>)>((types, certificates));
            }

            private readonly OnboardingSigningJournalTests m_fixture;
            private readonly Func<ByteString, Certificate> m_assign;
        }

        /// <summary>The application URI installed on this device.</summary>
        private const string kApplicationUri = "urn:sample:onboarding:dca";

        /// <summary>The registrar's model namespace.</summary>
        private const string kRegistrarNamespace = "urn:sample:registrar:model";

        /// <summary>The trusted registrar endpoint.</summary>
        private const string kRegistrarUrl = "opc.tcp://registrar.example:4840/";

        /// <summary>Telemetry for stores and configuration.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The isolated test directory.</summary>
        private string m_root = null!;

        /// <summary>The journal file path.</summary>
        private string m_journalPath = null!;

        /// <summary>The dedicated signing-key store path.</summary>
        private string m_keyStorePath = null!;

        /// <summary>The dedicated signing-key store.</summary>
        private CertificateStoreIdentifier m_keyStore = null!;

        /// <summary>The fixture-owned long-lived identity.</summary>
        private Certificate m_identity = null!;

        /// <summary>The registrar CA that signs requests.</summary>
        private Certificate m_authority = null!;

        /// <summary>Thumbprints of each chain passed to trust validation.</summary>
        private List<List<string>> m_validatedChains = null!;

        /// <summary>Thumbprints of activated application certificates.</summary>
        private List<string> m_installed = null!;

        /// <summary>Certificates issued by the fixture CA.</summary>
        private List<Certificate> m_issued = null!;

        /// <summary>The strict host certificate manager.</summary>
        private Mock<ICertificateManager> m_manager = null!;

        /// <summary>The application certificate identifier.</summary>
        private CertificateIdentifier m_identifier = null!;

        /// <summary>The application configuration.</summary>
        private ApplicationConfiguration m_configuration = null!;

        /// <summary>The registrar session namespace table.</summary>
        private NamespaceTable m_namespaces = null!;

        /// <summary>The registrar-bound request identifiers.</summary>
        private Binding m_binding = null!;
    }
}
