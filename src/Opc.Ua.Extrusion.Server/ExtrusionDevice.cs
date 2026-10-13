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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// A concrete typed device, its complete selected feature graph and its authoritative snapshot projection.
    /// </summary>
    /// <typeparam name="TState">The generated concrete sibling device state.</typeparam>
    public sealed class ExtrusionDevice<TState> : IAsyncDisposable
        where TState : ExtrusionDeviceState
    {
        /// <summary>
        /// Creates a typed handle around one registered device implementation.
        /// </summary>
        internal ExtrusionDevice(TState node, ExtrusionDeviceBinding binding)
        {
            Node = node;
            m_binding = binding;
        }

        /// <summary>
        /// Gets the exact generated type, not an extruder-shaped substitute.
        /// </summary>
        public TState Node { get; }

        /// <summary>
        /// Gets the stable application-owned device identifier.
        /// </summary>
        public NodeId NodeId => Node.NodeId;

        /// <summary>
        /// Gets the complete currently selected variable graph.
        /// </summary>
        public ArrayOf<BaseVariableState> Variables => m_binding.Variables;

        /// <summary>
        /// Gets the OPC 40084-1 Machinery state projection.
        /// </summary>
        public ExtrusionStateController State => m_binding.State;

        /// <summary>
        /// Gets the line job controller when JobGroups was selected.
        /// </summary>
        public ExtrusionJobController? Jobs => m_binding.Jobs;

        /// <summary>
        /// Gets the physical-cut projection for a cutter.
        /// </summary>
        public ExtrusionCutController? Cuts => m_binding.Cuts;

        /// <summary>
        /// Gets line-wide material/event behavior even when optional JobGroups is absent.
        /// </summary>
        public ExtrusionLineController? LineControl => m_binding.LineControl;

        /// <summary>
        /// Opens the existing owner's fluent view for AMB registration and other standard AddIns.
        /// </summary>
        public INodeBuilder<BaseObjectState> AsNode()
        {
            return m_binding.AsNode();
        }

        /// <summary>
        /// Resolves a selected node for repeated devices or deeply nested optional features.
        /// </summary>
        public BaseInstanceState Find(string path)
        {
            return m_binding.Find(path);
        }

        /// <summary>
        /// Resolves a real repeated instance or optional member as its generated type.
        /// </summary>
        /// <typeparam name="TNode">The expected generated node type.</typeparam>
        public TNode Find<TNode>(string path) where TNode : BaseInstanceState
        {
            return m_binding.Find(path) as TNode ?? throw ServiceResultException.Create(
                StatusCodes.BadTypeMismatch, "Feature '{0}' has a different generated type.", path);
        }

        /// <summary>
        /// Applies an atomic absolute update. Replays are idempotent; stale or conflicting revisions fail.
        /// </summary>
        public ValueTask ApplySnapshotAsync(ExtrusionSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return m_binding.ApplyAsync(snapshot, cancellationToken);
        }

        /// <summary>
        /// Stops new operations and releases owned resources; the address-space owner removes the nodes.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>The implementation shared by typed handles and owner lifetime management.</summary>
        private readonly ExtrusionDeviceBinding m_binding;
    }

    /// <summary>
    /// Serializes committed state and routes only family-specific commands to hardware.
    /// </summary>
    internal sealed partial class ExtrusionDeviceBinding : IAsyncDisposable
    {
        /// <summary>
        /// Creates the projection before the device is exposed to clients.
        /// </summary>
        internal ExtrusionDeviceBinding(
            ExtrusionRuntime runtime,
            ExtrusionDeviceState node,
            ExtrusionDeviceOptions options,
            ExtrusionNodeMaterializer materializer)
        {
            m_runtime = runtime;
            m_node = node;
            m_commands = options.Commands;
            m_common = options.Common;
            m_materializer = materializer;
            State = new ExtrusionStateController(runtime, node, ApplyAsync);
            if (node is Line.ExtrusionLine_InterfaceState { JobGroups: not null } line)
            {
                Jobs = new ExtrusionJobController(runtime, line, this, options.Jobs, materializer);
            }
            if (node is Cutter.Cutter_InterfaceState cutter)
            {
                Cuts = new ExtrusionCutController(runtime, cutter, this);
            }
            if (node is Line.ExtrusionLine_InterfaceState extrusionLine)
            {
                LineControl = new ExtrusionLineController(runtime, extrusionLine);
            }
            BindVariables(node);
            BindMethods();
        }

        /// <summary>
        /// Gets the required state controller.
        /// </summary>
        internal ExtrusionStateController State { get; }

        /// <summary>
        /// Gets the optional extrusion-line job protocol.
        /// </summary>
        internal ExtrusionJobController? Jobs { get; }

        /// <summary>
        /// Gets the actual cutter's event/counter projection.
        /// </summary>
        internal ExtrusionCutController? Cuts { get; }

        /// <summary>
        /// Gets mandatory line material/event behavior independently of jobs.
        /// </summary>
        internal ExtrusionLineController? LineControl { get; }

        /// <summary>
        /// Reuses the owner context and never adopts the already registered device through a second manager.
        /// </summary>
        internal INodeBuilder<BaseObjectState> AsNode()
        {
            ThrowIfDisposed();
            IMachineryBuildContext context = m_runtime.Manager is MachineryNodeManager machinery
                ? machinery.CreateMachineryBuildContext()
                : m_runtime.Manager.CreateMachineryBuildContext(new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks,
                    InstanceNamespaceUri =
                        m_runtime.SystemContext.NamespaceUris.GetString(m_runtime.InstanceNamespaceIndex)
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError, "The extrusion instance namespace is not registered.")
                });
            return context.Nodes.Node<BaseObjectState>(m_node);
        }

        /// <summary>
        /// Finishes asynchronous event and common-provider setup after node registration.
        /// </summary>
        internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await BindCommonAsync(pending.Token).ConfigureAwait(false);
            if (Jobs != null)
            {
                await Jobs.InitializeAsync(pending.Token).ConfigureAwait(false);
            }
            if (Cuts != null)
            {
                await Cuts.InitializeAsync(pending.Token).ConfigureAwait(false);
            }
            if (LineControl != null)
            {
                await LineControl.InitializeAsync(pending.Token).ConfigureAwait(false);
            }
            BindRemainingWrites();
        }

        /// <summary>
        /// Gets all owned variable instances.
        /// </summary>
        internal ArrayOf<BaseVariableState> Variables
        {
            get
            {
                lock (m_sync)
                {
                    return m_variables.Values.ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Resolves a real selected member and rejects unsupported features explicitly.
        /// </summary>
        internal BaseInstanceState Find(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A relative BrowseName path is required.", nameof(path));
            }
            ThrowIfDisposed();
            return ExtrusionNodeAccess.Find(m_runtime.SystemContext, m_node, path)
                ?? throw ServiceResultException.Create(StatusCodes.BadNotFound, "Feature '{0}' is not selected.", path);
        }

        /// <summary>
        /// Validates every update before changing any node or emitting any notifications.
        /// </summary>
        internal async ValueTask ApplyAsync(ExtrusionSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            ThrowIfDisposed();
            snapshot = Freeze(snapshot);
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_publications.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await ApplyCommittedAsync(snapshot).ConfigureAwait(false);
            }
            finally
            {
                m_publications.Release();
            }
        }

        /// <summary>
        /// Adds a generated dynamic job subtree to the same snapshot and request handling.
        /// </summary>
        internal void BindVariables(NodeState root, bool initializeUnknown = true)
        {
            DateTimeUtc now = DateTimeUtc.Now;
            foreach ((string _, BaseInstanceState node) in ExtrusionNodeAccess.Walk(m_runtime.SystemContext, root))
            {
                if (node is not BaseVariableState variable)
                {
                    continue;
                }
                lock (m_sync)
                {
                    if (!m_variables.TryAdd(variable.NodeId, variable))
                    {
                        continue;
                    }
                    bool metadata = !initializeUnknown || ExtrusionNodeAccess.IsMetadata(variable);
                    var value = new DataValue(variable.WrappedValue,
                        metadata ? variable.StatusCode : StatusCodes.BadWaitingForInitialData,
                        variable.Timestamp == DateTimeUtc.MinValue ? now : variable.Timestamp);
                    m_values.Add(variable.NodeId, value);
                    ExtrusionNodeAccess.Set(variable, value);
                }
                if (BelongsToCommonCapability(variable))
                {
                    continue;
                }
                variable.OnReadValueAsync = ReadAsync;
                if ((variable.AccessLevel & AccessLevels.CurrentWrite) != 0 && variable.OnWriteValueAsync == null)
                {
                    variable.OnWriteValueAsync = WriteAsync;
                }
            }
        }

        /// <summary>
        /// Removes dynamic values when their owning job is removed.
        /// </summary>
        internal void UnbindVariables(NodeState root)
        {
            lock (m_sync)
            {
                foreach ((string _, BaseInstanceState node) in ExtrusionNodeAccess.Walk(m_runtime.SystemContext, root))
                {
                    m_variables.Remove(node.NodeId);
                    m_values.Remove(node.NodeId);
                }
            }
        }

        /// <summary>
        /// Updates controller-owned protocol state without inventing a production commit sequence.
        /// </summary>
        internal void SetProtocolValue(BaseVariableState variable, in DataValue value)
        {
            ExtrusionNodeAccess.ValidateValue(m_runtime.SystemContext, variable, value);
            lock (m_sync)
            {
                m_values[variable.NodeId] = value;
                ExtrusionNodeAccess.Set(variable, value);
            }
        }

        /// <summary>
        /// Shuts down only this binding's pending work and owned controllers.
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
                        Task pendingOperations = m_lifetime.DisposeAsync().AsTask();
                        await DisposeCoreAsync(pendingOperations).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_operations.Dispose();
                        m_publications.Dispose();
                        m_qualityGate.Dispose();
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
        /// Binds exact cutter and clamp commands, leaving OPC 40083 methods to the common binder.
        /// </summary>
        private void BindMethods()
        {
            foreach ((string _, BaseInstanceState child) in ExtrusionNodeAccess.Walk(m_runtime.SystemContext, m_node))
            {
                if (child is not MethodState method)
                {
                    continue;
                }
                ExtrusionCommandKind? kind = method.BrowseName.Name switch
                {
                    "OpenClamp" when m_node is HaulOff.HaulOff_InterfaceState => ExtrusionCommandKind.OpenClamp,
                    "CloseClamp" when m_node is HaulOff.HaulOff_InterfaceState => ExtrusionCommandKind.CloseClamp,
                    "ManualCut" when m_node is Cutter.Cutter_InterfaceState => ExtrusionCommandKind.ManualCut,
                    "SampleCut" when m_node is Cutter.Cutter_InterfaceState => ExtrusionCommandKind.SampleCut,
                    "ResetOutput" when m_node is Cutter.Cutter_InterfaceState => ExtrusionCommandKind.ResetOutput,
                    _ => null
                };
                if (kind.HasValue)
                {
                    ExtrusionCommandKind command = kind.Value;
                    method.OnCallMethod2Async = async (context, _, objectId, _, _, ct) =>
                    {
                        if (method.Parent == null || objectId != method.Parent.NodeId)
                        {
                            return StatusCodes.BadNodeIdInvalid;
                        }
                        return await ExecuteAsync(new ExtrusionCommand(
                            context, m_node, method, command, Variant.Null), ct).ConfigureAwait(false);
                    };
                }
            }
        }

        /// <summary>
        /// Reads authoritative state instead of the write pipeline's optimistic cache.
        /// </summary>
        private ValueTask<AttributeReadResult> ReadAsync(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName encoding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataValue value;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                        StatusCodes.BadShutdown, Variant.Null, StatusCodes.BadShutdown, DateTimeUtc.Now));
                }
                value = m_values[node.NodeId].Copy();
            }
            if (!encoding.IsNull)
            {
                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    StatusCodes.BadDataEncodingUnsupported, Variant.Null,
                    StatusCodes.BadDataEncodingUnsupported, value.SourceTimestamp));
            }
            Variant result = value.WrappedValue;
            StatusCode range = indexRange.ApplyRange(ref result);
            return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                range, result, value.StatusCode, value.SourceTimestamp));
        }

        /// <summary>
        /// Sends writes through the end-user-aware hardware seam without changing counters locally.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WriteAsync(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!indexRange.IsNull)
            {
                return new AttributeWriteResult(StatusCodes.BadIndexRangeInvalid);
            }
            var command = new ExtrusionCommand(
                context, m_node, (BaseInstanceState)node, ExtrusionCommandKind.Write, value);
            return new AttributeWriteResult(await ExecuteAsync(command, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Authorizes before executing and publishes only a provider's committed snapshot.
        /// </summary>
        private async ValueTask<StatusCode> ExecuteAsync(ExtrusionCommand command, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (m_commands == null)
            {
                return StatusCodes.BadNotSupported;
            }
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_operations.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                StatusCode permission = await m_commands.AuthorizeAsync(command, pending.Token).ConfigureAwait(false);
                if (StatusCode.IsBad(permission))
                {
                    return permission;
                }
                ExtrusionCommandResult result = await m_commands.ExecuteAsync(command,
                    pending.Token).ConfigureAwait(false);
                if (StatusCode.IsGood(result.StatusCode))
                {
                    if (result.CommittedSnapshot != null)
                    {
                        result = result with { CommittedSnapshot = Freeze(result.CommittedSnapshot) };
                    }
                    ValidateCommandCommit(command, result);
                    if (result.CommittedSnapshot != null)
                    {
                        await m_publications.WaitAsync(pending.Token).ConfigureAwait(false);
                        try
                        {
                            bool superseded;
                            lock (m_sync)
                            {
                                superseded = m_hasRevision && result.CommittedSnapshot.Revision < m_revision;
                            }

                            if (!superseded)
                            {
                                await ApplyCommittedAsync(result.CommittedSnapshot).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            m_publications.Release();
                        }
                    }
                }
                return result.StatusCode;
            }

            finally
            {
                m_operations.Release();
            }
        }

        /// <summary>
        /// Captures producer-owned buffers before awaiting or retaining a committed update.
        /// </summary>
        private ExtrusionSnapshot Freeze(ExtrusionSnapshot snapshot)
        {
            lock (m_sync)
            {
                if (snapshot.Values.Count > m_variables.Count)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTooManyOperations, "The snapshot exceeds the device's variable count.");
                }
            }
            return snapshot with
            {
                Values = snapshot.Values.ConvertAll(value => new ExtrusionValue(value.NodeId, value.Value.Copy()))
            };
        }

        /// <summary>
        /// Requires committed write/reset effects instead of acknowledging an unimplemented command.
        /// </summary>
        private static void ValidateCommandCommit(ExtrusionCommand command, ExtrusionCommandResult result)
        {
            if (command.Kind == ExtrusionCommandKind.Write &&
                (result.CommittedSnapshot == null ||
                    !result.CommittedSnapshot.Values.Contains(value => value.NodeId == command.Target.NodeId)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState, "A successful write requires its authoritative committed value.");
            }
            if (command.Kind == ExtrusionCommandKind.ResetOutput &&
                command.Target.Parent is Cutter.CuttingProductState product &&
                (result.CommittedSnapshot == null ||
                    !result.CommittedSnapshot.Values.Contains(value =>
                        value.NodeId == product.ActualOutput!.NodeId || value.NodeId == product.TotalOutput!.NodeId)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState, "ResetOutput requires committed output counters from the cutter.");
            }
        }

        /// <summary>
        /// Atomically applies validated absolute values with their source timestamps and quality.
        /// </summary>
        private async ValueTask ApplyCommittedAsync(ExtrusionSnapshot snapshot)
        {
            bool? changedQuality = null;
            DateTimeUtc qualityTimestamp = DateTimeUtc.MinValue;
            lock (m_sync)
            {
                if (m_hasRevision && snapshot.Revision < m_revision)
                {
                    throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "The committed revision is stale.");
                }
                var seen = new HashSet<NodeId>();
                foreach (ExtrusionValue update in snapshot.Values)
                {
                    if (!seen.Add(update.NodeId) || !m_variables.TryGetValue(update.NodeId,
                        out BaseVariableState? variable))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNodeIdInvalid, "A snapshot contains an unknown or repeated variable.");
                    }
                    ExtrusionNodeAccess.ValidateValue(m_runtime.SystemContext, variable, update.Value);
                    if (m_node is Line.ExtrusionLine_InterfaceState line &&
                        ReferenceEquals(variable, line.ProductionParameters?.GoodProduct) &&
                        StatusCode.IsGood(update.Value.StatusCode) &&
                        update.Value.WrappedValue.TryGetValue(out bool good))
                    {
                        if (good && Jobs?.HasBadActiveProduction == true)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadInvalidState,
                                    "The line cannot report good quality while an active job is bad.");
                        }
                        if (m_values[update.NodeId].WrappedValue.TryGetValue(out bool previous) && previous != good)
                        {
                            changedQuality = good;
                            qualityTimestamp = update.Value.SourceTimestamp;
                        }
                    }
                    if (m_hasRevision && snapshot.Revision == m_revision
                        && !m_values[update.NodeId].Equals(update.Value))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                                "A committed revision cannot be reused with different values.");
                    }
                }
                if (m_hasRevision && snapshot.Revision == m_revision)
                {
                    return;
                }
                foreach (ExtrusionValue update in snapshot.Values)
                {
                    m_values[update.NodeId] = update.Value.Copy();
                    ExtrusionNodeAccess.Set(m_variables[update.NodeId], update.Value);
                }
                m_revision = snapshot.Revision;
                m_hasRevision = true;
            }
            PublishCommonMeasurements(snapshot);
            m_node.ClearChangeMasks(m_runtime.SystemContext, true);
            if (changedQuality.HasValue)
            {
                await ReportProductQualityAsync(m_node, changedQuality.Value, qualityTimestamp).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts all child shutdowns before awaiting their drains and preserves every cleanup failure.
        /// </summary>
        private async Task DisposeCoreAsync(Task pendingOperations)
        {
            var shutdowns = new List<Task> { pendingOperations, State.DisposeAsync().AsTask() };
            if (Jobs != null)
            {
                shutdowns.Add(Jobs.DisposeAsync().AsTask());
            }
            if (Cuts != null)
            {
                shutdowns.Add(Cuts.DisposeAsync().AsTask());
            }
            if (LineControl != null)
            {
                shutdowns.Add(LineControl.DisposeAsync().AsTask());
            }
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
            // Initialization is included in pendingOperations, so no common binding can
            // still be added while its ownership is being released.
            try
            {
                await DisposeCommonAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            if (failures != null)
            {
                throw new AggregateException("Extrusion device cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Rejects commands after disposal begins.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed))
            {
                throw new ObjectDisposedException(nameof(ExtrusionDeviceBinding));
            }
        }

        /// <summary>The single hosting runtime.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The generated concrete device.</summary>
        private readonly ExtrusionDeviceState m_node;

        /// <summary>The optional explicit hardware adapter.</summary>
        private readonly IExtrusionCommandProvider? m_commands;

        /// <summary>The existing compiled-declaration materializer, also used for stable alarm identities.</summary>
        private readonly ExtrusionNodeMaterializer m_materializer;

        /// <summary>The addressable variables belonging to this device.</summary>
        private readonly Dictionary<NodeId, BaseVariableState> m_variables = [];

        /// <summary>The authoritative values, separate from optimistic UA write caches.</summary>
        private readonly Dictionary<NodeId, DataValue> m_values = [];

        /// <summary>Protects short in-memory commits and the disposal task.</summary>
        private readonly Lock m_sync = new();

        /// <summary>
        /// Serializes asynchronous hardware commands without blocking independent committed telemetry.
        /// </summary>
        private readonly SemaphoreSlim m_operations = new(1, 1);

        /// <summary>Serializes atomic committed publication independently of awaited hardware.</summary>
        private readonly SemaphoreSlim m_publications = new(1, 1);

        /// <summary>Counts all active and queued commands, publications, initialization and quality work.</summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionDeviceBinding));

        /// <summary>The last externally committed revision.</summary>
        private ulong m_revision;

        /// <summary>Whether any external revision has been applied.</summary>
        private bool m_hasRevision;

        /// <summary>Whether shutdown has begun.</summary>
        private bool m_disposed;

        /// <summary>Claims the single terminal operation.</summary>
        private int m_disposalStarted;

        /// <summary>Shares completion after all owned controllers and gates have been released.</summary>
        private readonly TaskCompletionSource<bool> m_disposal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
