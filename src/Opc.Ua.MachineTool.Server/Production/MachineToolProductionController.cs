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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Composes retained OPC 40501-1 production monitoring on an actual generated MachineTool subtype.
    /// The owner initializes before registering the machine, supplies coherent snapshots, and disposes this
    /// module before removing the machine. No operation executes equipment or acquires provider data.
    /// </summary>
    public sealed partial class MachineToolProductionController : IAsyncDisposable
    {
        /// <summary>
        /// Creates a per-instance production module using the existing namespace owner.
        /// </summary>
        public MachineToolProductionController(
            IMachineryBuildContext context,
            MachineToolState machine,
            MachineToolProductionOptions options,
            TimeProvider timeProvider)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_machine = machine ?? throw new ArgumentNullException(nameof(machine));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            State = machine.Production ?? throw Invalid("The generated machine has no mandatory Production node.");
            ValidateOptions();
        }

        /// <summary>
        /// Gets the actual generated production node; it is never replaced by a parallel protocol object.
        /// </summary>
        public ProductionState State { get; }

        /// <summary>
        /// Gets the last successfully committed full snapshot, or null before initialization.
        /// Publication failures do not roll back a committed observation.
        /// </summary>
        public MachineToolSnapshot? Observation => Volatile.Read(ref m_observation);

        /// <summary>
        /// Gets whether the current execution is one of this module's staging or publication callbacks.
        /// </summary>
        internal bool IsInCallback => ReferenceEquals(s_callbackOwner.Value, this);

        /// <summary>
        /// Initializes the unpublished generated production tree.
        /// </summary>
        public ValueTask InitializeAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return ApplyCoreAsync(snapshot, true, cancellationToken);
        }

        /// <summary>
        /// Validates and publishes a complete observation. A concurrent or reentrant mutation is rejected instead
        /// of waiting behind callbacks; readers and event subscribers never need the module's lifetime gate.
        /// Cancellation is honored up to commit. Publication of an accepted observation runs to completion.
        /// </summary>
        public ValueTask ApplyAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            return ApplyCoreAsync(snapshot, false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (ReferenceEquals(s_callbackOwner.Value, this))
            {
                throw Invalid("A production publication callback cannot await disposal of its own operation.");
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
        /// Runs one admitted operation without holding a gate during application or server callbacks.
        /// </summary>
        private async ValueTask ApplyCoreAsync(
            MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            PreparedUpdate prepared = await PrepareAsync(snapshot, initialize, cancellationToken)
                .ConfigureAwait(false);
            await using (prepared.ConfigureAwait(false))
            {
                prepared.Commit();
                await prepared.PublishAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validates and stages the entire production update before the containing runtime commits monitoring.
        /// No callbacks are invoked under the containing runtime's commit gate.
        /// </summary>
        internal async ValueTask<PreparedUpdate> PrepareAsync(
            MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            var batch = new ProductionBatch { Initializing = initialize };
            MachineToolProductionController? previousOwner = s_callbackOwner.Value;
            s_callbackOwner.Value = this;
            try
            {
                var prepared = new PreparedUpdate(this, cancellationToken);
                try
                {
                    CancellationToken operationToken = prepared.CancellationToken;
                    prepared.Configure(
                        () => CommitPrepared(batch, snapshot, operationToken),
                        () => CompletePublicationAsync(batch),
                        () => ReleasePreparedAsync(batch),
                        key => (batch.Skipped ? m_jobs : batch.Jobs).TryGetValue(key, out JobNodes? job)
                            ? job.State : null,
                        (jobKey, setKey, partKey) =>
                            (batch.Skipped ? m_jobs : batch.Jobs).TryGetValue(jobKey, out JobNodes? job) &&
                            job.PartSets.TryGetValue(setKey, out PartSetNodes? set) &&
                            set.Parts.TryGetValue(partKey, out PartNodes? part) ? part.State : null);
                    batch = await PrepareContentsAsync(batch, snapshot, initialize, operationToken)
                        .ConfigureAwait(false);
                    return prepared;
                }
                catch
                {
                    try
                    {
                        await prepared.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Preserve the preparation error after attempting all owned cleanup.
                    }
                    throw;
                }
            }
            finally
            {
                s_callbackOwner.Value = previousOwner;
            }
        }

        /// <summary>
        /// Builds a reversible batch without exposing any changed values to existing subscribers.
        /// </summary>
        private async ValueTask<ProductionBatch> PrepareContentsAsync(
            ProductionBatch batch, MachineToolSnapshot snapshot, bool initialize, CancellationToken cancellationToken)
        {
            if (initialize == (m_observation != null))
            {
                throw Invalid(initialize ? "Production is already initialized." : "Production is not initialized.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (m_pendingPublication != null)
            {
                if (!ReferenceEquals(snapshot, m_observation))
                {
                    throw Invalid("Retry the committed observation before submitting another production update.");
                }
                return m_pendingPublication;
            }
            ValidateObservation(snapshot);
            if (m_observation != null && snapshot.Revision <= m_observation.Revision)
            {
                if (ReferenceEquals(snapshot, m_observation))
                {
                    batch.Committed = true;
                    batch.Skipped = true;
                    return batch;
                }
                throw Invalid("A different production observation must have a newer revision.");
            }
            List<VariableWrite> writes = batch.Writes;
            await EnsureEventTypesAsync(cancellationToken).ConfigureAwait(false);
            await StageTopologyAsync(batch, snapshot, initialize, cancellationToken).ConfigureAwait(false);
            ProductionActiveProgramState active = State.ActiveProgram ??
                throw Invalid("The generated production node has no ActiveProgram.");
            PrepareState(active.State!, initialize);
            StageActiveProgram(batch, snapshot);
            StageActiveState(batch, snapshot);
            Write(writes, active.Name!, Variant.From(snapshot.ActiveProgram.Name), snapshot.ActiveProgram.Sample);
            StageEvents(batch, snapshot);
            cancellationToken.ThrowIfCancellationRequested();
            await RegisterAdditionsAsync(batch, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return batch;
        }

        /// <summary>
        /// Commits prepared node values without awaiting provider, publication or disposal callbacks.
        /// </summary>
        private void CommitPrepared(
            ProductionBatch batch, MachineToolSnapshot snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Committed)
            {
                return;
            }
            foreach (NodeAddition addition in batch.Additions)
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
            ReorderAndLink(batch, snapshot.Production.Jobs);
            m_jobs = batch.Jobs;
            batch.Committed = true;
            Volatile.Write(ref m_observation, snapshot);
            Link(m_machine, State);
            m_pendingPublication = batch;
        }

        /// <summary>
        /// Releases the staged transaction on success or rejection, retaining any committed publication for retry.
        /// </summary>
        private async ValueTask ReleasePreparedAsync(ProductionBatch batch)
        {
            var errors = new List<Exception>();
            try
            {
                if (!batch.Committed)
                {
                    try
                    {
                        await RollbackAsync(batch).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                    if (m_observation == null)
                    {
                        try
                        {
                            await ReleaseEventTypesAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            errors.Add(exception);
                        }
                    }
                }
                if (!ReferenceEquals(batch, m_pendingPublication))
                {
                    ReleaseEventReservations(batch);
                }
                ThrowCleanupErrors(errors);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Checks all scalar observations before staging any node changes.
        /// </summary>
        private void ValidateObservation(MachineToolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.ActiveProgram == null || snapshot.Production == null || snapshot.Operation == null)
            {
                throw Invalid("A complete production observation is required.");
            }
            ValidateText(snapshot.ActiveProgram.Name, false);
            ValidateText(snapshot.ActiveProgram.Key, false);
            ValidateText(snapshot.ActiveProgram.JobIdentifier, false);
            ValidatePhase(snapshot.ActiveProgram.Phase);
            ValidateSample(snapshot.ActiveProgram.Sample);
            ValidateProduction(snapshot);
        }

        /// <summary>
        /// Validates bounded configuration before allocating nodes.
        /// </summary>
        private void ValidateOptions()
        {
            if ((m_options.Features & ~MachineToolFeatures.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(m_options), "Unknown MachineTool feature selection.");
            }
            if (m_options.MaximumJobs is < 1 or > 65536 ||
                m_options.MaximumProgramsPerJob is < 1 or > 65536 ||
                m_options.MaximumPartSetsPerJob < 1 || m_options.MaximumPartsPerSet < 1 ||
                m_options.MaximumInterruptionsPerJob < 1 || m_options.MaximumElements < 1 ||
                m_options.MaximumTextLength < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(m_options), "Production limits must be positive and ordered.");
            }
            MachineToolFeatures dependent = MachineToolFeatures.DynamicProductionPlan |
                MachineToolFeatures.ProductionParts | MachineToolFeatures.ProductionInterruptions;
            if ((m_options.Features & dependent) != 0 && !Has(MachineToolFeatures.Production))
            {
                throw Invalid("Dynamic plans, parts and interruptions require the Production feature.");
            }
            if (Has(MachineToolFeatures.DynamicProductionPlan) && m_options.PublishFinalObservationsAsync == null)
            {
                throw Invalid("Dynamic production requires an awaited final-observation delivery barrier.");
            }
        }

        /// <summary>
        /// Materializes optional state variables before registration, never during a published update.
        /// </summary>
        private void PrepareState(FiniteStateMachineState state, bool initialize)
        {
            if (initialize)
            {
                state.AddLastTransition(m_context.Context);
                state.CurrentState!.AddNumber(m_context.Context);
                state.LastTransition!.AddNumber(m_context.Context);
                state.LastTransition.AddTransitionTime(m_context.Context);
            }
            if (state.CurrentState?.Id == null || state.CurrentState.Number == null ||
                state.LastTransition?.Id == null || state.LastTransition.Number == null ||
                state.LastTransition.TransitionTime == null)
            {
                throw Invalid("The production state machine does not provide its required state variables.");
            }
        }

        /// <summary>
        /// Uses the shared dispatcher on a detached state to stage names, numbers and canonical model identities.
        /// Dispatcher notifications therefore cannot expose half-written live observations.
        /// </summary>
        private void StageState(
            List<VariableWrite> writes, FiniteStateMachineState state,
            MachineToolProductionPhase? previous, ulong previousSequence,
            MachineToolProductionPhase phase, ulong sequence, MachineToolSampleInfo sample)
        {
            StateSchema schema = GetSchema(state.TypeDefinitionId);
            FiniteStateMachineEntry target = schema.States[(int)phase];
            uint transition = ValidateTransition(previous, previousSequence, phase, sequence);
            ProductionStateMachineState staged = m_context.Context.CreateInstanceOfProductionStateMachineType();
            try
            {
                staged.CreateAsPredefinedNode(m_context.Context);
                var dispatcher = new FiniteStateMachineDispatcher(schema.NamespaceIndex,
                    new ArrayOf<FiniteStateMachineEntry>(schema.States),
                    new ArrayOf<FiniteStateMachineEntry>(schema.Transitions));
                dispatcher.InitializeToInitialState(staged, target.Id, m_context.Context);
                Write(writes, state.CurrentState!, Variant.From(staged.CurrentState!.Value), sample);
                Write(writes, state.CurrentState!.Id!,
                    Variant.From(new NodeId(target.Id, schema.NamespaceIndex)), sample);
                Write(writes, state.CurrentState.Number!, Variant.From(staged.CurrentState.Number!.Value), sample);
                if (previous == null || transition != uint.MaxValue)
                {
                    if (transition != uint.MaxValue)
                    {
                        dispatcher.ApplyTransition(staged, schema.Transitions[transition].Id, m_context.Context);
                    }
                    Write(writes, state.LastTransition!, Variant.From(staged.LastTransition!.Value), sample);
                    Write(writes, state.LastTransition!.Id!, Variant.From(transition == uint.MaxValue
                        ? NodeId.Null : new NodeId(schema.Transitions[transition].Id, schema.NamespaceIndex)), sample);
                    Write(writes, state.LastTransition.Number!,
                        Variant.From(staged.LastTransition.Number!.Value), sample);
                    Write(writes, state.LastTransition.TransitionTime!,
                        Variant.From(transition == uint.MaxValue ? default : sample.SourceTimestamp), sample);
                }
            }
            finally
            {
                ReleaseReservations(staged);
            }
        }

        /// <summary>
        /// Resolves each state and transition from the generated model, including subtype overrides.
        /// </summary>
        private StateSchema GetSchema(NodeId typeId)
        {
            if (m_schemas.TryGetValue(typeId, out StateSchema? schema))
            {
                return schema;
            }
            NodeState type = m_context.Manager.FindPredefinedNode(typeId) ??
                throw Invalid("The production state-machine model is not loaded.");
            var children = new List<BaseInstanceState>();
            type.GetChildren(m_context.Context, children);
            var states = new FiniteStateMachineEntry[5];
            var transitions = new FiniteStateMachineEntry[10];
            for (int i = 0; i < s_states.Length; i++)
            {
                states[i] = Entry(s_states[i], Opc.Ua.BrowseNames.StateNumber);
            }
            for (int i = 0; i < s_transitions.Length; i++)
            {
                transitions[i] = Entry(s_transitions[i], Opc.Ua.BrowseNames.TransitionNumber);
            }
            schema = new StateSchema(typeId.NamespaceIndex, states, transitions);
            m_schemas.Add(typeId, schema);
            return schema;

            FiniteStateMachineEntry Entry(string name, string numberName)
            {
                BaseInstanceState child = children.Find(candidate => candidate.BrowseName.Name == name) ??
                    throw Invalid($"The production model is missing {name}.");
                if (!child.NodeId.TryGetValue(out uint id) ||
                    child.FindChild(m_context.Context, new QualifiedName(numberName)) is not BaseVariableState number ||
                    !number.WrappedValue.TryGetValue(out uint value))
                {
                    throw Invalid($"The production model has invalid metadata for {name}.");
                }
                return new FiniteStateMachineEntry(id, value, name);
            }
        }

        /// <summary>
        /// Rejects missing transitions, impossible moves and skipped repeated runs.
        /// </summary>
        private static uint ValidateTransition(
            MachineToolProductionPhase? previous, ulong previousSequence,
            MachineToolProductionPhase phase, ulong sequence)
        {
            if (previous == null)
            {
                return uint.MaxValue;
            }
            if (previous == phase && previousSequence == sequence)
            {
                return uint.MaxValue;
            }
            if (previousSequence == ulong.MaxValue || sequence != previousSequence + 1)
            {
                throw Invalid("Every observed transition must advance its sequence by exactly one.");
            }
            string name = $"{previous}To{phase}";
            int transition = Array.IndexOf(s_transitions, name);
            if (transition < 0)
            {
                throw Invalid($"The production model has no {name} transition.");
            }
            return (uint)transition;
        }

        /// <summary>
        /// Checks observation phases without confusing enum ordinals with standard StateNumber values.
        /// </summary>
        private static void ValidatePhase(MachineToolProductionPhase phase)
        {
            if ((int)phase is < 0 or > 4)
            {
                throw Invalid("Unknown production phase.");
            }
        }

        /// <summary>
        /// Rejects usable observations without an acquisition time.
        /// </summary>
        private static void ValidateSample(MachineToolSampleInfo sample)
        {
            if (!StatusCode.IsBad(sample.StatusCode) && sample.SourceTimestamp == default)
            {
                throw Invalid("A usable production observation requires its acquisition timestamp.");
            }
        }

        /// <summary>
        /// Checks provider text against the configured bound.
        /// </summary>
        private void ValidateText(string? text, bool required)
        {
            if (text == null || text.Length > m_options.MaximumTextLength ||
                (required && string.IsNullOrWhiteSpace(text)))
            {
                throw Invalid("Production text is missing or exceeds its configured bound.");
            }
        }

        /// <summary>
        /// Tests one explicit feature.
        /// </summary>
        private bool Has(MachineToolFeatures feature) => (m_options.Features & feature) != 0;

        /// <summary>
        /// Adds a scalar write to the transaction without changing the live node.
        /// </summary>
        private static void Write(
            List<VariableWrite> writes, BaseVariableState variable, Variant value, MachineToolSampleInfo sample)
        {
            writes.Add(new VariableWrite(variable, value, sample));
        }

        /// <summary>
        /// Releases factory reservations for a discarded detached subtree.
        /// </summary>
        private void ReleaseReservations(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context.Context, children);
            foreach (BaseInstanceState child in children)
            {
                ReleaseReservations(child);
            }
            if (m_context.Context.NodeIdFactory is Di.Server.ICompanionNodeIdReservationOwner owner)
            {
                owner.ReleaseNodeIdReservation(node);
            }
        }

        /// <summary>
        /// Admits exactly one operation while permitting callbacks to read all public state.
        /// </summary>
        private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
        {
            lock (m_lifetime)
            {
                if (m_disposal != null)
                {
                    throw new ObjectDisposedException(nameof(MachineToolProductionController));
                }
                if (m_operation != null)
                {
                    throw Invalid("A production update is already in progress; reentrant mutation is not supported.");
                }
                var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
                m_operation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return linked;
            }
        }

        /// <summary>
        /// Releases the operation lease and wakes disposal.
        /// </summary>
        private void EndOperation()
        {
            lock (m_lifetime)
            {
                m_operation!.TrySetResult(true);
                m_operation = null;
            }
        }

        /// <summary>
        /// Cancels staging and drains any accepted publication before releasing resources.
        /// </summary>
        private async Task DisposeCoreAsync(Task drained, TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
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
                try
                {
                    await ReleaseOwnedNodesAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                try
                {
                    await ReleaseEventTypesAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                if (m_pendingPublication != null)
                {
                    ReleaseEventReservations(m_pendingPublication);
                    m_pendingPublication = null;
                }
                m_machine.RemoveReference(ReferenceTypeIds.HasNotifier, false, State.NodeId);
                State.RemoveReference(ReferenceTypeIds.HasNotifier, true, m_machine.NodeId);
                m_shutdown.Dispose();
                ThrowCleanupErrors(errors);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Creates a consistent validation error.
        /// </summary>
        private static ServiceResultException Invalid(string message) =>
            new(StatusCodes.BadInvalidState, message);

        /// <summary>
        /// A single admitted update used by the containing runtime to commit monitoring and production together.
        /// </summary>
        internal sealed class PreparedUpdate : IAsyncDisposable
        {
            /// <summary>
            /// Admits one update and owns its linked cancellation source.
            /// </summary>
            internal PreparedUpdate(MachineToolProductionController owner, CancellationToken cancellationToken)
            {
                m_owner = owner;
                m_operation = owner.BeginOperation(cancellationToken);
            }

            /// <summary>
            /// Gets the cancellation token for reversible preparation work.
            /// </summary>
            internal CancellationToken CancellationToken => m_operation.Token;

            /// <summary>
            /// Captures the prepared commit, publication and rollback operations.
            /// </summary>
            internal void Configure(
                Action commit,
                Func<ValueTask> publish,
                Func<ValueTask> dispose,
                Func<string, ProductionJobState?> findJob,
                Func<string, string, string, ProductionPartState?> findPart)
            {
                m_commit = commit;
                m_publish = publish;
                m_dispose = dispose;
                m_findJob = findJob;
                m_findPart = findPart;
            }

            /// <summary>
            /// Resolves an actual job in this prospective inventory, including newly staged companion subtypes.
            /// Removed job keys do not resolve.
            /// </summary>
            internal bool TryGetJob(string jobKey, [NotNullWhen(true)] out ProductionJobState? state)
            {
                if (Volatile.Read(ref m_disposed) != 0 || m_findJob == null)
                {
                    throw Invalid("Production references require an active prepared update.");
                }
                state = m_findJob(jobKey);
                return state != null;
            }

            /// <summary>
            /// Resolves an actual part in this prospective inventory without depending on committed sibling nodes.
            /// </summary>
            internal bool TryGetPart(
                string jobKey,
                string partSetKey,
                string partKey,
                [NotNullWhen(true)] out ProductionPartState? state)
            {
                if (Volatile.Read(ref m_disposed) != 0 || m_findPart == null)
                {
                    throw Invalid("Production references require an active prepared update.");
                }
                state = m_findPart(jobKey, partSetKey, partKey);
                return state != null;
            }

            /// <summary>
            /// Applies the already prepared values; this never awaits an application callback.
            /// </summary>
            internal void Commit()
            {
                if (m_committed || Volatile.Read(ref m_disposed) != 0)
                {
                    throw Invalid("A prepared production update can only be committed once.");
                }
                m_commit!();
                m_committed = true;
            }

            /// <summary>
            /// Publishes the committed observations outside the containing runtime's commit gate.
            /// </summary>
            internal ValueTask PublishAsync()
            {
                if (!m_committed || Volatile.Read(ref m_disposed) != 0)
                {
                    throw Invalid("A prepared production update must be committed before publication.");
                }
                return m_publish!();
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    try
                    {
                        if (m_dispose != null)
                        {
                            await m_dispose().ConfigureAwait(false);
                        }
                        else
                        {
                            m_owner.EndOperation();
                        }
                    }
                    finally
                    {
                        m_operation.Dispose();
                    }
                }
            }

            /// <summary>The owner whose admission is released on disposal.</summary>
            private readonly MachineToolProductionController m_owner;

            /// <summary>The linked cancellation source owned by this disposable preparation.</summary>
            private readonly CancellationTokenSource m_operation;

            /// <summary>The prepared synchronous commit.</summary>
            private Action? m_commit;

            /// <summary>The asynchronous publication.</summary>
            private Func<ValueTask>? m_publish;

            /// <summary>The rollback and lease release.</summary>
            private Func<ValueTask>? m_dispose;

            /// <summary>Resolves jobs from the captured prospective inventory.</summary>
            private Func<string, ProductionJobState?>? m_findJob;

            /// <summary>Resolves parts from the captured prospective inventory.</summary>
            private Func<string, string, string, ProductionPartState?>? m_findPart;

            /// <summary>Whether commit succeeded.</summary>
            private bool m_committed;

            /// <summary>Whether the lease was released.</summary>
            private int m_disposed;
        }

        /// <summary>
        /// A staged write preserving quality and acquisition time.
        /// </summary>
        private sealed record VariableWrite(BaseVariableState Variable, Variant Value, MachineToolSampleInfo Sample)
        {
            /// <summary>
            /// Applies the already-validated value without calling an application delegate.
            /// </summary>
            public void Apply()
            {
                Variable.AccessLevel = AccessLevels.CurrentRead;
                Variable.UserAccessLevel = AccessLevels.CurrentRead;
                Variable.Value = Value;
                Variable.StatusCode = Sample.StatusCode;
                Variable.Timestamp = Sample.SourceTimestamp;
            }
        }

        /// <summary>
        /// Canonical metadata resolved from one generated state-machine type.
        /// </summary>
        private sealed record StateSchema(
            ushort NamespaceIndex, FiniteStateMachineEntry[] States, FiniteStateMachineEntry[] Transitions);

        /// <summary>The containing machine, which remains owned by the parent runtime.</summary>
        private readonly MachineToolState m_machine;

        /// <summary>The shared namespace owner.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The per-instance bounded feature configuration.</summary>
        private readonly MachineToolProductionOptions m_options;

        /// <summary>The injected publication clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>Resolved generated schemas.</summary>
        private readonly Dictionary<NodeId, StateSchema> m_schemas = [];

        /// <summary>The committed observation.</summary>
        private MachineToolSnapshot? m_observation;

        /// <summary>Protects only operation admission and lifetime, never application callbacks.</summary>
        private readonly Lock m_lifetime = new();

        /// <summary>Cancels uncommitted work during disposal.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>The admitted operation's completion.</summary>
        private TaskCompletionSource<bool>? m_operation;

        /// <summary>The idempotent disposal completion.</summary>
        private Task? m_disposal;

        /// <summary>A bounded committed publication retained for retry if a subscriber callback fails.</summary>
        private ProductionBatch? m_pendingPublication;

        /// <summary>Detects disposal reentrancy without exposing gates to callbacks.</summary>
        private static readonly AsyncLocal<MachineToolProductionController?> s_callbackOwner = new();

        /// <summary>Names in the contract enum order, not in numeric StateNumber order.</summary>
        private static readonly string[] s_states = ["Initializing", "Running", "Interrupted", "Ended", "Aborted"];

        /// <summary>Names in the standard TransitionNumber order from Tables 47, 50, 53 and 56.</summary>
        private static readonly string[] s_transitions =
        [
            "InitializingToRunning", "RunningToEnded", "EndedToInitializing", "RunningToRunning",
            "RunningToInterrupted", "InterruptedToRunning", "RunningToAborted", "InterruptedToAborted",
            "AbortedToInitializing", "InitializingToAborted"
        ];
    }
}
