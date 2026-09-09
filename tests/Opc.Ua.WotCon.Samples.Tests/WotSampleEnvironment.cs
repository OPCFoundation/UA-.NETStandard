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
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AggregationClient;
using AggregationServer;
using FlatTagServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.WotCon.Client;

namespace Opc.Ua.WotCon.Samples.Tests
{
    internal sealed class WotSampleEnvironment : IAsyncDisposable
    {
        private WotSampleEnvironment(
            string root,
            IHost sourceAHost,
            IHost sourceBHost,
            IHost aggregationHost,
            AggregationClientOptions clientOptions,
            FlatTagValues sourceAValues,
            FlatTagValues sourceBValues,
            FlatTagValues sourceAPump2Values,
            FlatTagValues sourceBPump2Values)
        {
            Root = root;
            SourceAHost = sourceAHost;
            SourceBHost = sourceBHost;
            AggregationHost = aggregationHost;
            ClientOptions = clientOptions;
            SourceAValues = sourceAValues;
            SourceBValues = sourceBValues;
            SourceAPump2Values = sourceAPump2Values;
            SourceBPump2Values = sourceBPump2Values;
        }

        public string Root { get; }

        public IHost SourceAHost { get; }

        public IHost SourceBHost { get; }

        public IHost AggregationHost { get; }

        public AggregationClientOptions ClientOptions { get; }

        public FlatTagValues SourceAValues { get; }

        public FlatTagValues SourceBValues { get; }

        public FlatTagValues SourceAPump2Values { get; }

        public FlatTagValues SourceBPump2Values { get; }

        public string DocumentsDirectory => FindDocumentsDirectory();

