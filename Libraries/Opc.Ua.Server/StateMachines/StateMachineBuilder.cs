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
using System.Diagnostics;
using System.Threading;

namespace Opc.Ua.Server.StateMachines
{
    /// <summary>
    /// Unified fluent builder for Part 16 finite state machines.
    /// Combines two complementary modes in a single API:
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <strong>Definition mode</strong> —
    /// <see cref="StateMachineBuilder.Create"/> constructs a new
    /// <see cref="FluentFiniteStateMachineState"/> in the address space
    /// and exposes <see cref="AddState"/> / <see cref="AddTransition"/>
    /// / <see cref="OnCause"/> for declarative table construction.
    /// </description></item>
    /// <item><description>
    /// <strong>Lifecycle mode</strong> —
    /// <see cref="StateMachineBuilder.For{TState}"/> adopts an
    /// already-Created state machine (stack-shipped, generator-emitted,
    /// or vendor) and wires
    /// <see cref="OnEnterState"/> / <see cref="OnExitState"/> /
    /// <see cref="OnTransition"/> / <see cref="OnBeforeTransition"/> /
    /// <see cref="WithCause"/> / <see cref="WithTimedTransition"/>
    /// behavior on top of the existing tables.
    /// </description></item>
    /// </list>
    /// <para>
    /// Both modes share the same lifecycle surface. The first call to
    /// any lifecycle method (or the first access to
    /// <see cref="StateMachine"/>) freezes the underlying definition
    /// holder — subsequent <see cref="AddState"/> / etc. calls throw.
    /// </para>
    /// <para>
    /// The builder is intentionally <em>not</em> <see cref="IDisposable"/>.
    /// Lifecycle hooks attach to the state machine's own
    /// <c>OnBeforeTransition</c> / <c>OnAfterTransition</c> delegate
    /// fields; the dispatcher (with its timers and per-state handler
    /// tables) is kept alive by those delegates and garbage-collected
    /// when the state machine itself becomes unreachable.
    /// </para>
    /// </remarks>
    public sealed class StateMachineBuilder<TState>
        where TState : FiniteStateMachineState
    {
        private readonly TState m_stateMachine;
        private readonly ISystemContext m_context;
        private readonly MutableStateMachineDefinition? m_definition;
        private readonly StateMachineDispatcher<TState> m_dispatcher;

        internal StateMachineBuilder(
            TState stateMachine,
            ISystemContext context,
            MutableStateMachineDefinition? definition,
            NodeId deferredCreateNodeId = default,
            QualifiedName deferredCreateBrowseName = default)
        {
            m_stateMachine = stateMachine
                ?? throw new ArgumentNullException(nameof(stateMachine));
            m_context = context
                ?? throw new ArgumentNullException(nameof(context));
            m_definition = definition;
            m_deferredCreateNodeId = deferredCreateNodeId;
            m_deferredCreateBrowseName = deferredCreateBrowseName;
            m_dispatcher = new StateMachineDispatcher<TState>(stateMachine, context);
        }

        private readonly NodeId m_deferredCreateNodeId;
        private readonly QualifiedName m_deferredCreateBrowseName;

        /// <summary>
        /// The underlying state machine. Reading this property freezes
        /// the definition holder (if any).
        /// </summary>
        public TState StateMachine
        {
            get
            {
                FreezeDefinition();
                return m_stateMachine;
            }
        }

        /// <summary>
        /// Adds a state to the machine. Definition-mode only — the
        /// underlying state machine must be a
        /// <see cref="FluentFiniteStateMachineState"/>.
        /// </summary>
        /// <param name="id">The numeric state id.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="isInitial">Whether this is the initial state.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="browseName"/> is null/empty or
        /// <paramref name="id"/> is already declared.</exception>
        /// <exception cref="InvalidOperationException">
        /// The builder is in lifecycle mode (no definition), the
        /// definition has been frozen, or another state was already
        /// marked initial.</exception>
        public StateMachineBuilder<TState> AddState(
            uint id,
            string browseName,
            bool isInitial = false)
        {
            MutableStateMachineDefinition def = RequireDefinition();
            def.EnsureNotFrozen();

            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("Browse name must not be empty.",
                    nameof(browseName));
            }

            foreach (StateMachineStateDefinition existing in def.States)
            {
                if (existing.Id == id)
                {
                    throw new ArgumentException(
                        $"State id {id} is already declared.", nameof(id));
                }
            }

            def.States.Add(new StateMachineStateDefinition(id, browseName, isInitial));

            if (isInitial)
            {
                if (def.InitialStateId.HasValue && def.InitialStateId.Value != id)
                {
                    throw new InvalidOperationException(
                        "Only one state may be marked as the initial state.");
                }
                def.InitialStateId = id;
            }

            def.Version++;
            return this;
        }

        /// <summary>
        /// Adds a transition between two states. Definition-mode only.
        /// </summary>
        /// <param name="id">The numeric transition id.</param>
        /// <param name="browseName">The browse name.</param>
        /// <param name="from">The from-state id.</param>
        /// <param name="to">The to-state id.</param>
        /// <param name="hasEffect">Whether the transition fires a
        /// <c>TransitionEventType</c>.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="browseName"/> is null/empty or
        /// <paramref name="id"/> is already declared.</exception>
        public StateMachineBuilder<TState> AddTransition(
            uint id,
            string browseName,
            uint from,
            uint to,
            bool hasEffect = true)
        {
            MutableStateMachineDefinition def = RequireDefinition();
            def.EnsureNotFrozen();

            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("Browse name must not be empty.",
                    nameof(browseName));
            }

            foreach (StateMachineTransitionDefinition existing in def.Transitions)
            {
                if (existing.Id == id)
                {
                    throw new ArgumentException(
                        $"Transition id {id} is already declared.", nameof(id));
                }
            }

