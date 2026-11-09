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
using Microsoft.Extensions.Logging;
using Opc.Ua.Programs;

namespace Opc.Ua.Server.Programs
{
    /// <summary>
    /// Owns asynchronous execution, diagnostics and results of one Part 10 Program invocation.
    /// </summary>
    public sealed partial class ProgramRuntime : IAsyncDisposable
    {
        private ProgramRuntime(
            ProgramStateMachineState node,
            ProgramDefinition definition,
            ISystemContext context,
            TimeProvider timeProvider,
            Action<ProgramRuntime>? onTerminated)
        {
            Node = node;
            Definition = definition;
            m_diagnostics.CreateSessionId = GetSessionId(context);
            m_diagnostics.CreateClientName = (context as ServerSystemContext)?.OperationContext?.Session
                ?.ReadDiagnostics(static value => value.ClientDescription?.ApplicationName.Text) ?? string.Empty;
            SessionSystemContext internalContext = context is ServerSystemContext serverContext
                ? new ServerSystemContext(serverContext.Server)
                {
                    NodeStateFactory = context.NodeStateFactory
                }
                : new SessionSystemContext(context.Telemetry)
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    TypeTable = context.TypeTable,
                    EncodeableFactory = context.EncodeableFactory,
                    NodeStateFactory = context.NodeStateFactory
                };
            if (context.NodeIdFactory is { } nodeIdFactory)
            {
                internalContext.NodeIdFactory = nodeIdFactory;
            }
            m_context = internalContext;
            m_timeProvider = timeProvider;
            m_onTerminated = onTerminated;
            m_logger = context.Telemetry.CreateLogger<ProgramRuntime>();
        }

        /// <summary>
        /// Gets the invocation's address-space representation. Changes must go through this runtime.
        /// </summary>
        public ProgramStateMachineState Node { get; }

        /// <summary>
        /// Gets the immutable type policy.
        /// </summary>
        public ProgramDefinition Definition { get; }

        /// <summary>
        /// Gets the last committed base state.
        /// </summary>
        public ProgramState CurrentState => (ProgramState)Volatile.Read(ref m_currentState);

        /// <summary>
        /// Gets the current execution generation. Updates from older generations are rejected.
        /// </summary>
        public long RunId => Interlocked.Read(ref m_runId);

