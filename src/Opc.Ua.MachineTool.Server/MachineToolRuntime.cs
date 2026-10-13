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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Owns the published behavior and resources of one generated MachineTool instance.
    /// </summary>
    /// <typeparam name="TState">The actual generated MachineTool subtype.</typeparam>
    public sealed partial class MachineToolRuntime<TState> : IAsyncDisposable, IConformanceContributor
        where TState : MachineToolState
    {
        /// <summary>
        /// Creates a runtime around an unpublished generated state.
        /// </summary>
        internal MachineToolRuntime(
            IMachineryBuildContext context, TState state, IMachineToolProvider provider, MachineToolFeatures features,
            MachineToolProductionOptions productionOptions, MachineToolEquipmentOptions equipmentOptions,
            MachineToolPrognosisOptions prognosisOptions,
            Func<ISystemContext, TState, MachineToolSnapshot, Action>? observationProjection = null)
        {
            m_context = context;
            State = state;
            m_provider = provider;
            m_observationProjection = observationProjection;
            Features = features;
            Production = new MachineToolProductionController(context, state, productionOptions with
            {
                Features = features,
                PublishFinalObservationsAsync = productionOptions.PublishFinalObservationsAsync ??
                    context.Manager.WaitForValuePublicationAsync
            }, context.GetService<TimeProvider>() ?? TimeProvider.System);
            Equipment = new MachineToolEquipmentController(context, state, equipmentOptions with
            {
                Features = features,
                PublishFinalObservationsAsync = equipmentOptions.PublishFinalObservationsAsync ??
                    context.Manager.WaitForValuePublicationAsync
            });
            Prognoses = new MachineToolPrognosisController(context, state, prognosisOptions with
            {
                Features = features,
                PublishFinalObservationsAsync = prognosisOptions.PublishFinalObservationsAsync ??
                    context.Manager.WaitForValuePublicationAsync
            });
        }

        /// <summary>
        /// Gets the actual generated state, retaining a companion or vendor subtype.
        /// </summary>
        public TState State { get; }

        /// <summary>
        /// Gets the instance identifier.
        /// </summary>
        public NodeId NodeId => State.NodeId;

        /// <summary>
        /// Gets the explicitly selected optional features of this instance.
        /// </summary>
        public MachineToolFeatures Features { get; }

        /// <summary>
        /// Gets the composed retained-production module and its last committed observation.
        /// Normal equipment updates use RefreshAsync so monitoring and production remain coherent.
        /// </summary>
        public MachineToolProductionController Production { get; }

        /// <summary>
        /// Gets the composed tool and tool-life observation module.
        /// </summary>
        public MachineToolEquipmentController Equipment { get; }

        /// <summary>
        /// Gets the composed provider-reported prognosis module.
        /// </summary>
        public MachineToolPrognosisController Prognoses { get; }

        /// <summary>
        /// Acquires and publishes a coherent provider observation, preserving acquisition quality and time.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operation = BeginOperation(cancellationToken);
            bool entered = false;
            MachineToolRuntime<TState>? previousRuntime = s_currentRuntime.Value;
            s_currentRuntime.Value = this;
            try
            {
                MachineToolSnapshot snapshot = await m_provider.ReadAsync(operation.Token).ConfigureAwait(false);
                MachineToolSnapshotValidation.Validate(snapshot);
                await m_gate.WaitAsync(operation.Token).ConfigureAwait(false);
                entered = true;
                operation.Token.ThrowIfCancellationRequested();
                if (snapshot.Revision < m_revision)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The provider returned a stale revision.");
                }
                ArrayOf<MachineToolChannel> channels = PrepareMonitoring(snapshot);
                m_gate.Release();
                entered = false;
                MachineToolProductionController.PreparedUpdate production =
                    await Production.PrepareAsync(snapshot, false, operation.Token).ConfigureAwait(false);
                await using (production.ConfigureAwait(false))
                {
                    MachineToolInventoryPublisher.PreparedUpdate equipment =
                        await Equipment.PrepareAsync(snapshot, false, operation.Token).ConfigureAwait(false);
                    await using (equipment.ConfigureAwait(false))
                    {
                        MachineToolInventoryPublisher.PreparedUpdate prognoses =
                            await Prognoses.PrepareAsync(snapshot, false, Equipment.GetPreparedTools(equipment),
                                key => production.TryGetJob(key, out ProductionJobState? job) ? job : null,
                                (job, set, part) => production.TryGetPart(
                                    job, set, part, out ProductionPartState? node) ? node : null,
                                operation.Token).ConfigureAwait(false);
                        await using (prognoses.ConfigureAwait(false))
                        {
                            await m_gate.WaitAsync(operation.Token).ConfigureAwait(false);
                            entered = true;
                            try
                            {
                                equipment.ValidateCommit();
                                prognoses.ValidateCommit();
                                Action? projection = PrepareObservationProjection(snapshot);
                                operation.Token.ThrowIfCancellationRequested();
                                production.Commit();
                                equipment.Commit();
                                prognoses.Commit();
                                ApplyMonitoring(snapshot, channels);
                                projection?.Invoke();
                                m_revision = snapshot.Revision;
                            }
                            finally
                            {
                                m_gate.Release();
                                entered = false;
                            }
                            await State.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                                .ConfigureAwait(false);
                            await production.PublishAsync().ConfigureAwait(false);
                            await equipment.PublishAsync().ConfigureAwait(false);
                            await prognoses.PublishAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
            finally
            {
                if (entered)
                {
                    m_gate.Release();
                }
                s_currentRuntime.Value = previousRuntime;
                EndOperation();
            }
        }

        /// <summary>
        /// Retries publication of the last accepted observation without reading or commanding the provider.
        /// This is useful when the provider has already advanced after an event or notification sink failed.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask RetryPublicationAsync(CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operation = BeginOperation(cancellationToken);
            MachineToolRuntime<TState>? previousRuntime = s_currentRuntime.Value;
            s_currentRuntime.Value = this;
            try
            {
                MachineToolSnapshot snapshot = Production.Observation ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "No production observation is committed.");
                operation.Token.ThrowIfCancellationRequested();
                await State.ClearChangeMasksAsync(m_context.Context, true, CancellationToken.None)
                    .ConfigureAwait(false);
                await Production.ApplyAsync(snapshot, operation.Token).ConfigureAwait(false);
                await Equipment.ApplyAsync(snapshot, operation.Token).ConfigureAwait(false);
                await Prognoses.ApplyAsync(snapshot, operation.Token).ConfigureAwait(false);
            }
            finally
            {
                s_currentRuntime.Value = previousRuntime;
                EndOperation();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (ReferenceEquals(s_currentRuntime.Value, this) ||
                Production.IsInCallback ||
                Equipment.IsInCallback ||
                Prognoses.IsInCallback ||
                Notifications?.IsInCallback == true)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A MachineTool observation callback cannot await disposal of its own runtime.");
            }
            var errors = new List<Exception>();
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
            try
            {
                if (ReferenceEquals(m_context.Manager.FindPredefinedNode(State.NodeId), State))
                {
                    m_context.MachinesFolder.RemoveChild(State);
                    await m_context.Manager.DeleteNodeAsync(
                        m_context.Manager.SystemContext, State.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
            if (errors.Count != 0)
            {
                throw new AggregateException("MachineTool disposal failed.", errors);
            }
        }

        /// <summary>
        /// Creates the registration resource; node deletion stops the runtime without recursively deleting the node.
        /// </summary>
        internal IAsyncDisposable CreateRegistration()
        {
            return new Registration(this);
        }

        /// <summary>
        /// Cancels and drains runtime work once, independently of which owner removes the root node.
        /// </summary>
        private ValueTask StopAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (m_lifetimeGate)
            {
                if (m_disposal != null)
                {
                    return new ValueTask(m_disposal);
                }
                m_disposed = true;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
                if (m_activeOperations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            _ = DisposeAsyncCore(completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Cancels acquisition, drains admitted operations, then releases the instance outside its commit gate.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        private async Task DisposeAsyncCore(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                try
                {
                    await m_shutdown.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                await m_drained.Task.ConfigureAwait(false);
                try
                {
                    await Production.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                try
                {
                    await Equipment.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                try
                {
                    await Prognoses.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                try
                {
                    if (Notifications != null)
                    {
                        await Notifications.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_gate.Dispose();
                    m_shutdown.Dispose();
                }
                if (errors.Count != 0)
                {
                    throw new AggregateException("MachineTool shutdown cancellation failed.", errors);
                }
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        /// <summary>
        /// Initializes mandatory values before the machine enters the address space.
        /// </summary>
        internal async ValueTask InitializeAsync(MachineToolSnapshot snapshot, CancellationToken cancellationToken)
        {
            ValidateExtendedMonitoring(snapshot, initializing: true);
            CreateExtendedMonitoring(snapshot);
            CreateMonitoring(snapshot);
            ArrayOf<MachineToolChannel> channels = PrepareMonitoring(snapshot);
            MachineToolProductionController.PreparedUpdate production =
                await Production.PrepareAsync(snapshot, true, cancellationToken).ConfigureAwait(false);
            await using (production.ConfigureAwait(false))
            {
                MachineToolInventoryPublisher.PreparedUpdate equipment =
                    await Equipment.PrepareAsync(snapshot, true, cancellationToken).ConfigureAwait(false);
                await using (equipment.ConfigureAwait(false))
                {
                    MachineToolInventoryPublisher.PreparedUpdate prognoses =
                        await Prognoses.PrepareAsync(snapshot, true, Equipment.GetPreparedTools(equipment),
                            key => production.TryGetJob(key, out ProductionJobState? job) ? job : null,
                            (job, set, part) => production.TryGetPart(
                                job, set, part, out ProductionPartState? node) ? node : null,
                            cancellationToken).ConfigureAwait(false);
                    await using (prognoses.ConfigureAwait(false))
                    {
                        equipment.ValidateCommit();
                        prognoses.ValidateCommit();
                        Action? projection = PrepareObservationProjection(snapshot);
                        cancellationToken.ThrowIfCancellationRequested();
                        production.Commit();
                        equipment.Commit();
                        prognoses.Commit();
                        ApplyMonitoring(snapshot, channels);
                        projection?.Invoke();
                        m_revision = snapshot.Revision;
                        await production.PublishAsync().ConfigureAwait(false);
                        await equipment.PublishAsync().ConfigureAwait(false);
                        await prognoses.PublishAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Validates the companion projection before generic commits; prepared updates own rollback.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Action? PrepareObservationProjection(MachineToolSnapshot snapshot)
        {
            return m_observationProjection == null ? null :
                m_observationProjection(m_context.Context, State, snapshot) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "A companion observation projection must return a nonthrowing assignment action.");
        }

        /// <summary>
        /// Rejects successful measurements without a supplied acquisition time.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateSample(MachineToolSampleInfo sample)
        {
            MachineToolSnapshotValidation.ValidateSample(sample);
        }

        /// <summary>
        /// Validates all monitoring inventories and canonical state identities before either module commits.
        /// </summary>
        private ArrayOf<MachineToolChannel> PrepareMonitoring(MachineToolSnapshot snapshot)
        {
            ArrayOf<MachineToolChannel> channels = PrepareChannels(snapshot);
            ValidateChannelTopology(channels);
            ValidateExtendedMonitoring(snapshot, initializing: false);
            if (Has(MachineToolFeatures.MachineryState))
            {
                MachineOperationMonitoringState operation = State.Monitoring!.MachineTool!;
                FindStateId(operation.MachineryItemState!.TypeDefinitionId,
                    snapshot.Operation.ItemState!.Value.ToString());
                FindStateId(operation.MachineryOperationMode!.TypeDefinitionId,
                    snapshot.Operation.MachineryMode!.Value.ToString());
                if (Has(MachineToolFeatures.MaintenanceMode))
                {
                    FindStateId(operation.MachineryOperationMode.MaintenanceMode!.TypeDefinitionId,
                        snapshot.Operation.MaintenanceMode!.Value.ToString());
                }
            }
            return channels;
        }

        /// <summary>
        /// Commits validated monitoring without duplicating the production module's ActiveProgram or state handling.
        /// </summary>
        private void ApplyMonitoring(MachineToolSnapshot snapshot, ArrayOf<MachineToolChannel> channels)
        {
            SetSample(State.Monitoring!.MachineTool!.OperationMode!, Variant.From(snapshot.Operation.Mode),
                snapshot.Operation.Sample);
            ApplyChannels(channels);
            ApplyExtendedMonitoring(snapshot);
        }

        /// <summary>
        /// Resolves canonical state identities from the generated model rather than inventing state NodeIds.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private NodeId FindStateId(NodeId stateMachineType, string name)
        {
            NodeState? type = m_context.Manager.FindPredefinedNode(stateMachineType);
            if (type != null)
            {
                var children = new List<BaseInstanceState>();
                type.GetChildren(m_context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    if (child is BaseObjectState state &&
                        child.BrowseName.Name == name &&
                        m_context.Context.TypeTable.IsTypeOf(state.TypeDefinitionId, Ua.ObjectTypeIds.StateType))
                    {
                        return child.NodeId;
                    }
                }
            }
            throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                "The generated production state machine does not declare the reported state.");
        }

        /// <summary>
        /// Writes a scalar without losing its quality or acquisition time.
        /// </summary>
        private static void SetSample(BaseVariableState variable, Variant value, MachineToolSampleInfo sample)
        {
            variable.Value = value;
            variable.StatusCode = sample.StatusCode;
            variable.Timestamp = sample.SourceTimestamp;
        }

        /// <summary>
        /// Resolves or selects a generated child without replacing an existing companion subtype.
        /// </summary>
        /// <typeparam name="TNode">The expected generated node type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        private TNode Child<TNode>(NodeState parent, string name, string namespaceUri, bool create = false)
            where TNode : BaseInstanceState
        {
            int index = m_context.Context.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "A required model namespace is absent.");
            }
            var browseName = new QualifiedName(name, (ushort)index);
            BaseInstanceState? child = parent.FindChild(m_context.Context, browseName);
            if (child == null && create)
            {
                child = parent.CreateChild(m_context.Context, browseName);
            }
            return child as TNode ??
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    $"The generated node '{parent.BrowseName}' does not provide '{name}'.");
        }

        /// <summary>
        /// Applies a provider-observed state using the canonical generated state declaration.
        /// </summary>
        private void SetObservedState(FiniteStateMachineState state, string name, MachineToolSampleInfo sample)
        {
            NodeId id = FindStateId(state.TypeDefinitionId, name);
            SetSample(state.CurrentState!, Variant.From(new LocalizedText(name)), sample);
            SetSample(state.CurrentState!.Id!, Variant.From(id), sample);
        }

        /// <summary>
        /// Rejects operations after runtime teardown.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
        {
            if (ReferenceEquals(s_currentRuntime.Value, this) ||
                Production.IsInCallback ||
                Equipment.IsInCallback ||
                Prognoses.IsInCallback ||
                Notifications?.IsInCallback == true)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A MachineTool observation callback cannot reenter an observation update.");
            }
            lock (m_lifetimeGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(MachineToolRuntime<>));
                }
                var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
                m_activeOperations++;
                return operation;
            }
        }

        /// <summary>
        /// Releases an admitted operation and wakes disposal after the last publication completes.
        /// </summary>
        private void EndOperation()
        {
            lock (m_lifetimeGate)
            {
                if (--m_activeOperations == 0 && m_disposed)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>The instance's owning Machinery context.</summary>
        private readonly IMachineryBuildContext m_context;

        /// <summary>The application-owned equipment provider.</summary>
        private readonly IMachineToolProvider m_provider;

        /// <summary>The pure companion projection participating in the generic transaction.</summary>
        private readonly Func<ISystemContext, TState, MachineToolSnapshot, Action>? m_observationProjection;

        /// <summary>Serializes commits and disposal, never provider acquisition.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Protects operation admission, not provider callbacks or publication.</summary>
        private readonly Lock m_lifetimeGate = new();

        /// <summary>Signals cancellation to this instance's admitted operations.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>Completes when shutdown has no admitted operations left to drain.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The shared completion of an idempotent asynchronous teardown.</summary>
        private Task? m_disposal;

        /// <summary>The admitted acquisition/publication operations.</summary>
        private int m_activeOperations;

        /// <summary>The last accepted observation revision.</summary>
        private ulong m_revision;

        /// <summary>Whether new observations are refused.</summary>
        private volatile bool m_disposed;

        /// <summary>Detects reentrancy from equipment acquisition and publication without exposing any gates.</summary>
        private static readonly AsyncLocal<MachineToolRuntime<TState>?> s_currentRuntime = new();

        /// <summary>
        /// Couples runtime lifetime to the shared registration transaction without owning root deletion.
        /// </summary>
        private sealed class Registration : IAsyncDisposable
        {
            /// <summary>
            /// Captures the runtime being staged for registration.
            /// </summary>
            public Registration(MachineToolRuntime<TState> runtime)
            {
                m_runtime = runtime;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return m_runtime.StopAsync();
            }

            /// <summary>The instance runtime whose operations are drained on rollback or removal.</summary>
            private readonly MachineToolRuntime<TState> m_runtime;
        }
    }
}
