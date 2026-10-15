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
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Creates and configures OPC 40223 pumps in an existing Device Integration owner.
    /// </summary>
    /// <remarks>
    /// Shared by the standalone <see cref="PumpsNodeManager"/> and composed Machinery hosts.
    /// Obtain the owner's single runtime with <see cref="DiNodeManagerPumpsExtensions.CreatePumpsRuntime"/>.
    /// The owner releases it on shutdown; disposing it explicitly stops further pump creation.
    /// </remarks>
    public sealed class PumpsRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Creates the shared runtime for an address-space owner.
        /// </summary>
        internal PumpsRuntime(DiNodeManager manager, PumpsServerOptions options)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
            m_logger = manager.SystemContext.Telemetry.CreateLogger<PumpsRuntime>();
        }

        /// <summary>
        /// Gets the existing owner of all model and instance nodes.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the owner's application instance namespace.
        /// </summary>
        public ushort InstanceNamespaceIndex => Manager is MachineryNodeManager machinery
            ? machinery.MachineryInstanceNamespaceIndex
            : Manager.InstanceNamespaceIndex;

        /// <summary>
        /// Gets the owner's system context.
        /// </summary>
        public ISystemContext SystemContext => Manager.SystemContext;

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
                    return m_namespaces ??= PumpNamespaceIndices.Resolve(SystemContext.NamespaceUris);
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
            ThrowIfDisposed();
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
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stopping.Token);
            await m_createPumpGate.WaitAsync(pending.Token).ConfigureAwait(false);
            PumpState pump;
            try
            {
                ThrowIfDisposed();
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
                    await Manager.AddPredefinedNodeAsync(pump, cancellationToken)
                        .ConfigureAwait(false);
                    await Manager.RegisterRootNotifierAsync(pump, cancellationToken).ConfigureAwait(false);
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

                if (m_options.OrganizeIntoMachinesFolder && !Manager.TryAddToMachinesFolder(pump))
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
                Register,
                Manager.FindPredefinedNode);
        }

        /// <summary>
        /// Returns a builder for a pump this manager already materialised.
        /// </summary>
        /// <param name="pump">The pump to configure further.</param>
        public IPumpBuilder Pump(PumpState pump)
        {
            ThrowIfDisposed();
            if (pump is null)
            {
                throw new ArgumentNullException(nameof(pump));
            }
            return new PumpBuilder(
                SystemContext,
                pump,
                NamespaceIndices,
                Register,
                Manager.FindPredefinedNode);
        }

        /// <summary>
        /// Returns a builder for a pump by NodeId, or <see langword="null"/>
        /// when this manager has no such pump.
        /// </summary>
        /// <param name="nodeId">The pump's NodeId.</param>
        public IPumpBuilder? Pump(NodeId nodeId)
        {
            ThrowIfDisposed();
            return Manager.FindPredefinedNode(nodeId) is PumpState pump ? Pump(pump) : null;
        }

        /// <summary>
        /// Stops new creations and drains the creation in flight before releasing its resources.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (m_pumpLock)
            {
                if (m_disposeTask == null)
                {
                    m_disposed = true;
                    m_disposeTask = DisposeCoreAsync();
                }
                return new ValueTask(m_disposeTask);
            }
        }

        /// <summary>
        /// Registers this runtime once with the Machinery owner's lifetime.
        /// </summary>
        internal void RegisterLifetime()
        {
            lock (m_pumpLock)
            {
                if (!m_lifetimeRegistered)
                {
                    Manager.RegisterMachineryResource(this);
                    m_lifetimeRegistered = true;
                }
            }
        }

        /// <summary>
        /// Releases queued creations and waits for the active creation.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            m_stopping.Cancel();
            await m_createPumpGate.WaitAsync().ConfigureAwait(false);
            m_createPumpGate.Dispose();
            m_stopping.Dispose();
        }

        /// <summary>
        /// Registers a lazily materialized group without changing the builder's synchronous contract.
        /// </summary>
        private void Register(NodeState node)
        {
            ThrowIfDisposed();
            Manager.AddNode(node);
        }

        /// <summary>
        /// Rejects use after the owner has stopped this runtime.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(PumpsRuntime));
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
                if (ReferenceEquals(Manager.FindPredefinedNode(pump.NodeId), pump))
                {
                    await Manager.DeleteNodeAsync(Manager.SystemContext, pump.NodeId, CancellationToken.None)
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
            NodeState? deviceSet = Manager.FindPredefinedNode(NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                SystemContext.NamespaceUris));
            return deviceSet ?? throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The Device Integration DeviceSet is not available.");
        }

        /// <summary>
        /// Runtime options shared by both hosting paths.
        /// </summary>
        private readonly PumpsServerOptions m_options;

        /// <summary>The runtime logger.</summary>
        private readonly ILogger m_logger;

        /// <summary>The materialized pumps.</summary>
        private readonly List<PumpState> m_pumps = [];

        /// <summary>Protects the runtime's snapshots and lifetime.</summary>
        private readonly Lock m_pumpLock = new();

        /// <summary>Serializes pump publication.</summary>
        private readonly SemaphoreSlim m_createPumpGate = new(1, 1);

        /// <summary>Cancels creations awaiting publication during shutdown.</summary>
        private readonly CancellationTokenSource m_stopping = new();

        /// <summary>The resolved model namespaces.</summary>
        private PumpNamespaceIndices? m_namespaces;

        /// <summary>The shared asynchronous disposal operation.</summary>
        private Task? m_disposeTask;

        /// <summary>Whether creation is closed.</summary>
        private bool m_disposed;

        /// <summary>Whether the owner has adopted this runtime.</summary>
        private bool m_lifetimeRegistered;
    }

    /// <summary>
    /// Source-generated logging for the shared Pumps runtime.
    /// </summary>
    internal static partial class PumpsRuntimeLog
    {
        /// <summary>
        /// Reports successful pump publication.
        /// </summary>
        [LoggerMessage(
            EventId = PumpsServerEventIds.PumpsNodeManager + 0,
            Level = LogLevel.Information,
            Message = "Materialised pump '{Name}' (PumpType), NodeId={NodeId}.")]
        public static partial void PumpMaterialised(
            this ILogger logger,
            string? name,
            NodeId nodeId);

        /// <summary>
        /// Reports a missing Machinery folder.
        /// </summary>
        [LoggerMessage(
            EventId = PumpsServerEventIds.PumpsNodeManager + 1,
            Level = LogLevel.Warning,
            Message = "The Machinery Machines folder is unavailable - pump '{Name}' " +
                "is reachable from the DeviceSet only.")]
        public static partial void MachinesFolderUnavailable(
            this ILogger logger,
            string? name);

        /// <summary>
        /// Reports a failed creation rollback.
        /// </summary>
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
