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
using Opc.Ua.LADS.Server.StateMachines;
using Opc.Ua.Server;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The base of all function handles.
    /// </summary>
    internal abstract class LadsFunction : ILadsFunction, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Creates the handle of a function node.
        /// </summary>
        protected LadsFunction(LadsFunctionalUnit unit, FunctionState node, string path)
        {
            Unit = unit;
            Node = node;
            Path = path;
            Runtime.Update(node.IsEnabled, Variant.From(true));
        }

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <summary>
        /// Gets the path of the function relative to the function set of its functional unit.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        public ILadsFunctionalUnit FunctionalUnit => Unit;

        /// <summary>
        /// Gets the functional unit.
        /// </summary>
        public LadsFunctionalUnit Unit { get; }

        /// <summary>
        /// Gets the node of the function.
        /// </summary>
        public FunctionState Node { get; }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime => Unit.Runtime;

        /// <inheritdoc/>
        public bool IsEnabled
        {
            get => Volatile.Read(ref m_enabled);
            set
            {
                Unit.Device.SetFunctionEnabled(this, value);
                Node.IsEnabled?.ClearChangeMasks(Runtime.Context, false);
                (this as LadsControlFunction)?.UpdateRunningExecutability();
            }
        }

        /// <summary>
        /// Assigns the enabled value without dispatching notifications.
        /// </summary>
        internal void AssignEnabled(bool enabled)
        {
            Volatile.Write(ref m_enabled, enabled);
            LadsRuntime.AssignValue(Node.IsEnabled, Variant.From(enabled));
        }

        /// <summary>
        /// Removes the unbound method copies from the Operational FunctionalGroup of the function.
        /// </summary>
        /// <remarks>
        /// The NodeSet models the Operational methods as separate declarations of the group, so
        /// the generated factories create copies that cannot be called. This runs before the
        /// device is registered; <see cref="OrganizeOperational"/> later organizes the bound nodes.
        /// </remarks>
        internal void RemoveUnboundOperationalMethods()
        {
            ISystemContext context = Runtime.Context;
            if (Node.FindChild(context, new QualifiedName("Operational", Runtime.LadsNamespaceIndex))
                is not BaseObjectState operational)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            operational.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState)
                {
                    ReleaseNodeIdReservations(context, child);
                    operational.RemoveChild(child);
                }
            }
        }

        /// <summary>
        /// Releases the NodeIds the factory reserved for a removed node and its children.
        /// </summary>
        private static void ReleaseNodeIdReservations(ISystemContext context, NodeState node)
        {
            if (context.NodeIdFactory is not ICompanionNodeIdReservationOwner owner)
            {
                return;
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                ReleaseNodeIdReservations(context, child);
            }
            owner.ReleaseNodeIdReservation(node);
        }

        /// <summary>
        /// Completes the Operational FunctionalGroup of the function.
        /// </summary>
        /// <remarks>
        /// The specification requires the Operational group of a control or cover function to
        /// organize the CurrentState of its state machine, all of the state machine's remote
        /// invocable methods, and the CurrentValue and TargetValue variables. Members already
        /// organized by the generated type retain their inverse references. This runs after the
        /// device is registered, when the nodes have their NodeIds.
        /// </remarks>
        internal void OrganizeOperational()
        {
            ISystemContext context = Runtime.Context;
            if (Node.FindChild(context, new QualifiedName("Operational", Runtime.LadsNamespaceIndex))
                is not BaseObjectState operational)
            {
                return;
            }
            NodeState? stateMachine = Node switch
            {
                BaseControlFunctionState control => control.ControlFunctionState,
                CoverFunctionState cover => cover.CoverState,
                _ => null
            };
            var children = new List<BaseInstanceState>();
            var members = new List<BaseInstanceState>();
            if (stateMachine is FiniteStateMachineState { CurrentState: { } currentState })
            {
                members.Add(currentState);
            }
            if (stateMachine != null)
            {
                stateMachine.GetChildren(context, children);
                members.AddRange(children.OfType<MethodState>());
                foreach (RunningStateMachineState runningStateMachine in
                    children.OfType<RunningStateMachineState>().ToArray())
                {
                    children.Clear();
                    runningStateMachine.GetChildren(context, children);
                    members.AddRange(children.OfType<MethodState>());
                }
            }
            children.Clear();
            Node.GetChildren(context, children);
            members.AddRange(children.Where(child =>
                operational.ReferenceExists(ReferenceTypeIds.Organizes, false, child.NodeId) ||
                (child is BaseVariableState &&
                    child.BrowseName.NamespaceIndex == Runtime.LadsNamespaceIndex &&
                    child.BrowseName.Name is "CurrentValue" or "TargetValue")));
            foreach (BaseInstanceState member in members)
            {
                if (!operational.ReferenceExists(ReferenceTypeIds.Organizes, false, member.NodeId))
                {
                    operational.AddReference(ReferenceTypeIds.Organizes, false, member.NodeId);
                }
                if (!member.ReferenceExists(ReferenceTypeIds.Organizes, true, operational.NodeId))
                {
                    member.AddReference(ReferenceTypeIds.Organizes, true, operational.NodeId);
                }
            }
        }

        /// <summary>
        /// Converts a numeric value to a double.
        /// </summary>
        protected static double ToDouble(Variant value)
        {
            if (value.TryGetValue(out double number))
            {
                return number;
            }
            if (value.TryGetValue(out float single))
            {
                return single;
            }
            if (value.TryGetValue(out int signed))
            {
                return signed;
            }
            if (value.TryGetValue(out uint unsigned))
            {
                return unsigned;
            }
            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A numeric value is expected.");
        }

        /// <summary>
        /// Rejects a non-finite value or a value outside of the range.
        /// </summary>
        protected static void CheckRange(Range? range, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "The value must be finite.");
            }
            if (range != null && (value < range.Low || value > range.High))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "The value {0} is outside of the range [{1}, {2}].",
                    value,
                    range.Low,
                    range.High);
            }
        }

        /// <summary>
        /// Configures the engineering units and the range of an analog variable.
        /// </summary>
        protected static void ConfigureAnalog(BaseVariableState? variable, EUInformation? units, Range? range)
        {
            if (variable is not BaseAnalogState analog)
            {
                return;
            }
            if (units != null && analog.EngineeringUnits != null)
            {
                analog.EngineeringUnits.Value = units;
            }
            if (range != null && analog.EURange != null)
            {
                analog.EURange.Value = range;
            }
        }

        /// <summary>
        /// Lets the function provider accept or reject a command.
        /// </summary>
        protected async ValueTask InvokeProviderAsync(
            LadsFunctionCommandKind kind,
            Variant value,
            CancellationToken cancellationToken)
        {
            ServiceResult result = await Unit.FunctionProvider
                .OnCommandAsync(new LadsFunctionCommand(this, kind, value), cancellationToken)
                .ConfigureAwait(false);
            Unit.Device.RequireStorageHealthy();
            if (ServiceResult.IsBad(result))
            {
                Runtime.Logger.CommandRejected(Path, kind.ToString(), result.StatusCode);
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Runs a command exclusively.
        /// </summary>
        protected async ValueTask ExclusiveAsync(
            Func<ValueTask> command, CancellationToken cancellationToken, bool releasing = false)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!releasing)
                {
                    Unit.Device.RequireStorageHealthy();
                }
                await command().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Rejects a command while the function is disabled.
        /// </summary>
        protected void RequireEnabled()
        {
            if (!IsEnabled)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The function '{0}' is disabled.",
                    Path);
            }
        }

        /// <summary>
        /// Releases the command gate.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        /// <summary>
        /// Releases the function after any specialized asynchronous cleanup.
        /// </summary>
        public virtual ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private bool m_enabled = true;
    }

    /// <summary>
    /// The base of the control functions with a control function state machine.
    /// </summary>
    internal abstract class LadsControlFunction : LadsFunction, ILadsControlFunction, ILadsRunningOperations
    {
        /// <summary>
        /// Creates the handle and binds the methods of the control function state machine.
        /// </summary>
        protected LadsControlFunction(
            LadsFunctionalUnit unit,
            BaseControlFunctionState node,
            string path,
            bool enableRunningOperations = false)
            : base(unit, node, path)
        {
            ISystemContext context = Runtime.Context;
            ControlFunctionStateMachineState stateMachine = node.ControlFunctionState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The control function state machine is missing.");
            stateMachine.AddStart(context).AddStartWithTargetValue(context);
            stateMachine.AddStop(context).AddAbort(context).AddClear(context);
            m_machine = new LadsStateMachine<LadsFunctionalStateValue>(
                stateMachine,
                context,
                LadsStateMaps.Functional,
                LadsFunctionalStateValue.Stopped,
                Unit.Device);
            m_machine.StateChanged += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Path, state);
                }
                StateChanged?.Invoke(state);
            };
            Runtime.Bind(stateMachine.Start, (_, ct) => StartAsync(ct));
            Runtime.Bind(stateMachine.Stop, (_, ct) => StopAsync(ct));
            Runtime.Bind(stateMachine.Abort, (_, ct) => AbortAsync(ct));
            Runtime.Bind(stateMachine.Clear, (_, ct) => ClearAsync(ct));
            Runtime.Bind(
                stateMachine.StartWithTargetValue,
                (inputs, ct) => StartWithTargetVariantAsync(inputs.Count > 0 ? inputs[0] : default, ct));
            if (enableRunningOperations)
            {
                stateMachine.AddRunningStateMachine(context);
                RunningStateMachineState runningNode = stateMachine.RunningStateMachine!;
                runningNode.AddHold(context).AddUnhold(context)
                    .AddSuspend(context).AddUnsuspend(context)
                    .AddReset(context).AddToComplete(context);
                m_running = new LadsStateMachine<LadsRunningStateValue>(
                    runningNode,
                    context,
                    LadsStateMaps.Running,
                    LadsRunningStateValue.Idle,
                    Unit.Device);
                m_running.StateChanged += _ => UpdateRunningExecutability();
                Runtime.Bind(runningNode.Hold, (_, ct) => HoldAsync(ct));
                Runtime.Bind(runningNode.Unhold, (_, ct) => UnholdAsync(ct));
                Runtime.Bind(runningNode.Suspend, (_, ct) => SuspendAsync(ct));
                Runtime.Bind(runningNode.Unsuspend, (_, ct) => UnsuspendAsync(ct));
                Runtime.Bind(runningNode.Reset, (_, ct) => ResetAsync(ct));
                Runtime.Bind(runningNode.ToComplete, (_, ct) => ToCompleteAsync(ct));
                UpdateRunningExecutability();
            }
            m_machine.StateChanged += _ => UpdateRunningExecutability();
        }

        /// <inheritdoc/>
        public LadsFunctionalStateValue State => m_machine.Current;

        /// <inheritdoc/>
        public event Action<LadsFunctionalStateValue>? StateChanged;

        /// <summary>
        /// Gets the optional running operations.
        /// </summary>
        public ILadsRunningOperations? RunningOperations => m_running == null ? null : this;

        /// <summary>
        /// Gets the optional running substate.
        /// </summary>
        public LadsRunningStateValue? RunningState => m_running?.Current;

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            Task drained = Task.CompletedTask;
            try
            {
                await ExclusiveAsync(
                    () =>
                    {
                        lock (m_runningLock)
                        {
                            m_runningDisposed = true;
                            drained = m_runningIdle?.Task ?? Task.CompletedTask;
                            m_runningCancellation?.Cancel();
                        }
                        return default;
                    },
                    CancellationToken.None,
                    releasing: true).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            try
            {
                await drained.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
            finally
            {
                m_runningCancellation?.Dispose();
                m_runningGate.Dispose();
                await base.DisposeAsync().ConfigureAwait(false);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("The LADS control function cleanup failed.", failures);
            }
        }

        /// <inheritdoc/>
        public ValueTask HoldAsync(CancellationToken cancellationToken = default)
        {
            return ChangeRunningAsync(
                LadsRunningStateValue.Holding,
                LadsRunningStateValue.Held,
                LadsRunningStateValue.Held,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask UnholdAsync(CancellationToken cancellationToken = default)
        {
            return ChangeRunningAsync(
                LadsRunningStateValue.Unholding,
                LadsRunningStateValue.Execute,
                LadsRunningStateValue.Execute,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SuspendAsync(CancellationToken cancellationToken = default)
        {
            return ChangeRunningAsync(
                LadsRunningStateValue.Suspending,
                LadsRunningStateValue.Suspended,
                LadsRunningStateValue.Suspended,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask UnsuspendAsync(CancellationToken cancellationToken = default)
        {
            return ChangeRunningAsync(
                LadsRunningStateValue.Unsuspending,
                LadsRunningStateValue.Execute,
                LadsRunningStateValue.Execute,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    lock (m_runningLock)
                    {
                        if (m_runningDisposed)
                        {
                            throw new ObjectDisposedException(nameof(LadsControlFunction));
                        }
                    }
                    RequireEnabled();
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    LadsStateMachine<LadsRunningStateValue> running = RequireRunningStateMachine();
                    running.Require(LadsRunningStateValue.Complete);
                    await TransitionRunningAsync(
                        cancellationToken,
                        LadsRunningStateValue.Resetting,
                        LadsRunningStateValue.Idle).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask ToCompleteAsync(CancellationToken cancellationToken = default)
        {
            await ChangeRunningAsync(
                LadsRunningStateValue.Completing,
                LadsRunningStateValue.Complete,
                LadsRunningStateValue.Completing,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Recomputes the executability of optional running methods.
        /// </summary>
        internal void UpdateRunningExecutability()
        {
            if (m_running == null)
            {
                return;
            }
            RunningStateMachineState node = ((BaseControlFunctionState)Node).ControlFunctionState!
                .RunningStateMachine!;
            bool running = State == LadsFunctionalStateValue.Running;
            bool providerAvailable = IsEnabled && Unit.FunctionProvider is ILadsControlFunctionRunningProvider;
            if (node.CurrentState != null)
            {
                node.CurrentState.StatusCode = running ? StatusCodes.Good : StatusCodes.BadStateNotActive;
                if (node.CurrentState.Id != null)
                {
                    node.CurrentState.Id.StatusCode = node.CurrentState.StatusCode;
                }
                node.CurrentState.ClearChangeMasks(Runtime.Context, true);
            }
            Set(node.Hold, running && providerAvailable && m_running.CanTransitionTo(LadsRunningStateValue.Holding));
            Set(node.Unhold,
                running && providerAvailable && m_running.CanTransitionTo(LadsRunningStateValue.Unholding));
            Set(node.Suspend,
                running && providerAvailable && m_running.CanTransitionTo(LadsRunningStateValue.Suspending));
            Set(node.Unsuspend,
                running && providerAvailable && m_running.CanTransitionTo(LadsRunningStateValue.Unsuspending));
            Set(
                node.ToComplete,
                running && providerAvailable && m_running.CanTransitionTo(LadsRunningStateValue.Completing));
            Set(node.Reset, running && IsEnabled && m_running.Current == LadsRunningStateValue.Complete);

            void Set(MethodState? method, bool executable)
            {
                if (method != null)
                {
                    method.Executable = executable;
                    method.UserExecutable = executable;
                    method.ClearChangeMasks(Runtime.Context, false);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    RequireEnabled();
                    RequireStartable();
                    await InvokeProviderAsync(LadsFunctionCommandKind.Start, default, cancellationToken)
                        .ConfigureAwait(false);
                    if (State == LadsFunctionalStateValue.Stopped)
                    {
                        await TransitionAsync(cancellationToken, LadsFunctionalStateValue.Running)
                            .ConfigureAwait(false);
                    }
                    if (m_running != null)
                    {
                        await TransitionRunningAsync(
                            cancellationToken,
                            LadsRunningStateValue.Starting,
                            LadsRunningStateValue.Execute).ConfigureAwait(false);
                    }
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            CancelRunningCommand();
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Stop, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Stopping,
                        LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                    m_running?.SetState(LadsRunningStateValue.Idle);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            CancelRunningCommand();
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Abort, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Aborting,
                        LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
                    m_running?.SetState(LadsRunningStateValue.Idle);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Aborted);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Clear, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Clearing,
                        LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <summary>
        /// Validates a target value and converts it to the data type of the function.
        /// </summary>
        protected abstract Variant NormalizeTarget(Variant value);

        /// <summary>
        /// Writes an accepted target value.
        /// </summary>
        protected void ApplyTarget(Variant value)
        {
            Unit.Device.SetControlTarget(this, value);
            Node.ClearChangeMasks(Runtime.Context, true);
        }

        /// <summary>
        /// Sets the target value after the provider accepted it.
        /// </summary>
        protected ValueTask SetTargetCoreAsync(Variant value, CancellationToken cancellationToken)
        {
            return ExclusiveAsync(
                async () =>
                {
                    Variant target = NormalizeTarget(value);
                    await InvokeProviderAsync(LadsFunctionCommandKind.SetTargetValue, target, cancellationToken)
                        .ConfigureAwait(false);
                    ApplyTarget(target);
                },
                cancellationToken);
        }

        /// <summary>
        /// Sets the target value and starts the function.
        /// </summary>
        protected ValueTask StartWithTargetVariantAsync(Variant value, CancellationToken cancellationToken)
        {
            return ExclusiveAsync(
                async () =>
                {
                    RequireEnabled();
                    Variant target = NormalizeTarget(value);
                    RequireStartable();
                    await InvokeProviderAsync(
                        LadsFunctionCommandKind.StartWithTargetValue,
                        target,
                        cancellationToken).ConfigureAwait(false);
                    ApplyTarget(target);
                    if (State == LadsFunctionalStateValue.Stopped)
                    {
                        await TransitionAsync(cancellationToken, LadsFunctionalStateValue.Running)
                            .ConfigureAwait(false);
                    }
                    if (m_running != null)
                    {
                        await TransitionRunningAsync(
                            cancellationToken,
                            LadsRunningStateValue.Starting,
                            LadsRunningStateValue.Execute).ConfigureAwait(false);
                    }
                },
                cancellationToken);
        }

        /// <summary>
        /// Applies transitions and rejects a vetoed transition.
        /// </summary>
        private async ValueTask TransitionAsync(
            CancellationToken cancellationToken,
            params LadsFunctionalStateValue[] states)
        {
            if (!await m_machine.TransitionThroughAsync(cancellationToken, states).ConfigureAwait(false))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A transition of the function '{0}' was vetoed.",
                    Path);
            }
        }

        /// <summary>
        /// Applies an acknowledged running-state operation.
        /// </summary>
        private async ValueTask ChangeRunningAsync(
            LadsRunningStateValue acting,
            LadsRunningStateValue stable,
            LadsRunningStateValue providerTarget,
            CancellationToken cancellationToken)
        {
            CancellationTokenSource? command = null;
            ILadsControlFunctionRunningProvider? provider = null;
            await ExclusiveAsync(
                async () =>
                {
                    lock (m_runningLock)
                    {
                        if (m_runningDisposed)
                        {
                            throw new ObjectDisposedException(nameof(LadsControlFunction));
                        }
                    }
                    RequireEnabled();
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    LadsStateMachine<LadsRunningStateValue> running = RequireRunningStateMachine();
                    if (Unit.FunctionProvider is not ILadsControlFunctionRunningProvider available)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotSupported,
                            "Running operations are not supported by the function provider.");
                    }
                    provider = available;
                    await TransitionRunningAsync(cancellationToken, acting).ConfigureAwait(false);
                    lock (m_runningLock)
                    {
                        m_runningCancellation?.Cancel();
                        command = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, Runtime.Stopping);
                        m_runningCancellation = command;
                        if (m_pendingRunningCommands++ == 0)
                        {
                            m_runningIdle = new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);
            try
            {
                await m_runningGate.WaitAsync(command!.Token).ConfigureAwait(false);
                ServiceResult acknowledgment;
                try
                {
                    acknowledgment = await provider!.ChangeExecutionAsync(this, providerTarget, command.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_runningGate.Release();
                }
                if (ServiceResult.IsBad(acknowledgment))
                {
                    throw new ServiceResultException(acknowledgment);
                }
                await ExclusiveAsync(
                    async () =>
                    {
                        command.Token.ThrowIfCancellationRequested();
                        m_machine.Require(LadsFunctionalStateValue.Running);
                        RequireRunningStateMachine().Require(acting);
                        if (acting != stable)
                        {
                            await TransitionRunningAsync(CancellationToken.None, stable).ConfigureAwait(false);
                        }
                        UpdateRunningExecutability();
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lock (m_runningLock)
                {
                    if (ReferenceEquals(m_runningCancellation, command))
                    {
                        m_runningCancellation = null;
                    }
                    command!.Dispose();
                    if (--m_pendingRunningCommands == 0)
                    {
                        m_runningIdle!.TrySetResult(true);
                    }
                }
            }
        }

        /// <summary>
        /// Cancels a pending physical operation before Stop or Abort takes the command gate.
        /// </summary>
        private void CancelRunningCommand()
        {
            lock (m_runningLock)
            {
                m_runningCancellation?.Cancel();
            }
        }

        /// <summary>
        /// Permits a first start or a new process after Complete-to-Reset.
        /// </summary>
        private void RequireStartable()
        {
            if (m_running != null && State == LadsFunctionalStateValue.Running)
            {
                m_running.Require(LadsRunningStateValue.Idle);
                return;
            }
            m_machine.Require(LadsFunctionalStateValue.Stopped);
        }

        /// <summary>
        /// Gets the running state machine or rejects an operation when it was not configured.
        /// </summary>
        private LadsStateMachine<LadsRunningStateValue> RequireRunningStateMachine()
        {
            return m_running ?? throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "Running operations are not configured for the function '{0}'.",
                Path);
        }

        /// <summary>
        /// Applies running-state transitions and rejects a vetoed transition.
        /// </summary>
        private async ValueTask TransitionRunningAsync(
            CancellationToken cancellationToken,
            params LadsRunningStateValue[] states)
        {
            LadsStateMachine<LadsRunningStateValue> running = RequireRunningStateMachine();
            if (!await running.TransitionThroughAsync(cancellationToken, states).ConfigureAwait(false))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A running-state transition of the function '{0}' was vetoed.",
                    Path);
            }
        }

        private readonly LadsStateMachine<LadsFunctionalStateValue> m_machine;
        private readonly LadsStateMachine<LadsRunningStateValue>? m_running;
        private readonly Lock m_runningLock = new();
        private readonly SemaphoreSlim m_runningGate = new(1, 1);
        private CancellationTokenSource? m_runningCancellation;
        private TaskCompletionSource<bool>? m_runningIdle;
        private int m_pendingRunningCommands;
        private bool m_runningDisposed;
    }

    /// <summary>
    /// The handle of an analog control function and its totalizer, relative and composed variants.
    /// </summary>
    internal sealed class LadsAnalogControlFunction : LadsControlFunction, IAnalogControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsAnalogControlFunction(
            LadsFunctionalUnit unit,
            AnalogControlFunctionState node,
            string path,
            LadsAnalogControlOptions options)
            : base(unit, node, path, options.EnableRunningOperations)
        {
            m_node = node;
            m_options = options;
            Kind = options.Kind;
            ServerSystemContext context = Runtime.Context;
            ConfigureAnalog(node.CurrentValue, options.EngineeringUnits, options.Range);
            ConfigureAnalog(node.TargetValue, options.EngineeringUnits, options.Range);
            m_current = options.InitialCurrentValue;
            m_target = options.InitialTargetValue;
            Runtime.Update(node.CurrentValue, Variant.From(m_current));
            Runtime.Update(node.TargetValue, Variant.From(m_target));
            m_lastUpdate = Runtime.TimeProvider.GetTimestamp();
            switch (node)
            {
                case AnalogControlFunctionWithTotalizerState totalizer:
                    totalizer.AddResetTotalizer(context);
                    ConfigureAnalog(totalizer.TotalizedValue, options.EngineeringUnits, null);
                    Runtime.Update(totalizer.TotalizedValue, Variant.From(0.0));
                    Runtime.Bind(totalizer.ResetTotalizer, (_, ct) => ResetTotalizerAsync(ct));
                    break;
                case AnalogControlFunctionWithRelativeTargetValueState relative:
                    relative.AddModifyTargetValueBy(context);
                    if (options.IncreaseRate.HasValue)
                    {
                        relative.AddIncreaseRate(context);
                        ConfigureAnalog(relative.IncreaseRate, options.EngineeringUnits, null);
                        Runtime.Update(relative.IncreaseRate, Variant.From(options.IncreaseRate.Value));
                    }
                    if (options.DecreaseRate.HasValue)
                    {
                        relative.AddDecreaseRate(context);
                        ConfigureAnalog(relative.DecreaseRate, options.EngineeringUnits, null);
                        Runtime.Update(relative.DecreaseRate, Variant.From(options.DecreaseRate.Value));
                    }
                    Runtime.Bind(
                        relative.ModifyTargetValueBy,
                        (inputs, ct) => ModifyTargetValueByAsync(
                            ToDouble(inputs.Count > 0 ? inputs[0] : default),
                            ct));
                    break;
                case AnalogControlFunctionWithComposedTargetValueState composed:
                    NodeState set = composed.TargetValueSet ??
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "The target value set is missing.");
                    foreach (string name in options.ComposedTargetValues)
                    {
                        BaseDataVariableState<double> variable =
                            BaseDataVariableState<double>.With<VariantBuilder>(set);
                        variable.SymbolicName = name;
                        variable.BrowseName = Runtime.InstanceName(name);
                        variable.DisplayName = new LocalizedText(name);
                        variable.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                        variable.TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType;
                        variable.DataType = global::Opc.Ua.DataTypeIds.Double;
                        variable.ValueRank = ValueRanks.Scalar;
                        variable.AccessLevel = AccessLevels.CurrentRead;
                        variable.UserAccessLevel = AccessLevels.CurrentRead;
                        variable.Value = 0.0;
                        variable.NodeId = context.NodeIdFactory!.New(context, variable);
                        set.AddChild(variable);
                    }
                    break;
            }
        }

        /// <inheritdoc/>
        public LadsAnalogControlKind Kind { get; }

        /// <inheritdoc/>
        public double CurrentValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
        }

        /// <inheritdoc/>
        public double TargetValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target;
                }
            }
        }

        /// <inheritdoc/>
        public double TotalizedValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_total;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(double value)
        {
            Unit.Device.UpdateControlReading(
                this, value, Runtime.TimeProvider.GetTimestamp(), Runtime.TimeProvider.TimestampFrequency);
            Node.ClearChangeMasks(Runtime.Context, true);
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ModifyTargetValueByAsync(double delta, CancellationToken cancellationToken = default)
        {
            RequireKind(LadsAnalogControlKind.Relative);
            return ExclusiveAsync(
                async () =>
                {
                    double target = TargetValue + delta;
                    CheckRange(m_options.Range, target);
                    await InvokeProviderAsync(
                        LadsFunctionCommandKind.ModifyTargetValueBy,
                        Variant.From(delta),
                        cancellationToken).ConfigureAwait(false);
                    ApplyTarget(Variant.From(target));
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ResetTotalizerAsync(CancellationToken cancellationToken = default)
        {
            RequireKind(LadsAnalogControlKind.Totalizer);
            return ExclusiveAsync(
                async () =>
                {
                    await InvokeProviderAsync(LadsFunctionCommandKind.ResetTotalizer, default, cancellationToken)
                        .ConfigureAwait(false);
                    Unit.Device.ResetTotalizer(this, Runtime.TimeProvider.GetTimestamp());
                    var totalizer = (AnalogControlFunctionWithTotalizerState)m_node;
                    totalizer.TotalizedValue?.ClearChangeMasks(Runtime.Context, false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public void SetComposedTargetValue(string name, double value)
        {
            Unit.Device.RequireStorageHealthy();
            if (m_node is not AnalogControlFunctionWithComposedTargetValueState composed ||
                composed.TargetValueSet == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The function '{0}' has no composed target value.",
                    Path);
            }
            BaseVariableState variable = Runtime.FindChild<BaseVariableState>(composed.TargetValueSet, name) ??
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "No target value '{0}'.", name);
            Unit.Device.SetFunctionParameter(variable, Variant.From(value));
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <summary>
        /// Assigns a normalized target without dispatching notifications.
        /// </summary>
        internal void AssignTarget(Variant value)
        {
            double target = ToDouble(value);
            lock (m_lock)
            {
                m_target = target;
            }
            LadsRuntime.AssignValue(m_node.TargetValue, Variant.From(target));
        }

        /// <summary>
        /// Assigns the current reading and integrated total without invoking the clock provider or observers.
        /// </summary>
        internal void AssignReading(double value, long timestamp, long frequency)
        {
            if (frequency <= 0)
            {
                throw new InvalidOperationException("The LADS clock timestamp frequency must be positive.");
            }
            double total;
            lock (m_lock)
            {
                timestamp = Math.Max(timestamp, m_lastUpdate);
                if (State == LadsFunctionalStateValue.Running)
                {
                    m_total += m_current * ((double)(timestamp - m_lastUpdate) / frequency);
                }
                m_lastUpdate = timestamp;
                m_current = value;
                total = m_total;
            }
            LadsRuntime.AssignValue(m_node.CurrentValue, Variant.From(value));
            if (m_node is AnalogControlFunctionWithTotalizerState totalizer)
            {
                LadsRuntime.AssignValue(totalizer.TotalizedValue, Variant.From(total));
            }
        }

        /// <summary>
        /// Resets the total and its clock sample without dispatching notifications.
        /// </summary>
        internal void AssignTotalizerReset(long timestamp)
        {
            lock (m_lock)
            {
                m_total = 0;
                m_lastUpdate = Math.Max(timestamp, m_lastUpdate);
            }
            LadsRuntime.AssignValue(
                ((AnalogControlFunctionWithTotalizerState)m_node).TotalizedValue, Variant.From(0.0));
        }

        /// <summary>
        /// Rejects a command the variant of the function does not support.
        /// </summary>
        private void RequireKind(LadsAnalogControlKind kind)
        {
            if (Kind != kind)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The function '{0}' is not a {1} analog control function.",
                    Path,
                    kind);
            }
        }

        private readonly AnalogControlFunctionState m_node;
        private readonly LadsAnalogControlOptions m_options;
        private readonly Lock m_lock = new();
        private double m_current;
        private double m_target;
        private double m_total;
        private long m_lastUpdate;
    }

    /// <summary>
    /// The handle of a multi-mode analog control function.
    /// </summary>
    internal sealed class LadsMultiModeAnalogControlFunction : LadsControlFunction, IMultiModeAnalogControlFunction
    {
        /// <summary>
        /// Creates the handle and the controller parameter of every mode.
        /// </summary>
        public LadsMultiModeAnalogControlFunction(
            LadsFunctionalUnit unit,
            MultiModeAnalogControlFunctionState node,
            string path,
            LadsMultiModeAnalogControlOptions options)
            : base(unit, node, path, options.EnableRunningOperations)
        {
            if (options.Modes.Count == 0)
            {
                throw new ArgumentException(
                    "A multi-mode analog control function needs at least one mode.",
                    nameof(options));
            }
            if (options.InitialMode >= options.Modes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The initial mode is not one of the modes.");
            }
            m_node = node;
            m_options = options;
            Modes = options.Modes;
            m_mode = options.InitialMode;
            ISystemContext context = Runtime.Context;
            var texts = new LocalizedText[options.Modes.Count];
            for (int ii = 0; ii < texts.Length; ii++)
            {
                texts[ii] = new LocalizedText(options.Modes[ii]);
            }
            if (node.CurrentMode != null)
            {
                Runtime.Update(node.CurrentMode.EnumStrings, Variant.From((ArrayOf<LocalizedText>)texts));
                Runtime.Update(node.CurrentMode, Variant.From(m_mode));
            }
            m_parameters = new ControllerParameterState[options.Modes.Count];
            NodeState set = node.ControllerModeSet ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The controller mode set is missing.");
            for (int ii = 0; ii < m_parameters.Length; ii++)
            {
                ControllerParameterState parameter = LadsRuntime.Named(
                    context.CreateInstanceOfControllerParameterType(set, Runtime.InstanceName(options.Modes[ii])),
                    global::Opc.Ua.ReferenceTypeIds.HasComponent);
                ConfigureAnalog(parameter.CurrentValue, options.EngineeringUnits, options.Range);
                ConfigureAnalog(parameter.TargetValue, options.EngineeringUnits, options.Range);
                Runtime.Update(parameter.CurrentValue, Variant.From(0.0));
                Runtime.Update(parameter.TargetValue, Variant.From(0.0));
                set.AddChild(parameter);
                m_parameters[ii] = parameter;
            }
        }

        /// <inheritdoc/>
        public uint CurrentMode => Volatile.Read(ref m_mode);

        /// <inheritdoc/>
        public ArrayOf<string> Modes { get; }

        /// <inheritdoc/>
        public ValueTask SetModeAsync(uint mode, CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    if (mode >= m_parameters.Length)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadOutOfRange,
                            "The mode {0} is unknown.",
                            mode);
                    }
                    await InvokeProviderAsync(LadsFunctionCommandKind.SetMode, Variant.From(mode), cancellationToken)
                        .ConfigureAwait(false);
                    Unit.Device.SetControlMode(this, mode);
                    m_node.CurrentMode?.ClearChangeMasks(Runtime.Context, false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public double GetTargetValue(string mode)
        {
            BaseVariableState? variable = Find(mode).TargetValue;
            return variable != null && variable.Value.TryGetValue(out double value) ? value : 0;
        }

        /// <inheritdoc/>
        public void SetTargetValue(string mode, double value)
        {
            Unit.Device.RequireStorageHealthy();
            CheckRange(m_options.Range, value);
            Unit.Device.SetFunctionParameter(Find(mode).TargetValue, Variant.From(value));
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(string mode, double value)
        {
            Unit.Device.SetFunctionParameter(Find(mode).CurrentValue, Variant.From(value));
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <summary>
        /// Assigns the target of the selected mode without dispatching notifications.
        /// </summary>
        internal void AssignTarget(Variant value)
        {
            LadsRuntime.AssignValue(m_parameters[CurrentMode].TargetValue, Variant.From(ToDouble(value)));
        }

        /// <summary>
        /// Assigns the selected mode without dispatching notifications.
        /// </summary>
        internal void AssignMode(uint mode)
        {
            Volatile.Write(ref m_mode, mode);
            LadsRuntime.AssignValue(m_node.CurrentMode, Variant.From(mode));
        }

        /// <summary>
        /// Finds the controller parameter of a mode.
        /// </summary>
        private ControllerParameterState Find(string mode)
        {
            for (int ii = 0; ii < m_parameters.Length; ii++)
            {
                if (string.Equals(Modes[ii], mode, StringComparison.Ordinal))
                {
                    return m_parameters[ii];
                }
            }
            throw ServiceResultException.Create(StatusCodes.BadNotFound, "The mode '{0}' is unknown.", mode);
        }

        private readonly MultiModeAnalogControlFunctionState m_node;
        private readonly LadsMultiModeAnalogControlOptions m_options;
        private readonly ControllerParameterState[] m_parameters;
        private uint m_mode;
    }

    /// <summary>
    /// The handle of a two-state discrete control function.
    /// </summary>
    internal sealed class LadsTwoStateDiscreteControlFunction : LadsControlFunction,
        ITwoStateDiscreteControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsTwoStateDiscreteControlFunction(
            LadsFunctionalUnit unit,
            TwoStateDiscreteControlFunctionState node,
            string path,
            LadsTwoStateDiscreteOptions options)
            : base(unit, node, path, options.EnableRunningOperations)
        {
            m_current = Configure(node.CurrentValue, options);
            m_target = Configure(node.TargetValue, options);
            m_currentValue = options.InitialValue;
            m_targetValue = options.InitialValue;
            Runtime.Update(m_current, Variant.From(m_currentValue));
            Runtime.Update(m_target, Variant.From(m_targetValue));
        }

        /// <inheritdoc/>
        public bool CurrentValue => Volatile.Read(ref m_currentValue);

        /// <inheritdoc/>
        public bool TargetValue => Volatile.Read(ref m_targetValue);

        /// <inheritdoc/>
        public void UpdateCurrentValue(bool value)
        {
            Unit.Device.UpdateControlReading(this, value);
            m_current?.ClearChangeMasks(Runtime.Context, false);
        }

        /// <summary>
        /// Assigns the current reading without dispatching notifications.
        /// </summary>
        internal void AssignReading(bool value)
        {
            Volatile.Write(ref m_currentValue, value);
            LadsRuntime.AssignValue(m_current, Variant.From(value));
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(bool value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(bool value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <summary>
        /// Sets the texts of the states of a two-state variable.
        /// </summary>
        internal static TwoStateDiscreteState? Configure(
            BaseVariableState? variable,
            LadsTwoStateDiscreteOptions options)
        {
            if (variable is not TwoStateDiscreteState discrete)
            {
                return null;
            }
            if (discrete.TrueState != null)
            {
                discrete.TrueState.Value = options.TrueState;
            }
            if (discrete.FalseState != null)
            {
                discrete.FalseState.Value = options.FalseState;
            }
            return discrete;
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            if (value.TryGetValue(out bool flag))
            {
                return Variant.From(flag);
            }
            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A Boolean value is expected.");
        }

        /// <summary>
        /// Assigns the normalized target without dispatching notifications.
        /// </summary>
        internal void AssignTarget(Variant value)
        {
            value.TryGetValue(out bool flag);
            Volatile.Write(ref m_targetValue, flag);
            LadsRuntime.AssignValue(m_target, Variant.From(flag));
        }

        private readonly TwoStateDiscreteState? m_current;
        private readonly TwoStateDiscreteState? m_target;
        private bool m_currentValue;
        private bool m_targetValue;
    }

    /// <summary>
    /// The handle of a multi-state discrete control function.
    /// </summary>
    internal sealed class LadsMultiStateDiscreteControlFunction : LadsControlFunction,
        IMultiStateDiscreteControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsMultiStateDiscreteControlFunction(
            LadsFunctionalUnit unit,
            MultiStateDiscreteControlFunctionState node,
            string path,
            LadsMultiStateDiscreteOptions options)
            : base(unit, node, path, options.EnableRunningOperations)
        {
            m_states = Validate(options);
            m_current = Configure(Runtime, node.CurrentValue, options);
            m_target = Configure(Runtime, node.TargetValue, options);
            m_currentValue = options.InitialValue;
            m_targetValue = options.InitialValue;
            Runtime.Update(m_current, Variant.From(m_currentValue));
            Runtime.Update(m_target, Variant.From(m_targetValue));
        }

        /// <inheritdoc/>
        public uint CurrentValue => Volatile.Read(ref m_currentValue);

        /// <inheritdoc/>
        public uint TargetValue => Volatile.Read(ref m_targetValue);

        /// <inheritdoc/>
        public void UpdateCurrentValue(uint value)
        {
            Unit.Device.UpdateControlReading(this, value);
            m_current?.ClearChangeMasks(Runtime.Context, false);
        }

        /// <summary>
        /// Validates and assigns the current reading without dispatching notifications.
        /// </summary>
        internal void AssignReading(uint value)
        {
            Check(m_states, value);
            Volatile.Write(ref m_currentValue, value);
            LadsRuntime.AssignValue(m_current, Variant.From(value));
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(uint value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(uint value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <summary>
        /// Validates the options of a multi-state function and returns the number of states.
        /// </summary>
        internal static int Validate(LadsMultiStateDiscreteOptions options)
        {
            if (options.States.Count == 0)
            {
                throw new ArgumentException(
                    "A multi-state discrete function needs at least one state.",
                    nameof(options));
            }
            if (options.InitialValue >= options.States.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The initial value is not one of the states.");
            }
            return options.States.Count;
        }

        /// <summary>
        /// Rejects an index outside of the states.
        /// </summary>
        internal static void Check(int states, uint value)
        {
            if (value >= states)
            {
                throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "The state {0} is unknown.", value);
            }
        }

        /// <summary>
        /// Sets the texts of the states of a multi-state variable.
        /// </summary>
        internal static MultiStateDiscreteState<uint>? Configure(
            LadsRuntime runtime,
            BaseVariableState? variable,
            LadsMultiStateDiscreteOptions options)
        {
            if (variable is not MultiStateDiscreteState<uint> discrete)
            {
                return null;
            }
            runtime.Update(discrete.EnumStrings, Variant.From(options.States));
            return discrete;
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            if (!value.TryGetValue(out uint index))
            {
                double number = ToDouble(value);
                if (number < 0 || number > uint.MaxValue || Math.Floor(number) != number)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A state index is expected.");
                }
                index = (uint)number;
            }
            Check(m_states, index);
            return Variant.From(index);
        }

        /// <summary>
        /// Assigns the normalized target without dispatching notifications.
        /// </summary>
        internal void AssignTarget(Variant value)
        {
            value.TryGetValue(out uint index);
            Volatile.Write(ref m_targetValue, index);
            LadsRuntime.AssignValue(m_target, Variant.From(index));
        }

        private readonly int m_states;
        private readonly MultiStateDiscreteState<uint>? m_current;
        private readonly MultiStateDiscreteState<uint>? m_target;
        private uint m_currentValue;
        private uint m_targetValue;
    }

    /// <summary>
    /// The handle of a timer control function.
    /// </summary>
    internal sealed class LadsTimerFunction : LadsControlFunction, ITimerFunction
    {
        /// <summary>
        /// Creates the handle and the optional values of the timer.
        /// </summary>
        public LadsTimerFunction(
            LadsFunctionalUnit unit,
            TimerControlFunctionState node,
            string path,
            LadsTimerOptions options)
            : base(unit, node, path, options.EnableRunningOperations)
        {
            m_node = node;
            m_options = options;
            ISystemContext context = Runtime.Context;
            node.AddCurrentValue(context).AddTargetValue(context).AddDifferenceValue(context);
            EUInformation units = options.EngineeringUnits ?? LadsUnits.Millisecond;
            ConfigureAnalog(node.CurrentValue, units, options.Range);
            ConfigureAnalog(node.TargetValue, units, options.Range);
            ConfigureAnalog(node.DifferenceValue, units, null);
            m_target = options.InitialTargetValue;
            AssignValues();
            Node.ClearChangeMasks(Runtime.Context, true);
        }

        /// <inheritdoc/>
        public double CurrentValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
        }

        /// <inheritdoc/>
        public double TargetValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target;
                }
            }
        }

        /// <inheritdoc/>
        public double DifferenceValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target - m_current;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(double value)
        {
            Unit.Device.UpdateControlReading(this, value);
            Node.ClearChangeMasks(Runtime.Context, true);
        }

        /// <summary>
        /// Assigns the current timer reading without dispatching notifications.
        /// </summary>
        internal void AssignReading(double value)
        {
            lock (m_lock)
            {
                m_current = value;
            }
            AssignValues();
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            if (target < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "A timer value must not be negative.");
            }
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <summary>
        /// Assigns the normalized timer target without dispatching notifications.
        /// </summary>
        internal void AssignTarget(Variant value)
        {
            lock (m_lock)
            {
                m_target = ToDouble(value);
            }
            AssignValues();
        }

        /// <summary>
        /// Writes the values of the timer.
        /// </summary>
        private void AssignValues()
        {
            double current;
            double target;
            lock (m_lock)
            {
                current = m_current;
                target = m_target;
            }
            LadsRuntime.AssignValue(m_node.CurrentValue, Variant.From(current));
            LadsRuntime.AssignValue(m_node.TargetValue, Variant.From(target));
            LadsRuntime.AssignValue(m_node.DifferenceValue, Variant.From(target - current));
        }

        private readonly TimerControlFunctionState m_node;
        private readonly LadsTimerOptions m_options;
        private readonly Lock m_lock = new();
        private double m_current;
        private double m_target;
    }
}
