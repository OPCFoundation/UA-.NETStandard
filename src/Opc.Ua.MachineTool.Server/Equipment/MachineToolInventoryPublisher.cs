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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Stages equipment inventories on the existing node manager; it does not implement a second UA protocol.
    /// Tool and prognosis modules share its ownership, rollback and model-change publication rules.
    /// </summary>
    internal sealed class MachineToolInventoryPublisher : IAsyncDisposable
    {
        /// <summary>
        /// Creates a per-instance inventory publisher.
        /// </summary>
        internal MachineToolInventoryPublisher(
            IMachineryBuildContext context, MachineToolState machine,
            Func<ArrayOf<NodeState>, CancellationToken, ValueTask>? finalPublication)
        {
            Context = context;
            m_machine = machine;
            m_finalPublication = finalPublication;
            m_timeProvider = context.GetService<TimeProvider>() ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the namespace owner's context.
        /// </summary>
        internal IMachineryBuildContext Context { get; }

        /// <summary>
        /// Gets the committed full observation, including a publication which needs retry.
        /// </summary>
        internal MachineToolSnapshot? Observation => Volatile.Read(ref m_observation);

        /// <summary>
        /// Gets whether a callback is running inside this publisher's admitted operation.
        /// </summary>
        internal bool IsInCallback => ReferenceEquals(s_callbackOwner.Value, this);

        /// <summary>
        /// Stages an inventory without holding an admission gate during any callback.
        /// </summary>
        internal async ValueTask<PreparedUpdate> PrepareAsync(
            MachineToolSnapshot snapshot, bool initialize,
            Func<Batch, CancellationToken, ValueTask> stage, CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            BeginOperation(cancellationToken);
            CancellationToken operationToken = m_cancellation!.Token;
            var batch = new Batch { Initializing = initialize };
            MachineToolInventoryPublisher? previous = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                operationToken.ThrowIfCancellationRequested();
                if (initialize == (Observation != null))
                {
                    throw Invalid(initialize ? "The inventory is already initialized." :
                        "The inventory is not initialized.");
                }
                if (m_pending != null)
                {
                    if (!ReferenceEquals(snapshot, Observation))
                    {
                        throw Invalid("Retry the accepted inventory publication before submitting another revision.");
                    }
                    batch = m_pending;
                }
                else if (Observation != null && snapshot.Revision <= Observation.Revision)
                {
                    if (!ReferenceEquals(snapshot, Observation))
                    {
                        throw Invalid("A different inventory observation requires a newer revision.");
                    }
                    batch.Committed = true;
                    batch.Skipped = true;
                }
                else
                {
                    await stage(batch, operationToken).ConfigureAwait(false);
                    operationToken.ThrowIfCancellationRequested();
                    if (!batch.Initializing && batch.ReportChanges && batch.Root != null &&
                        (batch.Additions.Count != 0 || batch.Removals.Count != 0 || batch.OrderChanged))
                    {
                        PropertyState<string> version = batch.Root.GetNodeVersionProperty() ??
                            throw Invalid("A dynamic inventory requires its standard NodeVersion property.");
                        Write(batch, version,
                            Variant.From("revision:" + snapshot.Revision.ToString(CultureInfo.InvariantCulture)),
                            new MachineToolSampleInfo(m_timeProvider.GetUtcNow().UtcDateTime, StatusCodes.Good));
                    }
                    if (batch.Removals.Count != 0)
                    {
                        if (m_finalPublication == null)
                        {
                            throw Invalid("Dynamic removal requires a final-value publication barrier.");
                        }
                        var nodes = new NodeState[batch.Removals.Count];
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            nodes[i] = batch.Removals[i].Node;
                        }
                        await m_finalPublication(nodes.ToArrayOf(), operationToken).ConfigureAwait(false);
                    }
                    await RegisterAsync(batch, operationToken).ConfigureAwait(false);
                }
                operationToken.ThrowIfCancellationRequested();
                return new PreparedUpdate(this, batch, snapshot, operationToken);
            }
            catch
            {
                try
                {
                    await ReleasePreparedAsync(batch).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the staging failure after all independently owned cleanup was attempted.
                }
                throw;
            }
            finally
            {
                s_callbackOwner.Value = previous;
            }
        }

        /// <summary>
        /// Validates a narrowly typed ownership transfer before any returned resource enters the live graph.
        /// </summary>
        /// <typeparam name="TState">The generated type accepted by this factory.</typeparam>
        internal async ValueTask ValidateFactoryAsync<TState>(
            MachineToolEquipmentNode<TState> created, NodeState parent, QualifiedName name,
            NodeId expectedType, Batch batch) where TState : BaseObjectState
        {
            TState node = created.State;
            bool knownResource = IsResourceOwned(created.Resource, batch);
            bool knownNode = m_owned.ContainsKey(node.NodeId) ||
                Context.Manager.FindPredefinedNode(node.NodeId) != null ||
                batch.Additions.Exists(addition => addition.Node.NodeId == node.NodeId);
            BaseInstanceState? named = parent.FindChild(Context.Context, name);
            if (knownNode || knownResource || node.NodeId.IsNull || !ReferenceEquals(node.Parent, parent) ||
                node.BrowseName != name || expectedType.IsNull ||
                (named != null && !ReferenceEquals(named, node)) ||
                !Context.Context.TypeTable.IsTypeOf(node.TypeDefinitionId, expectedType) ||
                Context.Manager.FindPredefinedNode(node.TypeDefinitionId) is not BaseObjectTypeState type ||
                type.IsAbstract)
            {
                try
                {
                    if (created.Resource != null && !knownResource && !knownNode)
                    {
                        await created.Resource.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (!knownNode)
                    {
                        ReleaseReservations(node);
                    }
                }
                throw Invalid("A factory must return a unique unpublished concrete node with its parent and QName.");
            }
        }

        /// <summary>
        /// Adds an owned, detached node and its symmetric attachment operations to a batch.
        /// </summary>
        internal static void Add(
            Batch batch, NodeState parent, BaseInstanceState node, Action attach, Action detach,
            IAsyncDisposable? resource = null)
        {
            batch.Additions.Add(new OwnedNode(parent, node, attach, detach, resource));
        }

        /// <summary>
        /// Marks an owned subtree for removal after the final published observation has reached subscriptions.
        /// </summary>
        internal void Remove(Batch batch, BaseInstanceState node)
        {
            if (!m_owned.TryGetValue(node.NodeId, out OwnedNode? owned))
            {
                throw Invalid("Only this inventory's owned nodes can be removed.");
            }
            batch.Removals.Add(owned);
        }

        /// <summary>
        /// Adds a raw source sample to a prepared transaction.
        /// </summary>
        internal static void Write(Batch batch, BaseVariableState node, in DataValue value)
        {
            if (node == null)
            {
                throw Invalid("The generated inventory is missing a mandatory variable.");
            }
            batch.Writes.Add(new VariableWrite(node, value));
        }

        /// <summary>
        /// Adds a scalar using explicit acquisition metadata.
        /// </summary>
        internal static void Write(
            Batch batch, BaseVariableState node, Variant value, MachineToolSampleInfo sample)
        {
            Write(batch, node, new DataValue(value, sample.StatusCode, sample.SourceTimestamp));
        }

        /// <summary>
        /// Enables the standard read-only NodeVersion property on a detached list.
        /// </summary>
        internal void Track(BaseObjectState list)
        {
            PropertyState<string> version = list.EnableModelChangeTracking(Context.InstanceNamespaceIndex);
            version.AccessLevel = AccessLevels.CurrentRead;
            version.UserAccessLevel = AccessLevels.CurrentRead;
            version.Timestamp = m_timeProvider.GetUtcNow().UtcDateTime;
            version.StatusCode = StatusCodes.Good;
        }

        /// <summary>
        /// Uses Parent propagation once and standard notifier references for browsing.
        /// </summary>
        internal static void Link(BaseObjectState parent, BaseObjectState child)
        {
            parent.EventNotifier |= EventNotifiers.SubscribeToEvents;
            child.EventNotifier |= EventNotifiers.SubscribeToEvents;
            parent.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, false, child.NodeId);
            child.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, parent.NodeId);
        }

        /// <summary>
        /// Releases unregistered factory and transient-event reservations without touching another owner's nodes.
        /// </summary>
        internal void ReleaseReservations(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context.Context, children);
            foreach (BaseInstanceState child in children)
            {
                ReleaseReservations(child);
            }
            if (Context.Context.NodeIdFactory is ICompanionNodeIdReservationOwner owner &&
                Context.Manager.FindPredefinedNode(node.NodeId) == null)
            {
                owner.ReleaseNodeIdReservation(node);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (IsInCallback)
            {
                throw Invalid("An inventory callback cannot await disposal of its own operation.");
            }
            TaskCompletionSource<bool> completion;
            Task drained;
            lock (m_lifetime)
            {
                if (m_disposal != null)
                {
                    return new ValueTask(m_disposal);
                }
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
                drained = m_operation?.Task ?? Task.CompletedTask;
            }
            _ = DisposeCoreAsync(drained, completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Rejects simultaneous or reentrant mutation instead of waiting behind application callbacks.
        /// </summary>
        private void BeginOperation(CancellationToken cancellationToken)
        {
            lock (m_lifetime)
            {
                if (m_disposal != null)
                {
                    throw new ObjectDisposedException(nameof(MachineToolInventoryPublisher));
                }
                if (m_operation != null)
                {
                    throw Invalid("An inventory update is already in progress.");
                }
                m_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
                m_operation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        /// <summary>
        /// Completes admission only after rollback or publication has released the operation's resources.
        /// </summary>
        private void EndOperation()
        {
            lock (m_lifetime)
            {
                m_cancellation!.Dispose();
                m_cancellation = null;
                m_operation!.TrySetResult(true);
                m_operation = null;
            }
        }

        /// <summary>
        /// Detects shared resource transfers without disposing a resource still owned by another live entry.
        /// </summary>
        private bool IsResourceOwned(IAsyncDisposable? resource, Batch batch)
        {
            if (resource == null)
            {
                return false;
            }
            foreach (OwnedNode owned in m_owned.Values)
            {
                if (ReferenceEquals(owned.Resource, resource))
                {
                    return true;
                }
            }
            return batch.Additions.Exists(addition => ReferenceEquals(addition.Resource, resource));
        }

        /// <summary>
        /// Validates every detached identity before any manager registration, then registers initialized additions.
        /// </summary>
        private async ValueTask RegisterAsync(Batch batch, CancellationToken cancellationToken)
        {
            var nodes = new HashSet<NodeState>();
            var ids = new HashSet<NodeId>();
            foreach (OwnedNode addition in batch.Additions)
            {
                if (!nodes.Contains(addition.Node))
                {
                    ValidateTree(addition.Node, nodes, ids);
                }
            }
            foreach (VariableWrite write in batch.Writes)
            {
                if (nodes.Contains(write.Node))
                {
                    write.Apply();
                }
            }
            if (ReferenceEquals(Context.Manager.FindPredefinedNode(m_machine.NodeId), m_machine))
            {
                foreach (OwnedNode addition in batch.Additions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    addition.Registered = true;
                    if (!ReferenceEquals(Context.Manager.FindPredefinedNode(addition.Node.NodeId), addition.Node))
                    {
                        await Context.Manager.AddPredefinedNodeAsync(addition.Node, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Rejects factories which steal registered descendants or duplicate another staged subtree's identity.
        /// </summary>
        private void ValidateTree(NodeState node, HashSet<NodeState> nodes, HashSet<NodeId> ids)
        {
            if (!nodes.Add(node) || node.NodeId.IsNull || !ids.Add(node.NodeId) ||
                Context.Manager.FindPredefinedNode(node.NodeId) != null)
            {
                throw Invalid("The inventory factory returned a duplicate or already registered descendant.");
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(Context.Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (!ReferenceEquals(child.Parent, node))
                {
                    throw Invalid("A generated descendant has a different parent.");
                }
                ValidateTree(child, nodes, ids);
            }
        }

        /// <summary>
        /// Applies only previously validated in-memory changes. Cancellation is checked by the containing commit.
        /// </summary>
        private void Commit(Batch batch, MachineToolSnapshot snapshot)
        {
            if (batch.Committed)
            {
                return;
            }
            foreach (OwnedNode addition in batch.Additions)
            {
                addition.Attach();
                m_owned.Add(addition.Node.NodeId, addition);
            }
            foreach (VariableWrite write in batch.Writes)
            {
                write.Apply();
            }
            foreach (Action action in batch.CommitActions)
            {
                action();
            }
            foreach (OwnedNode removal in batch.Removals)
            {
                removal.Detach();
            }
            batch.Committed = true;
            Volatile.Write(ref m_observation, snapshot);
            m_pending = batch;
        }

        /// <summary>
        /// Publishes changes and drains removed nodes using the shared namespace owner's actual lifecycle.
        /// </summary>
        private async ValueTask PublishAsync(Batch batch)
        {
            if (batch.Skipped)
            {
                return;
            }
            MachineToolInventoryPublisher? previous = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                if (batch.Root != null)
                {
                    await batch.Root.ClearChangeMasksAsync(Context.Context, true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                while (batch.RemovalsCompleted < batch.Removals.Count)
                {
                    await ReleaseNodeAsync(batch.Removals[batch.RemovalsCompleted]).ConfigureAwait(false);
                    batch.RemovalsCompleted++;
                }
                if (!batch.Initializing && batch.ReportChanges && batch.Root != null &&
                    (batch.Additions.Count != 0 || batch.Removals.Count != 0 || batch.OrderChanged))
                {
                    await PublishModelChangeAsync(batch).ConfigureAwait(false);
                }
                m_pending = null;
                if (batch.ModelEvent != null)
                {
                    ReleaseReservations(batch.ModelEvent);
                }
            }
            finally
            {
                s_callbackOwner.Value = previous;
            }
        }

        /// <summary>
        /// Builds one bounded standard event, retaining its identity if a subscriber callback fails.
        /// </summary>
        private async ValueTask PublishModelChangeAsync(Batch batch)
        {
            BaseObjectState root = batch.Root!;
            if (batch.ModelEvent == null)
            {
                var changes = new ModelChangeAggregator();
                foreach (OwnedNode addition in batch.Additions)
                {
                    changes.Add(new ModelChangeStructureDataType
                    {
                        Affected = addition.Node.NodeId,
                        AffectedType = TypeOf(addition.Node),
                        Verb = (byte)ModelChangeVerbs.NodeAdded
                    });
                }
                foreach (OwnedNode removal in batch.Removals)
                {
                    changes.Add(new ModelChangeStructureDataType
                    {
                        Affected = removal.Node.NodeId,
                        AffectedType = TypeOf(removal.Node),
                        Verb = (byte)ModelChangeVerbs.NodeDeleted
                    });
                }
                changes.Add(new ModelChangeStructureDataType
                {
                    Affected = root.NodeId,
                    AffectedType = root.TypeDefinitionId,
                    Verb = (byte)(ModelChangeVerbs.ReferenceAdded | ModelChangeVerbs.ReferenceDeleted)
                });
                var modelEvent = new GeneralModelChangeEventState(null);
                modelEvent.Initialize(Context.Context, root, EventSeverity.Low,
                    new LocalizedText("Equipment inventory changed."));
                modelEvent.Time!.Value = m_timeProvider.GetUtcNow().UtcDateTime;
                modelEvent.ReceiveTime!.Value = modelEvent.Time.Value;
                modelEvent.CreateOrReplaceChanges(Context.Context, null);
                modelEvent.Changes!.Value = changes.Drain();
                batch.ModelEvent = modelEvent;
            }
            await root.ReportEventAsync(Context.Context, batch.ModelEvent, CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the actual type of an object or variable included in a topology change.
        /// </summary>
        private static NodeId TypeOf(BaseInstanceState node) => node switch
        {
            BaseObjectState value => value.TypeDefinitionId,
            BaseVariableState value => value.TypeDefinitionId,
            _ => NodeId.Null
        };

        /// <summary>
        /// Releases a subtree and each transferred resource, including nested multitool members.
        /// </summary>
        private async ValueTask ReleaseNodeAsync(OwnedNode owned)
        {
            var descendants = new List<OwnedNode>();
            foreach (OwnedNode candidate in m_owned.Values)
            {
                NodeState? node = candidate.Node;
                while (node is BaseInstanceState instance)
                {
                    if (ReferenceEquals(node, owned.Node))
                    {
                        descendants.Add(candidate);
                        break;
                    }
                    node = m_owned.TryGetValue(instance.NodeId, out OwnedNode? parent) ?
                        parent.Parent : instance.Parent;
                }
            }
            if (!descendants.Contains(owned))
            {
                descendants.Add(owned);
            }
            if ((owned.Registered || m_owned.ContainsKey(owned.Node.NodeId)) &&
                ReferenceEquals(Context.Manager.FindPredefinedNode(owned.Node.NodeId), owned.Node))
            {
                await Context.Manager.DeleteNodeAsync(
                    Context.Manager.SystemContext, owned.Node.NodeId, emitModelChange: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            var errors = new List<Exception>();
            foreach (OwnedNode entry in descendants)
            {
                m_owned.Remove(entry.Node.NodeId);
                if (!entry.Disposed)
                {
                    entry.Disposed = true;
                    if (entry.Resource != null)
                    {
                        try
                        {
                            await entry.Resource.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            errors.Add(exception);
                        }
                    }
                }
                ReleaseReservations(entry.Node);
            }
            ThrowCleanupErrors(errors);
        }

        /// <summary>
        /// Rolls back new detached registrations while retaining the last committed inventory.
        /// </summary>
        private async ValueTask ReleasePreparedAsync(Batch batch)
        {
            MachineToolInventoryPublisher? previous = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                if (!batch.Committed)
                {
                    var errors = new List<Exception>();
                    for (int i = batch.Additions.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            await ReleaseNodeAsync(batch.Additions[i]).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            errors.Add(exception);
                        }
                    }
                    ThrowCleanupErrors(errors);
                }
            }
            finally
            {
                EndOperation();
                s_callbackOwner.Value = previous;
            }
        }

        /// <summary>
        /// Cancels staging, joins accepted publication and attempts every owned resource outside the lifetime gate.
        /// </summary>
        private async Task DisposeCoreAsync(Task drained, TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            MachineToolInventoryPublisher? previous = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                try
                {
                    m_shutdown.Cancel();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                await drained.ConfigureAwait(false);
                var nodes = new List<OwnedNode>(m_owned.Values);
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        nodes[i].Detach();
                        await ReleaseNodeAsync(nodes[i]).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }
                if (m_pending?.ModelEvent != null)
                {
                    ReleaseReservations(m_pending.ModelEvent);
                }
                m_pending = null;
                m_cancellation?.Dispose();
                m_shutdown.Dispose();
                ThrowCleanupErrors(errors);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                s_callbackOwner.Value = previous;
            }
        }

        /// <summary>
        /// Reports independent cleanup errors after attempting all releases.
        /// </summary>
        private static void ThrowCleanupErrors(List<Exception> errors)
        {
            if (errors.Count != 0)
            {
                throw new AggregateException("Equipment inventory cleanup failed.", errors);
            }
        }

        /// <summary>
        /// Creates a consistent invalid-observation error.
        /// </summary>
        internal static ServiceResultException Invalid(string message) => new(StatusCodes.BadInvalidState, message);

        /// <summary>
        /// Owns one admission and the reversible batch supplied to the containing runtime.
        /// </summary>
        internal sealed class PreparedUpdate : IAsyncDisposable
        {
            /// <summary>
            /// Captures one admitted operation.
            /// </summary>
            internal PreparedUpdate(
                MachineToolInventoryPublisher owner, Batch batch, MachineToolSnapshot snapshot,
                CancellationToken cancellationToken)
            {
                m_owner = owner;
                m_cancellationToken = cancellationToken;
                Batch = batch;
                m_snapshot = snapshot;
            }

            /// <summary>
            /// Gets or sets the staged batch, or a retained accepted publication being retried.
            /// </summary>
            internal Batch Batch { get; set; }

            /// <summary>
            /// Checks cancellation before the containing runtime crosses its coherent commit boundary.
            /// </summary>
            internal void ValidateCommit() => m_cancellationToken.ThrowIfCancellationRequested();

            /// <summary>
            /// Commits without awaiting or introducing a second cancellation boundary between sibling modules.
            /// </summary>
            internal void Commit()
            {
                if (m_committed || m_disposed != 0)
                {
                    throw Invalid("A prepared inventory can only be committed once.");
                }
                m_owner.Commit(Batch, m_snapshot);
                m_committed = true;
            }

            /// <summary>
            /// Completes publication outside the containing runtime's commit gate.
            /// </summary>
            internal ValueTask PublishAsync()
            {
                if (!m_committed || m_disposed != 0)
                {
                    throw Invalid("Commit the prepared inventory before publication.");
                }
                return m_owner.PublishAsync(Batch);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    await m_owner.ReleasePreparedAsync(Batch).ConfigureAwait(false);
                }
            }

            /// <summary>The publisher whose admission this preparation owns.</summary>
            private readonly MachineToolInventoryPublisher m_owner;

            /// <summary>The operation token owned by the publisher until admission is released.</summary>
            private readonly CancellationToken m_cancellationToken;

            /// <summary>The exact immutable provider observation.</summary>
            private readonly MachineToolSnapshot m_snapshot;

            /// <summary>Whether this lease crossed its commit boundary.</summary>
            private bool m_committed;

            /// <summary>Whether the admission has been released.</summary>
            private int m_disposed;
        }

        /// <summary>
        /// Holds bounded changes and retry state for a single observation.
        /// </summary>
        internal sealed class Batch
        {
            /// <summary>
            /// Gets newly allocated subtrees.
            /// </summary>
            internal List<OwnedNode> Additions { get; } = [];

            /// <summary>
            /// Gets existing subtrees whose final values must be published before removal.
            /// </summary>
            internal List<OwnedNode> Removals { get; } = [];

            /// <summary>
            /// Gets scalar changes prepared without mutating live variables.
            /// </summary>
            internal List<VariableWrite> Writes { get; } = [];

            /// <summary>
            /// Gets validated domain-index and reference-order updates.
            /// </summary>
            internal List<Action> CommitActions { get; } = [];

            /// <summary>
            /// Gets or sets the generated list which publishes these changes.
            /// </summary>
            internal BaseObjectState? Root { get; set; }

            /// <summary>
            /// Gets or sets whether this is the unpublished initial topology.
            /// </summary>
            internal bool Initializing { get; init; }

            /// <summary>
            /// Gets or sets whether this instance selected dynamic model-change reporting.
            /// </summary>
            internal bool ReportChanges { get; set; }

            /// <summary>
            /// Gets or sets whether the provider reordered existing entries.
            /// </summary>
            internal bool OrderChanged { get; set; }

            /// <summary>
            /// Gets or sets whether the observation was committed.
            /// </summary>
            internal bool Committed { get; set; }

            /// <summary>
            /// Gets or sets whether a previously published immutable observation needs no further action.
            /// </summary>
            internal bool Skipped { get; set; }

            /// <summary>
            /// Gets or sets the count of removals whose server lifecycle completed.
            /// </summary>
            internal int RemovalsCompleted { get; set; }

            /// <summary>
            /// Gets or sets the exact event retained across failed publication.
            /// </summary>
            internal GeneralModelChangeEventState? ModelEvent { get; set; }

            /// <summary>
            /// Gets or sets the prospective equipment index for same-transaction prognosis references.
            /// </summary>
            internal Dictionary<string, MachineToolEquipmentController.ToolNodes>? Tools { get; set; }
        }

        /// <summary>
        /// Associates a subtree with its attachment and asynchronously owned resource.
        /// </summary>
        internal sealed record OwnedNode(
            NodeState Parent, BaseInstanceState Node, Action Attach, Action Detach, IAsyncDisposable? Resource)
        {
            /// <summary>
            /// Gets or sets whether manager registration was attempted.
            /// </summary>
            internal bool Registered { get; set; }

            /// <summary>
            /// Gets or sets whether its application resource has already been drained.
            /// </summary>
            internal bool Disposed { get; set; }
        }

        /// <summary>
        /// A scalar sample whose original Variant is never coerced to Double.
        /// </summary>
        internal sealed record VariableWrite(BaseVariableState Node, DataValue Value)
        {
            /// <summary>
            /// Writes only cached observations; subscribers are notified later, outside the commit gate.
            /// </summary>
            internal void Apply()
            {
                Node.AccessLevel = AccessLevels.CurrentRead;
                Node.UserAccessLevel = AccessLevels.CurrentRead;
                Node.Value = Value.WrappedValue;
                Node.StatusCode = Value.StatusCode;
                Node.Timestamp = Value.SourceTimestamp;
            }
        }

        /// <summary>The containing machine, owned by its runtime.</summary>
        private readonly MachineToolState m_machine;

        /// <summary>The actual server-publication barrier for final observations.</summary>
        private readonly Func<ArrayOf<NodeState>, CancellationToken, ValueTask>? m_finalPublication;

        /// <summary>The injected event clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The independently owned inventory subtrees.</summary>
        private readonly Dictionary<NodeId, OwnedNode> m_owned = [];

        /// <summary>Protects only admission and shutdown, never callbacks.</summary>
        private readonly Lock m_lifetime = new();

        /// <summary>Cancels uncommitted staging during disposal.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>The last committed full snapshot.</summary>
        private MachineToolSnapshot? m_observation;

        /// <summary>The active operation's completion.</summary>
        private TaskCompletionSource<bool>? m_operation;

        /// <summary>The active operation's linked cancellation, disposed when its admission drains.</summary>
        private CancellationTokenSource? m_cancellation;

        /// <summary>The idempotent shutdown task.</summary>
        private Task? m_disposal;

        /// <summary>The accepted batch retained until publication succeeds.</summary>
        private Batch? m_pending;

        /// <summary>Detects same-operation disposal without exposing any gate.</summary>
        private static readonly AsyncLocal<MachineToolInventoryPublisher?> s_callbackOwner = new();
    }
}
