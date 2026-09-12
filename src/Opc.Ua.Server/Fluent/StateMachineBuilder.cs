/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Default implementation of <see cref="IStateMachineBuilder{TState}"/>.
    /// Installs composed coordinator handlers on the underlying
    /// <see cref="FiniteStateMachineState"/> so user callbacks (enter,
    /// exit, transition, guards) and pre-existing handlers all run.
    /// </summary>
    /// <typeparam name="TState">Concrete finite state machine state type.</typeparam>
    /// <remarks>
    /// <para>
    /// State IDs are derived from <c>CurrentState.Id.Value</c> — a
    /// <see cref="NodeId"/> whose numeric identifier is the internal
    /// state ID assigned by the subclass's state table. Each transition
    /// receives its own immutable source and destination snapshots.
    /// </para>
    /// <para>
    /// Nested transition callbacks cannot overwrite another invocation's
    /// source or destination state.
    /// </para>
    /// </remarks>
    internal sealed class StateMachineBuilder<TState> : IStateMachineBuilder<TState>
        where TState : FiniteStateMachineState
    {
        public StateMachineBuilder(INodeBuilder<TState> nodeBuilder)
        {
            m_nodeBuilder = nodeBuilder ?? throw new ArgumentNullException(nameof(nodeBuilder));
            StateMachine = nodeBuilder.Node;

            m_existingCallbacks = StateMachine.TransitionCallbackFactory;
            StateMachine.TransitionCallbackFactory = CreateCallbacks;
            ISystemContext context = nodeBuilder.Builder.Context;
            m_timeProvider = ((context as ServerSystemContext)?.Server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_logger = context.Telemetry.CreateLogger<StateMachineBuilder<TState>>();
        }

        public TState StateMachine { get; }

        public INodeBuilder Builder => m_nodeBuilder;

        public IStateMachineBuilder<TState> WithInitialState(uint stateId)
        {
            if (stateId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stateId),
                    stateId,
                    "State ID must be non-zero.");
            }

            ISystemContext ctx = m_nodeBuilder.Builder.Context;
            StateMachine.SetState(ctx, stateId);
            return this;
        }

        public IStateMachineBuilder<TState> OnEnterState(
            uint stateId,
            Action<ISystemContext, TState> handler)
        {
            if (stateId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stateId), stateId, "State ID must be non-zero.");
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (!m_onEnter.TryGetValue(stateId, out List<Action<ISystemContext, TState>>? list))
            {
                list = [];
                m_onEnter[stateId] = list;
            }
            list.Add(handler);
            return this;
        }

        public IStateMachineBuilder<TState> OnExitState(
            uint stateId,
            Action<ISystemContext, TState> handler)
        {
            if (stateId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stateId), stateId, "State ID must be non-zero.");
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (!m_onExit.TryGetValue(stateId, out List<Action<ISystemContext, TState>>? list))
            {
                list = [];
                m_onExit[stateId] = list;
            }
            list.Add(handler);
            return this;
        }

        public IStateMachineBuilder<TState> OnTransition(
            Action<ISystemContext, TState, uint, uint> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_onTransition.Add(handler);
            return this;
        }

        public IStateMachineBuilder<TState> OnBeforeTransition(
            Func<ISystemContext, TState, uint, ServiceResult> guard)
        {
            if (guard == null)
            {
                throw new ArgumentNullException(nameof(guard));
            }
            m_guards.Add(guard);
            return this;
        }

        public IStateMachineBuilder<TState> WithCause(
            NodeId methodNodeId,
            uint causeId,
            bool reportExecutable = true)
        {
            if (methodNodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(methodNodeId));
            }
            if (causeId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(causeId), causeId, "Cause ID must be non-zero.");
            }

            NodeState? methodNode = ResolveNode(methodNodeId);
            if (methodNode is not MethodState method)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "WithCause requires a MethodState node; '{0}' is '{1}'.",
                    methodNodeId,
                    methodNode?.GetType().Name ?? "(not found)");
            }

            if (method.OnCallMethod2 != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Method '{0}' already has an OnCallMethod2 handler.",
                    method.BrowseName);
            }

            uint capturedCauseId = causeId;
            TState machine = StateMachine;
            method.OnCallMethod2 = (
                context,
                m,
                objectId,
                inputArguments,
                outputArguments) => machine.DoCause(
                    context, m, capturedCauseId, inputArguments, outputArguments);

            if (reportExecutable)
            {
                // Part 3 §5.7: Executable / UserExecutable answer "may
                // this Method be called right now"; for a Part 16 cause
                // that is exactly IsCausePermitted. Without this the
                // attributes stay at the node's construction value
                // (true) and a client can only discover the answer by
                // calling and being refused.
                method.OnReadExecutable =
                    (ISystemContext context, NodeState node, ref bool value) => {
                        value = machine.IsCausePermitted(context, capturedCauseId, false);
                        return ServiceResult.Good;
                    };
                method.OnReadUserExecutable =
                    (ISystemContext context, NodeState node, ref bool value) => {
                        value = machine.IsCausePermitted(context, capturedCauseId, true);
                        return ServiceResult.Good;
                    };
            }
            return this;
        }

        public IStateMachineBuilder<TState> WithTimedTransition(
            uint fromStateId,
            TimeSpan timeout,
            uint causeId)
        {
            if (fromStateId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromStateId), fromStateId, "State ID must be non-zero.");
            }
            if (causeId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(causeId), causeId, "Cause ID must be non-zero.");
            }
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout), timeout, "Timeout must be positive.");
            }

            if (m_nodeBuilder.Builder is not NodeManagerBuilder root ||
                root.Simulations == null)
            {
                throw new InvalidOperationException(
                    "WithTimedTransition requires the owning manager to derive " +
                    "from FluentNodeManagerBase. The simulation registry is not " +
                    "available on this manager.");
            }

            m_timedTransitions.Add(
                new TimedTransition(fromStateId, timeout, causeId));

            if (!m_simulationRegistered)
            {
                // Reaches the registry directly rather than through Simulation(...), so
                // it has to register the lifecycle behavior itself — that behavior is
                // what starts the loops and releases them again.
                root.EnsureSimulationLifecycleRegistered();
                root.Simulations
                    .NewSimulation(s_timedTransitionTickInterval)
                    .OnTick(OnSimulationTick);
                m_simulationRegistered = true;
            }
            return this;
        }

        public IStateMachineBuilder<TState> ConfigureStateMachine(Action<TState> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(StateMachine);
            return this;
        }

        private (StateMachineTransitionHandler? Before, StateMachineTransitionHandler? After) CreateCallbacks(
            uint from,
            uint to,
            StateMachineTransitionHandler? before,
            StateMachineTransitionHandler? after)
        {
            if (m_existingCallbacks != null)
            {
                (before, after) = m_existingCallbacks(from, to, before, after);
            }
            return (
                (context, machine, transition, cause, inputs, outputs) =>
                    CoordinatorBefore(context, machine, transition, cause, inputs, outputs, from, before),
                (context, machine, transition, cause, inputs, outputs) =>
                    CoordinatorAfter(context, machine, transition, cause, inputs, outputs, from, to, after));
        }

        private ServiceResult CoordinatorBefore(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments,
            uint fromStateId,
            StateMachineTransitionHandler? existingBefore)
        {
            if (existingBefore != null)
            {
                ServiceResult existingResult = existingBefore(
                    context, machine, transitionId, causeId,
                    inputArguments, outputArguments);
                if (ServiceResult.IsBad(existingResult))
                {
                    return existingResult;
                }
            }

            for (int i = 0; i < m_guards.Count; i++)
            {
                ServiceResult guardResult;
                try
                {
                    guardResult = m_guards[i](context, StateMachine, fromStateId);
                }
                catch (Exception ex)
                {
                    return new ServiceResult(ex);
                }
                if (ServiceResult.IsBad(guardResult))
                {
                    return guardResult;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult CoordinatorAfter(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments,
            uint fromStateId,
            uint toStateId,
            StateMachineTransitionHandler? existingAfter)
        {
            long revision = StateMachine.StateRevision;
            ServiceResult existingResult = ServiceResult.Good;
            if (existingAfter != null)
            {
                try
                {
                    existingResult = existingAfter(
                        context, machine, transitionId, causeId,
                        inputArguments, outputArguments);
                }
                catch (Exception ex)
                {
                    existingResult = new ServiceResult(ex);
                }
            }

            // Track the entered-state timestamp for timed transitions
            // before firing user callbacks so handlers see a coherent
            // arming state.
            if (m_timedTransitions.Count > 0 && StateMachine.StateRevision == revision)
            {
                m_currentStateEnteredAt = m_timeProvider.GetTimestamp();
                m_currentStateForTimer = toStateId;
                m_currentStateRevision = revision;
            }

            if (fromStateId != 0 &&
                m_onExit.TryGetValue(
                    fromStateId, out List<Action<ISystemContext, TState>>? exitHandlers))
            {
                FireListSafely(exitHandlers, context);
            }

            if (toStateId != 0 &&
                m_onEnter.TryGetValue(
                    toStateId, out List<Action<ISystemContext, TState>>? enterHandlers))
            {
                FireListSafely(enterHandlers, context);
            }

            for (int i = 0; i < m_onTransition.Count; i++)
            {
                try
                {
                    m_onTransition[i](context, StateMachine, fromStateId, toStateId);
                }
                catch (Exception ex)
                {
                    // Observers do not abort the transition; log via
                    // existing-result so callers see something useful.
                    if (ServiceResult.IsGood(existingResult))
                    {
                        existingResult = new ServiceResult(ex);
                    }
                }
            }

            return existingResult;
        }

        private void OnSimulationTick(ISystemContext context, TimeSpan elapsed)
        {
            // No-op on the first tick after registration if we have not
            // observed any state entry yet.
            if (m_currentStateForTimer == 0 || m_currentStateRevision != StateMachine.StateRevision)
            {
                m_currentStateForTimer = ReadCurrentStateId();
                m_currentStateEnteredAt = m_timeProvider.GetTimestamp();
                m_currentStateRevision = StateMachine.StateRevision;
                return;
            }

            uint stateId = m_currentStateForTimer;
            long enteredAt = m_currentStateEnteredAt;
            double elapsedSinceEnter = m_timeProvider.GetElapsedTime(enteredAt).TotalSeconds;

            for (int i = 0; i < m_timedTransitions.Count; i++)
            {
                TimedTransition t = m_timedTransitions[i];
                if (t.FromStateId != stateId)
                {
                    continue;
                }
                if (elapsedSinceEnter < t.Timeout.TotalSeconds)
                {
                    continue;
                }
                // Re-check the current state right before firing the
                // cause; an interleaving server-driven transition may
                // have moved us off the armed state.
                if (ReadCurrentStateId() != t.FromStateId)
                {
                    continue;
                }

                ServiceResult result = StateMachine.TryTimedTransition(
                    context, t.FromStateId, m_currentStateRevision, transitionId: 0, t.CauseId, static () => true);
                if (ServiceResult.IsBad(result) && result.StatusCode != StatusCodes.BadInvalidState)
                {
                    m_logger.FluentTimedTransitionRejected(t.FromStateId, result);
                }
                // After firing, the entered-at field has been refreshed
                // in CoordinatorAfter via the state transition.
                break;
            }
        }

        private uint ReadCurrentStateId()
        {
            if (StateMachine.CurrentState == null ||
                StateMachine.CurrentState.Id == null)
            {
                return 0;
            }

            // One shared resolve rule: the machine's own mapping first
            // (materialized state NodeIds), then the namespace-agnostic
            // numeric fallback for externally-written vendor ids.
            return StateMachines.StateMachineBuilder.ResolveStateId(
                StateMachine, StateMachine.CurrentState.Id.Value);
        }

        private NodeState? ResolveNode(NodeId nodeId)
        {
            // INodeManagerBuilder.Node(NodeId) throws BadNodeIdUnknown
            // when the node cannot be resolved. We mirror that by
            // letting the exception propagate to the caller.
            INodeBuilder builder = m_nodeBuilder.Builder.Node(nodeId);
            return builder.Node;
        }

        private void FireListSafely(
            List<Action<ISystemContext, TState>> list,
            ISystemContext context)
        {
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    list[i](context, StateMachine);
                }
                catch (Exception ex)
                {
                    m_logger.FluentStateObserverFailed(ex);
                }
            }
        }

        private readonly INodeBuilder<TState> m_nodeBuilder;
        private readonly StateMachineTransitionCallbackFactory? m_existingCallbacks;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;

        private readonly Dictionary<uint, List<Action<ISystemContext, TState>>> m_onEnter
            = [];

        private readonly Dictionary<uint, List<Action<ISystemContext, TState>>> m_onExit
            = [];

        private readonly List<Action<ISystemContext, TState, uint, uint>> m_onTransition
            = [];

        private readonly List<Func<ISystemContext, TState, uint, ServiceResult>> m_guards
            = [];

        private readonly List<TimedTransition> m_timedTransitions = [];
        private long m_currentStateRevision;
        private uint m_currentStateForTimer;
        private long m_currentStateEnteredAt;
        private bool m_simulationRegistered;

        private static readonly TimeSpan s_timedTransitionTickInterval =
            TimeSpan.FromMilliseconds(100);

        private readonly struct TimedTransition
        {
            public TimedTransition(uint fromStateId, TimeSpan timeout, uint causeId)
            {
                FromStateId = fromStateId;
                Timeout = timeout;
                CauseId = causeId;
            }

            public uint FromStateId { get; }
            public TimeSpan Timeout { get; }
            public uint CauseId { get; }
        }
    }

    internal static partial class StateMachineBuilderLog
    {
        [LoggerMessage(EventId = ServerEventIds.FluentStateMachineBuilder, Level = LogLevel.Warning,
            Message = "Timed cause from state {StateId} was rejected: {Result}.")]
        public static partial void FluentTimedTransitionRejected(this ILogger logger, uint stateId, ServiceResult result);

        [LoggerMessage(EventId = ServerEventIds.FluentStateMachineBuilder + 1, Level = LogLevel.Error,
            Message = "State-machine lifecycle observer failed.")]
        public static partial void FluentStateObserverFailed(this ILogger logger, Exception exception);
    }
}