            def.Transitions.Add(new StateMachineTransitionDefinition(
                id, browseName, from, to, hasEffect));
            def.Version++;
            return this;
        }

        /// <summary>
        /// Adds a cause-to-transition mapping. When the method with
        /// the given cause id is invoked while the machine is in the
        /// given from-state, the named transition fires.
        /// Definition-mode only.
        /// </summary>
        public StateMachineBuilder<TState> OnCause(
            uint causeId,
            uint from,
            uint transition)
        {
            MutableStateMachineDefinition def = RequireDefinition();
            def.EnsureNotFrozen();

            def.CauseMappings.Add(new StateMachineCauseMapping(
                causeId, from, transition));
            def.Version++;
            return this;
        }

        /// <summary>
        /// Designates the namespace URI that qualifies the state and
        /// transition numeric NodeIds. Defaults to the standard UA
        /// namespace. Definition-mode only.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="namespaceUri"/> is null or empty.</exception>
        public StateMachineBuilder<TState> UseElementNamespace(string namespaceUri)
        {
            MutableStateMachineDefinition def = RequireDefinition();
            def.EnsureNotFrozen();

            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be empty.",
                    nameof(namespaceUri));
            }
            def.ElementNamespaceUri = namespaceUri;
            def.Version++;
            return this;
        }

        /// <summary>
        /// Sets the initial state — applied immediately via
        /// <see cref="FiniteStateMachineState.SetState"/>. Does not
        /// invoke lifecycle handlers (initial state assignment is not
        /// a transition).
        /// </summary>
        public StateMachineBuilder<TState> WithInitialState(uint stateId)
        {
            FreezeDefinition();
            ValidateDefinitionStateExists(stateId);
            m_stateMachine.SetState(m_context, stateId);
            return this;
        }

        /// <summary>
        /// Registers a handler invoked when the state machine enters
        /// the given state. Order within a single transition is
        /// <c>OnExitState(from) → OnTransition(from, to) → OnEnterState(to)</c>.
        /// </summary>
        public StateMachineBuilder<TState> OnEnterState(
            uint stateId,
            Action<ISystemContext, TState> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddEnterStateHandler(stateId, handler);
            return this;
        }

        /// <summary>
        /// Async overload of <see cref="OnEnterState"/>. The handler
        /// is invoked fire-and-forget on the thread pool from the sync
        /// transition path, so the handler runs on a fully-async path
        /// (no <c>GetAwaiter().GetResult()</c> / <c>Wait()</c> /
        /// <c>Result</c>) and never blocks the transition. Exceptions
        /// are captured and logged.
        /// </summary>
#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public StateMachineBuilder<TState> OnEnterStateAsync(
            uint stateId,
            Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddEnterStateHandlerAsync(stateId, handler);
            return this;
        }

        /// <summary>
        /// Registers a handler invoked when the state machine leaves
        /// the given state.
        /// </summary>
        public StateMachineBuilder<TState> OnExitState(
            uint stateId,
            Action<ISystemContext, TState> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddExitStateHandler(stateId, handler);
            return this;
        }

        /// <summary>
        /// Async overload of <see cref="OnExitState"/>. See
        /// <see cref="OnEnterStateAsync"/> for invocation semantics.
        /// </summary>
#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public StateMachineBuilder<TState> OnExitStateAsync(
            uint stateId,
            Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddExitStateHandlerAsync(stateId, handler);
            return this;
        }

        /// <summary>
        /// Registers a generic transition observer invoked after every
        /// completed transition. Receives the resolved <c>from</c> and
        /// <c>to</c> state ids (extracted from <c>LastState</c> /
        /// <c>CurrentState</c>).
        /// </summary>
        public StateMachineBuilder<TState> OnTransition(
            Action<ISystemContext, TState, uint, uint> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddTransitionObserver(handler);
            return this;
        }

        /// <summary>
        /// Async overload of <see cref="OnTransition"/>. See
        /// <see cref="OnEnterStateAsync"/> for invocation semantics.
        /// </summary>
