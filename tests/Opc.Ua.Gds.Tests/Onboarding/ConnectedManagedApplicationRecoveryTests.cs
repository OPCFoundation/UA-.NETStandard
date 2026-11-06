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
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingRegistrar;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Identity;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Joins the push processor, a non-singleton DCA bridge, actual managed-server configuration methods,
    /// secure peer proof and reopened enrollment, issuance, lifecycle and application/request files. Ticket admission
    /// are controlled; the CSR, signer, private-key installation, listener rotation and persistence are real.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    [NonParallelizable]
    public sealed class ConnectedManagedApplicationRecoveryTests
    {
        /// <summary>
        /// Interrupts final installed-channel proof after real activation, then reconstructs registrar services
        /// from durable storage. The retained issued peer resumes without another CSR, issuance or mutation.
        /// </summary>
        [Test]
        public async Task InterruptedManagedRotationReopensFilesAndResumesOnlyRetainedCertificate()
        {
            await using var fixture = new ConnectedFixture();
            await fixture.InitializeAsync().ConfigureAwait(false);
            DeviceRegistrarPushEnrollment retained = await fixture.InterruptRotationAsync().ConfigureAwait(false);
            CertificateRequestIssuance issued = await fixture.ReadReceiptAsync(retained).ConfigureAwait(false);
            int mutations = fixture.Bridge.Mutations;
            int dcaMutations = fixture.DcaMutations.Count;
            int signingCalls = fixture.SigningCalls;
            int requests = fixture.Database.CertificateRequests.Count;
            int publications = fixture.CheckpointPublications;
            ArrayOf<string> checkpoints = await fixture.CheckpointsAsync().ConfigureAwait(false);

            await fixture.ReopenAsync().ConfigureAwait(false);
            DeviceRegistrarPushEnrollment restored = (await fixture.Enrollments.FindAsync(
                ConnectedFixture.Product, retained.ApplicationId).ConfigureAwait(false))!;
            CertificateRequestIssuance receipt = await fixture.ReadReceiptAsync(restored).ConfigureAwait(false);

            Assert.That(restored.RequestId, Is.EqualTo(retained.RequestId));
            Assert.That(restored.CertificateRequest, Is.EqualTo(retained.CertificateRequest));
            Assert.That(restored.Identity.SelectedIdentity, Is.EqualTo(retained.Identity.SelectedIdentity));
            Assert.That(restored.Identity.EncodedTicket, Is.EqualTo(retained.Identity.EncodedTicket));
            Assert.That(receipt.Certificate, Is.EqualTo(issued.Certificate));
            Assert.That(receipt.IssuerCertificates, Is.EqualTo(issued.IssuerCertificates));
            Assert.That(receipt.PrivateKey.IsEmpty, Is.True);
            await fixture.Processor.ProcessAsync(fixture.DcaApplication).ConfigureAwait(false);

            Assert.That(fixture.Proofs.Certificates, Is.EqualTo(new[] { issued.Certificate }));
            Assert.That(fixture.Proofs.OperatorPins, Is.EqualTo(new[] { fixture.InitialManagedCertificate }));
            Assert.That(fixture.Bridge.Mutations, Is.EqualTo(mutations));
            Assert.That(fixture.DcaMutations, Has.Count.EqualTo(dcaMutations));
            Assert.That(fixture.SigningCalls, Is.EqualTo(signingCalls));
            Assert.That(fixture.Database.CertificateRequests, Has.Count.EqualTo(requests));
            Assert.That(await fixture.CheckpointsAsync().ConfigureAwait(false), Is.EqualTo(checkpoints));
            Assert.That(fixture.CheckpointPublications, Is.EqualTo(publications));
            Assert.That(await fixture.Enrollments.FindRequestAsync(
                ConnectedFixture.Product, retained.ApplicationId).ConfigureAwait(false),
                Is.EqualTo(retained.RequestId));
            Assert.That(await fixture.ReadManagedCertificateAsync().ConfigureAwait(false),
                Is.EqualTo(issued.Certificate));
            Assert.That(fixture.Identities.Invocations, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// An independently issued certificate with the same application URI and trusted CA is not a retained
        /// enrollment receipt. On restart it is rejected before secure proof, journal writes or device mutations.
        /// </summary>
        [Test]
        public async Task RestartRejectsUnrelatedInstalledCertificateBeforeWrites()
        {
            await using var fixture = new ConnectedFixture();
            await fixture.InitializeAsync().ConfigureAwait(false);
            DeviceRegistrarPushEnrollment retained = await fixture.InterruptRotationAsync().ConfigureAwait(false);
            CertificateRequestIssuance issued = await fixture.ReadReceiptAsync(retained).ConfigureAwait(false);
            ByteString unrelated = await fixture.InstallUnrelatedCertificateAsync().ConfigureAwait(false);
            Assert.That(unrelated, Is.Not.EqualTo(issued.Certificate));
            Assert.That(unrelated, Is.Not.EqualTo(fixture.InitialManagedCertificate));
            Assert.That(await fixture.ReadManagedCertificateAsync().ConfigureAwait(false), Is.EqualTo(unrelated));
            int mutations = fixture.Bridge.Mutations;
            int dcaMutations = fixture.DcaMutations.Count;
            int signingCalls = fixture.SigningCalls;
            int requests = fixture.Database.CertificateRequests.Count;
            int publications = fixture.CheckpointPublications;
            ArrayOf<string> checkpoints = await fixture.CheckpointsAsync().ConfigureAwait(false);

            await fixture.ReopenAsync().ConfigureAwait(false);
            ArrayOf<string> registrarState = await fixture.RegistrarStateAsync().ConfigureAwait(false);
            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await fixture.Processor.ProcessAsync(fixture.DcaApplication).ConfigureAwait(false));

            Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)error.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
            Assert.That(fixture.Proofs.Certificates, Is.Empty);
            Assert.That(fixture.Bridge.Mutations, Is.EqualTo(mutations));
            Assert.That(fixture.DcaMutations, Has.Count.EqualTo(dcaMutations));
            Assert.That(fixture.SigningCalls, Is.EqualTo(signingCalls));
            Assert.That(fixture.Database.CertificateRequests, Has.Count.EqualTo(requests));
            Assert.That(await fixture.CheckpointsAsync().ConfigureAwait(false), Is.EqualTo(checkpoints));
            Assert.That(fixture.CheckpointPublications, Is.EqualTo(publications));
            Assert.That(await fixture.RegistrarStateAsync().ConfigureAwait(false), Is.EqualTo(registrarState));
            Assert.That((await fixture.ReadReceiptAsync(retained).ConfigureAwait(false)).Certificate,
                Is.EqualTo(issued.Certificate));
        }

        /// <summary>
        /// The actual sample provider resolves its host-selected identity only from its dedicated secret store.
        /// </summary>
        [Test]
        public async Task ManagedSampleProviderRequiresDedicatedCredentialSecretBeforeRegistration()
        {
            await using var fixture = new ConnectedFixture();
            await fixture.InitializeAsync().ConfigureAwait(false);
            ArrayOf<string> checkpoints = await fixture.CheckpointsAsync().ConfigureAwait(false);
            DeviceRegistrarManagedApplicationTarget target = await fixture.ManagedTargets.GetTargetAsync(
                fixture.ManagedApplicationUri).ConfigureAwait(false);
            Assert.That(target.InstalledCertificate, Is.EqualTo(fixture.InitialManagedCertificate));
            await fixture.Proofs.VerifyAsync(
                new ApplicationDescription { ApplicationUri = fixture.ManagedApplicationUri },
                target, target.InstalledCertificate).ConfigureAwait(false);

            Assert.That(await fixture.RemoveManagedPasswordAsync().ConfigureAwait(false), Is.True);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.ManagedTargets.GetTargetAsync(fixture.ManagedApplicationUri).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(fixture.Database.CertificateRequests, Is.Empty);
            Assert.That(fixture.Bridge.Calls, Is.Empty);
            Assert.That(await fixture.CheckpointsAsync().ConfigureAwait(false), Is.EqualTo(checkpoints));
        }

        /// <summary>
        /// Owns two actual servers and real client sessions, with only onboarding admission controlled.
        /// Each test closes every processor/provider/writer before deleting its own workspace-relative directory.
        /// </summary>
        private sealed class ConnectedFixture : IAsyncDisposable
        {
            /// <summary>
            /// Allocates independent configuration, key-store and public-checkpoint paths.
            /// </summary>
            public ConnectedFixture()
            {
                m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                    nameof(ConnectedManagedApplicationRecoveryTests), Guid.NewGuid().ToString("N"));
                m_client = new ClientFixture(m_telemetry);
                m_managed = new ServerFixture<ReferenceServer>(CreateServer)
                {
                    AutoAccept = false
                };
                m_dca = new ServerFixture<ReferenceServer>(telemetry =>
                {
                    ReferenceServer server = CreateServer(telemetry);
                    var factory = new Mock<IAsyncNodeManagerFactory>();
                    factory.SetupGet(value => value.NamespacesUris)
                        .Returns(new[] { ConnectedManagedApplicationBridge.NamespaceUri }.ToArrayOf());
                    factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                        .Returns<IServerInternal, ApplicationConfiguration, CancellationToken>((host, config, _) =>
                        {
                            Bridge = new ConnectedManagedApplicationBridge(host, config, m_managed.Config,
                                ConnectManagedAsync, DrainManagedAsync);
                            return ValueTask.FromResult<IAsyncNodeManager>(Bridge);
                        });
                    server.AddNodeManager(factory.Object);
                    return server;
                })
                {
                    AutoAccept = false
                };
            }

            /// <summary>
            /// Gets the actual DCA bridge node manager created during server startup.
            /// </summary>
            public ConnectedManagedApplicationBridge Bridge { get; private set; } = null!;

            /// <summary>
            /// Gets the application/request database reconstructed from its JSON file.
            /// </summary>
            public JsonApplicationsDatabase Database { get; private set; } = null!;

            /// <summary>
            /// Gets the production signer with observation-only counting.
            /// </summary>
            public CountingCertificateGroup Signer { get; private set; } = null!;

            /// <summary>
            /// Gets signing calls across separately constructed CA bindings.
            /// </summary>
            public int SigningCalls => m_previousSigningCalls + Signer.SigningCalls;

            /// <summary>
            /// Gets the selected identity policy; retained restarts must not invoke it again.
            /// </summary>
            public Mock<IOnboardingIdentitySelector> Identities { get; } = new(MockBehavior.Strict);

            /// <summary>
            /// Gets actual DCA mutation observations, independent of the managed bridge counters.
            /// </summary>
            public List<string> DcaMutations { get; } = [];

            /// <summary>
            /// Gets actual complete-file publications, including identical-value replacement attempts.
            /// </summary>
            public int CheckpointPublications { get; private set; }

            /// <summary>
            /// Gets the current reconstructed production enrollment journal.
            /// </summary>
            public SecretStoreDeviceRegistrarPushEnrollmentProvider Enrollments { get; private set; } = null!;

            /// <summary>
            /// Gets the current reconstructed processor.
            /// </summary>
            public DeviceRegistrarPushProcessor Processor { get; private set; } = null!;

            /// <summary>
            /// Gets the real verifier with a deterministic final-proof interruption seam.
            /// </summary>
            public InterruptibleProof Proofs { get; private set; } = null!;

            /// <summary>
            /// Gets the DCA's actual discovery application.
            /// </summary>
            public ApplicationDescription DcaApplication { get; private set; } = null!;

            /// <summary>
            /// Gets the unchanged operator-selected managed bootstrap pin.
            /// </summary>
            public ByteString InitialManagedCertificate { get; private set; }

            /// <summary>
            /// Gets the actual sample's managed target provider.
            /// </summary>
            public PushManagedApplicationTargetProvider ManagedTargets { get; private set; } = null!;

            /// <summary>
            /// Gets the independently configured managed application URI.
            /// </summary>
            public string ManagedApplicationUri => m_managed.Config.ApplicationUri!;

            /// <summary>
            /// Creates the connected address space, a real GDS signing engine and file-backed providers.
            /// </summary>
            public async Task InitializeAsync()
            {
                await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "client"), "ConnectedRegistrar")
                    .ConfigureAwait(false);
                m_client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
                m_client.Config.SecurityConfiguration.UseValidatedCertificates = true;
                m_client.Config.CertificateManager ??= CertificateManagerFactory.Create(
                    m_client.Config.SecurityConfiguration, m_telemetry);
                await m_client.Config.CertificateManager.UpdateAsync(
                    m_client.Config.SecurityConfiguration, m_client.Config.ApplicationUri).ConfigureAwait(false);
                await m_managed.LoadConfigurationAsync(Path.Combine(m_root, "managed")).ConfigureAwait(false);
                m_managed.Config.ApplicationUri = "urn:localhost:connected:managed";
                m_managed.Config.ServerConfiguration!.UserTokenPolicies =
                    [new UserTokenPolicy(UserTokenType.UserName)];
                await TrustRegistrarAsync(m_managed.Config).ConfigureAwait(false);
                await m_managed.StartAsync().ConfigureAwait(false);
                m_managedStarted = true;
                RegisterAdministrator(m_managed.Server);
                EndpointDescription managed = await DiscoverAsync(m_managed).ConfigureAwait(false);
                InitialManagedCertificate = Leaf(managed.ServerCertificate);
                await File.WriteAllBytesAsync(Path.Combine(m_root, "managed-operator.der"),
                    InitialManagedCertificate.Span.ToArray()).ConfigureAwait(false);
                await TrustAsync(managed.ServerCertificate).ConfigureAwait(false);
                await m_dca.LoadConfigurationAsync(Path.Combine(m_root, "dca")).ConfigureAwait(false);
                m_dca.Config.ApplicationUri = "urn:localhost:connected:dca";
                m_dca.Config.ServerConfiguration!.UserTokenPolicies =
                    [new UserTokenPolicy(UserTokenType.UserName)];
                await TrustRegistrarAsync(m_dca.Config).ConfigureAwait(false);
                await m_dca.StartAsync().ConfigureAwait(false);
                m_dcaStarted = true;
                RegisterAdministrator(m_dca.Server);
                EndpointDescription dca = await DiscoverAsync(m_dca).ConfigureAwait(false);
                DcaApplication = CoreUtils.Clone(dca.Server)!;
                ByteString bootstrap = Leaf(dca.ServerCertificate);
                await TrustAsync(dca.ServerCertificate).ConfigureAwait(false);
                ObserveDcaMutations();
                Identities.Setup(value => value.SelectAsync(
                    It.IsAny<ArrayOf<ByteString>>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new OnboardingIdentitySelection(
                        bootstrap, new DeviceIdentityTicketType(), Product, "fixture-ticket", string.Empty));
                await OpenRegistrarStateAsync().ConfigureAwait(false);
                Certificate ca = Signer.Authority!.Certificates[Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType]!;
                await TrustAsync(ca.RawData.ToByteString()).ConfigureAwait(false);
                using (ICertificateStore authority =
                    new CertificateStoreIdentifier(Path.Combine(m_root, "authorities")).OpenStore(m_telemetry))
                {
                    X509CRLCollection crls = await authority.EnumerateCRLsAsync(ca, true).ConfigureAwait(false);
                    Assert.That(crls, Is.Not.Empty);
                    using ICertificateStore trusted = m_client.Config.CertificateManager
                        .OpenTrustedStore(TrustListIdentifier.Peers);
                    foreach (X509CRL crl in crls)
                    {
                        await trusted.AddCRLAsync(crl).ConfigureAwait(false);
                    }
                    m_client.Config.CertificateManager.NotifyTrustListChanged(
                        TrustListIdentifier.Peers, trustChanged: false, crlChanged: true);
                }
                using CertificateEntry registrar = m_client.Config.CertificateManager!
                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256)!;
                using Certificate publicRegistrar = Certificate.FromRawData(registrar.Certificate.RawData);
                using ICertificateStore groupTrust =
                    new CertificateStoreIdentifier(m_group!.TrustedListPath).OpenStore(m_telemetry);
                await groupTrust.AddAsync(publicRegistrar).ConfigureAwait(false);
                await OpenProvidersAsync(interrupt: true).ConfigureAwait(false);
            }

            /// <summary>
            /// Verifies a real rotation occurred and fails exactly at its final installed-channel proof.
            /// </summary>
            public async Task<DeviceRegistrarPushEnrollment> InterruptRotationAsync()
            {
                AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await Processor.ProcessAsync(DcaApplication).ConfigureAwait(false));
                Assert.That(error!.InnerExceptions, Has.Count.EqualTo(1));
                Assert.That(error.InnerExceptions[0], Is.SameAs(Proofs.Interruption));
                ApplicationRecordDataType record = Database.FindApplications(m_managed.Config.ApplicationUri!).Single();
                DeviceRegistrarPushEnrollment retained = (await Enrollments.FindAsync(Product, record.ApplicationId)
                    .ConfigureAwait(false))!;
                CertificateRequestIssuance receipt = await ReadReceiptAsync(retained).ConfigureAwait(false);
                Assert.That(receipt.Certificate, Is.Not.EqualTo(InitialManagedCertificate));
                Assert.That(await ReadManagedCertificateAsync().ConfigureAwait(false), Is.EqualTo(receipt.Certificate));
                Assert.That(Proofs.Certificates, Is.EqualTo(new[] { InitialManagedCertificate, receipt.Certificate }));
                Assert.That(Bridge.Calls.Count(static name => name == Ua.BrowseNames.CreateSigningRequest),
                    Is.EqualTo(1));
                Assert.That(Bridge.Calls, Does.Contain(Ua.BrowseNames.UpdateCertificate));
                Assert.That(Bridge.Calls, Does.Contain(Ua.BrowseNames.CloseAndUpdate));
                Assert.That(Bridge.Calls, Does.Contain(Ua.BrowseNames.ApplyChanges));
                Assert.That(Signer.SigningCalls, Is.EqualTo(2));
                Assert.That(Database.CertificateRequests, Has.Count.EqualTo(2));
                Assert.That(Database.ReadRequest(record.ApplicationId, retained.RequestId,
                    out _, out _, out _, out _, out _, out _, out _), Is.EqualTo(CertificateRequestState.Accepted));
                DeviceRegistrarDevice device = (await m_lifecycle.FindDeviceAsync(Product).ConfigureAwait(false))!;
                Assert.That(device.DcaCertificate.IsEmpty, Is.False);
                Assert.That(device.PendingDcaCertificate.IsEmpty, Is.True);
                Assert.That(device.IsSingletonRestricted, Is.False);
                Assert.That(device.Applications.ToList().Select(static application => application.ApplicationId),
                    Does.Contain(record.ApplicationId));
                return retained;
            }

            /// <summary>
            /// Disposes registrar services and reconstructs their state from files.
            /// The independent device servers remain live.
            /// </summary>
            public async Task ReopenAsync()
            {
                CloseProviders();
                Assert.That(await m_managedPasswords.RemoveAsync(m_managedPasswordId).ConfigureAwait(false), Is.True);
                ApplicationsNodeManager previousManager = m_manager!;
                JsonApplicationsDatabase previousDatabase = Database;
                FileSystemDeviceRegistrarLifecycle previousLifecycle = m_lifecycle;
                CountingCertificateGroup previousSigner = Signer;
                DeviceRegistrarDevice previousDevice = (await m_lifecycle.FindDeviceAsync(Product)
                    .ConfigureAwait(false))!;
                ByteString authority = Signer.Authority!
                    .Certificates[Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType]!.RawData.ToByteString();
                m_previousSigningCalls += Signer.SigningCalls;
                m_manager!.Dispose();
                m_manager = null;
                m_lifecycle.Dispose();
                Signer.Dispose();
                await OpenRegistrarStateAsync().ConfigureAwait(false);
                Assert.That(m_manager, Is.Not.SameAs(previousManager));
                Assert.That(Database, Is.Not.SameAs(previousDatabase));
                Assert.That(m_lifecycle, Is.Not.SameAs(previousLifecycle));
                Assert.That(Signer, Is.Not.SameAs(previousSigner));
                Assert.That(Signer.Authority!
                    .Certificates[Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType]!.RawData.ToByteString(),
                    Is.EqualTo(authority));
                DeviceRegistrarDevice restored = (await m_lifecycle.FindDeviceAsync(Product)
                    .ConfigureAwait(false))!;
                ApplicationRecordDataType application =
                    Database.FindApplications(m_managed.Config.ApplicationUri!).Single();
                Assert.That(restored.DcaCertificate.IsEmpty, Is.False);
                Assert.That(restored.DcaCertificate, Is.EqualTo(previousDevice.DcaCertificate));
                Assert.That(restored.PendingDcaCertificate.IsEmpty, Is.True);
                Assert.That(restored.IsSingletonRestricted, Is.False);
                Assert.That(restored.Applications.ToList().Select(static value => value.ApplicationId),
                    Does.Contain(application.ApplicationId));
                await OpenProvidersAsync(interrupt: false).ConfigureAwait(false);
                NodeId request = await Enrollments.FindRequestAsync(Product, application.ApplicationId)
                    .ConfigureAwait(false);
                Assert.That(Database.ReadRequest(application.ApplicationId, request,
                    out _, out _, out _, out _, out _, out _, out _), Is.EqualTo(CertificateRequestState.Accepted));
            }

            /// <summary>
            /// Reads the original request-bound public receipt without allowing an issuance factory.
            /// </summary>
            public ValueTask<CertificateRequestIssuance> ReadReceiptAsync(DeviceRegistrarPushEnrollment enrollment)
            {
                return m_receipts.GetOrCreateAsync(enrollment.ApplicationId, enrollment.RequestId,
                    _ => throw new InvalidOperationException("A retained receipt must not issue again."));
            }

            /// <summary>
            /// Snapshots complete checkpoint bytes as well as names so replacement writes cannot escape assertions.
            /// </summary>
            public async Task<ArrayOf<string>> CheckpointsAsync()
            {
                var values = new List<string>();
                foreach (string path in Directory.GetFiles(Path.Combine(m_root, "checkpoints"), "*.bin")
                    .OrderBy(static path => path, StringComparer.Ordinal))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    values.Add(Path.GetFileName(path) + ":" + Convert.ToBase64String(bytes));
                }
                return values.ToArrayOf();
            }

            /// <summary>
            /// Captures lifecycle and application/request storage to detect writes on a rejected retry.
            /// </summary>
            public async Task<ArrayOf<string>> RegistrarStateAsync()
            {
                var values = new List<string>();
                foreach (string name in new[] { "applications.json", "lifecycle.json" })
                {
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(m_root, name)).ConfigureAwait(false);
                    values.Add(name + ":" + Convert.ToBase64String(bytes));
                }
                return values.ToArrayOf();
            }

            /// <summary>
            /// Removes the credential from the isolated secret store without touching public checkpoint files.
            /// </summary>
            public ValueTask<bool> RemoveManagedPasswordAsync()
            {
                return m_managedPasswords.RemoveAsync(m_managedPasswordId);
            }

            /// <summary>
            /// Reads the certificate from the actual standard manager on a new managed-server secure session.
            /// </summary>
            public async Task<ByteString> ReadManagedCertificateAsync()
            {
                await using ISession session = await ConnectManagedAsync(CancellationToken.None).ConfigureAwait(false);
                var configuration = new ServerConfigurationTypeClient(session, Ua.ObjectIds.ServerConfiguration,
                    m_telemetry);
                var (types, certificates) =
                    await configuration.GetCertificatesAsync(NodeId.Null).ConfigureAwait(false);
                Assert.That(types.Contains(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType), Is.True);
                return certificates[types.ToList().IndexOf(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)];
            }

            /// <summary>
            /// Creates and installs a second real CA-signed CSR outside the enrollment/receipt providers.
            /// It has the same application URI and trusted issuer, but no retained request correlation.
            /// </summary>
            public async Task<ByteString> InstallUnrelatedCertificateAsync()
            {
                await using ISession session = await ConnectManagedAsync(CancellationToken.None).ConfigureAwait(false);
                var configuration = new ServerConfigurationTypeClient(session, Ua.ObjectIds.ServerConfiguration,
                    m_telemetry);
                ByteString csr = await configuration.CreateSigningRequestAsync(
                    NodeId.Null, Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, string.Empty, true,
                    Nonce.CreateRandomNonceData(32).ToByteString()).ConfigureAwait(false);
                ApplicationRecordDataType application =
                    Database.FindApplications(m_managed.Config.ApplicationUri!).Single();
                using Certificate unrelated = await Signer.Authority!.SigningRequestAsync(application,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, ["localhost"], csr).ConfigureAwait(false);
                Certificate ca = Signer.Authority.Certificates[Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType]!;
                Assert.That(await configuration.UpdateCertificateAsync(NodeId.Null,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, unrelated.RawData.ToByteString(),
                    [ca.RawData.ToByteString()], string.Empty, default).ConfigureAwait(false), Is.True);
                await configuration.ApplyChangesAsync().ConfigureAwait(false);
                await DrainManagedAsync(CancellationToken.None).ConfigureAwait(false);
                return unrelated.RawData.ToByteString();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                CloseProviders();
                m_manager?.Dispose();
                m_lifecycle?.Dispose();
                Signer?.Dispose();
                CryptographicOperations.ZeroMemory(m_password);
                try
                {
                    if (m_dcaStarted)
                    {
                        await m_dca.StopAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (m_managedStarted)
                        {
                            await m_managed.StopAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await m_client.DisposeAsync().ConfigureAwait(false);
                        if (Directory.Exists(m_root))
                        {
                            Directory.Delete(m_root, recursive: true);
                        }
                    }
                }
            }

            /// <summary>
            /// Reconstructs the request manager, CA and lifecycle using their retained storage paths.
            /// </summary>
            private async Task OpenRegistrarStateAsync()
            {
                Database = JsonApplicationsDatabase.Load(Path.Combine(m_root, "applications.json"));
                Signer = new CountingCertificateGroup(m_telemetry);
                m_group = new CertificateGroupConfiguration
                {
                    Id = "DefaultApplicationGroup",
                    SubjectName = "CN=Connected Recovery CA",
                    BaseStorePath = Path.Combine(m_root, "group"),
                    CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)],
                    DefaultCertificateKeySize = 2048
                };
                var config = new ApplicationConfiguration(m_dca.Config);
                config.UpdateExtension(null, new GlobalDiscoveryServerConfiguration
                {
                    ApplicationCertificatesStorePath = Path.Combine(m_root, "applications"),
                    AuthoritiesStorePath = Path.Combine(m_root, "authorities"),
                    CertificateGroups = [m_group]
                });
                m_manager = new ApplicationsNodeManager(
                    m_dca.Server.CurrentInstance, config, Database, Database, Signer, autoApprove: true);
                await m_manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                Database.NamespaceIndex = m_manager.ApplicationsNamespaceIndex;
                m_lifecycle = new FileSystemDeviceRegistrarLifecycle(
                    Path.Combine(m_root, "lifecycle.json"), m_dca.Server.CurrentInstance.NamespaceUris);
            }

            /// <summary>
            /// Reconstructs the production persistence adapters and selected providers without changing their contract.
            /// </summary>
            private async Task OpenProvidersAsync(bool interrupt)
            {
                var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
                LocalFileSystem local = LocalFileSystem.Instance;
                fileSystem.Setup(value => value.Exists(It.IsAny<string>(), It.IsAny<bool>()))
                    .Returns((string path, bool isDirectory) => local.Exists(path, isDirectory));
                fileSystem.Setup(value => value.GetLength(It.IsAny<string>())).Returns<string>(local.GetLength);
                fileSystem.Setup(value => value.OpenRead(It.IsAny<string>())).Returns<string>(local.OpenRead);
                fileSystem.Setup(value => value.OpenWrite(It.IsAny<string>())).Returns<string>(local.OpenWrite);
                fileSystem.Setup(value => value.Delete(It.IsAny<string>(), It.IsAny<bool>()))
                    .Callback<string, bool>(local.Delete);
                fileSystem.Setup(value => value.Replace(It.IsAny<string>(), It.IsAny<string>()))
                    .Callback<string, string>((source, destination) =>
                    {
                        local.Replace(source, destination);
                        CheckpointPublications++;
                    });
                m_files = new PushRegistrarCheckpointFiles(Path.Combine(m_root, "checkpoints"), fileSystem.Object);
                m_receipts = new PushRegistrarIssuanceStore(m_files);
                await m_receipts.OnServerStartingAsync(m_dca.Server.CurrentInstance).ConfigureAwait(false);
                m_manager!.CertificateRequestIssuanceStore = m_receipts;
                Enrollments = new SecretStoreDeviceRegistrarPushEnrollmentProvider(m_manager,
                    new PushEnrollmentCheckpointStore(m_files), "connected-recovery",
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType, m_lifecycle, m_lifecycle, m_telemetry);
                var policy = new Mock<IDeviceRegistrarOnboardingPolicy>();
                policy.Setup(value => value.EvaluateAsync(
                    It.IsAny<OnboardingIdentitySelection>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DeviceRegistrarOnboardingDecision(new ApplicationRecordDataType
                    {
                        ApplicationUri = DcaApplication.ApplicationUri,
                        ProductUri = DcaApplication.ProductUri,
                        ApplicationType = ApplicationType.Server,
                        ApplicationNames = [DcaApplication.ApplicationName],
                        DiscoveryUrls = DcaApplication.DiscoveryUrls,
                        ServerCapabilities = ["DA"]
                    }, false, null));
                m_managedPasswords = new InMemorySecretStore("ConnectedManagedCredentials");
                await m_managedPasswords.SetAsync(m_managedPasswordId, m_password).ConfigureAwait(false);
                EndpointDescription managedEndpoint = await DiscoverAsync(m_managed).ConfigureAwait(false);
                ManagedTargets = new PushManagedApplicationTargetProvider(
                    m_managed.Config.ApplicationUri!, managedEndpoint.EndpointUrl!, SecurityPolicies.Basic256Sha256,
                    Path.Combine(m_root, "managed-operator.der"), ["DA"], default,
                    new UserNamePasswordIdentityProvider(
                        m_userName, new SecretRegistry(m_managedPasswords), m_managedPasswordId),
                    () => m_client.Config);
                Proofs = new InterruptibleProof(new SessionDeviceRegistrarManagedApplicationChannelVerifier(
                    m_client.Config, m_client.SessionFactory), InitialManagedCertificate, interrupt);
                var connections = new SessionDeviceRegistrarPushConnectionProvider(
                    m_client.Config, m_client.SessionFactory, async (_, ct) =>
                    {
                        await ((ConfigurationNodeManager)m_dca.Server.CurrentInstance.ConfigurationNodeManager)
                            .DrainPendingApplyChangesAsync(ct).ConfigureAwait(false);
                        EndpointDescription endpoint = await DiscoverAsync(m_dca).ConfigureAwait(false);
                        NamespaceTable namespaces = m_dca.Server.CurrentInstance.NamespaceUris;
                        return new DeviceRegistrarPushTarget(
                            new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(m_client.Config)),
                            Administrator(), NodeId.ToExpandedNodeId(Bridge.DeviceId, namespaces),
                            NodeId.ToExpandedNodeId(Bridge.SingletonId, namespaces),
                            Ua.ObjectIds.ServerConfiguration,
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                            Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                            Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup_TrustList);
                    });
                Processor = new DeviceRegistrarPushProcessor(connections, Enrollments,
                    new OnboardingPushIdentityValidator(Identities.Object, m_telemetry), policy.Object,
                    Database, m_lifecycle, m_lifecycle, m_receipts, m_lifecycle,
                    Mock.Of<IDeviceRegistrarAuthorization>(), m_telemetry, ManagedTargets, Proofs);
            }

            /// <summary>
            /// Closes all persistence users before releasing the filesystem's exclusive writer lease.
            /// </summary>
            private void CloseProviders()
            {
                Processor?.Dispose();
                Enrollments?.Dispose();
                m_receipts?.Dispose();
                m_files?.Dispose();
            }

            /// <summary>
            /// Creates a fresh actual encrypted admin session to the managed server's current endpoint.
            /// </summary>
            private async ValueTask<ISession> ConnectManagedAsync(CancellationToken ct)
            {
                EndpointDescription endpoint = await DiscoverAsync(m_managed).ConfigureAwait(false);
                return await m_client.SessionFactory.CreateAsync(m_client.Config,
                    new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(m_client.Config)),
                    false, true, "Connected bridge", 60000, Administrator(), default, ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Waits for actual configuration-manager certificate reload and transport retirement.
            /// </summary>
            private Task DrainManagedAsync(CancellationToken ct)
            {
                return ((ConfigurationNodeManager)m_managed.Server.CurrentInstance.ConfigurationNodeManager)
                    .DrainPendingApplyChangesAsync(ct);
            }

            /// <summary>
            /// Wraps only real DCA handlers with counters, preserving their original implementation.
            /// </summary>
            private void ObserveDcaMutations()
            {
                var manager = (ConfigurationNodeManager)m_dca.Server.CurrentInstance.ConfigurationNodeManager;
                ServerConfigurationState node =
                    manager.FindPredefinedNode<ServerConfigurationState>(Ua.ObjectIds.ServerConfiguration)!;
                var csr = node.CreateSigningRequest!.OnCallAsync!;
                node.CreateSigningRequest.OnCallAsync = (c, m, o, g, t, s, r, n, ct) =>
                {
                    DcaMutations.Add(Ua.BrowseNames.CreateSigningRequest);
                    return csr(c, m, o, g, t, s, r, n, ct);
                };
                var update = node.UpdateCertificate!.OnCallAsync!;
                node.UpdateCertificate.OnCallAsync = (c, m, o, g, t, cert, issuers, f, k, ct) =>
                {
                    DcaMutations.Add(Ua.BrowseNames.UpdateCertificate);
                    return update(c, m, o, g, t, cert, issuers, f, k, ct);
                };
                var apply = node.ApplyChanges!.OnCallMethod2Async!;
                node.ApplyChanges.OnCallMethod2Async = (c, m, o, i, outputs, ct) =>
                {
                    DcaMutations.Add(Ua.BrowseNames.ApplyChanges);
                    return apply(c, m, o, i, outputs, ct);
                };
                TrustListState trust = node.CertificateGroups!.DefaultApplicationGroup!.TrustList!;
                var write = trust.Write!.OnCallAsync!;
                trust.Write.OnCallAsync = (c, m, o, h, bytes, ct) =>
                {
                    DcaMutations.Add(Ua.BrowseNames.Write);
                    return write(c, m, o, h, bytes, ct);
                };
                var close = trust.CloseAndUpdate!.OnCallAsync!;
                trust.CloseAndUpdate.OnCallAsync = (c, m, o, h, ct) =>
                {
                    DcaMutations.Add(Ua.BrowseNames.CloseAndUpdate);
                    return close(c, m, o, h, ct);
                };
            }

            /// <summary>
            /// Explicitly trusts only the fixture's initial peers and the real selected CA.
            /// </summary>
            private async Task TrustAsync(ByteString encoded)
            {
                using CertificateCollection chain =
                    DefaultCertificateFactory.Instance.ParseChainBlob(encoded.ToArray().AsMemory());
                using ICertificateStore trusted = m_client.Config.CertificateManager!
                    .OpenTrustedStore(TrustListIdentifier.Peers);
                await trusted.AddAsync(chain[0]).ConfigureAwait(false);
                m_client.Config.CertificateManager.NotifyTrustListChanged(
                    TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
            }

            /// <summary>
            /// Explicitly trusts the registrar's public credential before accepting any inbound server sessions.
            /// </summary>
            private async Task TrustRegistrarAsync(ApplicationConfiguration configuration)
            {
                using CertificateEntry registrar = m_client.Config.CertificateManager!
                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256)!;
                using Certificate certificate = Certificate.FromRawData(registrar.Certificate.RawData);
                using ICertificateStore trusted = configuration.SecurityConfiguration.TrustedPeerCertificates
                    .OpenStore(m_telemetry);
                await trusted.AddAsync(certificate).ConfigureAwait(false);
            }

            /// <summary>
            /// Discovers the actual SHA-256 encrypted endpoint after each deferred listener activation.
            /// </summary>
            private async Task<EndpointDescription> DiscoverAsync(ServerFixture<ReferenceServer> server)
            {
                using DiscoveryClient discovery = await DiscoveryClient.CreateAsync(
                    new Uri("opc.tcp://localhost:" + server.Port.ToString(CultureInfo.InvariantCulture)),
                    EndpointConfiguration.Create(m_client.Config), m_telemetry, ct: CancellationToken.None)
                    .ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints =
                    await discovery.GetEndpointsAsync(default, CancellationToken.None).ConfigureAwait(false);
                return CoreUtils.Clone(endpoints.ToList().First(static endpoint =>
                    endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256))!;
            }

            /// <summary>
            /// Extracts the authenticated leaf representation used by retained identity matching.
            /// </summary>
            private static ByteString Leaf(ByteString encoded)
            {
                using CertificateCollection chain =
                    DefaultCertificateFactory.Instance.ParseChainBlob(encoded.ToArray().AsMemory());
                return chain[0].RawData.ToByteString();
            }

            /// <summary>
            /// Creates each independent reference server.
            /// </summary>
            private static ReferenceServer CreateServer(ITelemetryContext telemetry)
            {
                return new ReferenceServer(telemetry);
            }

            /// <summary>
            /// Registers the generated account through live user management before assigning its roles.
            /// </summary>
            private void RegisterAdministrator(ReferenceServer server)
            {
                var users = server.CurrentInstance.UserManagement!;
                ServiceResult result = users.AddUser(
                    m_userName, Encoding.UTF8.GetString(m_password), 0, string.Empty);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(server.UserDatabase.CreateUser(m_userName, m_password,
                    [Role.SecurityAdmin, Role.ConfigureAdmin, Role.AuthenticatedUser]), Is.False);
                Assert.That(users.IsUserActive(m_userName), Is.True);
                Assert.That(server.UserDatabase.CheckCredentials(m_userName, m_password), Is.True);
            }

            /// <summary>
            /// Uses only the generated fixture credential on encrypted sessions.
            /// </summary>
            private UserIdentity Administrator()
            {
                return new UserIdentity(m_userName, m_password);
            }

            /// <summary>The controlled product identity bound to both real application enrollments.</summary>
            internal const string Product = "urn:opcfoundation:test:connected-device";

            /// <summary>The per-test workspace-relative persistence directory.</summary>
            private readonly string m_root;

            /// <summary>The shared test telemetry context.</summary>
            private readonly ITelemetryContext m_telemetry = NUnitTelemetryContext.Create();

            /// <summary>The real registrar client and session factory.</summary>
            private readonly ClientFixture m_client;

            /// <summary>The independently configured actual managed server.</summary>
            private readonly ServerFixture<ReferenceServer> m_managed;

            /// <summary>The actual DCA server exposing the test-only bridge.</summary>
            private readonly ServerFixture<ReferenceServer> m_dca;

            /// <summary>The lifecycle reconstructed from its checkpoint after registrar restart.</summary>
            private FileSystemDeviceRegistrarLifecycle m_lifecycle = null!;

            /// <summary>The retained CA storage configuration.</summary>
            private CertificateGroupConfiguration? m_group;

            /// <summary>Signing observations from disposed CA bindings.</summary>
            private int m_previousSigningCalls;

            /// <summary>The unique per-fixture admin account.</summary>
            private readonly string m_userName = "registrar-" + Guid.NewGuid().ToString("N");

            /// <summary>The generated credential shared only with the fixture's servers.</summary>
            private readonly byte[] m_password = Encoding.UTF8.GetBytes(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

            /// <summary>The generated managed credential store, separate from public registrar persistence.</summary>
            private InMemorySecretStore m_managedPasswords = null!;

            /// <summary>The host-selected credential identifier in the isolated store.</summary>
            private readonly SecretIdentifier m_managedPasswordId =
                new("admin-password", "ConnectedManagedCredentials");

            /// <summary>The actual GDS request service and CA binding.</summary>
            private ApplicationsNodeManager? m_manager;

            /// <summary>The exclusive file-backed enrollment/receipt writer.</summary>
            private PushRegistrarCheckpointFiles m_files = null!;

            /// <summary>The sample's production public issuance adapter.</summary>
            private PushRegistrarIssuanceStore m_receipts = null!;

            /// <summary>Records completed managed startup for failure-safe cleanup.</summary>
            private bool m_managedStarted;

            /// <summary>Records completed DCA startup for failure-safe cleanup.</summary>
            private bool m_dcaStarted;
        }

        /// <summary>
        /// Observation-only production CA factory; generated authorities still use the real signing implementation.
        /// </summary>
        private sealed class CountingCertificateGroup : CertificateGroup
        {
            /// <summary>
            /// Creates the CA factory.
            /// </summary>
            public CountingCertificateGroup(ITelemetryContext telemetry)
                : base(telemetry)
            {
                m_telemetry = telemetry;
                m_owner = this;
            }

            /// <summary>
            /// Creates a real independent authority while retaining the factory's observation counter.
            /// </summary>
            private CountingCertificateGroup(string path, CertificateGroupConfiguration configuration,
                string? issuers, CountingCertificateGroup owner)
                : base(path, configuration, owner.m_telemetry, issuers)
            {
                m_telemetry = owner.m_telemetry;
                m_owner = owner;
            }

            /// <summary>
            /// Gets actual signing invocations across authorities.
            /// </summary>
            public int SigningCalls { get; private set; }

            /// <summary>
            /// Gets the real authority selected by the GDS configuration.
            /// </summary>
            public CountingCertificateGroup? Authority { get; private set; }

            /// <inheritdoc/>
            public override ICertificateGroup Create(string authoritiesStorePath,
                CertificateGroupConfiguration certificateGroupConfiguration, string? issuerCertificatesStorePath)
            {
                Authority = new CountingCertificateGroup(authoritiesStorePath, certificateGroupConfiguration,
                    issuerCertificatesStorePath, this);
                return Authority;
            }

            /// <inheritdoc/>
            public override Task<Certificate> SigningRequestAsync(ApplicationRecordDataType application,
                NodeId certificateType, string[] domainNames, ByteString certificateRequest,
                CancellationToken ct = default)
            {
                m_owner.SigningCalls++;
                return base.SigningRequestAsync(application, certificateType, domainNames, certificateRequest, ct);
            }

            /// <summary>The shared factory telemetry.</summary>
            private readonly ITelemetryContext m_telemetry;

            /// <summary>The observation owner, not an alternate signer.</summary>
            private readonly CountingCertificateGroup m_owner;
        }

        /// <summary>
        /// Performs every proof through the production verifier and real session factory before fault injection.
        /// </summary>
        private sealed class InterruptibleProof(
            IDeviceRegistrarManagedApplicationChannelVerifier verifier,
            ByteString initial,
            bool interrupt) : IDeviceRegistrarManagedApplicationChannelVerifier
        {
            /// <summary>
            /// Gets completed actual peer proofs.
            /// </summary>
            public List<ByteString> Certificates { get; } = [];

            /// <summary>
            /// Gets unchanged operator pins supplied to each proof.
            /// </summary>
            public List<ByteString> OperatorPins { get; } = [];

            /// <summary>
            /// Gets the exact injected failure used for deterministic interruption assertions.
            /// </summary>
            public IOException Interruption { get; } = new("Interrupted at final managed certificate proof.");

            /// <inheritdoc/>
            public async ValueTask VerifyAsync(ApplicationDescription application,
                DeviceRegistrarManagedApplicationTarget target, ByteString certificate, CancellationToken ct = default)
            {
                // The target deliberately retains its operator pin and pre-rotation endpoint snapshot.
                // The production verifier must use the actual transport peer rather than that metadata.
                await verifier.VerifyAsync(application, target, certificate, ct).ConfigureAwait(false);
                Certificates.Add(certificate);
                OperatorPins.Add(target.InstalledCertificate);
                if (interrupt && certificate != initial)
                {
                    throw Interruption;
                }
            }
        }
    }
}
#endif
