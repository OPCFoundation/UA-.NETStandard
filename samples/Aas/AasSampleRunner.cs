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

using Opc.Ua.Aas.V3;
using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Aas;
using Opc.Ua.Aas.Client;
using Opc.Ua.Aas.Client.Registry;
using Opc.Ua.Aas.Server;
using Opc.Ua.Aas.Server.Materialization;
using Opc.Ua.Aas.Server.Packaging;
using Opc.Ua.Aas.Server.Registry;
using Opc.Ua.Client;
using Opc.Ua.Server.Hosting;

namespace AasSample
{
    /// <summary>
    /// Runs the AAS server, materialization step, and demonstration client.
    /// </summary>
    public static class AasSampleRunner
    {
        /// <summary>
        /// Runs the sample from command-line configuration.
        /// </summary>
        public static async Task RunAsync(
            string[] args,
            CancellationToken cancellationToken = default)
        {
            HostApplicationBuilder configurationBuilder = Host.CreateApplicationBuilder(args);
            var options = new AasSampleOptions
            {
                EndpointUrl = configurationBuilder.Configuration["endpoint"],
                Host = configurationBuilder.Configuration["host"] ?? "localhost",
                Port = ReadPort(configurationBuilder.Configuration),
                ApplicationName = configurationBuilder.Configuration["applicationName"] ?? "AasSample",
                PkiRoot = configurationBuilder.Configuration["pkiRoot"]
            };
            AasSampleResult result = await RunAsync(options, cancellationToken).ConfigureAwait(false);
            Print(result);
        }

        /// <summary>
        /// Runs the sample from explicit options.
        /// </summary>
        public static async Task<AasSampleResult> RunAsync(
            AasSampleOptions options,
            CancellationToken cancellationToken = default)
        {
            Validate(options);
            AasSampleDataset dataset = await AasSampleData.CreateAsync(cancellationToken).ConfigureAwait(false);
            using var registry = new AasRegistryService();
            for (int ii = 0; ii < dataset.RegistryRequests.Count; ii++)
            {
                await registry.UpsertResourceAsync(dataset.RegistryRequests[ii], cancellationToken)
                    .ConfigureAwait(false);
            }

            using IHost server = BuildServer(options, registry);
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WaitForTcpAsync(options.Port, cancellationToken).ConfigureAwait(false);
                await WaitForSessionAsync(options, cancellationToken).ConfigureAwait(false);
                var store = new AasSampleMaterializationDocumentStore(dataset.MaterializationDocuments);
                var projectionHost = new AasSampleProjectionHost(
                    server.Services.GetRequiredService<IAasEnvironmentProjectionHost>(),
                    new AasSampleOperationHandler());
                using var coordinator = new AasMaterializationCoordinator(
                    store,
                    projectionHost);
                AasMaterializeResult materialize = await coordinator.MaterializeAsync(
                    new AasMaterializeRequest { Force = true },
                    cancellationToken).ConfigureAwait(false);
                AasClientObservation observation = await RunClientAsync(
                    options,
                    dataset,
                    cancellationToken)
                    .ConfigureAwait(false);
                return new AasSampleResult(dataset, materialize, store.States, observation, registry.Current.Generation);
            }
            finally
            {
                await server.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static IHost BuildServer(
            AasSampleOptions options,
            IAasRegistryService registry)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            IOpcUaBuilder opcUa = builder.Services.AddOpcUa();
            opcUa.AddOpcTcpTransport();
            opcUa.AddServer(server =>
            {
                server.ApplicationName = options.ApplicationName;
                server.ApplicationUri = $"urn:localhost:OPCFoundation:{options.ApplicationName}";
                server.ProductUri = "uri:opcfoundation.org:AasSample";
                if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                {
                    server.PkiRoot = options.PkiRoot;
                }
                server.AutoAcceptUntrustedCertificates = true;
                server.IncludeUnsecurePolicyNone = true;
                server.EndpointUrls.Add(options.Endpoint);
            });
            opcUa.AddAasV3Server(aas => aas.ControlNamespaceUri = AasSampleData.InstanceNamespaceUri)
                .AddEnvironmentProvider(_ => new InMemoryAasEnvironmentProvider([]))
                .AddOperationHandler<AasSampleOperationHandler>();
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton<IAasRegistryService>(registry);
            builder.Services.AddSingleton<AasRegistryNodeManagerFactory>();
            builder.Services.AddSingleton(sp => new OpcUaServerNodeManagerRegistration(
                sp.GetRequiredService<AasRegistryNodeManagerFactory>()));
            return builder.Build();
        }

