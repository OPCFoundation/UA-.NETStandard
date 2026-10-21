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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Di;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    /// <summary>
    /// A real UA-TCP host with generated models, original service contexts, and isolated temporary PKI.
    /// </summary>
    internal sealed class ProductionDatasetServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates an isolated bounded test authority and deterministic transfer clock.
        /// </summary>
        internal ProductionDatasetServerFixture(
            ProductionDatasetOptions? options = null,
            Action<ProductionDatasetManagementState, ISystemContext>? select = null)
        {
            m_select = select;
            m_options = options ?? new ProductionDatasetOptions
            {
                MaximumFileBytes = 1024,
                MaximumTransfers = 4,
                MaximumDatasetCount = 8,
                MaximumComponents = 16,
                MaximumStringLength = 64,
                TransferTimeout = TimeSpan.FromSeconds(10)
            };
            m_root = Path.Combine(
                Path.GetTempPath(), "ds-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Gets the deterministic byte-per-component test authority.
        /// </summary>
        internal ProductionDatasetTestProvider Provider { get; } = new();

        /// <summary>
        /// Gets the policy that records real callers and can deny selected operations.
        /// </summary>
        internal ProductionDatasetTestPolicy Policy { get; } = new();

        /// <summary>
        /// Gets the controllable inactivity clock.
        /// </summary>
        internal ProductionDatasetTestClock Clock { get; } = new();

        /// <summary>
        /// Gets the first real session.
        /// </summary>
        internal ClientSession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the typed high-level client.
        /// </summary>
        internal ProductionDatasetClient Client { get; private set; } = null!;

        /// <summary>
        /// Gets the owner of the generated test address space.
        /// </summary>
        internal ProductionDatasetTestNodeManager Manager => m_server!.Server.Manager;

        /// <summary>
        /// Gets the real production dataset binding under test.
        /// </summary>
        internal ProductionDatasetBinding Binding => Manager.Binding;

        /// <summary>
        /// Starts the real service host and connects a typed client over UA-TCP.
        /// </summary>
        internal async Task StartAsync()
        {
            Directory.CreateDirectory(m_root);
            m_server = new ServerFixture<ProductionDatasetTestServer>(
                telemetry => new ProductionDatasetTestServer(telemetry, Provider, Policy, Clock, m_options, m_select))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.StartAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            m_clients = new ClientFixture(m_server.Server.CurrentInstance.Telemetry);
            m_clients.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await m_clients.LoadClientConfigurationAsync(Path.Combine(m_root, "client"), "DatasetTestClient")
                .ConfigureAwait(false);
            Session = await ConnectSessionAsync().ConfigureAwait(false);
            Client = new ProductionDatasetClient(Session, Binding.Node.NodeId, new ProductionDatasetClientOptions
            {
                MaximumFileBytes = 1024,
                MaximumDatasetCount = 8,
                ChunkSize = 64
            });
        }

        /// <summary>
        /// Opens another real session for ownership and identity tests.
        /// </summary>
        internal async Task<ClientSession> ConnectSessionAsync()
        {
            ClientSession session = await m_clients!.ConnectAsync(
                new Uri(m_server!.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None)
                .ConfigureAwait(false);
            m_sessions.Add(session);
            return session;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ClientSession session in m_sessions)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                try
                {
                    if (m_server.Server?.Manager?.Binding != null)
                    {
                        await m_server.Server.Manager.Binding.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await m_server.StopAsync().ConfigureAwait(false);
                }
            }
            if (m_clients != null)
            {
                await m_clients.DisposeAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, true);
            }
        }

        /// <summary>The explicitly selected protocol limits.</summary>
        private readonly ProductionDatasetOptions m_options;

        /// <summary>An optional exact subtree selection for composition tests.</summary>
        private readonly Action<ProductionDatasetManagementState, ISystemContext>? m_select;

        /// <summary>The fixture-owned worktree artifact directory.</summary>
        private readonly string m_root;

        /// <summary>The reusable real-server fixture.</summary>
        private ServerFixture<ProductionDatasetTestServer>? m_server;

        /// <summary>The reusable real-client fixture.</summary>
        private ClientFixture? m_clients;

        /// <summary>The sessions owned by this fixture.</summary>
        private readonly List<ClientSession> m_sessions = [];
    }

    /// <summary>
    /// Places the test manager in the real server's service dispatch tree.
    /// </summary>
    internal sealed class ProductionDatasetTestServer : StandardServer
    {
        /// <summary>
        /// Captures the injected test dependencies.
        /// </summary>
        internal ProductionDatasetTestServer(
            ITelemetryContext telemetry,
            IProductionDatasetProvider provider,
            IPlasticsRubberAccessPolicy policy,
            TimeProvider clock,
            ProductionDatasetOptions options,
            Action<ProductionDatasetManagementState, ISystemContext>? select)
            : base(telemetry)
        {
            m_provider = provider;
            m_policy = policy;
            m_clock = clock;
            m_options = options;
            m_select = select;
        }

        /// <summary>
        /// Gets the registered address-space owner.
        /// </summary>
        internal ProductionDatasetTestNodeManager Manager { get; private set; } = null!;

        /// <inheritdoc/>
        protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Manager = new ProductionDatasetTestNodeManager(
                server, configuration, m_provider, m_policy, m_clock, m_options, m_select);
            return new ValueTask<IMasterNodeManager>(new MasterNodeManager(server, configuration, null, [Manager]));
        }

        /// <summary>The injected storage and activation authority.</summary>
        private readonly IProductionDatasetProvider m_provider;

        /// <summary>The injected shared authorization policy.</summary>
        private readonly IPlasticsRubberAccessPolicy m_policy;

        /// <summary>The deterministic inactivity clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The selected resource limits.</summary>
        private readonly ProductionDatasetOptions m_options;

        /// <summary>The exact pre-binding member selection.</summary>
        private readonly Action<ProductionDatasetManagementState, ISystemContext>? m_select;
    }

    /// <summary>
    /// Loads licensed generated models and binds their real method instances.
    /// </summary>
    internal sealed class ProductionDatasetTestNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Registers model and application namespaces without fixing their numeric indexes.
        /// </summary>
        internal ProductionDatasetTestNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IProductionDatasetProvider provider,
            IPlasticsRubberAccessPolicy policy,
            TimeProvider clock,
            ProductionDatasetOptions options,
            Action<ProductionDatasetManagementState, ISystemContext>? select)
            : base(server, configuration, kInstanceUri, Di.Namespaces.OpcUaDi, Namespaces.PlasticsRubber)
        {
            m_provider = provider;
            m_policy = policy;
            m_clock = clock;
            m_options = options;
            m_select = select;
        }

        /// <summary>
        /// Gets the full protocol binding.
        /// </summary>
        internal ProductionDatasetBinding Binding { get; private set; } = null!;

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            ushort index = SystemContext.NamespaceUris.GetIndexOrAppend(kInstanceUri);
            var root = new FolderState(null)
            {
                NodeId = new NodeId("DatasetTests", index),
                BrowseName = new QualifiedName("DatasetTests", index),
                DisplayName = new LocalizedText("Dataset tests"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = ReferenceTypeIds.Organizes
            };
            root.AddReference(ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ObjectsFolder);
            await AddPredefinedNodeAsync(root, cancellationToken).ConfigureAwait(false);
            var node = SystemContext.CreateInstanceOfProductionDatasetManagementType(
                root, new QualifiedName("Datasets", index));
            m_select?.Invoke(node, SystemContext);
            root.AddChild(node);
            if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                references = [];
                externalReferences.Add(Opc.Ua.ObjectIds.ObjectsFolder, references);
            }
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            Binding = await ProductionDatasetBinding.CreateAsync(
                this, node, m_provider, m_policy, m_options, timeProvider: m_clock,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            nodes.AddOpcUaPlasticsRubber(context);
            IEncodeableFactoryBuilder builder = context.EncodeableFactory.Builder;
            builder.AddOpcUaPlasticsRubber();
            builder.Commit();
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>The private test instance namespace.</summary>
        private const string kInstanceUri = "urn:opcfoundation:tests:production-datasets";

        /// <summary>The injected per-instance device authority.</summary>
        private readonly IProductionDatasetProvider m_provider;

        /// <summary>The shared-model authorization policy.</summary>
        private readonly IPlasticsRubberAccessPolicy m_policy;

        /// <summary>The injected inactivity clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The selected bounds and optional capabilities.</summary>
        private readonly ProductionDatasetOptions m_options;

        /// <summary>The exact pre-binding member selection.</summary>
        private readonly Action<ProductionDatasetManagementState, ISystemContext>? m_select;
    }

    /// <summary>
    /// Advances protocol time explicitly without sleeping in expiry tests.
    /// </summary>
    internal sealed class ProductionDatasetTestClock : TimeProvider
    {
        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>
        /// Advances the deterministic UTC clock.
        /// </summary>
        internal void Advance(TimeSpan value)
        {
            Interlocked.Add(ref m_ticks, value.Ticks);
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(Interlocked.Read(ref m_ticks), TimeSpan.Zero);
        }

        /// <inheritdoc/>
        public override long GetTimestamp()
        {
            return Interlocked.Read(ref m_ticks);
        }

        /// <summary>The deterministic UTC tick count.</summary>
        private long m_ticks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;
    }

    /// <summary>
    /// Records the original context and supports deterministic member- or session-specific denial.
    /// </summary>
    internal sealed class ProductionDatasetTestPolicy : IPlasticsRubberAccessPolicy
    {
        /// <summary>
        /// Gets or sets the method or property to deny.
        /// </summary>
        internal NodeId DeniedMember { get; set; }

        /// <summary>
        /// Gets or sets whether every member is denied.
        /// </summary>
        internal bool DenyAll { get; set; }

        /// <summary>
        /// Gets the most recent original operation context.
        /// </summary>
        internal PlasticsRubberRequestContext LastRequest { get; private set; }

        /// <inheritdoc/>
        public ValueTask AuthorizeAsync(
            PlasticsRubberRequestContext context,
            NodeId memberId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = context;
            if (DenyAll || memberId == DeniedMember)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            return default;
        }
    }
}