#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public StateMachineBuilder<TState> OnTransitionAsync(
            Func<ISystemContext, TState, uint, uint, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            ArgumentNullException.ThrowIfNull(handler);
            FreezeDefinition();
            m_dispatcher.AddTransitionObserverAsync(handler);
            return this;
        }

        /// <summary>
        /// Registers a pre-transition guard. Returning a non-Good
        /// <see cref="ServiceResult"/> cancels the transition. The
        /// builder guards run BEFORE any pre-existing
        /// <c>OnBeforeTransition</c> delegate the state machine had
        /// when the builder adopted it.
        /// </summary>
        public StateMachineBuilder<TState> OnBeforeTransition(
            Func<ISystemContext, TState, uint, uint, ServiceResult> guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            FreezeDefinition();
            m_dispatcher.AddBeforeTransitionGuard(guard);
            return this;
        }

        /// <summary>
        /// Adds a transition-scoped guard. The predicate fires only
        /// when the inbound transition matches
        /// <paramref name="transitionId"/>; returning <c>false</c>
        /// vetoes the transition with
        /// <see cref="StatusCodes.BadUserAccessDenied"/>. Guards
        /// registered via the <c>When*</c> family compose with
        /// <see cref="OnBeforeTransition"/> in registration order —
        /// the first failing guard wins.
        /// </summary>
        public StateMachineBuilder<TState> WhenTransition(
            uint transitionId,
            Func<ISystemContext, TState, bool> predicate)
            => WhenTransition(transitionId, predicate, StatusCodes.BadUserAccessDenied);

        /// <summary>
        /// Overload of <see cref="WhenTransition(uint, Func{ISystemContext, TState, bool})"/>
        /// that lets the caller customize the deny status returned
        /// when the predicate evaluates to <c>false</c>.
        /// </summary>
        public StateMachineBuilder<TState> WhenTransition(
            uint transitionId,
            Func<ISystemContext, TState, bool> predicate,
            ServiceResult denyStatus)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            FreezeDefinition();
            m_dispatcher.AddBeforeTransitionGuard((ctx, sm, tid, cid) =>
            {
                if (tid != transitionId)
                {
                    return ServiceResult.Good;
                }
                return predicate(ctx, sm) ? ServiceResult.Good : denyStatus;
            });
            return this;
        }

        /// <summary>
        /// Adds a cause-scoped guard. The predicate fires only when
        /// the inbound cause id matches <paramref name="causeId"/>.
        /// Useful for permission checks on specific method-driven
        /// transitions (e.g. <c>Acknowledge</c>, <c>Suspend</c>).
        /// </summary>
        public StateMachineBuilder<TState> WhenCause(
            uint causeId,
            Func<ISystemContext, TState, bool> predicate)
            => WhenCause(causeId, predicate, StatusCodes.BadUserAccessDenied);

        /// <summary>
        /// Overload of <see cref="WhenCause(uint, Func{ISystemContext, TState, bool})"/>
        /// with a caller-supplied deny status.
        /// </summary>
        public StateMachineBuilder<TState> WhenCause(
            uint causeId,
            Func<ISystemContext, TState, bool> predicate,
            ServiceResult denyStatus)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            FreezeDefinition();
            m_dispatcher.AddBeforeTransitionGuard((ctx, sm, tid, cid) =>
            {
                if (cid != causeId)
                {
                    return ServiceResult.Good;
                }
                return predicate(ctx, sm) ? ServiceResult.Good : denyStatus;
            });
            return this;
        }

        /// <summary>
        /// Adds an enter-state-scoped guard. The predicate fires only
        /// when the inbound transition would put the machine into
        /// <paramref name="toStateId"/>. Definition-mode only — the
        /// builder uses its own transition table to resolve the
        /// to-state from the transition id.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The builder is in lifecycle mode (no definition).
        /// </exception>
        public StateMachineBuilder<TState> WhenEnter(
            uint toStateId,
            Func<ISystemContext, TState, bool> predicate)
            => WhenEnter(toStateId, predicate, StatusCodes.BadUserAccessDenied);

        /// <summary>
        /// Overload of <see cref="WhenEnter(uint, Func{ISystemContext, TState, bool})"/>
        /// with a caller-supplied deny status.
        /// </summary>
        public StateMachineBuilder<TState> WhenEnter(
            uint toStateId,
            Func<ISystemContext, TState, bool> predicate,
            ServiceResult denyStatus)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            if (m_definition == null)
            {
                throw new InvalidOperationException(
                    "WhenEnter is only available in definition mode. "
                    + "Use WhenTransition with the relevant transition id "
                    + "in lifecycle mode.");
            }
            FreezeDefinition();
            // Snapshot the matching transitions at registration time;
            // the definition is frozen so this list is stable.
            HashSet<uint> matchingTransitions = [];
            foreach (StateMachineTransitionDefinition t in m_definition.Transitions)
            {
                if (t.ToStateId == toStateId)
                {
                    matchingTransitions.Add(t.Id);
                }
            }
            m_dispatcher.AddBeforeTransitionGuard((ctx, sm, tid, cid) =>
            {
                if (!matchingTransitions.Contains(tid))
                {
                    return ServiceResult.Good;
                }
                return predicate(ctx, sm) ? ServiceResult.Good : denyStatus;
            });
            return this;
        }

        /// <summary>
        /// Adds an exit-state-scoped guard. The predicate fires only
        /// when the inbound transition would leave
        /// <paramref name="fromStateId"/>. Works in both definition
        /// and lifecycle modes — the dispatcher captures the current
        /// state in <c>DispatchBefore</c> and matches against
        /// <paramref name="fromStateId"/>.
        /// </summary>
        public StateMachineBuilder<TState> WhenExit(
            uint fromStateId,
            Func<ISystemContext, TState, bool> predicate)
            => WhenExit(fromStateId, predicate, StatusCodes.BadUserAccessDenied);

        /// <summary>
        /// Overload of <see cref="WhenExit(uint, Func{ISystemContext, TState, bool})"/>
        /// with a caller-supplied deny status.
        /// </summary>
        public StateMachineBuilder<TState> WhenExit(
            uint fromStateId,
            Func<ISystemContext, TState, bool> predicate,
            ServiceResult denyStatus)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            FreezeDefinition();
            m_dispatcher.AddBeforeTransitionGuard((ctx, sm, tid, cid) =>
            {
                uint currentFrom = ExtractCurrentStateId(sm);
                if (currentFrom != fromStateId)
                {
                    return ServiceResult.Good;
                }
                return predicate(ctx, sm) ? ServiceResult.Good : denyStatus;
            });
            return this;
        }

        private static uint ExtractCurrentStateId(TState sm)
        {
            NodeId? id = sm.CurrentState?.Id?.Value;
            if (id is null || id.Value.IsNull)
            {
                return 0;
            }
            return id.Value.TryGetValue(out uint stateId) ? stateId : 0;
        }

        /// <summary>
        /// Attaches a sub-state-machine to the parent state identified
        /// by <paramref name="parentStateId"/>. The sub-SM is added
        /// as a <c>HasSubStateMachine</c>-referenced child of the
        /// parent FSM, configured through the nested
        /// <paramref name="configure"/> builder, and managed by the
        /// dispatcher lifecycle:
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>
        /// When the parent enters <paramref name="parentStateId"/>,
        /// the child sub-SM's <see cref="FluentFiniteStateMachineState.IsSuspended"/>
        /// is set to <c>false</c> and (unless
        /// <paramref name="preserveOnReentry"/> is <c>true</c>) the
        /// child is reset to its declared initial state.
        /// </description></item>
        /// <item><description>
        /// When the parent exits <paramref name="parentStateId"/>,
        /// the child is suspended — subsequent <c>DoTransition</c> /
        /// <c>DoCause</c> calls on the child return
        /// <see cref="StatusCodes.BadInvalidState"/> until the parent
        /// re-enters the attached state.
        /// </description></item>
        /// </list>
        /// <para>
        /// Definition-mode only: the parent must be a
        /// <see cref="FluentFiniteStateMachineState"/>. In lifecycle
        /// mode (when adopting a stack-shipped or vendor FSM) the
        /// sub-SM is already part of the type definition; observe it
        /// through the client-side sub-SM accessors instead.
        /// </para>
        /// </remarks>
        /// <param name="parentStateId">The parent state whose entry
        /// activates the sub-SM.</param>
        /// <param name="browseName">The sub-SM's browse name (becomes
        /// the child node's BrowseName).</param>
        /// <param name="configure">Nested builder action that defines
        /// the sub-SM's states, transitions, and behavior.</param>
        /// <param name="preserveOnReentry">When <c>true</c>, the
        /// sub-SM retains its last state across parent re-entries;
        /// otherwise it resets to its declared initial state.</param>
        /// <exception cref="InvalidOperationException">
        /// Definition-mode only — the builder must own a
        /// <see cref="FluentFiniteStateMachineState"/>.
        /// </exception>
        public StateMachineBuilder<TState> WithSubStateMachine(
            uint parentStateId,
            QualifiedName browseName,
            Action<StateMachineBuilder<FluentFiniteStateMachineState>> configure,
            bool preserveOnReentry = false)
        {
            ArgumentNullException.ThrowIfNull(configure);
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "Browse name must not be null.", nameof(browseName));
            }
            if (m_definition == null)
            {
                throw new InvalidOperationException(
                    "WithSubStateMachine is only available in definition "
                    + "mode (use StateMachineBuilder.Create). Lifecycle-mode "
                    + "FSMs already declare their sub-state-machines as part "
                    + "of the type definition.");
            }

            FreezeDefinition();

            // Configure the sub-SM through a nested builder. The
            // sub-SM's NodeId is derived from the parent's NodeId so
            // it lives under the parent in the address space.
            var subHolder = new MutableStateMachineDefinition();
            FluentFiniteStateMachineState child =
                FluentFiniteStateMachineState.CreateWithHolder(m_stateMachine, subHolder);
            NodeId childNodeId = m_stateMachine.NodeId.IsNull
                ? new NodeId(System.Guid.NewGuid())
                : ComposeChildNodeId(m_stateMachine.NodeId, browseName);
            child.Create(
                m_context,
                childNodeId,
                browseName,
                new LocalizedText(browseName.Name!),
                true);
            var childBuilder = new StateMachineBuilder<FluentFiniteStateMachineState>(
                child, m_context, subHolder);
            configure(childBuilder);
            // Read StateMachine to freeze the child's definition and
            // ensure it is fully constructed.
            FluentFiniteStateMachineState materializedChild = childBuilder.StateMachine;
            // Set the reference type BEFORE adding to the parent so
            // AddChild does not default it to HasComponent. Then add.
            materializedChild.ReferenceTypeId = ReferenceTypeIds.HasSubStateMachine;
            m_stateMachine.AddChild(materializedChild);

            // Initial state of the sub-SM is "suspended" unless the
            // parent already starts in the attached state.
            uint initialChildStateId = subHolder.InitialStateId ?? 0;
            bool parentInAttachedState =
                ExtractCurrentStateId(m_stateMachine) == parentStateId;
            materializedChild.IsSuspended = !parentInAttachedState;
            if (parentInAttachedState && initialChildStateId != 0)
            {
                // Set the child to its initial state immediately —
                // the parent's OnEnterState handler isn't fired on
                // WithInitialState (it goes through SetState, not
                // DoTransition), so we must seed the child here.
                materializedChild.SetState(m_context, initialChildStateId);
            }

            // Wire the lifecycle hooks on the parent.
            m_dispatcher.AddEnterStateHandler(parentStateId, (ctx, parent) =>
            {
                if (!preserveOnReentry && initialChildStateId != 0)
                {
                    materializedChild.IsSuspended = false;
                    materializedChild.SetState(ctx, initialChildStateId);
                }
                else
                {
                    materializedChild.IsSuspended = false;
                }
            });
            m_dispatcher.AddExitStateHandler(parentStateId, (ctx, parent) =>
            {
                materializedChild.IsSuspended = true;
            });

            return this;
        }

        private static NodeId ComposeChildNodeId(NodeId parentNodeId, QualifiedName browseName)
        {
            // Derive a deterministic child NodeId from the parent
            // and the child's browse name so multiple
            // WithSubStateMachine calls produce stable, distinct ids.
            string suffix = browseName.Name ?? "Child";
            if (parentNodeId.TryGetValue(out string parentStr))
            {
                return new NodeId(parentStr + "_" + suffix, parentNodeId.NamespaceIndex);
            }
            return new NodeId(parentNodeId + "_" + suffix, parentNodeId.NamespaceIndex);
        }

        /// <summary>
        /// Binds an inbound method call on the given method node to
        /// the cause-processing pipeline. The cause id is derived from
        /// <paramref name="methodNodeId"/>'s numeric identifier (the
        /// OPC UA convention). The cause-to-transition mapping must
        /// already exist on the state machine — either via
        /// <see cref="OnCause"/> (definition mode) or hardcoded in a
        /// stack/vendor subclass.
        /// </summary>
        /// <param name="methodNodeId">The method node whose
        /// <c>OnCallMethod2</c> handler will be installed.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="methodNodeId"/> is null, has a non-numeric
        /// identifier, or could not be resolved to a child method
        /// of the state machine.
        /// </exception>
        public StateMachineBuilder<TState> WithCause(NodeId methodNodeId)
        {
            FreezeDefinition();

            if (methodNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Method node id must not be null.",
                    nameof(methodNodeId));
            }

            if (!methodNodeId.TryGetValue(out uint causeId))
            {
                throw new ArgumentException(
                    "Method node id must have a numeric identifier; " +
                    $"got '{methodNodeId.IdentifierAsString}' " +
                    $"(type {methodNodeId.IdType}).",
                    nameof(methodNodeId));
            }

            MethodState? method = FindMethodInTree(m_stateMachine, methodNodeId) ??
                throw new ArgumentException(
                    $"Method '{methodNodeId}' not found among the state " +
                    "machine's children.",
                    nameof(methodNodeId));

            m_dispatcher.InstallCauseMethod(method, causeId);
            return this;
        }

        /// <summary>
        /// Registers an automatic transition that fires after the
        /// machine has been in <paramref name="fromStateId"/> for at
        /// least <paramref name="timeout"/>. The timer is armed on
        /// entry into <paramref name="fromStateId"/> and cancelled on
        /// exit; multiple entries re-arm a fresh timer.
        /// </summary>
        /// <param name="fromStateId">The state whose entry arms the
        /// timer.</param>
        /// <param name="timeout">The duration to wait before firing
        /// the transition.</param>
        /// <param name="transitionId">The transition to fire when the
        /// timer expires — driven via
        /// <see cref="FiniteStateMachineState.DoTransition"/>.</param>
        /// <param name="causeId">The cause id reported in the audit
        /// event (0 if no human-meaningful cause).</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is zero or negative.</exception>
        public StateMachineBuilder<TState> WithTimedTransition(
            uint fromStateId,
            TimeSpan timeout,
            uint transitionId,
            uint causeId = 0)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout),
                    "Timeout must be positive.");
            }
            FreezeDefinition();
            ValidateTimedTransition(fromStateId, transitionId);
            m_dispatcher.AddTimedTransition(
                fromStateId, timeout, transitionId, causeId);
            return this;
        }

        private void ValidateTimedTransition(uint fromStateId, uint transitionId)
        {
            // Validate only when the builder owns the definition. For
            // lifecycle-mode (For()) we can't reach the FSM's protected
            // tables — the caller is responsible.
            if (m_definition == null)
            {
                return;
            }

            bool fromOk = false;
            foreach (StateMachineStateDefinition s in m_definition.States)
            {
                if (s.Id == fromStateId)
                {
                    fromOk = true;
                    break;
                }
            }
            if (!fromOk)
            {
                throw new InvalidOperationException(
                    "Timed transition references unknown from-state " +
                    $"{fromStateId}.");
            }

            bool transitionOk = false;
            foreach (StateMachineTransitionDefinition t in m_definition.Transitions)
            {
                if (t.Id == transitionId)
                {
                    if (t.FromStateId != fromStateId)
                    {
                        throw new InvalidOperationException(
                            $"Timed transition {transitionId} declares " +
                            $"from-state {t.FromStateId} but was " +
                            $"registered with fromStateId={fromStateId}.");
                    }
                    transitionOk = true;
                    break;
                }
            }
            if (!transitionOk)
            {
                throw new InvalidOperationException(
                    "Timed transition references unknown transition " +
                    $"{transitionId}.");
            }
        }

        /// <summary>
        /// Escape hatch — invokes <paramref name="configure"/> with the
        /// underlying state machine. Useful for setting properties or
        /// invoking generated-type setters that the builder does not
        /// surface directly.
        /// </summary>
        public StateMachineBuilder<TState> ConfigureStateMachine(
            Action<TState> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            FreezeDefinition();
            configure(m_stateMachine);
            return this;
        }

        private MutableStateMachineDefinition RequireDefinition()
        {
            if (m_definition == null)
            {
                throw new InvalidOperationException(
                    "Definition-mode methods (AddState / AddTransition / " +
                    "OnCause / UseElementNamespace) are only available " +
                    "when the builder owns the state-machine definition. " +
                    "Use StateMachineBuilder.Create(...) to create a " +
                    "FluentFiniteStateMachineState, or stick to lifecycle " +
                    "methods (OnEnterState / WithCause / ...) when " +
                    "wrapping an existing state machine via " +
                    "StateMachineBuilder.For(...).");
            }
            return m_definition;
        }

        private void FreezeDefinition()
        {
            if (m_definition != null && !m_definition.Frozen)
            {
                ValidateDefinition(m_definition);
                m_definition.Frozen = true;
                m_definition.Version++;

                // For definition-mode builders the FSM has not yet been
                // added to the address space — we defer
                // NodeState.Create() until freeze so callers can use
                // UseElementNamespace() to override the element
                // namespace URI before OnAfterCreate caches the
                // namespace index.
                if (!m_deferredCreateNodeId.IsNull && !m_deferredCreateBrowseName.IsNull)
                {
                    m_stateMachine.Create(
                        m_context,
                        m_deferredCreateNodeId,
                        m_deferredCreateBrowseName,
                        new LocalizedText(m_deferredCreateBrowseName.Name),
                        true);
                }
            }
        }

        private void ValidateDefinitionStateExists(uint stateId)
        {
            if (m_definition == null)
            {
                return;
            }

            foreach (StateMachineStateDefinition s in m_definition.States)
            {
                if (s.Id == stateId)
                {
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Initial state {stateId} is not declared. Call AddState " +
                "to declare it before WithInitialState.");
        }

        private static void ValidateDefinition(MutableStateMachineDefinition def)
        {
            if (def.States.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one state must be declared before the " +
                    "definition is frozen.");
            }

            var stateIds = new HashSet<uint>();
            foreach (StateMachineStateDefinition s in def.States)
            {
                stateIds.Add(s.Id);
            }

            foreach (StateMachineTransitionDefinition t in def.Transitions)
            {
                if (!stateIds.Contains(t.FromStateId))
                {
                    throw new InvalidOperationException(
                        $"Transition '{t.BrowseName}' references unknown " +
                        $"from-state {t.FromStateId}.");
                }
                if (!stateIds.Contains(t.ToStateId))
                {
                    throw new InvalidOperationException(
                        $"Transition '{t.BrowseName}' references unknown " +
                        $"to-state {t.ToStateId}.");
                }
            }

            var transitionIds = new HashSet<uint>();
            foreach (StateMachineTransitionDefinition t in def.Transitions)
            {
                transitionIds.Add(t.Id);
            }

            foreach (StateMachineCauseMapping c in def.CauseMappings)
            {
                if (!stateIds.Contains(c.FromStateId))
                {
                    throw new InvalidOperationException(
                        "Cause mapping references unknown from-state " +
                        $"{c.FromStateId}.");
                }
                if (!transitionIds.Contains(c.TransitionId))
                {
                    throw new InvalidOperationException(
                        "Cause mapping references unknown transition " +
                        $"{c.TransitionId}.");
                }
            }

            // Detect duplicate cause mappings — only the first match
            // would ever fire, so silently shadowed mappings are an
            // authoring bug.
            var seenCauseKeys = new HashSet<(uint, uint)>();
            foreach (StateMachineCauseMapping c in def.CauseMappings)
            {
                if (!seenCauseKeys.Add((c.CauseId, c.FromStateId)))
                {
                    throw new InvalidOperationException(
                        "Duplicate cause mapping for causeId=" +
                        $"{c.CauseId}, fromState={c.FromStateId} — " +
                        "subsequent mappings would be silently " +
                        "shadowed by the first.");
                }
            }
        }

        private static MethodState? FindMethodInTree(
            NodeState root, NodeId methodNodeId)
        {
            var children = new List<BaseInstanceState>();
            root.GetChildren(null!, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is MethodState method &&
                    method.NodeId == methodNodeId)
                {
                    return method;
                }
                MethodState? nested = FindMethodInTree(child, methodNodeId);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Static facade for <see cref="StateMachineBuilder{TState}"/>.
    /// </summary>
    public static class StateMachineBuilder
    {
        /// <summary>
        /// Creates a new <see cref="FluentFiniteStateMachineState"/>
        /// in the address space and returns a builder ready for
        /// definition-mode chaining
        /// (<see cref="StateMachineBuilder{TState}.AddState"/> /
        /// <see cref="StateMachineBuilder{TState}.AddTransition"/> /
        /// <see cref="StateMachineBuilder{TState}.OnCause"/>).
        /// </summary>
        /// <param name="parent">The parent node (may be <c>null</c>
        /// for stand-alone machines).</param>
        /// <param name="context">The system context used for the
        /// underlying <see cref="NodeState.Create(ISystemContext, NodeId, QualifiedName, LocalizedText, bool)"/> call.</param>
        /// <param name="nodeId">The state machine's NodeId.</param>
        /// <param name="browseName">The state machine's browse
        /// name.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="nodeId"/> or <paramref name="browseName"/>
        /// is null.</exception>
        public static StateMachineBuilder<FluentFiniteStateMachineState> Create(
            NodeState? parent,
            ISystemContext context,
            NodeId nodeId,
            QualifiedName browseName)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (nodeId.IsNull)
            {
                throw new ArgumentException(
                    "NodeId must not be null.", nameof(nodeId));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "Browse name must not be null.", nameof(browseName));
            }

            var holder = new MutableStateMachineDefinition();
            var sm =
                FluentFiniteStateMachineState.CreateWithHolder(parent!, holder);
            // Defer sm.Create() until FreezeDefinition() so that
            // UseElementNamespace() called between Create() and the
            // first lifecycle method takes effect — OnAfterCreate
            // caches ElementNamespaceIndex from ElementNamespaceUri.
            return new StateMachineBuilder<FluentFiniteStateMachineState>(
                sm, context, holder,
                deferredCreateNodeId: nodeId,
                deferredCreateBrowseName: browseName);
        }

        /// <summary>
        /// Adopts an existing, already-Created
        /// <see cref="FiniteStateMachineState"/> instance and returns a
        /// builder ready for lifecycle-mode chaining. Definition
        /// methods (<see cref="StateMachineBuilder{TState}.AddState"/>
        /// etc.) throw <see cref="InvalidOperationException"/> in this
        /// mode.
        /// </summary>
        /// <typeparam name="TState">The concrete
        /// <see cref="FiniteStateMachineState"/> subclass of the
        /// existing state machine.</typeparam>
        /// <param name="stateMachine">The pre-existing state machine
        /// (e.g. a <c>ShelvedStateMachineState</c> or a
        /// generator-emitted vendor type).</param>
        /// <param name="context">The system context used to drive
        /// auto-transitions and method dispatch.</param>
        public static StateMachineBuilder<TState> For<TState>(
            TState stateMachine,
            ISystemContext context)
            where TState : FiniteStateMachineState
        {
            ArgumentNullException.ThrowIfNull(stateMachine);
            ArgumentNullException.ThrowIfNull(context);
            return new StateMachineBuilder<TState>(stateMachine, context, null);
        }
    }

    /// <summary>
    /// Internal dispatcher that wires builder-collected handlers onto
    /// the state machine's <c>OnBefore/AfterTransition</c> delegates,
    /// resolves the <c>from</c>/<c>to</c> numeric state ids from
    /// <c>LastState</c>/<c>CurrentState</c>, and owns the per-state
    /// timed-transition registry.
    /// </summary>
    /// <typeparam name="TState">The concrete
    /// <see cref="FiniteStateMachineState"/> subclass the dispatcher
    /// wraps.</typeparam>
    internal sealed class StateMachineDispatcher<TState>
        where TState : FiniteStateMachineState
    {
        private readonly TState m_stateMachine;
        private readonly ISystemContext m_context;
        private readonly Dictionary<uint, List<Action<ISystemContext, TState>>> m_enterHandlers = [];
        private readonly Dictionary<uint, List<Action<ISystemContext, TState>>> m_exitHandlers = [];
        private readonly List<Action<ISystemContext, TState, uint, uint>> m_transitionObservers = [];
        private readonly List<Func<ISystemContext, TState, uint, uint, ServiceResult>> m_guards = [];
        private readonly Dictionary<uint, TimedTransitionEntry> m_timedTransitions = [];
        // Async observer counterparts. Scheduled fire-and-forget from
        // the sync transition path (which itself remains sync since
        // FiniteStateMachineState.DoTransition is sync). Each invocation
        // runs on the thread pool with ConfigureAwait(false), so no
        // sync-over-async wait occurs anywhere; exceptions are
        // captured and logged via Debug.WriteLine in line with the
        // existing SafeInvoke pattern.
        private readonly Dictionary<uint,
            List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>>
                m_enterHandlersAsync = [];
        private readonly Dictionary<uint,
            List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>>
                m_exitHandlersAsync = [];
        private readonly
            List<Func<ISystemContext, TState, uint, uint, CancellationToken, System.Threading.Tasks.ValueTask>>
                m_transitionObserversAsync = [];

        private readonly StateMachineTransitionHandler? m_originalBefore;
        private readonly StateMachineTransitionHandler? m_originalAfter;
        private bool m_installed;

        /// <summary>
        /// Thread-keyed pending-from-state so concurrent transitions
        /// do not clobber each other's stash. OPC UA service requests
        /// can hit a node manager concurrently; each DoTransition runs
        /// synchronously on one thread (DispatchBefore -> state update
        /// -> DispatchAfter all on the same thread), so a thread-id
        /// keyed slot is sufficient.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, uint>
            m_pendingFromByThread = new();

        public StateMachineDispatcher(TState stateMachine, ISystemContext context)
        {
            m_stateMachine = stateMachine;
            m_context = context;
            m_originalBefore = stateMachine.OnBeforeTransition;
            m_originalAfter = stateMachine.OnAfterTransition;
        }

        public void AddEnterStateHandler(
            uint stateId, Action<ISystemContext, TState> handler)
        {
            if (!m_enterHandlers.TryGetValue(stateId, out List<Action<ISystemContext, TState>>? list))
            {
                list = [];
                m_enterHandlers[stateId] = list;
            }
            list.Add(handler);
            EnsureInstalled();
        }

        public void AddExitStateHandler(
            uint stateId, Action<ISystemContext, TState> handler)
        {
            if (!m_exitHandlers.TryGetValue(stateId, out List<Action<ISystemContext, TState>>? list))
            {
                list = [];
                m_exitHandlers[stateId] = list;
            }
            list.Add(handler);
            EnsureInstalled();
        }

        public void AddTransitionObserver(
            Action<ISystemContext, TState, uint, uint> handler)
        {
            m_transitionObservers.Add(handler);
            EnsureInstalled();
        }

#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public void AddEnterStateHandlerAsync(
            uint stateId,
            Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            if (!m_enterHandlersAsync.TryGetValue(stateId,
                out List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>? list))
            {
                list = [];
                m_enterHandlersAsync[stateId] = list;
            }
            list.Add(handler);
            EnsureInstalled();
        }

