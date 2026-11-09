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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The per node manager LADS runtime: owns the services, the registration of devices and the
    /// release of everything a device holds when the owning node manager is disposed.
    /// </summary>
    internal sealed class LadsRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Creates the runtime of a node manager.
        /// </summary>
        private LadsRuntime(
            DiNodeManager manager,
            LadsServerOptions options,
            TimeProvider timeProvider,
            IFileTypeBinder fileBinder,
            ILadsDeviceProvider deviceProvider,
            ILadsFunctionProvider functionProvider,
            ILadsProgramExecutor programExecutor,
            ILadsStore store,
            ILadsNodeOperations nodeOperations,
            LadsDeviceRegistry registry)
        {
            Stopping = m_stopping.Token;
            Manager = manager;
            Options = options;
            TimeProvider = options.TimeProvider ?? timeProvider;
            FileBinder = fileBinder;
            DeviceProvider = deviceProvider;
            FunctionProvider = functionProvider;
            ProgramExecutor = programExecutor;
            Store = options.Store ?? store;
            m_nodeOperations = nodeOperations;
            Registry = registry;
            Logger = manager.SystemContext.Telemetry.CreateLogger<LadsRuntime>();
            NamespaceUris = manager.SystemContext.NamespaceUris;
            InstanceNamespaceIndex = manager is MachineryNodeManager machinery
                ? machinery.MachineryInstanceNamespaceIndex
                : manager.InstanceNamespaceIndex;
            MachineryNamespaceIndex = IndexOf(Opc.Ua.Machinery.Namespaces.Machinery);
            LadsNamespaceIndex = IndexOf(Namespaces.LADS);
        }

        /// <summary>
        /// Gets the owning node manager.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the system context of the owning node manager.
        /// </summary>
        public ServerSystemContext Context => Manager.SystemContext;

        /// <summary>
        /// Gets the namespace table of the server.
        /// </summary>
        public NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the options.
        /// </summary>
        public LadsServerOptions Options { get; }

        /// <summary>
        /// Gets the clock.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the binder that serves result files.
        /// </summary>
        public IFileTypeBinder FileBinder { get; }

        /// <summary>
        /// Gets the default device provider.
        /// </summary>
        public ILadsDeviceProvider DeviceProvider { get; }

        /// <summary>
        /// Gets the default function provider.
        /// </summary>
        public ILadsFunctionProvider FunctionProvider { get; }

        /// <summary>
        /// Gets the default program executor.
        /// </summary>
        public ILadsProgramExecutor ProgramExecutor { get; }

        /// <summary>
        /// Gets the stored-data provider; it never restores physical execution.
        /// </summary>
        public ILadsStore Store { get; }

        /// <summary>
        /// Gets the registry the devices are published to.
        /// </summary>
        public LadsDeviceRegistry Registry { get; }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Gets the namespace of the instances.
        /// </summary>
        public ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the Machinery namespace index.
        /// </summary>
        public ushort MachineryNamespaceIndex { get; }

        /// <summary>
        /// Gets the LADS namespace index.
        /// </summary>
        public ushort LadsNamespaceIndex { get; }

        /// <summary>
        /// Gets a token that is cancelled when the runtime is released.
        /// </summary>
        public CancellationToken Stopping { get; }

        /// <summary>
        /// Gets the DI DeviceSet the devices are created in.
        /// </summary>
        public NodeState DeviceSet
        {
            get
            {
                if (m_deviceSet == null)
                {
                    NodeId id = NodeId.Create(Opc.Ua.Di.Objects.DeviceSet, DiNodeManager.DiNamespaceUri, NamespaceUris);
                    m_deviceSet = Manager.FindPredefinedNode(id) ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The DI DeviceSet is not available.");
                }
                return m_deviceSet;
            }
        }

        /// <summary>
        /// Gets the runtime of the node manager of a build context, creating it on first use.
        /// </summary>
        /// <param name="buildContext">The Machinery build context.</param>
        public static LadsRuntime GetOrCreate(IMachineryBuildContext buildContext)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
            DiNodeManager manager = buildContext.Manager;
            var deviceType = ExpandedNodeId.ToNodeId(ObjectTypeIds.LADSDeviceType, manager.SystemContext.NamespaceUris);
            if (deviceType.IsNull || manager.FindPredefinedNode(deviceType) == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Load the LADS model with AddLads or AddLadsToMachinery before creating LADS devices.");
            }
            LadsRuntime runtime = s_runtimes.GetValue(
                manager,
                owner => new Lazy<LadsRuntime>(() =>
                {
                    LadsServerOptions options =
                        buildContext.GetService<IOptions<LadsServerOptions>>()?.Value ?? new LadsServerOptions();
                    options.Validate();
                    return new LadsRuntime(
                        owner,
                        options,
                        buildContext.GetService<TimeProvider>() ?? TimeProvider.System,
                        buildContext.GetService<IFileTypeBinder>() ?? new FileTypeBinder(),
                        buildContext.GetService<ILadsDeviceProvider>() ?? new InMemoryLadsDeviceProvider(),
                        buildContext.GetService<ILadsFunctionProvider>() ?? new InMemoryLadsFunctionProvider(),
                        buildContext.GetService<ILadsProgramExecutor>() ?? new InMemoryLadsProgramExecutor(),
                        buildContext.GetService<ILadsStore>() ?? new InMemoryLadsStore(),
                        buildContext.GetService<ILadsNodeOperations>() ?? new LadsNodeOperations(),
                        buildContext.GetService<ILadsDeviceRegistry>() as LadsDeviceRegistry ??
                            new LadsDeviceRegistry());
                })).Value;
            runtime.RegisterLifetime();
            return runtime;
        }

        /// <summary>
        /// Returns the namespace index of a namespace URI, appending it when it is unknown.
        /// </summary>
        public ushort IndexOf(string namespaceUri)
        {
            return NamespaceUris.GetIndexOrAppend(namespaceUri);
        }

        /// <summary>
        /// Creates a browse name in the instance namespace.
        /// </summary>
        public QualifiedName InstanceName(string name)
        {
            return new QualifiedName(name, InstanceNamespaceIndex);
        }

        /// <summary>
        /// Rejects an empty browse name.
        /// </summary>
        /// <param name="name">The browse name.</param>
        /// <param name="parameterName">The name of the parameter.</param>
        public static void ValidateName(string name, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A browse name must not be empty.", parameterName);
            }
        }

        /// <summary>
        /// Reserves a device name. Returns false when the name is already in use.
        /// </summary>
        public bool TryReserveDeviceName(string name)
        {
            lock (m_lock)
            {
                ThrowIfDisposed();
                if (m_reservedNames.Contains(name))
                {
                    return false;
                }
                var existing = new List<BaseInstanceState>();
                DeviceSet.GetChildren(Context, existing);
                if (existing.Any(child => child.BrowseName.Name == name))
                {
                    return false;
                }
                m_reservedNames.Add(name);
                return true;
            }
        }

        /// <summary>
        /// Releases a reserved device name.
        /// </summary>
        public void ReleaseDeviceName(string name)
        {
            lock (m_lock)
            {
                if (!m_orphanedNames.Contains(name))
                {
                    m_reservedNames.Remove(name);
                }
            }
        }

        /// <summary>
        /// Attaches a fully built device to the DeviceSet and registers it with the node manager.
        /// </summary>
        public async ValueTask RegisterDeviceAsync(LadsDevice device, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await m_registrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool reserved = false;
            bool registered = false;
            bool orphaned = false;
            NodeState? attachedParent = null;
            List<NodeState>? registrationNodes = null;
            try
            {
                ThrowIfDisposed();
                if (!m_storageIdentities.Add(device.StoreIdentity))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A LADS device already uses this stored-data identity.");
                }
                reserved = true;
                LADSDeviceState node = device.Node;
                await device.RestoreStoredDataAsync(cancellationToken).ConfigureAwait(false);
                await device.SaveStoredDataAsync(cancellationToken).ConfigureAwait(false);
                NodeState deviceSet = DeviceSet;
                RemoveDeclarationReferences(node);
                deviceSet.AddChild(node);
                attachedParent = deviceSet;
                node.EventNotifier |= EventNotifiers.SubscribeToEvents;
                registrationNodes = CaptureSubtree(node);
                await device.RegisterLockingAsync(cancellationToken).ConfigureAwait(false);
                await m_nodeOperations.AddAsync(Manager, node, cancellationToken).ConfigureAwait(false);
                device.OrganizeOperationalGroups();
                await Manager.RegisterRootNotifierAsync(node, cancellationToken).ConfigureAwait(false);
                if (!Manager.TryOrganizeUnder(
                    ExpandedNodeId.ToNodeId(Opc.Ua.Machinery.ObjectIds.Machines, NamespaceUris), node))
                {
                    Logger.MachinesFolderUnavailable(device.Name);
                }
                try
                {
                    await device.RegisterAssetManagementAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    registrationNodes = CaptureSubtree(node);
                }
                await device.StartAsync(cancellationToken).ConfigureAwait(false);
                Registry.Add(device);
                lock (m_lock)
                {
                    m_devices.Add(device);
                }
                registered = true;
            }
            catch (Exception registrationFailure)
            {
                var failures = new List<Exception> { registrationFailure };
                device.MarkRegistrationFailed();
                Registry.Remove(device);
                try
                {
                    await device.UnregisterAssetManagementAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                    orphaned = true;
                }
                if (attachedParent != null)
                {
                    try
                    {
                        await RemoveUnregisteredAsync(attachedParent, device.Node).ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        failures.Add(cleanupFailure);
                    }
                    orphaned |= registrationNodes != null && registrationNodes.Any(candidate =>
                        ReferenceEquals(Manager.FindPredefinedNode(candidate.NodeId), candidate));
                }
                if (orphaned)
                {
                    lock (m_lock)
                    {
                        m_orphanedNames.Add(device.Name);
                    }
                }
                try
                {
                    await device.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                }
                if (failures.Count > 1)
                {
                    throw new AggregateException("The LADS device registration and cleanup failed.", failures);
                }
                throw;
            }
            finally
            {
                if (reserved && !registered && !orphaned)
                {
                    m_storageIdentities.Remove(device.StoreIdentity);
                }
                m_registrationGate.Release();
            }
            Logger.DevicePublished(device.Name, device.FunctionalUnits.Count, device.NodeId);
        }

        /// <summary>
        /// Adds a child created after the device was registered.
        /// </summary>
        public async ValueTask AddNodeAsync(
            NodeState parent,
            BaseInstanceState child,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            RemoveDeclarationReferences(child);
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            if (!children.Any(existing => ReferenceEquals(existing, child)))
            {
                parent.AddChild(child);
            }
            if (CaptureSubtree(child).Any(candidate =>
                !ReferenceEquals(Manager.FindPredefinedNode(candidate.NodeId), candidate)))
            {
                await m_nodeOperations.AddAsync(Manager, child, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes the references an instance subtree keeps to instance declarations of its types.
        /// </summary>
        /// <remarks>
        /// Generated instance factories retain declaration references so optional-child creation
        /// can rebase them when the child is added. Before LADS publishes an assembled subtree,
        /// this method removes references that still target instance declarations.
        /// </remarks>
        /// <param name="root">The root of the instance subtree.</param>
        internal void RemoveDeclarationReferences(NodeState root)
        {
            var pending = new Stack<NodeState>();
            pending.Push(root);
            var references = new List<IReference>();
            var children = new List<BaseInstanceState>();
            while (pending.Count > 0)
            {
                NodeState node = pending.Pop();
                references.Clear();
                node.GetReferences(Context, references);
                foreach (IReference reference in references)
                {
                    if (IsInstanceDeclaration(reference.TargetId))
                    {
                        node.RemoveReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                    }
                }
                children.Clear();
                node.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Returns true when a node is an instance declaration of a type, i.e. carries a modelling rule.
        /// </summary>
        private bool IsInstanceDeclaration(ExpandedNodeId targetId)
        {
            var nodeId = ExpandedNodeId.ToNodeId(targetId, NamespaceUris);
            return !nodeId.IsNull &&
                nodeId.NamespaceIndex != InstanceNamespaceIndex &&
                Manager.FindPredefinedNode(nodeId) is BaseInstanceState declaration &&
                !declaration.ModellingRuleId.IsNull;
        }

        /// <summary>
        /// Removes a registered child and its subtree.
        /// </summary>
        public async ValueTask RemoveNodeAsync(
            NodeState parent,
            BaseInstanceState child,
            CancellationToken cancellationToken)
        {
            var containment = new List<(NodeState Parent, BaseInstanceState[] Children)>();
            foreach (NodeState node in CaptureSubtree(child))
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(Context, children);
                containment.Add((node, children.ToArray()));
            }
            Exception? removalFailure = null;
            try
            {
                if (ReferenceEquals(Manager.FindPredefinedNode(child.NodeId), child))
                {
                    await m_nodeOperations.RemoveAsync(Manager, child, cancellationToken).ConfigureAwait(false);
                }
                parent.RemoveChild(child);
            }
            catch (Exception failure)
            {
                removalFailure = failure;
            }
            // Deletion clears generated child slots as well as ordinary containment.
            // Keep the detached object complete so compensation can register the same handles.
            var failures = new List<Exception>();
            foreach ((NodeState owner, BaseInstanceState[] children) in containment)
            {
                foreach (BaseInstanceState descendant in children)
                {
                    try
                    {
                        owner.ReplaceChild(Context, descendant);
                    }
                    catch (Exception failure)
                    {
                        failures.Add(failure);
                    }
                }
            }
            if (failures.Count > 0)
            {
                if (removalFailure != null)
                {
                    failures.Insert(0, removalFailure);
                }
                throw new AggregateException("The detached LADS subtree could not be retained.", failures);
            }
            if (removalFailure != null)
            {
                ExceptionDispatchInfo.Capture(removalFailure).Throw();
            }
        }

        /// <summary>
        /// Writes the value of a variable and reports the change.
        /// </summary>
        public void Update(BaseVariableState? variable, Variant value)
        {
            AssignValue(variable, value);
            variable?.ClearChangeMasks(Context, false);
        }

        /// <summary>
        /// Assigns a variable's attributes without dispatching observable notifications.
        /// </summary>
        internal static void AssignValue(BaseVariableState? variable, Variant value)
        {
            if (variable == null)
            {
                return;
            }
            variable.Value = value;
            variable.Timestamp = DateTimeUtc.Now;
            variable.StatusCode = StatusCodes.Good;
        }

        /// <summary>
        /// Increments the NodeVersion of a set after its children changed.
        /// </summary>
        public void BumpVersion(SetState? set)
        {
            if (set?.NodeVersion == null)
            {
                return;
            }
            long version = Interlocked.Increment(ref m_version);
            Update(set.NodeVersion, Variant.From(version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Routes client value writes through an asynchronous command that owns publication of the accepted value.
        /// </summary>
        public static void BindWrite(
            BaseVariableState? variable,
            Func<Variant, CancellationToken, ValueTask> handler)
        {
            if (variable == null)
            {
                return;
            }
            variable.AccessLevel |= AccessLevels.CurrentWrite;
            variable.UserAccessLevel |= AccessLevels.CurrentWrite;
            variable.OnWriteValueAsync = null;
            variable.OnSimpleWriteValueAsync = async (_, _, value, ct) =>
            {
                try
                {
                    await handler(value, ct).ConfigureAwait(false);
                    return new AttributeWriteResult(ServiceResult.Good) { SuppressCacheUpdate = true };
                }
                catch (ServiceResultException exception)
                {
                    return new AttributeWriteResult(exception.Result);
                }
            };
        }

        /// <summary>
        /// Binds a method to a handler. A <see cref="ServiceResultException"/> of the handler becomes the result.
        /// </summary>
        public void Bind(
            MethodState? method,
            Func<ArrayOf<Variant>, List<Variant>, CancellationToken, ValueTask<ServiceResult>> handler)
        {
            if (method == null)
            {
                return;
            }
            method.Executable = true;
            method.UserExecutable = true;
            method.OnCallMethod2Async = async (context, m, objectId, inputs, outputs, ct) =>
            {
                // The method state pre-populates the outputs with default values; handlers append results.
                var results = new List<Variant>();
                ServiceResult result;
                try
                {
                    result = await handler(inputs, results, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    return new ServiceResult(ex);
                }
                for (int ii = 0; ii < results.Count; ii++)
                {
                    if (ii < outputs.Count)
                    {
                        outputs[ii] = results[ii];
                    }
                    else
                    {
                        outputs.Add(results[ii]);
                    }
                }
                return result;
            };
        }

        /// <summary>
        /// Binds a method without outputs to an action.
        /// </summary>
        public void Bind(MethodState? method, Func<ArrayOf<Variant>, CancellationToken, ValueTask> handler)
        {
            Bind(method, async (inputs, outputs, ct) =>
            {
                await handler(inputs, ct).ConfigureAwait(false);
                return ServiceResult.Good;
            });
        }

        /// <summary>
        /// Finds a child by its browse name, ignoring the namespace.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        public T? FindChild<T>(NodeState parent, string name)
            where T : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(Context, children);
            return children.OfType<T>().FirstOrDefault(c => c.BrowseName.Name == name);
        }

        /// <summary>
        /// Prepares a node created with its parent for <see cref="NodeState.AddChild"/>.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        public static T Named<T>(T node, NodeId referenceTypeId)
            where T : BaseInstanceState
        {
            node.DisplayName = new LocalizedText(node.BrowseName.Name);
            node.ReferenceTypeId = referenceTypeId;
            return node;
        }

        /// <summary>
        /// Stops all devices and releases the resources they hold.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (m_lock)
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
        /// Registers this runtime once with the lifetime of the node manager.
        /// </summary>
        private void RegisterLifetime()
        {
            lock (m_lock)
            {
                if (!m_lifetimeRegistered)
                {
                    Manager.RegisterMachineryResource(this);
                    m_lifetimeRegistered = true;
                }
            }
        }

        /// <summary>
        /// Cancels the running programs and releases the file bindings of all devices.
        /// </summary>
        private async Task DisposeCoreAsync()
        {
            m_stopping.Cancel();
            await m_registrationGate.WaitAsync().ConfigureAwait(false);
            LadsDevice[] devices;
            lock (m_lock)
            {
                devices = [.. m_devices];
                m_devices.Clear();
            }
            foreach (LadsDevice device in devices)
            {
                try
                {
                    await device.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.ReleaseFailed(ex);
                }
                Registry.Remove(device);
            }
            m_registrationGate.Release();
            m_registrationGate.Dispose();
            m_stopping.Dispose();
        }

        /// <summary>
        /// Undoes a registration that failed.
        /// </summary>
        private async ValueTask RemoveUnregisteredAsync(NodeState parent, BaseInstanceState node)
        {
            var failures = new List<Exception>();
            try
            {
                if (ReferenceEquals(Manager.FindPredefinedNode(node.NodeId), node))
                {
                    await m_nodeOperations.RemoveAsync(Manager, node, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logger.ReleaseFailed(ex);
                failures.Add(ex);
            }
            try
            {
                parent.RemoveChild(node);
            }
            catch (Exception ex)
            {
                Logger.ReleaseFailed(ex);
                failures.Add(ex);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("The failed LADS registration could not be removed.", failures);
            }
        }

        /// <summary>
        /// Captures the handles of a subtree before registration or destructive removal.
        /// </summary>
        private List<NodeState> CaptureSubtree(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            for (int index = 0; index < nodes.Count; index++)
            {
                var children = new List<BaseInstanceState>();
                nodes[index].GetChildren(Context, children);
                nodes.AddRange(children);
            }
            return nodes;
        }

        /// <summary>
        /// Rejects use after the runtime was released.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(LadsRuntime));
            }
        }

        private static readonly ConditionalWeakTable<DiNodeManager, Lazy<LadsRuntime>> s_runtimes = new();
        private readonly Lock m_lock = new();
        private readonly SemaphoreSlim m_registrationGate = new(1, 1);
        private readonly CancellationTokenSource m_stopping = new();
        private readonly HashSet<string> m_reservedNames = new(StringComparer.Ordinal);

        /// <summary>The names retained while failed registrations still have indexed nodes.</summary>
        private readonly HashSet<string> m_orphanedNames = new(StringComparer.Ordinal);

        /// <summary>The stored-data keys reserved by registering or published devices.</summary>
        private readonly HashSet<string> m_storageIdentities = new(StringComparer.Ordinal);

        /// <summary>The dynamic node registration and deletion dispatcher.</summary>
        private readonly ILadsNodeOperations m_nodeOperations;
        private readonly List<LadsDevice> m_devices = [];
        private NodeState? m_deviceSet;
        private Task? m_disposeTask;
        private bool m_disposed;
        private bool m_lifetimeRegistered;
        private long m_version;
    }

    /// <summary>
    /// Dispatches dynamic LADS node changes through the owning node manager.
    /// </summary>
    internal interface ILadsNodeOperations
    {
        /// <summary>
        /// Registers a dynamic subtree.
        /// </summary>
        ValueTask AddAsync(DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken);

        /// <summary>
        /// Removes a registered dynamic subtree.
        /// </summary>
        ValueTask RemoveAsync(DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Uses the existing node-manager registration and deletion paths.
    /// </summary>
    internal sealed class LadsNodeOperations : ILadsNodeOperations
    {
        /// <inheritdoc/>
        public ValueTask AddAsync(
            DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
        {
            return manager.AddPredefinedNodeAsync(node, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
        {
            bool removed = await manager.DeleteNodeAsync(manager.SystemContext, node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!removed && ReferenceEquals(manager.FindPredefinedNode(node.NodeId), node))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The LADS node could not be removed from the address space.");
            }
        }
    }
}