        public static async Task<WotSampleEnvironment> StartAsync(
            CancellationToken cancellationToken,
            bool secure = false,
            bool? allowAnonymousManagement = null,
            bool? aggregationSecurityNone = null,
            bool grantSecurityAdmin = true)
        {
            string id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotSampleEnvironment),
                id);
            Directory.CreateDirectory(root);
            var userDatabase = new LinqUserDatabase();
            IClientIdentityProvider? identityProvider = null;
            if (secure)
            {
                byte[] password = Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                try
                {
                    var secretStore = new InMemorySecretStore();
                    var passwordId = new SecretIdentifier("wot-test-admin", secretStore.StoreType);
                    await secretStore.SetAsync(passwordId, password, cancellationToken).ConfigureAwait(false);
                    identityProvider = new UserNamePasswordIdentityProvider(
                        id, new SecretRegistry(secretStore), passwordId);
                    if (!userDatabase.CreateUser(id, password, [Role.SecurityAdmin]))
                    {
                        throw new InvalidOperationException("Could not create the isolated administrator.");
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                }
            }

            void ConfigureAuthentication(IOpcUaServerBuilder server)
            {
                server.Services.Configure<OpcUaServerOptions>(options =>
                {
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous });
                    options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                });
                server.AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = true;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                });
                server.Services.AddSingleton<IUserDatabase>(userDatabase);
                server.Services.AddSingleton<IUserManagement>(_ => new UserManagement(userDatabase));
                server.ConfigureRoles(options =>
                {
                    if (!grantSecurityAdmin)
                    {
                        return;
                    }
                    var administrator = new RoleDefinitionOptions { Name = "SecurityAdmin" };
                    administrator.Identities.Add(new RoleIdentityMappingOptions
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = id
                    });
                    options.Roles.Add(administrator);
                });
            }

            int[] ports = TestPorts.GetFreePorts(3);
            int sourceAPort = ports[0];
            int sourceBPort = ports[1];
            int aggregationPort = ports[2];
            string sourceAEndpoint = $"opc.tcp://127.0.0.1:{sourceAPort}/SourceA";
            string sourceBEndpoint = $"opc.tcp://127.0.0.1:{sourceBPort}/SourceB";
            string aggregationEndpoint =
                $"opc.tcp://127.0.0.1:{aggregationPort}/AggregationServer";

            var sourceAValues = new FlatTagValues
            {
                DifferentialPressure = 111.25,
                FluidTemperature = 301.15,
                MassFlow = 0.42,
                Level = 4.25,
                Cavitation = true,
                BearingTemperature = 340.15,
                PumpPowerInput = 21.0,
                PumpEfficiency = 82.0,
                NumberOfStarts = 99,
                MotorOverheat = false
            };
            var sourceBValues = new FlatTagValues
            {
                DifferentialPressure = 222.5,
                FluidTemperature = 310.15,
                MassFlow = 0.84,
                Level = 8.5,
                Cavitation = false,
                BearingTemperature = 333.15,
                PumpPowerInput = 17.75,
                PumpEfficiency = 91.5,
                NumberOfStarts = 23,
                MotorOverheat = true
            };
            var sourceAPump2Values = new FlatTagValues
            {
                SerialNumber = "SN-002",
                ProductInstanceUri = "urn:simdevice:SimPump:PumpX-2000:SN-002",
                DifferentialPressure = 211.25,
                FluidTemperature = 304.15,
                MassFlow = 0.52,
                Level = 4.75,
                Cavitation = false
            };
            var sourceBPump2Values = new FlatTagValues
            {
                SerialNumber = "SN-002",
                ProductInstanceUri = "urn:simdevice:SimPump:PumpX-2000:SN-002",
                BearingTemperature = 337.15,
                PumpPowerInput = 19.75,
                PumpEfficiency = 89.5,
                NumberOfStarts = 31,
                MotorOverheat = false
            };

            IHost sourceAHost = FlatTagServerHost.Build(new FlatTagServerOptions
            {
                EndpointUrl = sourceAEndpoint,
                SourceNamespaceUri = FlatTagServerOptions.SourceANamespaceUri,
                ApplicationName = $"FlatTagServerSourceA{id}",
                IncludeUnsecurePolicyNone = !secure,
                InstanceName = "SourceA",
                PkiRoot = Path.Combine(root, "SourceA", "pki"),
                Values = sourceAValues,
                Pump2Values = sourceAPump2Values
            });
            IHost sourceBHost = FlatTagServerHost.Build(new FlatTagServerOptions
            {
                EndpointUrl = sourceBEndpoint,
                SourceNamespaceUri = FlatTagServerOptions.SourceBNamespaceUri,
                ApplicationName = $"FlatTagServerSourceB{id}",
                IncludeUnsecurePolicyNone = !secure,
                InstanceName = "SourceB",
                PkiRoot = Path.Combine(root, "SourceB", "pki"),
                Values = sourceBValues,
                Pump2Values = sourceBPump2Values
            });
            IHost aggregationHost = AggregationServerHost.Build(
                new AggregationServerOptions
                {
                    EndpointUrl = aggregationEndpoint,
                    ApplicationName = $"AggregationServer{id}",
                    IncludeUnsecurePolicyNone = aggregationSecurityNone ?? !secure,
                    AllowAnonymousManagement = allowAnonymousManagement ?? !secure,
                    ConfigureAuthentication = secure ? ConfigureAuthentication : null,
                    PkiRoot = Path.Combine(root, "Aggregation", "pki")
                });
            var clientOptions = new AggregationClientOptions
            {
                AggregationEndpoint = aggregationEndpoint,
                SourceAEndpoint = sourceAEndpoint,
                SourceBEndpoint = sourceBEndpoint,
                ApplicationName = $"AggregationClient{id}",
                UseSecurityPolicyNone = !secure,
                IdentityProvider = identityProvider,
                PkiRoot = Path.Combine(root, "Client", "pki"),
                DocumentsDirectory = FindDocumentsDirectory()
            };
            var environment = new WotSampleEnvironment(
                root,
                sourceAHost,
                sourceBHost,
                aggregationHost,
                clientOptions,
                sourceAValues,
                sourceBValues,
                sourceAPump2Values,
                sourceBPump2Values);
            try
            {
                await sourceAHost.StartAsync(cancellationToken).ConfigureAwait(false);
                await sourceBHost.StartAsync(cancellationToken).ConfigureAwait(false);
                await WaitForTcpAsync(sourceAPort, cancellationToken).ConfigureAwait(false);
                await WaitForTcpAsync(sourceBPort, cancellationToken).ConfigureAwait(false);
                await aggregationHost.StartAsync(cancellationToken).ConfigureAwait(false);
                if (secure)
                {
                    await WaitForTcpAsync(aggregationPort, cancellationToken).ConfigureAwait(false);
                    using IHost clientHost = AggregationClientRunner.BuildHost(clientOptions);
                    await clientHost.Services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                        .GetAsync(cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(aggregationHost, sourceAHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(sourceAHost, aggregationHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(aggregationHost, sourceBHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(sourceBHost, aggregationHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(aggregationHost, clientHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(clientHost, aggregationHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(sourceAHost, clientHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(clientHost, sourceAHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(sourceBHost, clientHost, cancellationToken).ConfigureAwait(false);
                    await TrustPeerAsync(clientHost, sourceBHost, cancellationToken).ConfigureAwait(false);
                }
                await environment.WaitForAggregationAsync(cancellationToken).ConfigureAwait(false);
                return environment;
            }
            catch
            {
                await environment.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public AggregationClientOptions CreateClientOptions(
            string documentsDirectory,
            string? sourceAEndpoint = null,
            string? sourceBEndpoint = null)
        {
            return new AggregationClientOptions
            {
                AggregationEndpoint = ClientOptions.AggregationEndpoint,
                SourceAEndpoint = sourceAEndpoint ?? ClientOptions.SourceAEndpoint,
                SourceBEndpoint = sourceBEndpoint ?? ClientOptions.SourceBEndpoint,
                ApplicationName = ClientOptions.UseSecurityPolicyNone
                    ? ClientOptions.ApplicationName + Guid.NewGuid().ToString("N")
                    : ClientOptions.ApplicationName,
                PkiRoot = ClientOptions.UseSecurityPolicyNone
                    ? Path.Combine(Root, "Clients", Guid.NewGuid().ToString("N"), "pki")
                    : ClientOptions.PkiRoot,
                UseSecurityPolicyNone = ClientOptions.UseSecurityPolicyNone,
                IdentityProvider = ClientOptions.IdentityProvider,
                DocumentsDirectory = documentsDirectory
            };
        }

        public Task<WotClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            AggregationClientOptions options = CreateClientOptions(DocumentsDirectory);
            return WotClientConnection.CreateAsync(options, cancellationToken);
        }

        public Task<OpcUaClientConnection> ConnectSourceAAsync(
            CancellationToken cancellationToken)
        {
            if (!ClientOptions.UseSecurityPolicyNone)
            {
                return ConnectSecureSourceAsync(ClientOptions.SourceAEndpoint, cancellationToken);
            }
            return OpcUaClientConnection.CreateAsync(
                Root,
                ClientOptions.SourceAEndpoint,
                ClientOptions.ApplicationName + ".SourceA" + Guid.NewGuid().ToString("N"),
                cancellationToken);
        }

        public Task<OpcUaClientConnection> ConnectSourceBAsync(
            CancellationToken cancellationToken)
        {
            if (!ClientOptions.UseSecurityPolicyNone)
            {
                return ConnectSecureSourceAsync(ClientOptions.SourceBEndpoint, cancellationToken);
            }
            return OpcUaClientConnection.CreateAsync(
                Root,
                ClientOptions.SourceBEndpoint,
                ClientOptions.ApplicationName + ".SourceB" + Guid.NewGuid().ToString("N"),
                cancellationToken);
        }

        public string CreateDocumentsCopy()
        {
            string target = Path.Combine(Root, "Documents", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            foreach (string source in Directory.EnumerateFiles(DocumentsDirectory, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(DocumentsDirectory, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            return target;
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (IHost host in new[] { AggregationHost, SourceBHost, SourceAHost })
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try
                    {
                        await host.StopAsync(timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        host.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Sample host teardown failed.", failures);
            }
        }

        internal static async Task TrustPeerAsync(IHost trustingHost, IHost peer, CancellationToken cancellationToken)
        {
            using Certificate publicCertificate = await GetPeerCertificateAsync(peer, cancellationToken)
                .ConfigureAwait(false);
            IOpcUaApplicationConfigurationProvider? provider = trustingHost.Services
                .GetService<IOpcUaApplicationConfigurationProvider>();
            if (provider is not null)
            {
                ICertificateManager manager = provider.Configuration.CertificateManager;
                await using ITrustListTransaction transaction = await manager.BeginUpdateAsync(
                    TrustListIdentifier.Peers, cancellationToken).ConfigureAwait(false);
                await transaction.AddTrustedCertificateAsync(publicCertificate, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                string root = trustingHost.Services.GetRequiredService<IOptions<OpcUaServerOptions>>().Value.PkiRoot!;
                var identifier = new CertificateStoreIdentifier(Path.Combine(root, "trusted"));
                using ICertificateStore store = identifier.OpenStore(
                    trustingHost.Services.GetRequiredService<ITelemetryContext>());
                await store.AddAsync(publicCertificate, ct: cancellationToken).ConfigureAwait(false);
            }
        }

        private Task<OpcUaClientConnection> ConnectSecureSourceAsync(
            string endpoint, CancellationToken cancellationToken)
        {
            AggregationClientOptions options = CreateClientOptions(DocumentsDirectory);
            options.AggregationEndpoint = endpoint;
            options.IdentityProvider = null;
            return OpcUaClientConnection.CreateAsync(options, cancellationToken);
        }

        private static async Task<Certificate> GetPeerCertificateAsync(IHost peer, CancellationToken cancellationToken)
        {
            IOpcUaApplicationConfigurationProvider? provider = peer.Services
                .GetService<IOpcUaApplicationConfigurationProvider>();
            if (provider is not null)
            {
                using CertificateEntry entry = provider.Configuration.CertificateManager
                    .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256) ??
                    throw new InvalidOperationException("The peer application certificate was not initialized.");
                return Certificate.FromRawData(entry.Certificate.RawData);
            }
            string root = peer.Services.GetRequiredService<IOptions<OpcUaServerOptions>>().Value.PkiRoot!;
            var identifier = new CertificateStoreIdentifier(root);
            using ICertificateStore store = identifier.OpenStore(peer.Services.GetRequiredService<ITelemetryContext>());
            using CertificateCollection certificates = await store.EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (Certificate certificate in certificates)
            {
                using RSA? key = certificate.GetRSAPublicKey();
                if (key is not null)
                {
                    return Certificate.FromRawData(certificate.RawData);
                }
            }
            throw new InvalidOperationException("The peer RSA certificate was not initialized.");
        }

        private async Task WaitForAggregationAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    WotClientConnection connection = await ConnectAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await using (connection.ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (ServiceResultException ex)
                    when (ex.StatusCode == StatusCodes.BadServerHalted ||
                        ex.StatusCode == StatusCodes.BadServerNotConnected ||
                        ex.StatusCode == StatusCodes.BadConnectionRejected)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private static async Task WaitForTcpAsync(
            int port,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync("127.0.0.1", port, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private static string FindDocumentsDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string solution = Path.Combine(directory.FullName, "UA.slnx");
                if (File.Exists(solution))
                {
                    string documents = Path.Combine(
                        directory.FullName,
                        "samples",
                        "WotCon",
                        "AggregationClient",
                        "Documents");
                    if (Directory.Exists(documents))
                    {
                        return documents;
                    }
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException(
                "The checked-in samples\\AggregationClient\\Documents directory was not found.");
        }
    }

    internal sealed class OpcUaClientConnection : IAsyncDisposable
    {
        private OpcUaClientConnection(IHost host, ManagedSession session)
        {
            Host = host;
            Session = session;
        }

        public IHost Host { get; }

        public ManagedSession Session { get; }

        public static Task<OpcUaClientConnection> CreateAsync(
            string root,
            string endpointUrl,
            string applicationName,
            CancellationToken cancellationToken)
        {
            IHost host = BuildClientHost(root, endpointUrl, applicationName);
            return ConnectAsync(host, cancellationToken);
        }

        public static Task<OpcUaClientConnection> CreateAsync(
            AggregationClientOptions options,
            CancellationToken cancellationToken)
        {
            return ConnectAsync(AggregationClientRunner.BuildHost(options), cancellationToken);
        }

        private static async Task<OpcUaClientConnection> ConnectAsync(IHost host, CancellationToken cancellationToken)
        {
            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                Func<CancellationToken, Task<ManagedSession>> connect =
                    host.Services.GetRequiredService<
                        Func<CancellationToken, Task<ManagedSession>>>();
                ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
                session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
                return new OpcUaClientConnection(host, session);
            }
            catch
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                host.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await Host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                Host.Dispose();
            }
        }

        private static IHost BuildClientHost(
            string root,
            string endpointUrl,
            string applicationName)
        {
            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host
                .CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(client =>
                {
                    client.ApplicationName = applicationName;
                    client.ApplicationUri = "urn:localhost:OPCFoundation:" + applicationName;
                    client.ProductUri = "uri:opcfoundation.org:WotSampleTestClient";
                    client.PkiRoot = Path.Combine(
                        root,
                        "Clients",
                        Guid.NewGuid().ToString("N"),
                        "pki");
                    client.AutoAcceptUntrustedCertificates = true;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = applicationName,
                        SessionTimeout = TimeSpan.FromSeconds(60)
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = endpointUrl;
                    discovery.SecurityMode = MessageSecurityMode.None;
                    discovery.SecurityPolicyUri = SecurityPolicies.None;
                });
            return builder.Build();
        }
    }

    internal sealed class WotClientConnection : IAsyncDisposable
    {
        private WotClientConnection(IHost host, ManagedSession session, WotRegistryClient registry)
        {
            Host = host;
            Session = session;
            Registry = registry;
        }

        public IHost Host { get; }

        public ManagedSession Session { get; }

        public WotRegistryClient Registry { get; }

        public static async Task<WotClientConnection> CreateAsync(
            AggregationClientOptions options,
            CancellationToken cancellationToken)
        {
            IHost host = AggregationClientRunner.BuildHost(options);
            try
            {
                await host.StartAsync(cancellationToken).ConfigureAwait(false);
                Func<CancellationToken, Task<ManagedSession>> connect =
                    host.Services.GetRequiredService<
                        Func<CancellationToken, Task<ManagedSession>>>();
                ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
                Func<ManagedSession, CancellationToken, Task<WotRegistryClient>> createClient =
                    host.Services.GetRequiredService<
                        Func<ManagedSession, CancellationToken, Task<WotRegistryClient>>>();
                WotRegistryClient registry = await createClient(session, cancellationToken)
                    .ConfigureAwait(false);
                return new WotClientConnection(host, session, registry);
            }
            catch
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                host.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await Host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                Host.Dispose();
            }
        }
    }
}