#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public void AddExitStateHandlerAsync(
            uint stateId,
            Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            if (!m_exitHandlersAsync.TryGetValue(stateId,
                out List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>? list))
            {
                list = [];
                m_exitHandlersAsync[stateId] = list;
            }
            list.Add(handler);
            EnsureInstalled();
        }

#pragma warning disable RCS1047 // 'Async' suffix marks the registered callback as async, not this registration method.
        public void AddTransitionObserverAsync(
            Func<ISystemContext, TState, uint, uint, CancellationToken, System.Threading.Tasks.ValueTask> handler)
#pragma warning restore RCS1047
        {
            m_transitionObserversAsync.Add(handler);
            EnsureInstalled();
        }

        public void AddBeforeTransitionGuard(
            Func<ISystemContext, TState, uint, uint, ServiceResult> guard)
        {
            m_guards.Add(guard);
            EnsureInstalled();
        }

        public void InstallCauseMethod(MethodState method, uint causeId)
        {
            method.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
                m_stateMachine.DoCause(ctx, m, causeId, inputs, outputs);
        }

        public void AddTimedTransition(
            uint fromStateId,
            TimeSpan timeout,
            uint transitionId,
            uint causeId)
        {
            // Cancel any pre-existing entry for the same from-state
            // before replacing it, so a re-call of WithTimedTransition
            // doesn't leak a stale timer or fire it with stale ids.
            CancelTimer(fromStateId);

            var entry = new TimedTransitionEntry(timeout, transitionId, causeId);
            m_timedTransitions[fromStateId] = entry;
            EnsureInstalled();

            // If the machine is already in the matching from-state,
            // arm the timer now — otherwise the initial-state entry
            // (applied via SetState, which doesn't fire OnAfterTransition)
            // would never arm a timer.
            uint currentState = ExtractStateId(
                m_stateMachine.CurrentState?.Id?.Value);
            if (currentState == fromStateId)
            {
                ArmTimer(fromStateId, entry);
            }
        }

        private void EnsureInstalled()
        {
            if (m_installed)
            {
                return;
            }
            m_installed = true;
            m_stateMachine.OnBeforeTransition = DispatchBefore;
            m_stateMachine.OnAfterTransition = DispatchAfter;
        }

        private ServiceResult DispatchBefore(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments)
        {
            // Capture from-state BEFORE the framework updates
            // CurrentState. Stored per-thread to support concurrent
            // transitions safely (each DoTransition call is fully
            // synchronous on one thread).
            m_pendingFromByThread[Environment.CurrentManagedThreadId] = ExtractStateId(
                m_stateMachine.CurrentState?.Id?.Value);

            // Builder guards run before any pre-existing OnBefore.
            foreach (Func<ISystemContext, TState, uint, uint, ServiceResult> g in m_guards)
            {
                ServiceResult r = g(context, m_stateMachine, transitionId, causeId);
                if (ServiceResult.IsBad(r))
                {
                    // Clear the per-thread stash on veto — DispatchAfter
                    // won't run, so leave-no-trace.
                    m_pendingFromByThread.TryRemove(
                        Environment.CurrentManagedThreadId, out _);
                    return r;
                }
            }
            if (m_originalBefore != null)
            {
                ServiceResult r = m_originalBefore(
                    context, machine, transitionId, causeId,
                    inputArguments, outputArguments);
                if (ServiceResult.IsBad(r))
                {
                    m_pendingFromByThread.TryRemove(
                        Environment.CurrentManagedThreadId, out _);
                }
                return r;
            }
            return ServiceResult.Good;
        }

        private ServiceResult DispatchAfter(
            ISystemContext context,
            StateMachineState machine,
            uint transitionId,
            uint causeId,
            ArrayOf<Variant> inputArguments,
            List<Variant>? outputArguments)
        {
            // Pre-existing OnAfter runs first so its side effects (e.g.
            // stack-shipped state-change reporting) complete before the
            // builder's observers see the new state.
            ServiceResult? originalResult = null;
            if (m_originalAfter != null)
            {
                originalResult = m_originalAfter(
                    context, machine, transitionId, causeId,
                    inputArguments, outputArguments);
            }

            m_pendingFromByThread.TryRemove(
                Environment.CurrentManagedThreadId, out uint from);
            uint to = ExtractStateId(m_stateMachine.CurrentState?.Id?.Value);

            // Exit handlers fire first, then transition observers, then
            // enter handlers (standard reactive-FSM lifecycle order).
            if (from != 0 && m_exitHandlers.TryGetValue(from, out List<Action<ISystemContext, TState>>? exitList))
            {
                foreach (Action<ISystemContext, TState> h in exitList)
                {
                    SafeInvoke(() => h(context, m_stateMachine));
                }
            }
            if (from != 0 && m_exitHandlersAsync.TryGetValue(from,
                out List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>? exitListAsync))
            {
                foreach (Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> h in exitListAsync)
                {
                    Schedule(h, context, fromAt: from, toAt: 0);
                }
            }

            foreach (Action<ISystemContext, TState, uint, uint> observer in m_transitionObservers)
            {
                SafeInvoke(() => observer(context, m_stateMachine, from, to));
            }
            foreach (Func<ISystemContext, TState, uint, uint, CancellationToken, System.Threading.Tasks.ValueTask>
                observer in m_transitionObserversAsync)
            {
                Schedule(observer, context, from, to);
            }

            if (to != 0 && m_enterHandlers.TryGetValue(to, out List<Action<ISystemContext, TState>>? enterList))
            {
                foreach (Action<ISystemContext, TState> h in enterList)
                {
                    SafeInvoke(() => h(context, m_stateMachine));
                }
            }
            if (to != 0 && m_enterHandlersAsync.TryGetValue(to,
                out List<Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask>>? enterListAsync))
            {
                foreach (Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> h in enterListAsync)
                {
                    Schedule(h, context, fromAt: 0, toAt: to);
                }
            }

            // Cancel timer armed on the from state (if any), then arm
            // the timer for the to state.
            if (from != 0)
            {
                CancelTimer(from);
            }
            if (to != 0 && m_timedTransitions.TryGetValue(to, out TimedTransitionEntry? armEntry))
            {
                ArmTimer(to, armEntry);
            }

            return originalResult ?? ServiceResult.Good;
        }

        private void ArmTimer(uint stateId, TimedTransitionEntry entry)
        {
            var cts = new CancellationTokenSource();
            entry.Cts = cts;
            entry.Timer = new Timer(_ =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    m_stateMachine.DoTransition(
                        m_context,
                        entry.TransitionId,
                        entry.CauseId,
                        default,
                        []);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "StateMachineBuilder timed transition for state " +
                        $"{stateId} threw: {ex}");
                }
            }, null, entry.Timeout, Timeout.InfiniteTimeSpan);
        }

        private void CancelTimer(uint stateId)
        {
            if (m_timedTransitions.TryGetValue(stateId, out TimedTransitionEntry? entry))
            {
                entry.Cts?.Cancel();
                entry.Timer?.Dispose();
                entry.Cts = null;
                entry.Timer = null;
            }
        }

        private static uint ExtractStateId(NodeId? nodeId)
        {
            if (!nodeId.HasValue || nodeId.Value.IsNull)
            {
                return 0;
            }
            return nodeId.Value.TryGetValue(out uint id) ? id : 0;
        }

        private static void SafeInvoke(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"StateMachineBuilder lifecycle handler threw: {ex}");
            }
        }

        private void Schedule(
            Func<ISystemContext, TState, CancellationToken, System.Threading.Tasks.ValueTask> handler,
            ISystemContext context,
            uint fromAt,
            uint toAt)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await handler(context, m_stateMachine, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "StateMachineBuilder async lifecycle handler " +
                        $"(from={fromAt}, to={toAt}) threw: {ex}");
                }
            });
        }

        private void Schedule(
            Func<ISystemContext, TState, uint, uint, CancellationToken, System.Threading.Tasks.ValueTask> handler,
            ISystemContext context,
            uint from,
            uint to)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await handler(context, m_stateMachine, from, to, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "StateMachineBuilder async transition observer " +
                        $"(from={from}, to={to}) threw: {ex}");
                }
            });
        }

        private sealed class TimedTransitionEntry
        {
            public TimedTransitionEntry(TimeSpan timeout, uint transitionId, uint causeId)
            {
                Timeout = timeout;
                TransitionId = transitionId;
                CauseId = causeId;
            }

            public TimeSpan Timeout { get; }
            public uint TransitionId { get; }
            public uint CauseId { get; }
            public CancellationTokenSource? Cts { get; set; }
            public Timer? Timer { get; set; }
        }
    }
}
