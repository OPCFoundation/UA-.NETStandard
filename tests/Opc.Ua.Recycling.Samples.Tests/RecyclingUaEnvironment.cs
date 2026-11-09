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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Real secure UA endpoints with generated, explicitly trusted, workspace-local test PKI and no trust-all hooks.
    /// </summary>
    internal sealed class RecyclingUaEnvironment : IAsyncDisposable
    {
        private RecyclingUaEnvironment(string root, ITelemetryContext telemetry)
        {
            Root = root;
            Telemetry = telemetry;
        }

        /// <summary>
        /// Isolated owned workspace root.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Test telemetry; no certificate/credential dumps are written to test output.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Coordinator host.
        /// </summary>
        internal IHost ServerHost { get; private set; } = null!;

        /// <summary>
        /// Effective non-secret coordinator settings, also used to provision a separately launched native host.
        /// </summary>
        internal RecyclingHostOptions ServerOptions => m_serverOptions;

        /// <summary>
        /// Direct authoritative runtime, used to advance a bounded number of material ticks.
        /// </summary>
        internal RecyclingCoordinator Coordinator =>
            ServerHost.Services.GetRequiredService<RecyclingRuntime>().Coordinator;

        /// <summary>
        /// Builds and explicitly trusts test identities before either side opens a session.
        /// A supplied synthetic recipe is shared by the coordinator and its independently hosted views.
        /// </summary>
        internal static Task<RecyclingUaEnvironment> StartAsync(
            CancellationToken cancellationToken,
            RecyclingConfiguration? configuration = null)
        {
            return CreateAsync(configuration, startCoordinator: true, cancellationToken);
        }

        /// <summary>
        /// Provisions the same isolated PKI and managed clients without starting a managed coordinator.
        /// Application identities match the existing executable command-line contract.
        /// </summary>
        internal static Task<RecyclingUaEnvironment> ProvisionNativeAsync(
            CancellationToken cancellationToken,
            RecyclingConfiguration? configuration = null)
        {
            return CreateAsync(configuration, startCoordinator: false, cancellationToken);
        }

        private static async Task<RecyclingUaEnvironment> CreateAsync(
            RecyclingConfiguration? configuration,
            bool startCoordinator,
            CancellationToken cancellationToken)
        {
            string root = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "u", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            ITelemetryContext telemetry = DefaultTelemetry.Create(static logging => logging.ClearProviders());
            var environment = new RecyclingUaEnvironment(root, telemetry);
            var certificates = new List<Certificate>();
            try
            {
                var bindings = new List<RecyclingIdentityBinding>();
                foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
                {
                    await environment.CreateIdentityAsync(
                        "view-" + process, RecyclingAccess.ProcessView, process,
                        bindings, certificates, cancellationToken)
                        .ConfigureAwait(false);
                }
                await environment.CreateIdentityAsync(
                    "administrator", RecyclingAccess.SimulatorAdministrator, RecyclingProcess.Collection,
                    bindings, certificates, cancellationToken).ConfigureAwait(false);
                await environment.CreateIdentityAsync(
                    "operator", RecyclingAccess.Operator, RecyclingProcess.Collection,
                    bindings, certificates, cancellationToken).ConfigureAwait(false);
                await environment.CreateIdentityAsync(
                    "observer", RecyclingAccess.Observer, RecyclingProcess.Collection,
                    bindings, certificates, cancellationToken).ConfigureAwait(false);

                string suffix = Guid.NewGuid().ToString("N")[..8];
                string line = startCoordinator ? "test-line" : "native-smoke";
                string name = startCoordinator ? "RecyclingTestServer" + suffix : "Recycling-" + line + "-coordinator";
                environment.m_serverOptions = new RecyclingHostOptions
                {
                    EndpointUrl = $"opc.tcp://localhost:{FreePort()}/Recycling",
                    ApplicationName = name,
                    ApplicationUri = startCoordinator
                        ? "urn:localhost:RecyclingTestServer:" + suffix
                        : "urn:localhost:OPCFoundation:" + name,
                    LineId = line,
                    PkiRoot = Path.Combine(root, "server", "pki"),
                    StateRoot = Path.Combine(root, "server", "state"),
                    Identities = bindings.ToArrayOf(),
                    Simulation = (configuration ?? new RecyclingConfiguration()) with { LeaseSeconds = 300 },
                    AutomaticTicks = false
                };
                environment.ServerHost = RecyclingHost.Build(
                    environment.m_serverOptions,
                    static services => services.AddLogging(
                        static logging => logging.SetMinimumLevel(LogLevel.Warning)));
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddOpcUa()
                    .AddClient(client =>
                    {
                        client.ApplicationName = "RecyclingTestClient" + suffix;
                        client.ApplicationUri = "urn:localhost:RecyclingTestClient:" + suffix;
                        client.PkiRoot = Path.Combine(root, "client", "pki");
                        client.AutoAcceptUntrustedCertificates = false;
                        client.RejectSHA1SignedCertificates = true;
                        client.MinimumCertificateKeySize = 2048;
                        client.Session = new ManagedSessionOptions { CheckDomain = true };
                    })
                    .AddDiscovery();
                environment.m_clientHost = builder.Build();
                ApplicationConfiguration server = await ConfigurationAsync(environment.ServerHost, cancellationToken)
                    .ConfigureAwait(false);
                ApplicationConfiguration client = await ConfigurationAsync(environment.m_clientHost, cancellationToken)
                    .ConfigureAwait(false);
                await TrustApplicationsAsync(server, client, telemetry, cancellationToken).ConfigureAwait(false);
                using (ICertificateStore userTrust =
                    server.SecurityConfiguration.TrustedUserCertificates!.OpenStore(telemetry))
                {
                    foreach (Certificate certificate in certificates)
                    {
                        using Certificate publicCertificate = Certificate.FromRawData(certificate.RawData);
                        await userTrust.AddAsync(publicCertificate, ct: cancellationToken).ConfigureAwait(false);
                    }
                }
                await server.CertificateManager!.UpdateAsync(
                    server.SecurityConfiguration, server.ApplicationUri, cancellationToken).ConfigureAwait(false);
                await client.CertificateManager!.UpdateAsync(
                    client.SecurityConfiguration, client.ApplicationUri, cancellationToken).ConfigureAwait(false);
                await environment.m_clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
                if (startCoordinator)
                {
                    await environment.ServerHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    await environment.ServerHost.Services.GetRequiredService<RecyclingRuntime>()
                        .AddressSpaceReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                return environment;
            }
            catch
            {
                await environment.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                foreach (Certificate certificate in certificates)
                {
                    certificate.Dispose();
                }
            }
        }

        /// <summary>
        /// Connects a fresh managed session using a selected independently provisioned user-certificate store.
        /// </summary>
        internal async Task<RecyclingCoordinatorClient> ConnectAsync(
            string identity,
            CancellationToken cancellationToken,
            string? endpointUrl = null)
        {
            ManagedSession session = await ConnectSessionAsync(
                identity, endpointUrl ?? m_serverOptions.EndpointUrl, cancellationToken).ConfigureAwait(false);
            var client = new RecyclingCoordinatorClient(session, Telemetry);
            m_clients.Add(client);
            return client;
        }

        /// <summary>
        /// Explicitly exchanges public application certificates with the coordinator and each owned view.
        /// </summary>
        internal async Task TrustClientApplicationAsync(
            ApplicationConfiguration client, CancellationToken cancellationToken)
        {
            foreach (IHost host in new[] { ServerHost }.Concat(m_views))
            {
                ApplicationConfiguration server = await ConfigurationAsync(host, cancellationToken)
                    .ConfigureAwait(false);
                await TrustApplicationsAsync(server, client, Telemetry, cancellationToken).ConfigureAwait(false);
                await server.CertificateManager!.UpdateAsync(
                    server.SecurityConfiguration, server.ApplicationUri, cancellationToken).ConfigureAwait(false);
            }
            await client.CertificateManager!.UpdateAsync(
                client.SecurityConfiguration, client.ApplicationUri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Initializes ten distinct role identities across the real secure OPC UA service boundary.
        /// </summary>
        internal async Task InitializeRolesAsync(CancellationToken cancellationToken)
        {
            foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
            {
                if (Coordinator.Current.Participants.Span.ToArray().Any(participant =>
                    participant.Process == process && participant.Initialized))
                {
                    continue;
                }
                RecyclingCoordinatorClient client = await ConnectAsync("view-" + process, cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteMembershipAsync(client, RecyclingCommandKind.Register, process, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(Coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.WaitingForParticipants));
                CommittedSnapshot initial = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(initial.Revision, Is.LessThanOrEqualTo(Coordinator.Current.Revision));
                Assert.That(initial.RunId, Is.EqualTo(Coordinator.Current.RunId));
                await ExecuteMembershipAsync(client, RecyclingCommandKind.Initialize, process, cancellationToken)
                    .ConfigureAwait(false);
            }
            Assert.That(Coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Running));
        }

        /// <summary>
        /// Starts one actual process-view endpoint with independent PKI and its own lazy X.509 managed client.
        /// </summary>
        internal Task<IHost> StartViewAsync(
            RecyclingProcess process,
            IRecyclingProcessViewAdapter? adapter,
            CancellationToken cancellationToken)
        {
            return CreateViewAsync(process, adapter, startView: true, cancellationToken);
        }

        /// <summary>
        /// Provisions a selected view's independent PKI without starting a managed process view.
        /// </summary>
        internal async Task<RecyclingHostOptions> ProvisionNativeProcessViewAsync(
            RecyclingProcess process,
            CancellationToken cancellationToken)
        {
            IHost host = await CreateViewAsync(
                process, null, startView: false, cancellationToken).ConfigureAwait(false);
            return host.Services.GetRequiredService<RecyclingHostOptions>();
        }

        /// <summary>
        /// Matches the existing production CLI spelling without embedding a collection-only identity.
        /// </summary>
        internal static string ProcessArgument(RecyclingProcess process)
        {
            return process switch
            {
                RecyclingProcess.DensitySeparation => "density-separation",
                RecyclingProcess.MeltFiltration => "melt-filtration",
                _ => process.ToString().ToLowerInvariant()
            };
        }

        private async Task<IHost> CreateViewAsync(
            RecyclingProcess process,
            IRecyclingProcessViewAdapter? adapter,
            bool startView,
            CancellationToken cancellationToken)
        {
            TestIdentity identity = m_identities["view-" + process];
            string name = startView
                ? "RecyclingTestView" + process
                : "Recycling-" + m_serverOptions.LineId + "-" + ProcessArgument(process);
            var options = m_serverOptions with
            {
                Role = RecyclingHostRole.ProcessView,
                Process = process,
                EndpointUrl = $"opc.tcp://localhost:{FreePort()}/RecyclingView",
                CoordinatorUrl = m_serverOptions.EndpointUrl,
                ApplicationName = name,
                ApplicationUri = startView
                    ? "urn:localhost:RecyclingTestView:" + process
                    : "urn:localhost:OPCFoundation:" + name,
                PkiRoot = Path.Combine(Root, "view-" + process, "pki"),
                StateRoot = Path.Combine(Root, "view-" + process, "unused-state"),
                IdentityStorePath = identity.StorePath,
                IdentitySubject = identity.Subject
            };
            IHost host = RecyclingHost.Build(options, services =>
            {
                if (adapter != null)
                {
                    services.AddSingleton(adapter);
                }
                services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Warning));
            });
            m_views.Add(host);
            ApplicationConfiguration view = await ConfigurationAsync(host, cancellationToken).ConfigureAwait(false);
            ApplicationConfiguration server = await ConfigurationAsync(ServerHost, cancellationToken)
                .ConfigureAwait(false);
            ApplicationConfiguration client = await ConfigurationAsync(m_clientHost, cancellationToken)
                .ConfigureAwait(false);
            await TrustApplicationsAsync(server, view, Telemetry, cancellationToken).ConfigureAwait(false);
            await TrustApplicationsAsync(view, client, Telemetry, cancellationToken).ConfigureAwait(false);
            using (ICertificateStore source =
                server.SecurityConfiguration.TrustedUserCertificates!.OpenStore(Telemetry))
            using (ICertificateStore target = view.SecurityConfiguration.TrustedUserCertificates!.OpenStore(Telemetry))
            using (CertificateCollection users = await source.EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (Certificate user in users)
                {
                    await target.AddAsync(user, ct: cancellationToken).ConfigureAwait(false);
                }
            }
            await server.CertificateManager!.UpdateAsync(
                server.SecurityConfiguration, server.ApplicationUri, cancellationToken).ConfigureAwait(false);
            await client.CertificateManager!.UpdateAsync(
                client.SecurityConfiguration, client.ApplicationUri, cancellationToken).ConfigureAwait(false);
            await view.CertificateManager!.UpdateAsync(
                view.SecurityConfiguration, view.ApplicationUri, cancellationToken).ConfigureAwait(false);
            if (startView)
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                await host.Services.GetRequiredService<RecyclingRuntime>()
                    .AddressSpaceReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return host;
        }

        /// <summary>
        /// Closes only this fixture's managed clients before deliberately terminating an owned external server.
        /// </summary>
        internal async Task CloseClientsAsync()
        {
            foreach (RecyclingCoordinatorClient client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            m_clients.Clear();
        }

        /// <summary>
        /// Restarts only the owned coordinator, retaining its state and PKI roots.
        /// </summary>
        internal async Task RestartAsync(CancellationToken cancellationToken)
        {
            await CloseClientsAsync().ConfigureAwait(false);
            await ServerHost.StopAsync(cancellationToken).ConfigureAwait(false);
            await DisposeHostAsync(ServerHost).ConfigureAwait(false);
            ServerHost = RecyclingHost.Build(
                m_serverOptions,
                static services => services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Warning)));
            await ServerHost.StartAsync(cancellationToken).ConfigureAwait(false);
            await ServerHost.Services.GetRequiredService<RecyclingRuntime>()
                .AddressSpaceReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await CloseClientsAsync().ConfigureAwait(false);
            foreach (IHost view in m_views)
            {
                await view.StopAsync().ConfigureAwait(false);
                await DisposeHostAsync(view).ConfigureAwait(false);
            }
            if (ServerHost != null)
            {
                await ServerHost.StopAsync().ConfigureAwait(false);
                await DisposeHostAsync(ServerHost).ConfigureAwait(false);
            }
            if (m_clientHost != null)
            {
                await m_clientHost.StopAsync().ConfigureAwait(false);
                await DisposeHostAsync(m_clientHost).ConfigureAwait(false);
            }
            (Telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private async Task<ManagedSession> ConnectSessionAsync(
            string identity,
            string endpointUrl,
            CancellationToken cancellationToken)
        {
            TestIdentity user = m_identities[identity];
            IServiceProvider services = m_clientHost.Services;
            ApplicationConfiguration configuration = await ConfigurationAsync(m_clientHost, cancellationToken)
                .ConfigureAwait(false);
            var provider = new X509ClientIdentityProvider(
                new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = user.StorePath,
                    SubjectName = user.Subject
                },
                new CertificatePasswordProvider(),
                configuration.CertificateManager!.CertificateProvider);
            ArrayOf<EndpointDescription> endpoints = await services.GetRequiredService<IOpcUaDiscoveryService>()
                .GetEndpointsAsync(endpointUrl, ct: cancellationToken).ConfigureAwait(false);
            EndpointDescription endpoint = endpoints.Span.ToArray().First(description =>
                description.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                description.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            return await services.GetRequiredService<IManagedSessionFactory>().ConnectAsync(
                new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)),
                session => session.WithSessionName(identity).WithIdentityProvider(provider).WithCheckDomain(),
                cancellationToken).ConfigureAwait(false);
        }

        private async Task ExecuteMembershipAsync(
            RecyclingCoordinatorClient client,
            RecyclingCommandKind kind,
            RecyclingProcess process,
            CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                CommittedSnapshot snapshot = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                RecyclingCommand command = CoordinatorTests.Command(Coordinator, kind, process) with
                {
                    RunId = snapshot.RunId,
                    ExpectedRevision = snapshot.Revision
                };
                try
                {
                    await client.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException exception)
                    when (exception.StatusCode == StatusCodes.BadInvalidState && attempt < 31)
                {
                    // A view may commit a heartbeat between the read and the registration call.
                }
            }
        }

        private async Task CreateIdentityAsync(
            string identity,
            RecyclingAccess access,
            RecyclingProcess process,
            List<RecyclingIdentityBinding> bindings,
            List<Certificate> certificates,
            CancellationToken cancellationToken)
        {
            string subject = "CN=RecyclingTest-" + identity;
            Certificate certificate = CertificateBuilder.Create(subject)
                .SetNotBefore(DateTime.UtcNow.AddMinutes(-5))
                .SetLifeTime(TimeSpan.FromDays(1))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            certificates.Add(certificate);
            string storePath = Path.Combine(Root, "identities", identity);
            using var store = new DirectoryCertificateStore(Telemetry);
            store.Open(storePath, noPrivateKeys: false);
            await store.AddAsync(certificate, ct: cancellationToken).ConfigureAwait(false);
            string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            bindings.Add(new RecyclingIdentityBinding(fingerprint, identity, access, process));
            m_identities.Add(identity, new TestIdentity(storePath, subject));
        }

        private static async Task TrustApplicationsAsync(
            ApplicationConfiguration server,
            ApplicationConfiguration client,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            using CertificateEntryCollection serverCertificates =
                server.CertificateManager!.SnapshotApplicationCertificates();
            using CertificateEntryCollection clientCertificates =
                client.CertificateManager!.SnapshotApplicationCertificates();
            using ICertificateStore serverTrust =
                server.SecurityConfiguration.TrustedPeerCertificates.OpenStore(telemetry);
            using ICertificateStore clientTrust =
                client.SecurityConfiguration.TrustedPeerCertificates.OpenStore(telemetry);
            foreach (CertificateEntry certificate in serverCertificates)
            {
                using Certificate publicCertificate = Certificate.FromRawData(certificate.Certificate.RawData);
                await clientTrust.AddAsync(publicCertificate, ct: cancellationToken).ConfigureAwait(false);
            }
            foreach (CertificateEntry certificate in clientCertificates)
            {
                using Certificate publicCertificate = Certificate.FromRawData(certificate.Certificate.RawData);
                await serverTrust.AddAsync(publicCertificate, ct: cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<ApplicationConfiguration> ConfigurationAsync(
            IHost host,
            CancellationToken cancellationToken)
        {
            IOpcUaApplicationConfigurationProvider provider =
                host.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>();
            ApplicationConfiguration configuration = await provider.GetAsync(cancellationToken).ConfigureAwait(false);
            await provider.Application.CheckApplicationInstanceCertificatesAsync(true, ct: cancellationToken)
                .ConfigureAwait(false);
            return configuration;
        }

        private static async ValueTask DisposeHostAsync(IHost host)
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        /// <summary>
        /// Non-secret user-certificate store selector.
        /// </summary>
        private sealed record TestIdentity(string StorePath, string Subject);

        private readonly Dictionary<string, TestIdentity> m_identities = new(StringComparer.Ordinal);
        private readonly List<RecyclingCoordinatorClient> m_clients = [];
        private readonly List<IHost> m_views = [];
        private IHost m_clientHost = null!;
        private RecyclingHostOptions m_serverOptions = null!;
    }
}