        private static async Task<AasClientObservation> RunClientAsync(
            AasSampleOptions options,
            AasSampleDataset dataset,
            CancellationToken cancellationToken)
        {
            using IHost clientHost = BuildClient(options);
            await clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Func<CancellationToken, Task<ManagedSession>> connect = clientHost.Services
                    .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
                ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                await using (session.ConfigureAwait(false))
                {
                    await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
                    session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
                    Func<ManagedSession, CancellationToken, Task<AasRegistryClient>> registryFactory = clientHost.Services
                        .GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>();
                    Func<ManagedSession, CancellationToken, Task<AasClient>> aasFactory = clientHost.Services
                        .GetRequiredService<Func<ManagedSession, CancellationToken, Task<AasClient>>>();
                    AasRegistryClient registry = await registryFactory(session, cancellationToken).ConfigureAwait(false);
                    AasClient aas = await aasFactory(session, cancellationToken).ConfigureAwait(false);
                    int shellMatches = await LookupShellCountAsync(registry, session, cancellationToken)
                        .ConfigureAwait(false);
                    AasGetSubmodelDocumentResult submodel = await registry.GetSubmodelAsync(
                        AasSampleData.PassportSubmodelId,
                        cancellationToken).ConfigureAwait(false);
                    NodeId carbonFootprintNodeId = aas.CreateSubmodelElementNodeId(
                        AasSampleData.PassportSubmodelId,
                        "CarbonFootprint");
                    Opc.Ua.Aas.Client.AasValueReadResult carbonFootprint = await aas.ReadValueAsync(
                        carbonFootprintNodeId,
                        cancellationToken).ConfigureAwait(false);
                    NodeId operationNodeId = aas.CreateSubmodelElementNodeId(
                        AasSampleData.PassportSubmodelId,
                        "RecalculatePassport");
                    Opc.Ua.Aas.Client.AasOperationInvokeResult operation = await aas.InvokeAsync(
                        operationNodeId,
                        new Variant[] { new("operator-request") }.ToArrayOf(),
                        ArrayOf<Variant>.Empty,
                        0d,
                        cancellationToken).ConfigureAwait(false);
                    AasPackageIntegrityResult packageCheck = AasPackageIntegrity.VerifyConsumerBlob(
                        dataset.Package,
                        AasPackageIntegrity.Sha256,
                        dataset.PackageDigest);
                    return new AasClientObservation(
                        shellMatches,
                        submodel.StatusCode,
                        submodel.Document.Length,
                        carbonFootprint.LexicalValue,
                        operation.CallStatusCode,
                        operation.Success,
                        operation.Diagnostic,
                        packageCheck.Succeeded);
                }
            }
            finally
            {
                await clientHost.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static IHost BuildClient(AasSampleOptions options)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services
                .AddOpcUa()
                .AddOpcTcpTransport()
                .AddClient(client =>
                {
                    client.ApplicationName = options.ApplicationName + ".Client";
                    client.ApplicationUri = $"urn:localhost:OPCFoundation:{options.ApplicationName}.Client";
                    client.ProductUri = "uri:opcfoundation.org:AasSample.Client";
                    if (!string.IsNullOrWhiteSpace(options.PkiRoot))
                    {
                        client.PkiRoot = Path.Combine(options.PkiRoot, "client");
                    }
                    client.AutoAcceptUntrustedCertificates = true;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = options.ApplicationName + ".Client",
                        SessionTimeout = TimeSpan.FromSeconds(60)
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.Endpoint;
                    discovery.SecurityMode = MessageSecurityMode.None;
                    discovery.SecurityPolicyUri = SecurityPolicies.None;
                })
                .AddAasV3Client(aas => aas.InstanceNamespaceUri = Opc.Ua.Aas.V3.Namespaces.AasV3)
                .AddAasV3RegistryClient();
            return builder.Build();
        }