        /// <summary>
        /// Attaches managed execution to a created, exclusively owned Program node.
        /// </summary>
        /// <remarks>
        /// The caller indexes any newly materialized optional children after attachment.
        /// The termination callback only queues cleanup; it must not dispose the runtime inline.
        /// </remarks>
        public static async ValueTask<ProgramRuntime> AttachAsync(
            ProgramStateMachineState node,
            ProgramDefinition definition,
            ISystemContext context,
            TimeProvider timeProvider,
            Action<ProgramRuntime>? onTerminated = null,
            ArrayOf<Variant> creationArguments = default,
            bool startObserving = true,
            CancellationToken cancellationToken = default)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (node.NodeId.IsNull || node.CurrentState?.Id is null)
            {
                throw new ArgumentException("Create the Program node before attaching execution.", nameof(node));
            }
            if (definition.AutoDelete && onTerminated is null)
            {
                throw new ArgumentException("AutoDelete requires a lifecycle owner.", nameof(onTerminated));
            }
            if (!node.TryClaimExecution())
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "The Program invocation already has an execution owner.");
            }

            ProgramRuntime? runtime = null;
            try
            {
                runtime = new ProgramRuntime(node, definition, context, timeProvider, onTerminated);
                runtime.InitializeNode();
                runtime.m_execution = await definition.Provider.CreateAsync(
                    new ProgramInvocationContext(node.NodeId, node.TypeDefinitionId,
                        timeProvider, context.Telemetry, runtime.m_lifetime.Token, CoreUtils.Clone(creationArguments)),
                    cancellationToken).ConfigureAwait(false);
                if (runtime.m_execution is null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The Program provider returned no execution resource.");
                }
                runtime.WireMethods();
                if (startObserving)
                {
                    await runtime.ActivateAsync(cancellationToken).ConfigureAwait(false);
                }
                return runtime;
            }
            catch
            {
                if (runtime is not null)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    node.ReleaseExecution();
                }
                throw;
            }
        }

        /// <summary>
        /// Starts observation after address-space registration and enables controls once enumeration has begun.
        /// </summary>
        public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_disposalLock)
            {
                if (m_stopping || m_activationStarted)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "A Program runtime can be activated only once before shutdown.");
                }
                m_activationStarted = true;
                return new ValueTask(StartObservation(cancellationToken));
            }
        }

        /// <summary>
        /// Executes a control through the same argument validation and diagnostics as an OPC UA Call.
        /// </summary>
        public async ValueTask<MethodInvocationResult> ControlAsync(
            ISystemContext context,
            ProgramControlMethod control,
            ArrayOf<Variant> inputArguments = default,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!m_methods.TryGetValue(control, out MethodState? method))
            {
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadNotSupported));
            }
            var errors = new List<ServiceResult>();
            var outputs = new List<Variant>();
            ServiceResult result = await method.CallAsync(
                context, Node.NodeId, inputArguments, errors, outputs, cancellationToken).ConfigureAwait(false);
            foreach (ServiceResult error in errors)
            {
                if (ServiceResult.IsBad(error))
                {
                    result = StatusCodes.BadInvalidArgument;
                    break;
                }
            }
            return new MethodInvocationResult(result, outputs.ToArray(), errors.ToArray());
        }

        /// <summary>
        /// Reserves removal and stops command admission after checking the client deletion policy.
        /// </summary>
        public async ValueTask<ServiceResult> PrepareDeleteAsync(
            bool byClient,
            CancellationToken cancellationToken = default)
        {
            if (!BeginOperation())
            {
                return StatusCodes.BadShutdown;
            }
            try
            {
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (m_stopping)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    if (byClient && !Definition.Deletable)
                    {
                        return StatusCodes.BadUserAccessDenied;
                    }
                    if (byClient && CurrentState != ProgramState.Halted)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    m_stopping = true;
                    return ServiceResult.Good;
                }
                finally
                {
                    m_gate.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_disposalLock)
            {
                m_disposal ??= DisposeCoreAsync();
                return new ValueTask(m_disposal);
            }
        }

        private async Task DisposeCoreAsync()
        {
            m_stopping = true;
            ReleaseDiagnosticReadAuthorization();
            if (m_operations == 0)
            {
                m_drained.TrySetResult(true);
            }
            var failures = new List<Exception>();
            try
            {
                m_lifetime.Cancel();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                await m_observation.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            await m_drained.Task.ConfigureAwait(false);
            try
            {
                foreach (MethodState method in m_methods.Values)
                {
                    method.OnCallMethodWithResultAsync = null;
                    method.OnCallValidationFailedAsync = null;
                    method.OnReadExecutable = null;
                    method.OnReadUserExecutable = null;
                    method.Executable = false;
                    method.UserExecutable = false;
                }
                if (m_execution is not null)
                {
                    await m_execution.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                m_gate.Dispose();
                m_lifetime.Dispose();
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Program execution cleanup failed.", failures);
            }
            Node.ReleaseExecution();
        }

        private bool BeginOperation()
        {
            lock (m_disposalLock)
            {
                if (m_stopping)
                {
                    return false;
                }
                m_operations++;
                return true;
            }
        }

        private void EndOperation()
        {
            lock (m_disposalLock)
            {
                m_operations--;
                if (m_stopping && m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        private void InitializeNode()
        {
            Node.SetState(m_context, StateNodeId(Definition.InitialState));
            Node.SynchronizeInitialState(m_context, StateNodeId(Definition.InitialState));
            Volatile.Write(ref m_currentState, (int)Definition.InitialState);
            if (Node.LastTransition is { } transition)
            {
                transition.Value = LocalizedText.Null;
                transition.Id!.Value = NodeId.Null;
                transition.Number!.Value = 0;
                transition.TransitionTime!.Value = DateTime.MinValue;
                transition.StatusCode = StatusCodes.BadWaitingForInitialData;
            }
            Required(Node.Deletable).Value = Definition.Deletable;
            Required(Node.AutoDelete).Value = Definition.AutoDelete;
            Required(Node.RecycleCount).Value = 0;
            Node.EventNotifier = EventNotifiers.SubscribeToEvents;
            InitializeDiagnostics();
        }

        private void WireMethods()
        {
            var children = new List<BaseInstanceState>();
            Node.GetChildren(m_context, children);
            foreach (ProgramControlMethod control in Definition.Controls)
            {
                string name = control.ToString();
                MethodState? method = null;
                foreach (BaseInstanceState child in children)
                {
                    if (child is MethodState candidate && candidate.BrowseName.Name == name)
                    {
                        if (method is not null)
                        {
                            throw new ServiceResultException(StatusCodes.BadConfigurationError,
                                $"Ambiguous Program control '{name}'.");
                        }
                        method = candidate;
                    }
                }
                method ??= Node.CreateChild(m_context,
                    new QualifiedName(name, Node.BrowseName.NamespaceIndex)) as MethodState;
                if (method is null)
                {
                    method = new MethodState(Node)
                    {
                        BrowseName = new QualifiedName(name, Node.BrowseName.NamespaceIndex),
                        DisplayName = new LocalizedText(name),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent
                    };
                    Node.AddChild(method);
                }
                if (method.OnCallMethodWithResultAsync is not null || method.OnCallMethod2Async is not null ||
                    method.OnCallMethod2 is not null ||
                    (method.OnCallMethod is { } existing && !ReferenceEquals(existing.Target, Node)))
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        $"Program control '{name}' already has an application handler.");
                }
                method.OnCallMethod = null;
                method.Executable = true;
                method.UserExecutable = true;
                method.OnCallMethodWithResultAsync = (ctx, target, _, inputs, ct) =>
                    ExecuteControlAsync(ctx, target, control, inputs, ct);
                method.OnCallValidationFailedAsync = RecordRejectedCallAsync;
                method.OnReadExecutable = (ISystemContext _, NodeState _, ref bool value) =>
                {
                    value = CanExecute(control);
                    return ServiceResult.Good;
                };
                method.OnReadUserExecutable = (ISystemContext ctx, NodeState _, ref bool value) =>
                {
                    value = CanExecute(control) && Node.IsCausePermitted(ctx, CauseId(control), true);
                    return ServiceResult.Good;
                };
                m_methods.Add(control, method);
            }
        }

        private bool CanExecute(ProgramControlMethod control)
        {
            return m_activated && !m_stopping &&
                (!m_observationFaulted || control == ProgramControlMethod.Reset) &&
                Node.CurrentState?.StatusCode != StatusCodes.BadStateNotActive &&
                TryTarget(CurrentState, control, out _) &&
                (control is not (ProgramControlMethod.Start or ProgramControlMethod.Reset) ||
                    CanRestart(m_started, Interlocked.Read(ref m_recycleCount), Definition.MaxRecycleCount));
        }

        private async ValueTask<MethodInvocationResult> ExecuteControlAsync(
            ISystemContext context,
            MethodState method,
            ProgramControlMethod control,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            if (!BeginOperation())
            {
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadShutdown));
            }
            try
            {
                return await ExecuteAdmittedControlAsync(context, method, control, inputs, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        private async ValueTask<MethodInvocationResult> ExecuteAdmittedControlAsync(
            ISystemContext context,
            MethodState method,
            ProgramControlMethod control,
            ArrayOf<Variant> inputs,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token);
            await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
            ServiceResult result = ServiceResult.Good;
            ArrayOf<Variant> outputs = default;
            bool terminated = false;
            try
            {
                if (!CanExecute(control) || !TryTarget(CurrentState, control, out ProgramState target))
                {
                    result = StatusCodes.BadInvalidState;
                }
                else
                {
                    var callbackOutputs = new List<Variant>();
                    result = Node.PrepareTransition(context,
                        TransitionId(CurrentState, target), CauseId(control), inputs, callbackOutputs,
                        out PreparedStateMachineTransition prepared);
                    if (ServiceResult.IsGood(result))
                    {
                        long runId = control == ProgramControlMethod.Start ? checked(m_runId + 1) : m_runId;
                        ProgramControlResult acceptance = await m_execution!.ControlAsync(
                            new ProgramControlContext(control, CurrentState, runId,
                                CoreUtils.Clone(inputs), GetSessionId(context), m_lifetime.Token),
                            linked.Token).ConfigureAwait(false);
                        if (acceptance is null || acceptance.Result is null)
                        {
                            throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                                "The Program provider returned no control result.");
                        }
                        result = acceptance.Result;
                        outputs = CoreUtils.Clone(acceptance.OutputArguments);
                        if (ServiceResult.IsGood(result))
                        {
                            result = ValidateOutputs(context, method, outputs);
                        }
                        if (ServiceResult.IsGood(result))
                        {
                            callbackOutputs.AddRange(outputs);
                            bool restartObservation = control == ProgramControlMethod.Reset && m_observationFaulted;
                            result = await CommitAsync(context, Node, prepared, acceptance.IntermediateResult,
                                default, control, method, inputs, () =>
                                {
                                    if (control == ProgramControlMethod.Start)
                                    {
                                        BeginRun(runId);
                                    }
                                }).ConfigureAwait(false);
                            if (ServiceResult.IsGood(result))
                            {
                                if (restartObservation)
                                {
                                    await RestartObservationAsync(m_lifetime.Token).ConfigureAwait(false);
                                }
                                result = acceptance.Result;
                            }
                            terminated = ServiceResult.IsGood(result) &&
                                target == ProgramState.Halted && m_started;
                        }
                    }
                }
                RecordCall(context, method, inputs, outputs, result);
                return new MethodInvocationResult(result, outputs);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                RecordCall(context, method, inputs, default, StatusCodes.BadRequestCancelledByClient);
                return new MethodInvocationResult(new ServiceResult(StatusCodes.BadRequestCancelledByClient));
            }
            catch (Exception exception)
            {
                m_logger.ControlFailed(Node.NodeId, control, exception);
                result = exception is ServiceResultException serviceException
                    ? new ServiceResult(serviceException.StatusCode)
                    : new ServiceResult(StatusCodes.BadUnexpectedError);
                RecordCall(context, method, inputs, default, result);
                return new MethodInvocationResult(result);
            }
            finally
            {
                m_gate.Release();
                if (terminated && Definition.AutoDelete)
                {
                    m_onTerminated!(this);
                }
            }
        }

        private Task<bool> StartObservation(CancellationToken cancellationToken)
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_observation = ObserveAsync(ready, cancellationToken);
            return ready.Task;
        }

        private async Task ObserveAsync(TaskCompletionSource<bool> ready, CancellationToken cancellationToken)
        {
            await Task.Yield();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_lifetime.Token.ThrowIfCancellationRequested();
                using var observationLifetime = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
                IAsyncEnumerator<ProgramExecutionUpdate> updates =
                    m_execution!.ObserveAsync(observationLifetime.Token).GetAsyncEnumerator(observationLifetime.Token);
                await using (updates.ConfigureAwait(false))
                {
                    Task<bool> firstMove = updates.MoveNextAsync().AsTask();
                    try
                    {
                        if (firstMove.IsCompleted)
                        {
                            await firstMove.ConfigureAwait(false);
                        }
                        lock (m_disposalLock)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            m_lifetime.Token.ThrowIfCancellationRequested();
                            m_observationFaulted = false;
                            m_activated = true;
                            ready.SetResult(true);
                        }
                        bool more = await firstMove.ConfigureAwait(false);
                        while (more)
                        {
                            ProgramExecutionUpdate update = updates.Current;
                            ServiceResult result = await ApplyUpdateAsync(update, m_lifetime.Token)
                                .ConfigureAwait(false);
                            if (ServiceResult.IsBad(result))
                            {
                                m_logger.UpdateRejected(Node.NodeId, update.RunId, result.StatusCode);
                            }
                            more = await updates.MoveNextAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (!firstMove.IsCompleted)
                        {
                            // Startup cancellation must drain MoveNext before disposing an async iterator.
                            try
                            {
                                observationLifetime.Cancel();
                            }
                            finally
                            {
                                try
                                {
                                    await firstMove.ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (observationLifetime.IsCancellationRequested)
                                {
                                    // The original startup cancellation remains the reported outcome.
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (
                m_lifetime.IsCancellationRequested ||
                (!ready.Task.IsCompleted && cancellationToken.IsCancellationRequested))
            {
                ready.TrySetCanceled(m_lifetime.IsCancellationRequested ? m_lifetime.Token : cancellationToken);
            }
            catch (Exception exception)
            {
                m_observationFaulted = true;
                m_logger.ExecutionFailed(Node.NodeId, exception);
                if (ready.TrySetException(exception) && !m_activated)
                {
                    return;
                }
                if (!m_stopping && CurrentState != ProgramState.Halted)
                {
                    ServiceResult result = await ApplyUpdateAsync(
                        new ProgramExecutionUpdate(RunId, ProgramState.Halted,
                            new Variant(StatusCodes.BadUnexpectedError)),
                        m_lifetime.Token).ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        m_logger.UpdateRejected(Node.NodeId, RunId, result.StatusCode);
                    }
                }
            }
        }

        private async ValueTask RestartObservationAsync(CancellationToken cancellationToken)
        {
            Task previous;
            lock (m_disposalLock)
            {
                previous = m_observation;
            }
            await previous.ConfigureAwait(false);
            Task ready;
            lock (m_disposalLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_lifetime.Token.ThrowIfCancellationRequested();
                ready = StartObservation(cancellationToken);
            }
            await ready.ConfigureAwait(false);
        }

        private static ServiceResult ValidateOutputs(
            ISystemContext context, MethodState method, ArrayOf<Variant> outputs)
        {
            ArrayOf<Argument> declarations = method.OutputArguments?.Value ?? default;
            if (outputs.Count != declarations.Count)
            {
                return ServiceResult.Create(StatusCodes.BadUnexpectedError,
                    "The Program provider returned an incomplete output array.");
            }
            for (int index = 0; index < outputs.Count; index++)
            {
                Argument declaration = declarations[index];
                if (TypeInfo.IsInstanceOfDataType(outputs[index], declaration.DataType,
                    declaration.ValueRank, context.NamespaceUris, context.TypeTable).IsUnknown)
                {
                    return ServiceResult.Create(StatusCodes.BadTypeMismatch,
                        "A Program output does not match its declared data type.");
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Applies an internal stimulus. This also permits providers with an externally owned update loop.
        /// </summary>
        public async ValueTask<ServiceResult> ApplyUpdateAsync(
            ProgramExecutionUpdate update,
            CancellationToken cancellationToken = default)
        {
            if (update is null)
            {
                throw new ArgumentNullException(nameof(update));
            }
            if (!BeginOperation())
            {
                return StatusCodes.BadShutdown;
            }
            try
            {
                return await ApplyAdmittedUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        private async ValueTask<ServiceResult> ApplyAdmittedUpdateAsync(
            ProgramExecutionUpdate update,
            CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool terminated = false;
            try
            {
                bool startsRun = update.SubStateMachineId.IsNull &&
                    CurrentState == ProgramState.Ready && update.TargetState == ProgramState.Running;
                if (m_stopping || !m_activated)
                {
                    return StatusCodes.BadInvalidState;
                }
                if (startsRun && RunId == long.MaxValue)
                {
                    return StatusCodes.BadOutOfRange;
                }
                if (update.RunId != (startsRun ? RunId + 1 : RunId))
                {
                    return StatusCodes.BadInvalidState;
                }
                FiniteStateMachineState machine = Node;
                uint transitionId;
                if (update.SubStateMachineId.IsNull)
                {
                    if ((startsRun || update.TargetState == ProgramState.Ready) &&
                        !CanRestart(m_started, m_recycleCount, Definition.MaxRecycleCount))
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    try
                    {
                        transitionId = TransitionId(CurrentState, update.TargetState);
                    }
                    catch (ArgumentException)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                }
                else
                {
                    if (!m_started ||
                        FindDescendant(Node, update.SubStateMachineId) is not FiniteStateMachineState nested)
                    {
                        return StatusCodes.BadInvalidState;
                    }
                    machine = nested;
                    transitionId = update.SubStateTransitionId;
                }
                if (update.FinalResults.Count != 0 &&
                    (!ReferenceEquals(machine, Node) ||
                        CurrentState is not (ProgramState.Running or ProgramState.Suspended) ||
                        update.TargetState is not (ProgramState.Ready or ProgramState.Halted)))
                {
                    return StatusCodes.BadInvalidState;
                }
                ValidateFinalResults(update.FinalResults);
                ServiceResult result = machine.PrepareTransition(
                    m_context, transitionId, 0, default, [], out PreparedStateMachineTransition prepared);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                result = await CommitAsync(m_context, machine, prepared, update.IntermediateResult,
                    update.FinalResults, null, null, default, startsRun ? () => BeginRun(update.RunId) : null)
                    .ConfigureAwait(false);
                terminated = ServiceResult.IsGood(result) && ReferenceEquals(machine, Node) &&
                    update.TargetState == ProgramState.Halted && m_started;
                return result;
            }
            finally
            {
                m_gate.Release();
                if (terminated && Definition.AutoDelete)
                {
                    m_onTerminated!(this);
                }
            }
        }

        private NodeState? FindDescendant(NodeState parent, NodeId id)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(m_context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.NodeId == id)
                {
                    return child;
                }
                NodeState? found = FindDescendant(child, id);
                if (found is not null)
                {
                    return found;
                }
            }
            return null;
        }

        private void UpdateRecycleCount()
        {
            PublishRecycleCount(Required(Node.RecycleCount), m_recycleCount);
        }

        private void BeginRun(long runId)
        {
            Node.ResetSubStates();
            ClearFinalResults();
            if (m_started)
            {
                Interlocked.Increment(ref m_recycleCount);
            }
            m_started = true;
            Interlocked.Exchange(ref m_runId, runId);
            UpdateRecycleCount();
        }

        /// <summary>
        /// Tests the restart allowance independently of the Int32 representation of the published counter.
        /// </summary>
        internal static bool CanRestart(bool started, long recycleCount, uint maximum)
        {
            if (recycleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recycleCount));
            }
            return !started || maximum == uint.MaxValue || recycleCount < maximum;
        }

        /// <summary>
        /// Publishes an accurate count or explicit bad quality when its wire representation is exhausted.
        /// </summary>
        internal static void PublishRecycleCount(PropertyState<int> variable, long recycleCount)
        {
            if (variable is null)
            {
                throw new ArgumentNullException(nameof(variable));
            }
            if (recycleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(recycleCount));
            }
            variable.StatusCode = recycleCount > int.MaxValue
                ? StatusCodes.BadOutOfRange : StatusCodes.Good;
            if (recycleCount <= int.MaxValue)
            {
                variable.Value = (int)recycleCount;
            }
        }

        private static T Required<T>(T? value)
            where T : class
        {
            return value ?? throw new ServiceResultException(StatusCodes.BadConfigurationError,
                "A mandatory Program model element is missing.");
        }

        private static NodeId GetSessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private static bool TryTarget(
            ProgramState state, ProgramControlMethod control, out ProgramState target)
        {
            target = (state, control) switch
            {
                (ProgramState.Ready, ProgramControlMethod.Start) => ProgramState.Running,
                (ProgramState.Running, ProgramControlMethod.Suspend) => ProgramState.Suspended,
                (ProgramState.Suspended, ProgramControlMethod.Resume) => ProgramState.Running,
                (ProgramState.Halted, ProgramControlMethod.Reset) => ProgramState.Ready,
                (ProgramState.Ready or ProgramState.Running or ProgramState.Suspended,
                    ProgramControlMethod.Halt) => ProgramState.Halted,
                _ => default
            };
            return target != default;
        }

        private static uint StateNodeId(ProgramState state)
        {
            return NumericIdentifier(ProgramStateMachineModel.GetStateId(state));
        }

        private static uint TransitionId(ProgramState from, ProgramState to)
        {
            return NumericIdentifier(ProgramStateMachineModel.GetTransitionId(from, to));
        }

        private static uint CauseId(ProgramControlMethod control)
        {
            return NumericIdentifier(ProgramStateMachineModel.GetMethodId(control));
        }

        private static uint NumericIdentifier(NodeId nodeId)
        {
            if (!nodeId.TryGetValue(out uint identifier))
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    "A standard Program element must have a numeric identifier.");
            }
            return identifier;
        }

        private readonly SessionSystemContext m_context;
        private readonly TimeProvider m_timeProvider;
        private readonly Action<ProgramRuntime>? m_onTerminated;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly Lock m_disposalLock = new();
        private readonly TaskCompletionSource<bool> m_drained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<ProgramControlMethod, MethodState> m_methods = [];
        private IProgramExecution? m_execution;
        private Task m_observation = Task.CompletedTask;
        private Task? m_disposal;
        private int m_currentState;
        private long m_runId;
        private long m_recycleCount;
        private int m_operations;
        private volatile bool m_started;
        private volatile bool m_stopping;
        private volatile bool m_activated;
        private bool m_activationStarted;
        private volatile bool m_observationFaulted;
    }

    /// <summary>
    /// Diagnostics for supervised Program execution.
    /// </summary>
    internal static partial class ProgramRuntimeLog
    {
        [LoggerMessage(EventId = ServerEventIds.ProgramRuntime, Level = LogLevel.Error,
            Message = "Program {InvocationId} control {Control} failed.")]
        public static partial void ControlFailed(
            this ILogger logger, NodeId invocationId, ProgramControlMethod control, Exception exception);

        [LoggerMessage(EventId = ServerEventIds.ProgramRuntime + 1, Level = LogLevel.Warning,
            Message = "Program {InvocationId} rejected an update for run {RunId}: {StatusCode}.")]
        public static partial void UpdateRejected(
            this ILogger logger, NodeId invocationId, long runId, StatusCode statusCode);

        [LoggerMessage(EventId = ServerEventIds.ProgramRuntime + 2, Level = LogLevel.Error,
            Message = "Program {InvocationId} execution stream failed.")]
        public static partial void ExecutionFailed(this ILogger logger, NodeId invocationId, Exception exception);
    }
}
