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

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Client;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes AutoID runtime parameter and typed-client paths in the NativeAOT binary.
    /// </summary>
    [ClassDataSource<AutoIdAotFixture>(Shared = SharedType.PerTestSession)]
    public sealed class AutoIdAotTests(AutoIdAotFixture fixture)
    {
        /// <summary>
        /// Verifies typed reads and writes for RuntimeParameters and ScanActive against a hosted reader.
        /// </summary>
        [Test]
        public async Task RuntimeParametersAndScanControlExecuteThroughTypedClientAsync()
        {
            AutoIdScanParameterReadResult initial =
                await fixture.Client.ReadScanParametersAsync(fixture.Reader).ConfigureAwait(false);

            await Assert.That(initial.Status.Duration).IsEqualTo(StatusCodes.Good);
            await Assert.That(initial.Status.Cycles).IsEqualTo(StatusCodes.Good);
            await Assert.That(initial.Status.DataAvailable).IsEqualTo(StatusCodes.Good);
            await Assert.That(initial.Status.CodeType).IsEqualTo(StatusCodes.Good);
            await Assert.That(initial.Status.CodeTypes).IsEqualTo(StatusCodes.Good);
            await Assert.That(initial.Parameters.CodeType).IsEqualTo("EPC");
            await Assert.That(initial.Parameters.CodeTypes.Count).IsEqualTo(1);
            await Assert.That(initial.Parameters.CodeTypes[0]).IsEqualTo(0u);

            AutoIdScanParameterStatus write = await fixture.Client.WriteScanParametersAsync(
                fixture.Reader,
                initial.Parameters with { Cycles = 2, Duration = 0, DataAvailable = false })
                .ConfigureAwait(false);

            await Assert.That(write.Duration).IsEqualTo(StatusCodes.Good);
            await Assert.That(write.Cycles).IsEqualTo(StatusCodes.Good);
            await Assert.That(write.DataAvailable).IsEqualTo(StatusCodes.Good);
            await Assert.That(write.CodeType).IsEqualTo(StatusCodes.Good);
            await Assert.That(write.CodeTypes).IsEqualTo(StatusCodes.Good);

            AutoIdScanParameterReadResult updated =
                await fixture.Client.ReadScanParametersAsync(fixture.Reader).ConfigureAwait(false);
            await Assert.That(updated.Parameters.Cycles).IsEqualTo(2);
            await Assert.That(fixture.Provider.Parameters.Cycles).IsEqualTo(2);

            ArrayOf<DataValue> specific = await fixture.WriteAndReadReaderParametersAsync().ConfigureAwait(false);
            await Assert.That(specific.Count).IsEqualTo(4);
            await Assert.That(specific[0].WrappedValue).IsEqualTo(Variant.From(new uint[] { 1 }.ToArrayOf()));
            await Assert.That(specific[1].WrappedValue).IsEqualTo(Variant.From((sbyte)15));
            await Assert.That(specific[2].WrappedValue).IsEqualTo(Variant.From(-70));
            await Assert.That(specific[3].WrappedValue).IsEqualTo(Variant.From(1u));
            for (int index = 0; index < specific.Count; index++)
            {
                await Assert.That(specific[index].StatusCode).IsEqualTo(StatusCodes.Good);
                await Assert.That(specific[index].SourceTimestamp.IsNull).IsFalse();
            }
            await Assert.That(fixture.Provider.Parameters.TagTypes[0]).IsEqualTo(1u);
            await Assert.That(fixture.Provider.Parameters.EnableAntennas).IsEqualTo(1u);

            AutoIdScanActiveReadResult idle =
                await fixture.Client.ReadScanActiveAsync(fixture.Reader).ConfigureAwait(false);
            await Assert.That(idle.Status).IsEqualTo(StatusCodes.Good);
            await Assert.That(idle.IsActive).IsFalse();

            await Assert.That(await fixture.Client.SetScanActiveAsync(fixture.Reader, true)
                .ConfigureAwait(false)).IsEqualTo(StatusCodes.Good);
            AutoIdScanActiveReadResult active =
                await fixture.Client.ReadScanActiveAsync(fixture.Reader).ConfigureAwait(false);
            await Assert.That(active.Status).IsEqualTo(StatusCodes.Good);
            await Assert.That(active.IsActive).IsTrue();

            await Assert.That(await fixture.Client.SetScanActiveAsync(fixture.Reader, false)
                .ConfigureAwait(false)).IsEqualTo(StatusCodes.Good);
            AutoIdScanActiveReadResult stopped =
                await fixture.Client.ReadScanActiveAsync(fixture.Reader).ConfigureAwait(false);
            await Assert.That(stopped.Status).IsEqualTo(StatusCodes.Good);
            await Assert.That(stopped.IsActive).IsFalse();
            await Assert.That(fixture.Provider.Starts).IsEqualTo(1);
            await Assert.That(fixture.Provider.Stops).IsEqualTo(1);
        }

        /// <summary>
        /// Executes the standard diagnostics projection and reads its encoded values over a session.
        /// </summary>
        [Test]
        public async Task DiagnosticReportsExecuteAndReadThroughDataAccessAsync()
        {
            ArrayOf<DataValue> values = await fixture.ReportAndReadDiagnosticsAsync().ConfigureAwait(false);
            await Assert.That(values.Count).IsEqualTo(4);
            await Assert.That(values[0].StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(values[0].WrappedValue).IsEqualTo(Variant.From((ushort)2));
            await Assert.That(values[1].StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(values[1].WrappedValue).IsEqualTo(Variant.From("AOT reader ready"));
            await Assert.That(values[2].StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(values[2].WrappedValue).IsEqualTo(Variant.From(ByteString.From(1, 2)));
            await Assert.That(values[3].StatusCode).IsEqualTo(StatusCodes.Good);
            await Assert.That(values[3].WrappedValue).IsEqualTo(Variant.FromStructure(new ScanDataEpc
            {
                PC = 7, UId = ByteString.From(3, 4), XPC_W1 = 8, XPC_W2 = 9
            }));
            await Assert.That(values[2].SourceTimestamp).IsEqualTo(values[3].SourceTimestamp);
            await Assert.That(values[2].SourceTimestamp.IsNull).IsFalse();
        }
    }

    /// <summary>
    /// Hosts one AutoID reader and connects a managed client session for AOT execution tests.
    /// </summary>
    public sealed class AutoIdAotFixture : IAsyncInitializer, IAsyncDisposable
    {
        /// <summary>
        /// Gets the typed AutoID client connected to the hosted reader.
        /// </summary>
        public AutoIdClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the AutoID reader entry discovered through the typed client.
        /// </summary>
        public AutoIdReaderEntry Reader { get; private set; } = null!;

        /// <summary>
        /// Gets the provider backing the hosted reader.
        /// </summary>
        public AutoIdAotProvider Provider { get; } = new();

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            m_telemetry = DefaultTelemetry.Create(
                builder => builder.SetMinimumLevel(LogLevel.Warning));
            m_endpointUrl = CreateEndpointUrl();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer<StandardServer>(ConfigureServer)
                .AddAutoId(options => options.MaximumScanDuration = TimeSpan.FromSeconds(5))
                .ConfigureAutoId(async context =>
                {
                    AutoIdReader reader = await context.Runtime.Reader(
                            new QualifiedName("AotRfidReader", context.Runtime.InstanceNamespaceIndex),
                            Provider)
                        .WithRuntimeParameters(enableScanControl: true)
                        .WithDiagnostics()
                        .BuildAsync(context.CancellationToken)
                        .ConfigureAwait(false);
                    m_readerId = reader.NodeId;
                    m_hostedReader = reader;
                })
                .AddStartupTask((_, _, _) =>
                {
                    m_serverReady.TrySetResult();
                    return default;
                });

            m_serviceProvider = services.BuildServiceProvider();
            m_host = m_serviceProvider.GetServices<IHostedService>().Single();
            await m_host.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await m_serverReady.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            ApplicationConfiguration configuration = await CreateClientConfigurationAsync()
                .ConfigureAwait(false);
            EndpointDescription endpoint = await CoreClientUtils.SelectEndpointAsync(
                configuration,
                m_endpointUrl,
                useSecurity: false,
                m_telemetry,
                CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No suitable AutoID endpoint was found.");
            var configuredEndpoint = new ConfiguredEndpoint(
                null,
                endpoint,
                EndpointConfiguration.Create(configuration));
            m_session = await new ManagedSessionBuilder(configuration, m_telemetry)
                .UseEndpoint(configuredEndpoint)
                .WithSessionName("AutoIdAotClient")
                .ConnectAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Client = new AutoIdClient(m_session, m_telemetry);
            Reader = await Client.GetReaderAsync(m_readerId).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports through the reader sink and reads the registered standard diagnostics over UA.
        /// </summary>
        public async Task<ArrayOf<DataValue>> ReportAndReadDiagnosticsAsync()
        {
            await m_hostedReader.ReportPresenceAsync(2).ConfigureAwait(false);
            await m_hostedReader.ReportLogEntryAsync("AOT reader ready").ConfigureAwait(false);
            byte[] identifier = [1, 2];
            byte[] data = [3, 4];
            var epc = new ScanDataEpc { PC = 7, UId = ByteString.From(data), XPC_W1 = 8, XPC_W2 = 9 };
            await m_hostedReader.ReportAccessAsync("Scan", "Device",
                [new RfidAccessResult
                {
                    EncodingMask = (uint)(RfidAccessResultFields.Identifier | RfidAccessResultFields.Timestamp |
                        RfidAccessResultFields.RWData),
                    Identifier = new ScanData
                    {
                        SwitchField = ScanDataFields.ByteString, ByteString = ByteString.From(identifier)
                    },
                    RWData = new ScanData
                    {
                        SwitchField = ScanDataFields.Custom, Custom = Variant.FromStructure(epc)
                    },
                    Timestamp = DateTimeUtc.Now
                }]).ConfigureAwait(false);
            identifier[0] = 99;
            data[0] = 99;
            epc.PC = 99;
            ISystemContext context = m_hostedReader.Runtime.SystemContext;
            ushort ns = context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
            NodeState diagnostics = m_hostedReader.Device.Diagnostics!;
            NodeState logbook = diagnostics.FindChildWithQualifiedName(context, new QualifiedName("Logbook", ns))!;
            NodeState access = diagnostics.FindChildWithQualifiedName(context, new QualifiedName("LastAccess", ns))!;
            ReadResponse response = await (m_session ??
                throw new InvalidOperationException("The AutoID session is not connected.")).ReadAsync(
                    null, 0, TimestampsToReturn.Source,
                    [
                        new ReadValueId
                        {
                            NodeId = diagnostics.FindChildWithQualifiedName(
                                context, new QualifiedName("Presence", ns))!.NodeId,
                            AttributeId = Attributes.Value
                        },
                        new ReadValueId
                        {
                            NodeId = logbook.FindChildWithQualifiedName(
                                context, new QualifiedName("LastLogEntry", ns))!.NodeId,
                            AttributeId = Attributes.Value
                        },
                        new ReadValueId
                        {
                            NodeId = access.FindChildWithQualifiedName(
                                context, new QualifiedName("Identifier", ns))!.NodeId,
                            AttributeId = Attributes.Value
                        },
                        new ReadValueId
                        {
                            NodeId = access.FindChildWithQualifiedName(
                                context, new QualifiedName("RWData", ns))!.NodeId,
                            AttributeId = Attributes.Value
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
            return response.Results;
        }

        /// <summary>
        /// Exercises the optional reader bindings and their wire encoding in the AOT executable.
        /// </summary>
        public async Task<ArrayOf<DataValue>> WriteAndReadReaderParametersAsync()
        {
            ISystemContext context = m_hostedReader.Runtime.SystemContext;
            ushort ns = context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
            string[] names = ["TagTypes", "RfPower", "MinRssi", "EnableAntennas"];
            Variant[] values =
            [
                Variant.From(new uint[] { 1 }.ToArrayOf()),
                Variant.From((sbyte)15), Variant.From(-70), Variant.From(1u)
            ];
            var writes = new WriteValue[names.Length];
            var reads = new ReadValueId[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                NodeState node = m_hostedReader.Device.RuntimeParameters!.FindChildWithQualifiedName(
                    context, new QualifiedName(names[index], ns))!;
                writes[index] = new WriteValue
                {
                    NodeId = node.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(values[index])
                };
                reads[index] = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value };
            }
            WriteResponse write = await m_session!.WriteAsync(null, writes.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            for (int index = 0; index < write.Results.Count; index++)
            {
                if (write.Results[index] != StatusCodes.Good)
                {
                    throw new ServiceResultException(write.Results[index]);
                }
            }
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Source,
                reads.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            return read.Results;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
                m_session = null;
            }
            if (m_host != null)
            {
                await m_host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                m_host = null;
            }
            if (m_serviceProvider != null)
            {
                await m_serviceProvider.DisposeAsync().ConfigureAwait(false);
                m_serviceProvider = null;
            }
            if (m_clientConfiguration?.CertificateManager is IDisposable certificateManager)
            {
                certificateManager.Dispose();
            }
            if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
                m_pkiRoot = null;
            }
            if (m_serverPkiRoot != null && Directory.Exists(m_serverPkiRoot))
            {
                Directory.Delete(m_serverPkiRoot, recursive: true);
                m_serverPkiRoot = null;
            }
        }

        /// <summary>
        /// Creates client configuration and a private certificate store for the fixture.
        /// </summary>
        private async Task<ApplicationConfiguration> CreateClientConfigurationAsync()
        {
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "OpcUaAutoIdAot",
                Guid.NewGuid().ToString("N"));
            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "AutoIdAotClient",
                ApplicationUri = "urn:localhost:OPCFoundation:AutoIdAotClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "own"),
                        SubjectName = "CN=AutoIdAotClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
            configuration.CertificateManager ??= CertificateManagerFactory.Create(
                configuration.SecurityConfiguration,
                m_telemetry);
            configuration.CertificateManager.AcceptError = static (_, _) => true;
            m_clientConfiguration = configuration;
            return configuration;
        }

        /// <summary>
        /// Configures the hosted server's endpoint and certificate store.
        /// </summary>
        private void ConfigureServer(OpcUaServerOptions options)
        {
            const string applicationName = "AutoIdAotServer";
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:OPCFoundation:" + applicationName;
            options.ProductUri = "urn:localhost:OPCFoundation:" + applicationName + ":product";
            m_serverPkiRoot = Path.Combine(
                Path.GetTempPath(),
                "OpcUaAutoIdAotServer",
                Guid.NewGuid().ToString("N"));
            options.PkiRoot = m_serverPkiRoot;
            options.AutoAcceptUntrustedCertificates = true;
            options.IncludeUnsecurePolicyNone = true;
            options.EndpointUrls.Clear();
            options.EndpointUrls.Add(m_endpointUrl);
        }

        /// <summary>
        /// Reserves an available loopback TCP port for the test server endpoint.
        /// </summary>
        private static string CreateEndpointUrl()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                return "opc.tcp://localhost:" + port.ToString(CultureInfo.InvariantCulture) +
                    "/AutoIdAotServer";
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Gets the telemetry context used by the hosted server and client.
        /// </summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>
        /// Gets the configured server endpoint.
        /// </summary>
        private string m_endpointUrl = string.Empty;

        /// <summary>
        /// Signals that the hosted server has completed startup, including reader registration.
        /// </summary>
        private readonly TaskCompletionSource m_serverReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The hosted reader used to report deterministic diagnostics.</summary>
        private AutoIdReader m_hostedReader = null!;

        /// <summary>
        /// Holds the NodeId of the configured test reader.
        /// </summary>
        private NodeId m_readerId = NodeId.Null;

        /// <summary>
        /// Holds the private PKI directory used by the client.
        /// </summary>
        private string? m_pkiRoot;

        /// <summary>
        /// Holds the private PKI directory used by the server.
        /// </summary>
        private string? m_serverPkiRoot;

        /// <summary>
        /// Holds the client application configuration and its certificate manager.
        /// </summary>
        private ApplicationConfiguration? m_clientConfiguration;

        /// <summary>
        /// Holds the hosted server service provider.
        /// </summary>
        private ServiceProvider? m_serviceProvider;

        /// <summary>
        /// Holds the hosted server service.
        /// </summary>
        private IHostedService? m_host;

        /// <summary>
        /// Holds the connected client session.
        /// </summary>
        private ManagedSession? m_session;
    }

    /// <summary>
    /// Deterministic provider used by the NativeAOT AutoID integration test.
    /// </summary>
    public sealed class AutoIdAotProvider : IAutoIdAsyncScanProvider, IAutoIdReaderParametersProvider
    {
        /// <summary>
        /// Gets the configured RFID reader kind.
        /// </summary>
        public AutoIdReaderKind Kind => AutoIdReaderKind.Rfid;

        /// <summary>
        /// Gets the supported RFID code formats.
        /// </summary>
        public ArrayOf<string> SupportedCodeTypes => ["EPC"];

        /// <summary>
        /// Gets the explicitly simulated reader capabilities; these are not inferred hardware settings.
        /// </summary>
        public AutoIdReaderParameterCapabilities ReaderParameterCapabilities { get; } = new()
        {
            TagTypes = ["EPC Class1 Gen2 V2", "ISO 15693"],
            SupportsRfPower = true,
            SupportsMinRssi = true,
            ConfiguredAntennas = 3
        };

        /// <summary>
        /// Gets the committed runtime parameters.
        /// </summary>
        public AutoIdScanParameters Parameters { get; private set; } =
            new()
            {
                CodeType = "EPC",
                CodeTypes = [0],
                TagTypes = [0],
                RfPower = 10,
                MinRssi = -60,
                EnableAntennas = 3
            };

        /// <summary>
        /// Gets the number of scan starts requested by the server.
        /// </summary>
        public int Starts { get; private set; }

        /// <summary>
        /// Gets the number of scan stops requested by the server.
        /// </summary>
        public int Stops { get; private set; }

        /// <inheritdoc/>
        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdScanParameters> ReadParametersAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<AutoIdScanParameters>(Parameters);
        }

        /// <inheritdoc/>
        public ValueTask WriteParametersAsync(
            AutoIdScanParameters parameters,
            CancellationToken cancellationToken = default)
        {
            Parameters = parameters;
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdOperationStatusEnumeration> StartScanAsync(
            ScanSettings settings,
            CancellationToken cancellationToken = default)
        {
            Starts++;
            return new ValueTask<AutoIdOperationStatusEnumeration>(
                AutoIdOperationStatusEnumeration.SUCCESS);
        }

        /// <inheritdoc/>
        public ValueTask StopScanAsync(CancellationToken cancellationToken = default)
        {
            Stops++;
            return default;
        }
    }
}
