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

#if NET10_0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using ISubscription = Opc.Ua.Client.Subscriptions.ISubscription;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Hosts genuine native GMS systems on one Machinery owner with real opc.tcp sessions and explicit providers.
    /// </summary>
    internal sealed partial class GmsMcpFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates a stock endpoint rather than replacing its service calls with a fake protocol.
        /// </summary>
        internal GmsMcpFixture()
        {
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
        }

        /// <summary>
        /// Gets the deterministic acquisition time used independently of server publication time.
        /// </summary>
        internal static DateTimeUtc Acquired { get; } = new(2026, 2, 3, 4, 5, 6);

        /// <summary>
        /// Gets the fully configured native instance.
        /// </summary>
        internal GmsHandle System { get; private set; } = null!;

        /// <summary>
        /// Gets the mandatory-only instance with a separate result store.
        /// </summary>
        internal GmsHandle Minimal { get; private set; } = null!;

        /// <summary>
        /// Gets the real caller-owned session with the shared subscription engine.
        /// </summary>
        internal ISession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the stock namespace owner.
        /// </summary>
        internal MachineryNodeManager Manager => m_factory.Manager!;

        /// <summary>
        /// Gets the actual subtype assigned before the full instance is published.
        /// </summary>
        internal NodeId VendorType { get; private set; }

        /// <summary>
        /// Gets the explicitly injected in-memory equipment provider.
        /// </summary>
        internal InMemoryGmsProvider Provider { get; } = new(Snapshot());

        /// <summary>
        /// Gets the store boundary used to prove that passive tools never acknowledge.
        /// </summary>
        internal Mock<IGmsResultStore> Store { get; } = new();

        /// <summary>
        /// Gets live subscriptions for cleanup and caller-resource isolation assertions.
        /// </summary>
        internal ISubscriptionManager Subscriptions =>
            Session.TryGetSubscriptionManager(out ISubscriptionManager? value)
                ? value : throw new InvalidOperationException("A managed subscription engine is required.");

        /// <summary>
        /// Starts a real endpoint and builds full/minimal GMS instances using the accepted native builder.
        /// </summary>
        internal async Task StartAsync(bool warnings = false)
        {
            m_pki = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,
                "artifacts", "gms-mcp-pki", Guid.NewGuid().ToString("N")[..8]));
            await m_server.LoadConfigurationAsync(Path.Combine(m_pki, "s")).ConfigureAwait(false);
            await m_server.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = Manager.CreateMachineryBuildContext(Options());
            var storage = new InMemoryGmsResultStore(Manager.SystemContext.AsMessageContext());
            Store.Setup(value => value.ReadLatestAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) => storage.ReadLatestAsync(ct));
            Store.Setup(value => value.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string id, CancellationToken ct) => storage.ReadAsync(id, ct));
            Store.Setup(value => value.ListIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((int maximum, CancellationToken ct) => storage.ListIdsAsync(maximum, ct));
            Store.Setup(value => value.WriteAsync(It.IsAny<GmsResult>(), It.IsAny<CancellationToken>()))
                .Returns((GmsResult result, CancellationToken ct) => storage.WriteAsync(result, ct));
            Store.Setup(value => value.AcknowledgeAsync(It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<string> ids, CancellationToken ct) => storage.AcknowledgeAsync(ids, ct));
            VendorType = new NodeId("VendorGmsType", context.InstanceNamespaceIndex);
            await Manager.AddPredefinedNodeAsync(new BaseObjectTypeState
            {
                NodeId = VendorType,
                BrowseName = new QualifiedName("VendorGmsType", context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Vendor metrology system"),
                SuperTypeId = NodeId.Create(
                    GMS.ObjectTypes.GMSType, GmsModel.NamespaceUri, context.Context.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            }).ConfigureAwait(false);
            GmsFeatures features = GmsFeatures.Sensors | GmsFeatures.Identification | GmsFeatures.Loading |
                GmsFeatures.ToolMonitoring | GmsFeatures.Calibration | GmsFeatures.Production |
                GmsFeatures.Accessories | GmsFeatures.AdditionalSensors;
            if (warnings)
            {
                features |= GmsFeatures.SensorWarnings;
                GmsSensorWarning first = EnvironmentalWarning();
                await Provider.SetSnapshotAsync(Snapshot() with
                {
                    SensorWarnings =
                    [
                        first,
                        first with
                        {
                            Key = "CompensationWarning", ErrorCode = "ENV43",
                            Message = new LocalizedText("Ambient temperature exceeds the compensation boundary")
                        }
                    ]
                }).ConfigureAwait(false);
            }
            System = await context.AddGms("McpGms")
                .WithProvider(Provider)
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("MCP metrology tests");
                    identity.SerialNumber = "GMS-001";
                    identity.ProductInstanceUri = "urn:gms:mcp:full";
                })
                .WithOptions(new GmsSystemOptions { Features = features })
                .WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                .WithMachinery(builder => builder.State.TypeDefinitionId = VendorType)
                .WithResults(Store.Object, new GmsResultBindingOptions
                {
                    PublishedResults = 2,
                    IncludeUsedTools = true,
                    EnableFileTransfer = true,
                    EnableIntermediateEvents = true,
                    Corrections =
                    [
                        new GmsCorrection("offset-1", "bore", Analog(0.25), Analog(10.25))
                        {
                            LowerControlLimit = Analog(-0.2), UpperControlLimit = Analog(0.2),
                            Description = new LocalizedText("Bore deviation"), ProgramName = "InspectBore"
                        },
                        new GmsCorrection("offset-2", "length", absoluteValue: Analog(20.0))
                    ]
                }).BuildAsync().ConfigureAwait(false);
            Minimal = await context.AddGms("McpMinimal")
                .WithProvider(new InMemoryGmsProvider(new GmsSystemSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("MCP metrology tests");
                    identity.SerialNumber = "GMS-002";
                    identity.ProductInstanceUri = "urn:gms:mcp:minimal";
                })
                .WithResults(new InMemoryGmsResultStore(Manager.SystemContext.AsMessageContext()),
                    new GmsResultBindingOptions { PublishedResults = 0, EnableFileTransfer = false })
                .BuildAsync().ConfigureAwait(false);
            Session = await ConnectAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Opens another real managed session with independent metadata and file ownership.
        /// </summary>
        internal async Task<ISession> ConnectAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var client = new ClientFixture(telemetry);
            m_clients.Add(client);
            client.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            client.SessionFactory = new ManagedSessionFactory(telemetry)
                .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            await client.LoadClientConfigurationAsync(Path.Combine(m_pki!, "c" + m_clients.Count), "GmsMcpClient")
                .ConfigureAwait(false);
            ISession session = await client.ConnectAsync(
                new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None)
                .ConfigureAwait(false);
            session.MessageContext.Factory.Builder.AddGmsTypeSystem().Commit();
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Waits for actual server-side monitored-item creation instead of racing publication against a sleep.
        /// </summary>
        internal async Task WaitForObservationAsync(ISubscription? unrelated = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!Subscriptions.Items.Any(subscription => subscription != unrelated &&
                subscription.MonitoredItems.Items.Any(item => item.Created)))
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds an identified final or intermediate result with known metadata, raw sample quality and file bytes.
        /// </summary>
        internal static GmsResult Result(
            string id, string job = "J-1", bool intermediate = false, int fileBytes = 64, bool includeUsedTools = true)
        {
            byte[] bytes = new byte[fileBytes];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 251);
            }
            return new GmsResult(Sample(Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType
                {
                    ResultId = id, JobId = job, EncodingMask = (uint)ResultMetaDataTypeFields.JobId
                },
                ResultContent = [Variant.From(10.25)]
            }), StatusCodes.UncertainDataSubNormal), includeUsedTools ? ["probe-7"] : default,
                intermediate ? [Characteristic()] : default, intermediate ? "InspectBore" : null, intermediate,
                new ByteString(bytes));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ISession session in m_sessions)
            {
                if (session.Connected)
                {
                    await session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                await session.DisposeAsync().ConfigureAwait(false);
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (Minimal != null)
            {
                await Minimal.DisposeAsync().ConfigureAwait(false);
            }
            if (System != null)
            {
                await System.DisposeAsync().ConfigureAwait(false);
            }
            await m_server.StopAsync().ConfigureAwait(false);
            if (m_pki != null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, recursive: true);
            }
        }

        /// <summary>
        /// Selects one stock owner with small live handle bounds so resource leaks are observable.
        /// </summary>
        private static MachineryServerOptions Options() => new()
        {
            Parts = MachineryParts.BuildingBlocks | MachineryParts.Result,
            InstanceNamespaceUri = "urn:opcfoundation:gms:mcp-tests",
            MaxPinnedResultHandles = 2,
            MaxConcurrentResultTransfers = 2
        };

        /// <summary>The one GMS-compatible namespace-manager factory.</summary>
        private readonly Factory m_factory = new();

        /// <summary>The owned real OPC UA server fixture.</summary>
        private readonly ServerFixture<StandardServer> m_server;

        /// <summary>The caller-owned sessions opened by this fixture.</summary>
        private readonly List<ISession> m_sessions = [];

        /// <summary>The owned client test applications.</summary>
        private readonly List<ClientFixture> m_clients = [];

        /// <summary>The test-local certificate-store root removed during teardown.</summary>
        private string? m_pki;

        /// <summary>
        /// Loads canonical GMS, MachineTool and Machinery dependencies into the same stock manager.
        /// </summary>
        private sealed class Factory : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => new GmsModelProvider().NamespaceUris;

            /// <summary>
            /// Gets the one server-owned manager.
            /// </summary>
            internal MachineryNodeManager? Manager { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server, ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new MachineryNodeManager(server, configuration, [new GmsModelProvider()], Options());
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }
    }
}
#endif
