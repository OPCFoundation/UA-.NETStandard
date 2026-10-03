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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Pumps.Server.Builders;
using Opc.Ua.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Device Integration node manager for the OPC 40223 Pumps model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The manager derives from <see cref="DiNodeManager"/> rather than
    /// composing one, because a pump <em>is</em> a Device Integration device:
    /// <c>PumpType</c> reaches into the DI namespace for four of its seven
    /// groups and for two thirds of its nameplate, and its instances belong in
    /// the <c>DeviceSet</c>. Deriving also means the DI hosting pipeline -
    /// <c>ConfigureDevicesFor&lt;PumpsNodeManager&gt;</c> - works on it
    /// unchanged.
    /// </para>
    /// <para>
    /// Model load order is not free choice. DI first, then OPC 10000-200 IA,
    /// then OPC 40001-1 Machinery, then Pumps: Machinery's
    /// <c>MonitoringType/Status/Stacklight</c> is typed by the IA
    /// <c>BasicStacklightType</c>, so IA has to be in the address space before
    /// Machinery loads, and Pumps builds on both.
    /// </para>
    /// </remarks>
    public sealed class PumpsNodeManager : DiNodeManager
    {
        private readonly PumpsServerOptions m_options;
        private readonly List<PumpState> m_pumps = [];
        private readonly Lock m_pumpLock = new();
        private readonly SemaphoreSlim m_createPumpGate = new(1, 1);
        private PumpNamespaceIndices? m_namespaces;

        /// <summary>
        /// Creates a Pumps node manager with default options and no
        /// DI-hosting integration.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, postSetupRunner: null, options: null)
        {
        }

        /// <summary>
        /// Creates a Pumps node manager with explicit options.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The manager options.</param>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            PumpsServerOptions options)
            : this(
                  server,
                  configuration,
                  postSetupRunner: null,
                  new OptionsWrapper<PumpsServerOptions>(
                      options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>
        /// Creates a Pumps node manager that participates in the Device
        /// Integration hosting post-setup pipeline.
        /// </summary>
        /// <param name="server">The hosting server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="postSetupRunner">The DI post-setup runner.</param>
        /// <param name="options">The manager options.</param>
        public PumpsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IDiPostSetupRunner? postSetupRunner,
            IOptions<PumpsServerOptions>? options = null)
            : base(
                  server,
                  configuration,
                  postSetupRunner,
                  ManagerNamespaceUris(options?.Value))
        {
            m_options = (options?.Value ?? new PumpsServerOptions()).Validate();
        }

        /// <summary>
        /// Gets the options the manager was created with.
        /// </summary>
        public PumpsServerOptions Options => m_options;

        /// <summary>
        /// Gets the pumps this manager has materialised, in creation order.
        /// </summary>
        public ArrayOf<PumpState> Pumps
        {
            get
            {
                lock (m_pumpLock)
                {
                    return m_pumps.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets the namespace indices a pump's children live in.
        /// </summary>
        /// <remarks>
        /// Resolved once, after the address space is created - the namespace
        /// table is not complete before that.
        /// </remarks>
        public PumpNamespaceIndices NamespaceIndices
        {
            get
            {
                lock (m_pumpLock)
                {
                    return m_namespaces ??= PumpNamespaceIndices.Resolve(Server.NamespaceUris);
                }
            }
        }

        /// <summary>
        /// Deterministic test seam invoked by
        /// <see cref="CreatePumpAsync(QualifiedName, NodeState?, CancellationToken)"/>
        /// after the new pump is attached to its parent, registered and made a
        /// root notifier, and before the creation completes. Lets a test fail
        /// the registration at its last step and prove the rollback. Always
        /// <c>null</c> in production.
        /// </summary>
        internal Func<PumpState, Task>? PumpRegisteredForTest { get; set; }

        /// <summary>
        /// Creates a pump below the Device Integration <c>DeviceSet</c> and
        /// registers it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="DiNodeManager.CreateDeviceAsync{TDevice}"/> cannot be
        /// used here: it requires a <c>ComponentType</c> descendant, and
        /// OPC 40223 derives <c>PumpType</c> directly from the DI
        /// <c>TopologyElementType</c>, the base of <c>ComponentType</c>. The
        /// same primitives are composed directly.
        /// </para>
        /// <para>
        /// The returned builder is live - the pump is already registered, so
        /// groups materialised through the builder are visible to clients as
        /// soon as they are added.
        /// </para>
        /// <para>
        /// A creation that fails while the pump is registered is undone: the
        /// pump is removed from its parent and from the address space, so the
        /// name can be used again.
        /// </para>
        /// </remarks>
        /// <param name="browseName">
        /// The pump's browse name. A name already taken below the
        /// <c>DeviceSet</c> is rejected.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">
        /// The DeviceSet is unavailable, or the name is already taken.
        /// </exception>
        public ValueTask<IPumpBuilder> CreatePumpAsync(
            QualifiedName browseName,
            CancellationToken cancellationToken = default)
        {
            return CreatePumpAsync(browseName, parent: null, cancellationToken);
        }

        /// <summary>
        /// Creates a pump below an explicit parent and registers it.
        /// </summary>
        /// <param name="browseName">The pump's browse name.</param>
        /// <param name="parent">
        /// The parent to organize the pump into, or <see langword="null"/> for
        /// the Device Integration <c>DeviceSet</c>.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">
        /// The DeviceSet is unavailable, or the name is already taken.
        /// </exception>
        public async ValueTask<IPumpBuilder> CreatePumpAsync(
            QualifiedName browseName,
            NodeState? parent,
            CancellationToken cancellationToken = default)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw new ArgumentException(
                    "A pump browse name is required.",
                    nameof(browseName));
            }

            NodeState deviceSet = parent ?? ResolveDeviceSet();

            // Creations are serialized from the duplicate check to the
            // registration: two concurrent calls with the same name would
            // otherwise both find the name free and both add a pump. The span
            // awaits, so a Lock cannot be held across it.
            await m_createPumpGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            PumpState pump;
            try
            {
                // The duplicate check is by browse name rather than by
                // predicting the NodeId the factory will mint: under
                // counter-based minting a prediction always looks free, so the
                // second pump of the same name would go straight through.
                var existing = new List<BaseInstanceState>();
                deviceSet.GetChildren(SystemContext, existing);
                if (existing.Any(child => child.BrowseName == browseName))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "'{0}' already contains a child named '{1}'.",
                        deviceSet.BrowseName,
                        browseName);
                }

                // The parent is passed so the factory can derive the
                // per-instance NodeIds it stamps on the pump and its mandatory
                // children from the parent chain rather than reusing the
                // type-level ones.
                pump = SystemContext.CreateInstanceOfPumpType(deviceSet, browseName);
                pump.DisplayName = new LocalizedText(browseName.Name);
                pump.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
                deviceSet.AddChild(pump);

                // A pump reports supervision events, so it has to be a notifier
                // before a client can subscribe to it.
                pump.EventNotifier |= EventNotifiers.SubscribeToEvents;

                try
                {
                    await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                        .ConfigureAwait(false);
                    await AddRootNotifierAsync(pump, cancellationToken).ConfigureAwait(false);
                    if (PumpRegisteredForTest != null)
                    {
                        await PumpRegisteredForTest(pump).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Left in place, the half-registered pump would keep its
                    // name taken: a retry fails with BadBrowseNameDuplicated.
                    await RemoveUnregisteredPumpAsync(deviceSet, pump).ConfigureAwait(false);
                    throw;
                }

                if (m_options.OrganizeIntoMachinesFolder && !TryAddToMachinesFolder(pump))
                {
                    m_logger.MachinesFolderUnavailable(browseName.Name);
                }

                lock (m_pumpLock)
                {
                    m_pumps.Add(pump);
                }
            }
            finally
            {
                m_createPumpGate.Release();
            }

            m_logger.PumpMaterialised(browseName.Name, pump.NodeId);
            return new PumpBuilder(
                SystemContext,
                pump,
                NamespaceIndices,
                AddPredefinedNodeSynchronously,
                FindPredefinedNode);
        }

        /// <summary>
        /// Returns a builder for a pump this manager already materialised.
        /// </summary>
        /// <param name="pump">The pump to configure further.</param>
        public IPumpBuilder Pump(PumpState pump)
        {
            if (pump is null)
            {
                throw new ArgumentNullException(nameof(pump));
            }
            return new PumpBuilder(
                SystemContext,
                pump,
                NamespaceIndices,
                AddPredefinedNodeSynchronously,
                FindPredefinedNode);
        }

        /// <summary>
        /// Returns a builder for a pump by NodeId, or <see langword="null"/>
        /// when this manager has no such pump.
        /// </summary>
        /// <param name="nodeId">The pump's NodeId.</param>
        public IPumpBuilder? Pump(NodeId nodeId)
        {
            return FindPredefinedNode(nodeId) is PumpState pump ? Pump(pump) : null;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            if (m_options.LoadIndustrialAutomationModel)
            {
                nodes.AddOpcUaIA(context);
            }
            nodes.AddOpcUaMachinery(context);
            nodes.AddOpcUaPumps(context);
            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>
        /// Releases the pump-creation gate once a creation still in flight
        /// has left it, before the manager itself.
        /// </summary>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await m_createPumpGate.WaitAsync().ConfigureAwait(false);
                m_createPumpGate.Dispose();
            }
            finally
            {
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Undoes a pump creation that failed after the pump was attached to
        /// its parent: deletes what was registered of it (its nodes, its root
        /// notifier and the references to it) and detaches it from the parent.
        /// </summary>
        /// <remarks>
        /// A failure of the cleanup is logged rather than thrown, so the caller
        /// sees the exception that failed the creation.
        /// </remarks>
        private async ValueTask RemoveUnregisteredPumpAsync(NodeState parent, PumpState pump)
        {
            try
            {
                // Only the node this call registered is deleted; a node of the
                // same NodeId that belongs to someone else is left alone.
                if (ReferenceEquals(FindPredefinedNode(pump.NodeId), pump))
                {
                    await DeleteNodeAsync(SystemContext, pump.NodeId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.PumpRollbackFailed(ex, pump.BrowseName.Name);
            }

            try
            {
                // Deleting a registered pump detaches it already; a pump that
                // failed before it was registered, or whose deletion failed, is
                // still attached. The parent is caller supplied and RemoveChild
                // is virtual, so a failing detach is caught like a failing delete.
                parent.RemoveChild(pump);
            }
            catch (Exception ex)
            {
                m_logger.PumpRollbackFailed(ex, pump.BrowseName.Name);
            }
        }

        /// <summary>
        /// Resolves the Device Integration <c>DeviceSet</c>, which is where
        /// OPC 40223 pumps belong.
        /// </summary>
        private NodeState ResolveDeviceSet()
        {
            NodeState? deviceSet = FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNamespaceUri,
                Server.NamespaceUris));
            return deviceSet ?? throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The Device Integration DeviceSet is not available.");
        }

        /// <summary>
        /// Builds the namespace list the base manager registers. DI is always
        /// appended by the base, so it is not repeated here.
        /// </summary>
        private static string[] ManagerNamespaceUris(PumpsServerOptions? options)
        {
            options ??= new PumpsServerOptions();
            var uris = new List<string>(3 + options.AdditionalNamespaceUris.Count)
            {
                Namespaces.Pumps,
                Opc.Ua.Machinery.Namespaces.Machinery
            };
            if (options.LoadIndustrialAutomationModel)
            {
                uris.Add(Opc.Ua.IA.Namespaces.IA);
            }
            foreach (string uri in options.AdditionalNamespaceUris)
            {
                uris.Add(uri);
            }
            return [.. uris];
        }
    }

    internal static partial class PumpsNodeManagerLog
    {
        [LoggerMessage(
            EventId = PumpsServerEventIds.PumpsNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Materialised pump '{Name}' (PumpType), NodeId={NodeId}.")]
        public static partial void PumpMaterialised(
            this ILogger logger,
            string? name,
            NodeId nodeId);

        [LoggerMessage(
            EventId = PumpsServerEventIds.PumpsNodeManager + 1,
            Level = LogLevel.Warning,
            Message = "The Machinery Machines folder is unavailable - pump '{Name}' " +
                "is reachable from the DeviceSet only.")]
        public static partial void MachinesFolderUnavailable(
            this ILogger logger,
            string? name);

        [LoggerMessage(
            EventId = PumpsServerEventIds.PumpsNodeManager + 2,
            Level = LogLevel.Error,
            Message = "Could not remove pump '{Name}' after its creation failed.")]
        public static partial void PumpRollbackFailed(
            this ILogger logger,
            Exception exception,
            string? name);
    }
}
