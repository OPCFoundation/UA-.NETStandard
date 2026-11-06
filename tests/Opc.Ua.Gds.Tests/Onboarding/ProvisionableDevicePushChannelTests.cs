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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using Ua = Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises device ownership through real encrypted channels and the standard generated push client.
    /// Includes concrete certificate installation, endpoint activation and host-restart recovery.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    [NonParallelizable]
    public sealed class ProvisionableDevicePushChannelTests
    {
        /// <summary>
        /// Standard role checks precede ownership, distinct channel certificates cannot share a pending claim,
        /// and physical security reset releases pending registrar ownership.
        /// </summary>
        [Test]
        public async Task EncryptedPushPinsActualRegistrarUntilPhysicalSecurityReset()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "obch", Guid.NewGuid().ToString("N")[..8]);
            string applicationUri = "urn:localhost:onboarding:channel-test";
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            var url = new Uri($"opc.tcp://localhost:{port}/onboarding-channel-test");
            string password = Guid.NewGuid().ToString("N");
            var users = new LinqUserDatabase();
            Assert.That(users.CreateUser("registrar", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser, Role.SecurityAdmin]), Is.True);
            Assert.That(users.CreateUser("observer", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser]), Is.True);
            var installer = new Mock<IProvisionableDeviceSecurityInstaller>(MockBehavior.Strict);
            installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ISecretStore>(new InMemorySecretStore());
            services.AddSingleton<ICertificateManager>(provider =>
                new CertificateManager(provider.GetRequiredService<ITelemetryContext>()));
            services.AddSingleton<IProvisionableDeviceSecurityInstaller>(installer.Object);
            services.AddSingleton<IUserDatabase>(users);
            services.AddSingleton<IUserManagement>(provider =>
                new UserManagement(provider.GetRequiredService<IUserDatabase>()));
            services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "OnboardingChannelTest";
                options.ApplicationUri = applicationUri;
                options.ProductUri = "urn:opcfoundation:test:onboarding";
                options.PkiRoot = Path.Combine(root, "server");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeSignAndEncryptPolicies = true;
                options.IncludeUnsecurePolicyNone = false;
                options.IncludeEccPolicies = false;
                options.EndpointUrls.Add(url.AbsoluteUri);
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
            })
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                })
                .ConfigureRoles(options =>
                {
                    foreach (string name in s_registrarRoles)
                    {
                        options.Roles.Add(new RoleDefinitionOptions
                        {
                            Name = name,
                            Identities =
                            {
                                new RoleIdentityMappingOptions
                                {
                                    CriteriaType = IdentityCriteriaType.UserName,
                                    Criteria = "registrar"
                                }
                            }
                        });
                    }
                })
                .AddProvisionableDeviceSecurity("channel-test", applicationUri);

            ServiceProvider provider = services.BuildServiceProvider();
            IHostedService? hosted = null;
            try
            {
                hosted = provider.GetServices<IHostedService>()
                    .Single(static service => service.GetType().Name == "OpcUaServerHostedService");
                await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
                using var firstClient = new ClientFixture(telemetry: telemetry);
                using var secondClient = new ClientFixture(telemetry: telemetry);
                await firstClient.LoadClientConfigurationAsync(Path.Combine(root, "first"), "FirstRegistrar")
                    .ConfigureAwait(false);
                await secondClient.LoadClientConfigurationAsync(Path.Combine(root, "second"), "SecondRegistrar")
                    .ConfigureAwait(false);
                ProvisionableDeviceSecurity security = provider.GetRequiredService<ProvisionableDeviceSecurity>();
                using (ISession observer = await firstClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("observer", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false))
                {
                    ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await CreateSigningRequestAsync(observer, telemetry).ConfigureAwait(false));
                    Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                        Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                    await observer.CloseAsync(5000, true).ConfigureAwait(false);
                }
                using ISession first = await firstClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                using ISession second = await secondClient.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                ByteString firstRequest = await CreateSigningRequestAsync(first, telemetry).ConfigureAwait(false);
                Assert.That(firstRequest.IsEmpty, Is.False);
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
                ServiceResultException? otherRegistrar = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await CreateSigningRequestAsync(second, telemetry).ConfigureAwait(false));
                Assert.That(otherRegistrar!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                await security.ResetToServerDefaultsAsync().ConfigureAwait(false);
                ByteString secondRequest = await CreateSigningRequestAsync(second, telemetry).ConfigureAwait(false);

                Assert.That(secondRequest.IsEmpty, Is.False);
                Assert.That(secondRequest, Is.Not.EqualTo(firstRequest));
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
                installer.Verify(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
                installer.VerifyNoOtherCalls();
                await first.CloseAsync(5000, true).ConfigureAwait(false);
                await second.CloseAsync(5000, true).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (hosted != null)
                    {
                        await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }
        }

        /// <summary>
        /// Standard push activates the issued key, a fresh host verifies the committed journal, and security reset
        /// restores the retained factory identity before another registrar can claim the device.
        /// File-backed cases reopen the sample's public checkpoint store for each fresh host.
        /// The test does not launch a separate operating-system process.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
#if NET10_0_OR_GREATER
        [TestCase(false, true)]
        [TestCase(true, true)]
#endif
        public async Task ConcretePushActivatesCertificateAndRecoversAcrossHostRestartAndFactoryReset(
            bool networkReset,
            bool fileCheckpoint)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string temporary = Path.GetTempPath();
            string root = Path.Combine(temporary, "onboarding-activation", Guid.NewGuid().ToString("N"));
            const string applicationUri = "urn:localhost:onboarding:activation-test";
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            var url = new Uri($"opc.tcp://localhost:{port}/onboarding-activation-test");
            string password = Guid.NewGuid().ToString("N");
            var memoryJournal = new InMemorySecretStore();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            ByteString factoryDer = default;
            ByteString issuedDer = default;
            var factoryTrust = new TrustListDataType();
            try
            {
                using var firstClient = new ClientFixture(telemetry: telemetry);
                using var nextClient = new ClientFixture(telemetry: telemetry);
                await firstClient.LoadClientConfigurationAsync(Path.Combine(root, "first"), "ActivationRegistrar")
                    .ConfigureAwait(false);
                await nextClient.LoadClientConfigurationAsync(Path.Combine(root, "next"), "ReplacementRegistrar")
                    .ConfigureAwait(false);
                CertificateIdentifier registrarIdentifier =
                    firstClient.Config.SecurityConfiguration.ApplicationCertificates[0];
                using ICertificateStore registrarStore = new CertificateStoreIdentifier(
                    registrarIdentifier.StorePath ??
                        throw new AssertionException("The registrar certificate store path is missing."),
                    registrarIdentifier.StoreType ??
                        throw new AssertionException("The registrar certificate store type is missing."))
                    .OpenStore(telemetry);
                using CertificateCollection registrarCertificates =
                    await registrarStore.EnumerateAsync().ConfigureAwait(false);
                using Certificate registrarCertificate = registrarCertificates.Single(certificate =>
                    CertificateIdentifier.ValidateCertificateType(
                        certificate, Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)).AddRef();
                using Certificate issuer = CertificateBuilder.Create("CN=Onboarding Activation Issuer")
                    .SetCAConstraint().CreateForRSA();
                X509CRL crl = DefaultCertificateIssuer.Instance.RevokeCertificates(issuer, null!, null!);
                foreach (ClientFixture client in new[] { firstClient, nextClient })
                {
                    using ICertificateStore trusted = client.Config.CertificateManager
                        .OpenTrustedStore(TrustListIdentifier.Peers);
                    await trusted.AddAsync(issuer).ConfigureAwait(false);
                    await trusted.AddCRLAsync(crl).ConfigureAwait(false);
                    client.Config.CertificateManager.NotifyTrustListChanged(
                        TrustListIdentifier.Peers, trustChanged: true, crlChanged: true);
                }
                var trust = new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates =
                    [
                        ByteString.From(registrarCertificate.RawData.AsSpan()),
                        ByteString.From(issuer.RawData.AsSpan())
                    ],
                    TrustedCrls = [ByteString.From(crl.RawData.AsSpan())]
                };
                for (int phase = 0; phase < 3; phase++)
                {
                    using IDisposable? checkpointLease = OpenDeviceCheckpoint(
                        root, memoryJournal, fileCheckpoint, out ISecretStore secrets);
                    var observed = new ConnectedDeviceStartup();
                    ServiceCollection services = ConnectedDeviceServices(
                        root, applicationUri, url, password, secrets, observed);
                    await using ServiceProvider provider = services.BuildServiceProvider();
                    IHostedService hosted = provider.GetServices<IHostedService>()
                        .Single(static service => service.GetType().Name == "OpcUaServerHostedService");
                    try
                    {
                        await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
                        Task startup = ((BackgroundService)hosted).ExecuteTask ??
                            throw new AssertionException("The server startup task was not scheduled.");
                        Task completed = await Task.WhenAny(observed.Started.Task, startup)
                            .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        await completed.ConfigureAwait(false);
                        Assert.That(observed.Started.Task.IsCompleted, Is.True,
                            "The server exited before the startup callback ran.");
                        var security = provider.GetRequiredService<ProvisionableDeviceSecurity>();
                        ICertificateManager certificates = provider.GetRequiredService<ICertificateManager>();
                        Assert.That(observed.CertificateManager, Is.SameAs(certificates));
                        using CertificateEntry active = certificates.AcquireApplicationCertificateByType(
                            Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                            throw new AssertionException("The device has no active application certificate.");
                        using ISession session = await firstClient.ConnectAsync(
                            url, SecurityPolicies.Basic256Sha256, default,
                            new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                        ByteString peer = ChannelPeer(session);
                        Assert.That(peer, Is.EqualTo(ByteString.From(active.Certificate.RawData.AsSpan())));
                        if (phase == 0)
                        {
                            factoryDer = peer;
                            using (TrustListData initial = await certificates.ReadTrustListAsync(
                                TrustListIdentifier.Peers, TrustListMasks.All).ConfigureAwait(false))
                            {
                                factoryTrust.TrustedCertificates = initial.TrustedCertificates
                                    .Select(static certificate => ByteString.From(certificate.RawData.AsSpan()))
                                    .ToArrayOf();
                                factoryTrust.IssuerCertificates = initial.IssuerCertificates
                                    .Select(static certificate => ByteString.From(certificate.RawData.AsSpan()))
                                    .ToArrayOf();
                                factoryTrust.TrustedCrls = initial.TrustedCrls
                                    .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArrayOf();
                                factoryTrust.IssuerCrls = initial.IssuerCrls
                                    .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArrayOf();
                            }
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                            ByteString requestBytes = await CreateSigningRequestAsync(session, telemetry)
                                .ConfigureAwait(false);
                            var request = new Pkcs10CertificationRequest(requestBytes.ToArray());
                            Assert.That(request.Verify(), Is.True);
                            using Certificate issued = DefaultCertificateFactory.Instance.CreateApplicationCertificate(
                                applicationUri, "OnboardingActivationTest", request.Subject.Name,
                                X509Utils.GetDomainsFromCertificate(active.Certificate).ToArray())
                                .SetIssuer(issuer)
                                .SetRSAPublicKey(request.SubjectPublicKeyInfo)
                                .CreateForRSA();
                            issuedDer = ByteString.From(issued.RawData.AsSpan());
                            Assert.That(issuedDer, Is.Not.EqualTo(factoryDer));
                            await using (ServerPushConfigurationClient push = await ServerPushConfigurationClient
                                .CaptureAsync(firstClient.Config, session).ConfigureAwait(false))
                            {
                                bool applyTrust = await push.UpdateTrustListAsync(trust).ConfigureAwait(false);
                                Assert.That(applyTrust, Is.True);
                                await push.ApplyChangesAsync().ConfigureAwait(false);
                                await observed.Manager.DrainPendingApplyChangesAsync(timeout.Token)
                                    .ConfigureAwait(false);
                            }
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));

                            using ISession certificateSession = await firstClient.ConnectAsync(
                                url, SecurityPolicies.Basic256Sha256, default,
                                new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                            Assert.That(ChannelPeer(certificateSession), Is.EqualTo(factoryDer));
                            var configuration = new ServerConfigurationTypeClient(
                                certificateSession, Ua.ObjectIds.ServerConfiguration, telemetry);
                            bool applyCertificate = await configuration.UpdateCertificateAsync(
                                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                                issuedDer, [], string.Empty, ByteString.Empty).ConfigureAwait(false);
                            Assert.That(applyCertificate, Is.True);
                            await configuration.ApplyChangesAsync().ConfigureAwait(false);
                            await observed.Manager.DrainPendingApplyChangesAsync(timeout.Token).ConfigureAwait(false);
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
                            using ISession proof = await firstClient.ConnectAsync(
                                url, SecurityPolicies.Basic256Sha256, default,
                                new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                            Assert.That(ChannelPeer(proof), Is.EqualTo(issuedDer));
                            Assert.That(ChannelPeer(proof), Is.Not.EqualTo(factoryDer));
                            using CertificateEntry installed = certificates.AcquireApplicationCertificateByType(
                                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                                throw new AssertionException("The installed application certificate is missing.");
                            Assert.That(installed.Certificate.HasPrivateKey, Is.True);
                            Assert.That(installed.Certificate.RawData, Is.EqualTo(issued.RawData));
                        }
                        else if (phase == 1)
                        {
                            Assert.That(peer, Is.EqualTo(issuedDer));
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
                            using ISession other = await nextClient.ConnectAsync(
                                url, SecurityPolicies.Basic256Sha256, default,
                                new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                            ServiceResultException? untrusted = await Assert.ThrowsAsync<ServiceResultException>(
                                async () => await CreateSigningRequestAsync(other, telemetry).ConfigureAwait(false));
                            Assert.That(untrusted!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));

                            if (networkReset)
                            {
                                var configuration = new ServerConfigurationTypeClient(
                                    session, Ua.ObjectIds.ServerConfiguration, telemetry);
                                await configuration.ResetToServerDefaultsAsync().ConfigureAwait(false);
                                Assert.That(await observed.Reset.Task.WaitAsync(timeout.Token).ConfigureAwait(false),
                                    Is.True);
                            }
                            else
                            {
                                await security.ResetToServerDefaultsAsync().ConfigureAwait(false);
                            }

                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                            using ISession resetProof = await firstClient.ConnectAsync(
                                url, SecurityPolicies.Basic256Sha256, default,
                                new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                            Assert.That(ChannelPeer(resetProof), Is.EqualTo(factoryDer));
                            using TrustListData defaults = await certificates.ReadTrustListAsync(
                                TrustListIdentifier.Peers, TrustListMasks.All).ConfigureAwait(false);
                            Assert.That(defaults.TrustedCertificates
                                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                                Is.EquivalentTo(factoryTrust.TrustedCertificates.ToList()));
                            Assert.That(defaults.IssuerCertificates
                                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                                Is.EquivalentTo(factoryTrust.IssuerCertificates.ToList()));
                            Assert.That(defaults.TrustedCrls
                                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                                Is.EquivalentTo(factoryTrust.TrustedCrls.ToList()));
                            Assert.That(defaults.IssuerCrls
                                .Select(static value => ByteString.From(value.RawData.AsSpan())).ToArray(),
                                Is.EquivalentTo(factoryTrust.IssuerCrls.ToList()));
                        }
                        else
                        {
                            Assert.That(peer, Is.EqualTo(factoryDer));
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                            using ISession replacement = await nextClient.ConnectAsync(
                                url, SecurityPolicies.Basic256Sha256, default,
                                new UserIdentity("registrar", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                            ByteString replacementRequest = await CreateSigningRequestAsync(replacement, telemetry)
                                .ConfigureAwait(false);
                            Assert.That(replacementRequest.IsEmpty, Is.False);
                            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
                            ServiceResultException? previous = await Assert.ThrowsAsync<ServiceResultException>(
                                async () => await CreateSigningRequestAsync(session, telemetry).ConfigureAwait(false));
                            Assert.That(previous!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                        }
                    }
                    finally
                    {
                        await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Reopens only the sample's public device checkpoints; private keys remain in the retained PKI stores.
        /// The in-memory variant remains available on all supported test frameworks.
        /// </summary>
        private static IDisposable? OpenDeviceCheckpoint(
            string root,
            ISecretStore memoryJournal,
            bool fileCheckpoint,
            out ISecretStore secrets)
        {
            if (!fileCheckpoint)
            {
                secrets = memoryJournal;
                return null;
            }
#if NET10_0_OR_GREATER
            OnboardingRegistrar.PushRegistrarCheckpointFiles? files = null;
            try
            {
                files = new OnboardingRegistrar.PushRegistrarCheckpointFiles(Path.Combine(root, "device-state"));
                secrets = new OnboardingDevice.DeviceSecurityCheckpointStore(files, "activation-test");
                IDisposable lease = files;
                files = null;
                return lease;
            }
            finally
            {
                files?.Dispose();
            }
#else
            throw new PlatformNotSupportedException("The device sample checkpoint adapter targets .NET 10.");
#endif
        }

        /// <summary>
        /// Recreates all container-owned device services while retaining only the explicit journal and PKI stores.
        /// Automatic channel acceptance is confined to this fixture; installed device authorization still uses
        /// the complete installed trust scope with automatic acceptance disabled.
        /// </summary>
        private static ServiceCollection ConnectedDeviceServices(
            string root,
            string applicationUri,
            Uri url,
            string password,
            ISecretStore secrets,
            ConnectedDeviceStartup observed)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            services.AddSingleton(secrets);
            services.AddSingleton<ICertificateManager>(provider =>
                new CertificateManager(provider.GetRequiredService<ITelemetryContext>()));
            var users = new LinqUserDatabase();
            if (!users.CreateUser("registrar", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser, Role.SecurityAdmin]))
            {
                throw new InvalidOperationException("The test registrar account could not be created.");
            }
            services.AddSingleton<IUserDatabase>(users);
            services.AddSingleton<IUserManagement>(provider =>
                new UserManagement(provider.GetRequiredService<IUserDatabase>()));
            services.AddSingleton<IServerStartupTask>(provider =>
            {
                observed.CertificateManager = provider.GetRequiredService<ICertificateManager>();
                return observed;
            });
            string pkiRoot = Path.Combine(root, "server");
            var identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(pkiRoot, "own"),
                SubjectName = "CN=OnboardingActivationTest, O=OPC Foundation, DC=localhost",
                CertificateType = Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType
            };
            services.AddOpcUa()
                .ConfigureApplication(application =>
                {
                    application.ApplicationName = "OnboardingActivationTest";
                    application.ApplicationUri = applicationUri;
                    application.ProductUri = "urn:opcfoundation:test:onboarding";
                    application.SubjectName = identifier.SubjectName;
                    application.PkiRoot = pkiRoot;
                    application.AutoAcceptUntrustedCertificates = true;
                    application.ConfigureSecurity = security => security.SetApplicationCertificates([identifier]);
                })
                .AddServer(options =>
                {
                    options.ApplicationName = "OnboardingActivationTest";
                    options.ApplicationUri = applicationUri;
                    options.ProductUri = "urn:opcfoundation:test:onboarding";
                    options.SubjectName = identifier.SubjectName;
                    options.PkiRoot = pkiRoot;
                    options.AutoAcceptUntrustedCertificates = true;
                    options.IncludeSignAndEncryptPolicies = true;
                    options.IncludeUnsecurePolicyNone = false;
                    options.IncludeEccPolicies = false;
                    options.EndpointUrls.Add(url.AbsoluteUri);
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                })
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                })
                .ConfigureServerConfiguration(options => options.ResetShutdownDelay = TimeSpan.FromSeconds(1))
                .ConfigureRoles(options =>
                {
                    foreach (string name in s_registrarRoles)
                    {
                        options.Roles.Add(new RoleDefinitionOptions
                        {
                            Name = name,
                            Identities =
                            {
                                new RoleIdentityMappingOptions
                                {
                                    CriteriaType = IdentityCriteriaType.UserName,
                                    Criteria = "registrar"
                                }
                            }
                        });
                    }
                })
                .AddProvisionableDeviceSecurity(
                    "activation-test", applicationUri,
                    new ProvisionableDeviceSecurityInstallerOptions
                    {
                        ApplicationCertificate = identifier,
                        DeviceIdentityStorePath = Path.Combine(root, "factory"),
                        DeviceIdentityStoreType = CertificateStoreType.Directory,
                        CertificateGroupId = Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup
                    });
            services.Replace(ServiceDescriptor.Singleton<IServerConfigurationResetProvider>(provider =>
                new ObservedDeviceReset(provider.GetRequiredService<IProvisionableDeviceSecurity>(), observed.Reset)));
            return services;
        }

        /// <summary>
        /// Reads the actual encrypted channel peer rather than the discovery endpoint's advertised certificate.
        /// </summary>
        private static ByteString ChannelPeer(ISession session)
        {
            using CertificateCollection chain = DefaultCertificateFactory.Instance.ParseChainBlob(
                session.TransportChannel.ServerChannelCertificate.AsMemory());
            Assert.That(chain, Has.Count.GreaterThan(0));
            return ByteString.From(chain[0].RawData.AsSpan());
        }

        /// <summary>
        /// Captures the real configuration manager so deferred endpoint rotation can be awaited without sleeps.
        /// </summary>
        private sealed class ConnectedDeviceStartup : IServerStartupTask
        {
            /// <summary>
            /// Gets the completion evidence from the standard network reset provider callback.
            /// </summary>
            public TaskCompletionSource<bool> Reset { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the actual configuration manager after server startup.
            /// </summary>
            public ConfigurationNodeManager Manager { get; private set; } = null!;

            /// <summary>
            /// Gets the certificate manager used by the running server.
            /// </summary>
            public ICertificateManager CertificateManager { get; set; } = null!;

            /// <summary>
            /// Completes when the host startup callback has run.
            /// </summary>
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public ValueTask OnServerStartedAsync(
                IServerContext server,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = server.FindNodeManagers<ConfigurationNodeManager>().Single();
                Started.TrySetResult(true);
                return default;
            }
        }

        /// <summary>
        /// Observes the same device security reset called by the standard configuration manager.
        /// Provider failures are both observable to the test and propagated to the server's background-work owner.
        /// </summary>
        private sealed class ObservedDeviceReset(
            IProvisionableDeviceSecurity security,
            TaskCompletionSource<bool> completion) : IServerConfigurationResetProvider
        {
            /// <inheritdoc/>
            public async ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
            {
                try
                {
                    await security.ResetToServerDefaultsAsync(cancellationToken).ConfigureAwait(false);
                    completion.TrySetResult(true);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                    throw;
                }
            }
        }

        /// <summary>
        /// Uses the generated method proxy and real server-held pending signing key.
        /// </summary>
        private static async ValueTask<ByteString> CreateSigningRequestAsync(
            ISession session, ITelemetryContext telemetry)
        {
            var client = new ServerConfigurationTypeClient(session, Ua.ObjectIds.ServerConfiguration, telemetry);
            return await client.CreateSigningRequestAsync(
                Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                string.Empty, true, Nonce.CreateRandomNonceData(32).ToByteString(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>The configured roles needed by the registrar's push operations.</summary>
        private static readonly string[] s_registrarRoles = ["SecurityAdmin", "ConfigureAdmin"];
    }
}
