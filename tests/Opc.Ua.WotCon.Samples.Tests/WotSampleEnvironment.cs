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
using AggregationClient;
using AggregationServer;
using FlatTagServer;

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
            FlatTagValues sourceBValues)
        {
            Root = root;
            SourceAHost = sourceAHost;
            SourceBHost = sourceBHost;
            AggregationHost = aggregationHost;
            ClientOptions = clientOptions;
            SourceAValues = sourceAValues;
            SourceBValues = sourceBValues;
        }

        public string Root { get; }

        public IHost SourceAHost { get; }

        public IHost SourceBHost { get; }

        public IHost AggregationHost { get; }

        public AggregationClientOptions ClientOptions { get; }

        public FlatTagValues SourceAValues { get; }

        public FlatTagValues SourceBValues { get; }

        public string DocumentsDirectory => FindDocumentsDirectory();

        public static async Task<WotSampleEnvironment> StartAsync(
            CancellationToken cancellationToken)
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

            IHost sourceAHost = FlatTagServerHost.Build(new FlatTagServerOptions
            {
                EndpointUrl = sourceAEndpoint,
                SourceNamespaceUri = FlatTagServerOptions.SourceANamespaceUri,
                ApplicationName = $"FlatTagServerSourceA{id}",
                InstanceName = "SourceA",
                PkiRoot = Path.Combine(root, "SourceA", "pki"),
                Values = sourceAValues
            });
            IHost sourceBHost = FlatTagServerHost.Build(new FlatTagServerOptions
            {
                EndpointUrl = sourceBEndpoint,
                SourceNamespaceUri = FlatTagServerOptions.SourceBNamespaceUri,
                ApplicationName = $"FlatTagServerSourceB{id}",
                InstanceName = "SourceB",
                PkiRoot = Path.Combine(root, "SourceB", "pki"),
                Values = sourceBValues
            });
            IHost aggregationHost = AggregationServerHost.Build(
                new AggregationServerOptions
                {
                    EndpointUrl = aggregationEndpoint,
                    ApplicationName = $"AggregationServer{id}",
                    PkiRoot = Path.Combine(root, "Aggregation", "pki")
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
                sourceBValues);
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

        public Task<WotClientConnection> ConnectAsync(
            CancellationToken cancellationToken)
        {
            AggregationClientOptions options = CreateClientOptions(DocumentsDirectory);
            return WotClientConnection.CreateAsync(options, cancellationToken);
        }

        public Task<OpcUaClientConnection> ConnectSourceAAsync(
            CancellationToken cancellationToken)
        {
            return OpcUaClientConnection.CreateAsync(
                Root,
                ClientOptions.SourceAEndpoint,
                ClientOptions.ApplicationName + ".SourceA" + Guid.NewGuid().ToString("N"),
                cancellationToken);
        }

        public string CreateDocumentsCopy()
        {
            string target = Path.Combine(Root, "Documents", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            foreach (string source in Directory.EnumerateFiles(DocumentsDirectory))
            {
                File.Copy(source, Path.Combine(target, Path.GetFileName(source)));
            }
            return target;
        }

        public async ValueTask DisposeAsync()
        {
            await StopHostAsync(AggregationHost).ConfigureAwait(false);
            await StopHostAsync(SourceBHost).ConfigureAwait(false);
            await StopHostAsync(SourceAHost).ConfigureAwait(false);

            AggregationHost.Dispose();
            SourceBHost.Dispose();
            SourceAHost.Dispose();
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