        private static async ValueTask<int> LookupShellCountAsync(
            AasRegistryClient registry,
            ManagedSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                ArrayOf<NodeId> shells = await registry.LookupShellsByAssetLinkAsync(
                    "serialNumber",
                    "BP-2026-0001",
                    cancellationToken).ConfigureAwait(false);
                return shells.Count;
            }
            catch (ServiceResultException)
            {
                CallResponse response = await session.CallAsync(
                    null,
                    new[]
                    {
                        new CallMethodRequest
                        {
                            ObjectId = registry.RegistryNodeId,
                            MethodId = ExpandedNodeId.ToNodeId(
                                Opc.Ua.Aas.V3.MethodIds.AASRegistryType_LookupShellsByAssetLink,
                                session.NamespaceUris),
                            InputArguments =
                            [
                                new Variant("serialNumber"),
                                new Variant("BP-2026-0001")
                            ]
                        }
                    }.ToArrayOf(),
                    cancellationToken).ConfigureAwait(false);
                if (response.Results.Count == 0 || StatusCode.IsBad(response.Results[0].StatusCode))
                {
                    return 0;
                }
                if (response.Results[0].OutputArguments.Count > 0 &&
                    response.Results[0].OutputArguments[0].TryGetValue(out ArrayOf<NodeId> nodes))
                {
                    return nodes.Count;
                }
                if (response.Results[0].OutputArguments.Count > 0 &&
                    response.Results[0].OutputArguments[0].TryGetValue(out ArrayOf<string> shellIds))
                {
                    return shellIds.Count;
                }
                return 0;
            }
        }

        private static async Task WaitForSessionAsync(AasSampleOptions options, CancellationToken cancellationToken)
        {
            Exception? last = null;
            for (int ii = 0; ii < 60; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using IHost clientHost = BuildClient(options);
                    await clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        Func<CancellationToken, Task<ManagedSession>> connect = clientHost.Services
                            .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
                        ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                        await using (session.ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                    finally
                    {
                        await clientHost.StopAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is SocketException or ServiceResultException or InvalidOperationException)
                {
                    last = ex;
                }
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The sample server did not accept a session.", last);
        }
        private static async Task WaitForTcpAsync(int port, CancellationToken cancellationToken)
        {
            Exception? last = null;
            for (int ii = 0; ii < 100; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var client = new TcpClient();
#if NET8_0_OR_GREATER
                    await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
#else
                    await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
#endif
                    return;
                }
                catch (SocketException ex)
                {
                    last = ex;
                }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The sample server did not start listening.", last);
        }

        private static void Print(AasSampleResult result)
        {
            Console.WriteLine("AAS sample completed.");
            Console.WriteLine($"Registry generation: {result.RegistryGeneration}");
            Console.WriteLine(
                $"Materialization generation: {result.Materialization.Generation}, " +
                $"documents: {result.Materialization.Results.Count}");
            for (int ii = 0; ii < result.Materialization.Results.Count; ii++)
            {
                AasMaterializationResultData item = result.Materialization.Results[ii];
                Console.WriteLine($"  {item.Xid}: {item.Outcome} -> {item.MaterializedNode}");
                if (!string.IsNullOrEmpty(item.Diagnostic))
                {
                    Console.WriteLine($"    diagnostic: {item.Diagnostic}");
                }
            }
            Console.WriteLine(
                $"DPP semantic IRI: {result.Dataset.CarbonFootprintIdentifier.Iri} " +
                $"({result.Dataset.CarbonFootprintIdentifier.Rule})");
            Console.WriteLine(
                $"DPP tiers: passport={result.Dataset.PassportDisclosure.Tier}, " +
                $"controlled={result.Dataset.ControlledDisclosure.Tier}");
            Console.WriteLine($"LookupShellsByAssetLink matches: {result.ClientObservation.ShellMatches}");
            Console.WriteLine(
                $"GetSubmodel status: {result.ClientObservation.GetSubmodelStatus}, " +
                $"bytes: {result.ClientObservation.GetSubmodelBytes}");
            Console.WriteLine($"CarbonFootprint typed value: {result.ClientObservation.CarbonFootprintLexical}");
            Console.WriteLine(
                $"Invoke RecalculatePassport: {result.ClientObservation.OperationStatus}, " +
                $"success={result.ClientObservation.OperationSuccess}, " +
                $"diagnostic='{result.ClientObservation.OperationDiagnostic}'");
            Console.WriteLine($"Package digest verified: {result.ClientObservation.PackageVerified}");
        }

        private static int ReadPort(ConfigurationManager configuration)
        {
            return int.TryParse(
                configuration["port"],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int port)
                ? port
                : 62560;
        }

        private static void Validate(AasSampleOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.ApplicationName))
            {
                throw new ArgumentException("ApplicationName is required.", nameof(options));
            }
            if (options.EndpointUrl is null &&
                (string.IsNullOrWhiteSpace(options.Host) || options.Port is < 1 or > 65535))
            {
                throw new ArgumentException("A valid host and port are required.", nameof(options));
            }
        }

