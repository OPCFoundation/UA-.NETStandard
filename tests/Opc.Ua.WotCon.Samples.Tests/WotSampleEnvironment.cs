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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Tests.Registry;
using AggregationClient;
using AggregationServer;
using FlatTagServer;

namespace Opc.Ua.WotCon.Samples.Tests
{
    /// <summary>
    /// Owns the source and aggregation hosts, client options, and files used by sample integration tests.
    /// </summary>
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
            FlatTagValues sourceBPump2Values,
            RecordingLeasedResourceStore? contentStore)
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
            m_contentStore = contentStore;
        }

        /// <summary>
        /// Gets the root directory containing this environment's isolated test state.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Gets the host for sample source server A.
        /// </summary>
        public IHost SourceAHost { get; }

        /// <summary>
        /// Gets the host for sample source server B.
        /// </summary>
        public IHost SourceBHost { get; }

        /// <summary>
        /// Gets the host for the sample aggregation server.
        /// </summary>
        public IHost AggregationHost { get; }

        /// <summary>
        /// Gets the default client endpoints and configuration for this environment.
        /// </summary>
        public AggregationClientOptions ClientOptions { get; }

        /// <summary>
        /// Gets the mutable values for the first pump on source server A.
        /// </summary>
        public FlatTagValues SourceAValues { get; }

        /// <summary>
        /// Gets the mutable values for the first pump on source server B.
        /// </summary>
        public FlatTagValues SourceBValues { get; }

        /// <summary>
        /// Gets the mutable values for the second pump on source server A.
        /// </summary>
        public FlatTagValues SourceAPump2Values { get; }

        /// <summary>
        /// Gets the mutable values for the second pump on source server B.
        /// </summary>
        public FlatTagValues SourceBPump2Values { get; }

        /// <summary>
        /// Gets the repository directory containing the aggregation sample documents.
        /// </summary>
        public string DocumentsDirectory => FindDocumentsDirectory();

        /// <summary>
        /// Starts the sample server environment.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        /// <param name="forceLeasedContent">The force-leased-content flag supplied by the caller.</param>
        public static async Task<WotSampleEnvironment> StartAsync(
            CancellationToken cancellationToken,
            bool forceLeasedContent = false)
        {
            string id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(WotSampleEnvironment),
                id);
            Directory.CreateDirectory(root);

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
                InstanceName = "SourceB",
                PkiRoot = Path.Combine(root, "SourceB", "pki"),
                Values = sourceBValues,
                Pump2Values = sourceBPump2Values
            });
            RecordingLeasedResourceStore? contentStore = forceLeasedContent || !OperatingSystem.IsWindows()
                ? new RecordingLeasedResourceStore() : null;
            IHost aggregationHost = AggregationServerHost.Build(
                new AggregationServerOptions
                {
                    EndpointUrl = aggregationEndpoint,
                    ApplicationName = $"AggregationServer{id}",
                    PkiRoot = Path.Combine(root, "Aggregation", "pki"),
                    StorageFolder = Path.Combine(root, "Aggregation", "registry"),
                    ResourceStore = contentStore
                });
            var clientOptions = new AggregationClientOptions
            {
                AggregationEndpoint = aggregationEndpoint,
                SourceAEndpoint = sourceAEndpoint,
                SourceBEndpoint = sourceBEndpoint,
                ApplicationName = $"AggregationClient{id}",
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
                sourceBPump2Values,
                contentStore);
            try
            {
                await sourceAHost.StartAsync(cancellationToken).ConfigureAwait(false);
                await sourceBHost.StartAsync(cancellationToken).ConfigureAwait(false);
                await WaitForTcpAsync(sourceAPort, cancellationToken).ConfigureAwait(false);
                await WaitForTcpAsync(sourceBPort, cancellationToken).ConfigureAwait(false);
                await aggregationHost.StartAsync(cancellationToken).ConfigureAwait(false);
                await environment.WaitForAggregationAsync(cancellationToken).ConfigureAwait(false);
                return environment;
            }
            catch
            {
                await environment.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates isolated client state using the supplied document directory and optional source endpoints.
        /// </summary>
        /// <param name="documentsDirectory">The directory containing the aggregation client documents.</param>
        /// <param name="sourceAEndpoint">The optional replacement endpoint for source server A.</param>
        /// <param name="sourceBEndpoint">The optional replacement endpoint for source server B.</param>
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
                ApplicationName = ClientOptions.ApplicationName + Guid.NewGuid().ToString("N"),
                PkiRoot = Path.Combine(Root, "Clients", Guid.NewGuid().ToString("N"), "pki"),
                DocumentsDirectory = documentsDirectory
            };
        }

        /// <summary>
        /// Connects a WoT registry client to the aggregation server with isolated client configuration.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        public Task<WotClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            AggregationClientOptions options = CreateClientOptions(DocumentsDirectory);
            return WotClientConnection.CreateAsync(options, cancellationToken);
        }

        /// <summary>
        /// Connects an OPC UA client to sample source server A.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        public Task<OpcUaClientConnection> ConnectSourceAAsync(
            CancellationToken cancellationToken)
        {
            return OpcUaClientConnection.CreateAsync(
                Root,
                ClientOptions.SourceAEndpoint,
                ClientOptions.ApplicationName + ".SourceA" + Guid.NewGuid().ToString("N"),
                cancellationToken);
        }

        /// <summary>
        /// Connects an OPC UA client to sample source server B.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        public Task<OpcUaClientConnection> ConnectSourceBAsync(
            CancellationToken cancellationToken)
        {
            return OpcUaClientConnection.CreateAsync(
                Root,
                ClientOptions.SourceBEndpoint,
                ClientOptions.ApplicationName + ".SourceB" + Guid.NewGuid().ToString("N"),
                cancellationToken);
        }

        /// <summary>
        /// Copies the sample document tree to an isolated directory for a test to modify.
        /// </summary>
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

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await StopHostAsync(AggregationHost).ConfigureAwait(false);
            await StopHostAsync(SourceBHost).ConfigureAwait(false);
            await StopHostAsync(SourceAHost).ConfigureAwait(false);

            AggregationHost.Dispose();
            SourceBHost.Dispose();
            SourceAHost.Dispose();
            m_contentStore?.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

        }

        private static async Task StopHostAsync(IHost host)
        {
            try
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine("Ignoring best-effort teardown failure: {0}", ex);
            }
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
                        ex.StatusCode == StatusCodes.BadNotConnected ||
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

        /// <summary>
        /// Locates the aggregation documents through the configured repository root or an ancestor checkout.
        /// </summary>
        internal static string FindDocumentsDirectory()
        {
            string? repositoryRoot = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(repositoryRoot))
            {
                string documents = Path.Combine(
                    repositoryRoot, "samples", "WotCon", "AggregationClient", "Documents");
                if (File.Exists(Path.Combine(repositoryRoot, "UA.slnx")) && Directory.Exists(documents))
                {
                    return documents;
                }
                throw new DirectoryNotFoundException(
                    "OPCUA_TEST_REPOSITORY_ROOT must identify the checkout containing the sample documents.");
            }
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

        private readonly RecordingLeasedResourceStore? m_contentStore;
    }

    /// <summary>
    /// Owns a hosted OPC UA client and its connected managed session for sample tests.
    /// </summary>
    internal sealed class OpcUaClientConnection : IAsyncDisposable
    {
        private OpcUaClientConnection(IHost host, ManagedSession session)
        {
            Host = host;
            Session = session;
        }

        /// <summary>
        /// Gets the host that owns the client services and configuration.
        /// </summary>
        public IHost Host { get; }

        /// <summary>
        /// Gets the connected managed OPC UA session.
        /// </summary>
        public ManagedSession Session { get; }

        /// <summary>
        /// Starts a client host, connects its managed session, and loads namespace tables.
        /// </summary>
        /// <param name="root">The root directory used for the test client configuration.</param>
        /// <param name="endpointUrl">The endpoint URL used to connect the client.</param>
        /// <param name="applicationName">The application name used by the test client.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        public static async Task<OpcUaClientConnection> CreateAsync(
            string root,
            string endpointUrl,
            string applicationName,
            CancellationToken cancellationToken)
        {
            IHost host = BuildClientHost(root, endpointUrl, applicationName);
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

        /// <inheritdoc />
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

    /// <summary>
    /// Owns a hosted OPC UA session and WoT registry client for aggregation sample tests.
    /// </summary>
    internal sealed class WotClientConnection : IAsyncDisposable
    {
        private WotClientConnection(IHost host, ManagedSession session, WotRegistryClient registry)
        {
            Host = host;
            Session = session;
            Registry = registry;
        }

        /// <summary>
        /// Gets the host that owns the aggregation client services.
        /// </summary>
        public IHost Host { get; }

        /// <summary>
        /// Gets the managed session connected to the aggregation server.
        /// </summary>
        public ManagedSession Session { get; }

        /// <summary>
        /// Gets the WoT registry client using the aggregation session.
        /// </summary>
        public WotRegistryClient Registry { get; }

        /// <summary>
        /// Creates and connects an aggregation client with the supplied options.
        /// </summary>
        /// <param name="options">The aggregation client options.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
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
                session.OperationTimeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
                session.KeepAliveInterval = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
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

        /// <inheritdoc />
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
