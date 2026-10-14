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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Hosts all eleven concrete OPC 40084 device types on one existing Machinery/DI owner.
    /// </summary>
    public sealed partial class ExtrusionRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Creates the owner's single runtime after its models have loaded.
        /// </summary>
        private ExtrusionRuntime(DiNodeManager manager)
        {
            Manager = manager;
            NodeId type = ExpandedNodeId.ToNodeId(Extrusion.ObjectTypeIds.ExtrusionDeviceType,
                manager.SystemContext.NamespaceUris);
            if (manager.FindPredefinedNode(type) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Load ExtrusionModelProvider before constructing devices.");
            }
            manager.RegisterMachineryResource(this);
        }

        /// <summary>
        /// Gets the single existing address-space owner.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the owner's system context for typed, generated configuration helpers.
        /// </summary>
        public ServerSystemContext SystemContext => Manager.SystemContext;

        /// <summary>
        /// Gets the application-owned namespace, never a companion namespace.
        /// </summary>
        public ushort InstanceNamespaceIndex => Manager is MachineryNodeManager machinery
            ? machinery.MachineryInstanceNamespaceIndex
            : Manager.InstanceNamespaceIndex;

        /// <summary>
        /// Gets the real concrete device instances in creation order.
        /// </summary>
        public ArrayOf<ExtrusionDeviceState> Devices
        {
            get
            {
                lock (m_sync)
                {
                    return m_devices.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets or creates the runtime directly, without requiring dependency injection.
        /// </summary>
        public static ExtrusionRuntime Create(DiNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return s_runtimes.GetValue(manager, static owner =>
                new Lazy<ExtrusionRuntime>(() => new ExtrusionRuntime(owner))).Value;
        }

        /// <summary>
        /// Stops new creation and disposes only this runtime's device bindings.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                lock (m_sync)
                {
                    m_disposed = true;
                }
                try
                {
                    try
                    {
                        try
                        {
                            await m_lifetime.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            await DisposeCoreAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_creation.Dispose();
                    }
                    m_disposal.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposal.TrySetException(failure);
                }
            }
            await m_disposal.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Deterministic test seam for proving rollback after registration.
        /// </summary>
        internal Func<ExtrusionDeviceState, ValueTask>? RegisteredForTest { get; set; }

        /// <summary>
        /// Publishes a generated child under the existing owner.
        /// </summary>
        internal ValueTask RegisterAsync(NodeState node, CancellationToken cancellationToken)
        {
            return Manager.AddPredefinedNodeAsync(node, cancellationToken);
        }

        /// <summary>
        /// Removes a generated dynamic child and all references through its owner.
        /// </summary>
        internal async ValueTask RemoveAsync(BaseInstanceState node)
        {
            if (ReferenceEquals(Manager.FindPredefinedNode(node.NodeId), node))
            {
                await Manager.DeleteNodeAsync(SystemContext, node.NodeId, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                BaseInstanceState[] descendants = ExtrusionNodeAccess.Walk(SystemContext, node)
                    .Select(item => item.Node).Reverse().ToArray();
                foreach (BaseInstanceState child in descendants)
                {
                    if (ReferenceEquals(Manager.FindPredefinedNode(child.NodeId), child))
                    {
                        await Manager.DeleteNodeAsync(SystemContext, child.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }
            node.Parent?.RemoveChild(node);
        }

        /// <summary>
        /// Creates a concrete state graph transactionally; validation failures cannot reserve an identity.
        /// </summary>
        /// <typeparam name="TState">The generated concrete device type.</typeparam>
        private async ValueTask<ExtrusionDevice<TState>> CreateAsync<TState>(
            ExtrusionDeviceOptions options,
            string prefix,
            string deviceClass,
            Func<ISystemContext, NodeState, QualifiedName, TState> factory,
            Action<TState>? configure,
            CancellationToken cancellationToken)
            where TState : ExtrusionDeviceState
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            ThrowIfDisposed();
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_creation.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                NodeState parent = Manager.FindPredefinedNode(
                    ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, SystemContext.NamespaceUris))
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "The Machinery Machines object is not available.");
                var name = new QualifiedName(
                    prefix + "_" + options.Identity.Manufacturer + "_" + options.Identity.SerialNumber,
                    InstanceNamespaceIndex);
                if (ExtrusionNodeAccess.Children(SystemContext, parent).Any(child => child.BrowseName == name) ||
                    m_identities.Contains(options.Identity.ProductInstanceUri))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated, "An extrusion device already has this name or identity.");
                }
                TState node = factory(SystemContext, parent, name);
                node.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
                var materializer = new ExtrusionNodeMaterializer(
                    Manager, options.Features.Copy(), InstanceNamespaceIndex, options.MaximumNodes);
                materializer.Materialize(node, options.Identity.ProductInstanceUri);
                InitializeIdentity(node, options, deviceClass);
                ExtrusionUnits.ApplyDefaults(SystemContext, node);
                configure?.Invoke(node);
                materializer.AssignStableIds(node,
                    "Extrusion/" + Uri.EscapeDataString(options.Identity.ProductInstanceUri));
                ValidateIdentities(node);
                ExtrusionDeviceBinding? binding = null;
                var rollbackFailures = new List<Exception>();
                try
                {
                    try
                    {
                        binding = new ExtrusionDeviceBinding(this, node, options, materializer);
                        await binding.InitializeAsync(pending.Token).ConfigureAwait(false);
                        parent.AddChild(node);
                        node.EventNotifier = EventNotifiers.SubscribeToEvents;
                        await Manager.AddPredefinedNodeAsync(node, pending.Token).ConfigureAwait(false);
                        await Manager.RegisterRootNotifierAsync(node, pending.Token).ConfigureAwait(false);
                        if (RegisteredForTest != null)
                        {
                            await RegisteredForTest(node).ConfigureAwait(false);
                        }

                        lock (m_sync)
                        {
                            m_devices.Add(node);
                            m_bindings.Add(binding);
                            m_identities.Add(options.Identity.ProductInstanceUri);
                        }
                        var device = new ExtrusionDevice<TState>(node, binding);
                        binding = null;
                        return device;
                    }
                    finally
                    {
                        if (binding != null)
                        {
                            try
                            {
                                await binding.DisposeAsync().ConfigureAwait(false);
                            }
                            catch (Exception cleanup)
                            {
                                rollbackFailures.Add(cleanup);
                            }
                            try
                            {
                                await RemoveAsync(node).ConfigureAwait(false);
                            }
                            catch (Exception cleanup)
                            {
                                rollbackFailures.Add(cleanup);
                            }
                        }
                    }
                }
                catch (Exception failure)
                {
                    if (rollbackFailures.Count != 0)
                    {
                        rollbackFailures.Insert(0, failure);
                        throw new AggregateException("Device creation rollback failed.", rollbackFailures);
                    }
                    throw;
                }
            }
            finally
            {
                m_creation.Release();
            }
        }

        /// <summary>
        /// Prevents another owner's existing nodes from being overwritten by predictable instance identities.
        /// </summary>
        private void ValidateIdentities(ExtrusionDeviceState node)
        {
            var seen = new HashSet<NodeId>();
            Check(node);
            foreach ((string _, BaseInstanceState child) in ExtrusionNodeAccess.Walk(SystemContext, node))
            {
                Check(child);
            }

            void Check(NodeState candidate)
            {
                if (!seen.Add(candidate.NodeId) || Manager.FindPredefinedNode(candidate.NodeId) != null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists,
                        "Extrusion instance identity '{0}' for '{1}' is duplicated or already owned.",
                        candidate.NodeId, candidate.BrowseName);
                }
            }
        }

        /// <summary>
        /// Initializes only static identity/configuration, never simulated physical state.
        /// </summary>
        private void InitializeIdentity(
            ExtrusionDeviceState node,
            ExtrusionDeviceOptions options,
            string deviceClass)
        {
            DateTimeUtc now = DateTimeUtc.Now;
            foreach ((string _, BaseInstanceState instance) in ExtrusionNodeAccess.Walk(SystemContext, node))
            {
                if (instance is not BaseVariableState variable)
                {
                    continue;
                }
                Variant value = variable.BrowseName.Name switch
                {
                    "Manufacturer" => Variant.From(new LocalizedText(options.Identity.Manufacturer)),
                    "SerialNumber" => Variant.From(options.Identity.SerialNumber),
                    "Model" => Variant.From(options.Identity.Model),
                    "ProductInstanceUri" => Variant.From(options.Identity.ProductInstanceUri),
                    "DeviceClass" => Variant.From(deviceClass),
                    "ControllerName" => Variant.From(options.Identity.ControllerName),
                    "LineId" => Variant.From(options.LineId),
                    "NodeVersion" => Variant.From("1"),
                    "SupportedLogbookEvents" => Variant.From(ArrayOf<PlasticsRubber.LogbookEventsEnumeration>.Empty),
                    _ => Variant.Null
                };
                if (!value.IsNull)
                {
                    ExtrusionNodeAccess.Set(variable, new DataValue(value, StatusCodes.Good, now));
                }
                if (variable.BrowseName.Name == "Id" && variable.DataType == Opc.Ua.DataTypeIds.String &&
                    (!variable.WrappedValue.TryGetValue(out string? id) || string.IsNullOrEmpty(id)))
                {
                    ExtrusionNodeAccess.Set(variable, new DataValue(
                        Variant.From(variable.Parent?.BrowseName.Name ?? string.Empty), StatusCodes.Good, now));
                }
                if (variable.BrowseName.Name == "Name" && variable.DataType == Opc.Ua.DataTypeIds.LocalizedText &&
                    (!variable.WrappedValue.TryGetValue(out LocalizedText display)
                        || string.IsNullOrEmpty(display.Text)))
                {
                    ExtrusionNodeAccess.Set(variable, new DataValue(
                        Variant.From(new LocalizedText(variable.Parent?.BrowseName.Name)), StatusCodes.Good, now));
                }
            }
        }

        /// <summary>
        /// Starts all device shutdowns after creation has drained, then observes every cleanup result.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            Task[] shutdowns = m_bindings.Select(binding => binding.DisposeAsync().AsTask()).ToArray();
            List<Exception>? failures = null;
            foreach (Task shutdown in shutdowns)
            {
                try
                {
                    await shutdown.ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("One or more extrusion providers failed during shutdown.", failures);
            }
        }

        /// <summary>
        /// Rejects new operations after owner shutdown.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(ExtrusionRuntime));
            }
        }

        /// <summary>The single runtime associated with each owner.</summary>
        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<ExtrusionRuntime>> s_runtimes = new();

        /// <summary>Serializes creation through rollback and name publication.</summary>
        private readonly SemaphoreSlim m_creation = new(1, 1);

        /// <summary>Protects short lifetime and device-list snapshots.</summary>
        private readonly Lock m_sync = new();

        /// <summary>
        /// Counts and cancels all active and queued creation, through rollback and ownership transfer.
        /// </summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionRuntime));

        /// <summary>The real generated devices.</summary>
        private readonly List<ExtrusionDeviceState> m_devices = [];

        /// <summary>The owned provider bindings.</summary>
        private readonly List<ExtrusionDeviceBinding> m_bindings = [];

        /// <summary>The currently reserved stable identities.</summary>
        private readonly HashSet<string> m_identities = new(StringComparer.Ordinal);

        /// <summary>Claims the single owner shutdown.</summary>
        private int m_disposalStarted;

        /// <summary>Shares completion after all device bindings and the creation gate have been released.</summary>
        private readonly TaskCompletionSource<bool> m_disposal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Whether new creations are rejected.</summary>
        private bool m_disposed;
    }
}