        private sealed class AasSampleProjectionHost : IAasEnvironmentProjectionHost
        {
            public AasSampleProjectionHost(
                IAasEnvironmentProjectionHost inner,
                IAasOperationHandler operationHandler)
            {
                m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
                m_operationHandler = operationHandler ?? throw new ArgumentNullException(nameof(operationHandler));
            }

            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.AddAsync(environment, valueProvider, m_operationHandler, cancellationToken);
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ShadowReloadAsync(
                    current,
                    environment,
                    valueProvider,
                    m_operationHandler,
                    cancellationToken);
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ImmediateReloadAsync(
                    current,
                    environment,
                    valueProvider,
                    m_operationHandler,
                    cancellationToken);
            }

            public ValueTask RemoveAsync(
                AasEnvironmentProjectionHandle handle,
                CancellationToken cancellationToken = default)
            {
                return m_inner.RemoveAsync(handle, cancellationToken);
            }

            // The projection host serves either AAS metamodel generation. This
            // sample only projects V3, so the V2 overloads delegate straight
            // through rather than substituting the sample's operation handler.
            public ValueTask<AasEnvironmentProjectionHandle> AddAsync(
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.AddAsync(environment, valueProvider, operationHandler, cancellationToken);
            }

            public ValueTask<AasEnvironmentProjectionHandle> ShadowReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ShadowReloadAsync(
                    current,
                    environment,
                    valueProvider,
                    operationHandler,
                    cancellationToken);
            }

            public ValueTask<AasEnvironmentProjectionHandle> ImmediateReloadAsync(
                AasEnvironmentProjectionHandle current,
                Opc.Ua.Aas.V2.AasEnvironment environment,
                IAasValueProvider valueProvider,
                IAasOperationHandler operationHandler,
                CancellationToken cancellationToken = default)
            {
                return m_inner.ImmediateReloadAsync(
                    current,
                    environment,
                    valueProvider,
                    operationHandler,
                    cancellationToken);
            }

            private readonly IAasEnvironmentProjectionHost m_inner;
            private readonly IAasOperationHandler m_operationHandler;
        }
    }

    /// <summary>
    /// Captures the result of one end-to-end run.
    /// </summary>
    public sealed class AasSampleResult
    {
        /// <summary>
        /// Initializes a sample result.
        /// </summary>
        public AasSampleResult(
            AasSampleDataset dataset,
            AasMaterializeResult materialization,
            ArrayOf<AasMaterializationDocumentState> materializationStates,
            AasClientObservation clientObservation,
            long registryGeneration)
        {
            Dataset = dataset;
            Materialization = materialization;
            MaterializationStates = materializationStates;
            ClientObservation = clientObservation;
            RegistryGeneration = registryGeneration;
        }

        /// <summary>
        /// Gets the sample data.
        /// </summary>
        public AasSampleDataset Dataset { get; }

        /// <summary>
        /// Gets the materialization result.
        /// </summary>
        public AasMaterializeResult Materialization { get; }

        /// <summary>
        /// Gets persisted materialization states.
        /// </summary>
        public ArrayOf<AasMaterializationDocumentState> MaterializationStates { get; }

        /// <summary>
        /// Gets the client observation.
        /// </summary>
        public AasClientObservation ClientObservation { get; }

        /// <summary>
        /// Gets the registry generation after seeding.
        /// </summary>
        public long RegistryGeneration { get; }
    }

    /// <summary>
    /// Values observed by the sample client.
    /// </summary>
    public sealed record AasClientObservation(
        int ShellMatches,
        StatusCode GetSubmodelStatus,
        int GetSubmodelBytes,
        string CarbonFootprintLexical,
        StatusCode OperationStatus,
        bool OperationSuccess,
        string OperationDiagnostic,
        bool PackageVerified);
}
