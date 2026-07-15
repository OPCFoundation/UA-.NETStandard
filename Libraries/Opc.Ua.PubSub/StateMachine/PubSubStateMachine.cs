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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.StateMachine
{
    /// <summary>
    /// Sealed, hierarchical state machine implementing the
    /// <see cref="PubSubState"/> transition rules of OPC UA Part 14.
    /// One instance is owned by every Application / Connection / Group /
    /// Writer / Reader component and carries the parent ↔ child propagation
    /// semantics that Part 14 mandates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the state model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.1">
    /// Part 14 §6.2.1 PubSubState</see> and the Enable / Disable preconditions
    /// from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.10">
    /// Part 14 §9.1.10 PubSubStatusType</see>. Parent-child propagation
    /// (<see cref="TryDisable"/> cascading to children before the parent
    /// itself transitions) implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.3.5">
    /// Part 14 §9.1.3.5 RemoveConnection</see>.
    /// </para>
    /// <para>
    /// Threading: the machine serialises *all* state mutations through an
    /// internal <see cref="Lock"/>; child registration,
    /// parent propagation, and event raising are atomic with respect to one
    /// another from the caller's perspective. The lock is never exposed —
    /// callers cannot deadlock with it.
    /// </para>
    /// </remarks>
    public sealed class PubSubStateMachine
    {
        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly List<PubSubStateMachine> m_children = [];
        private PubSubStateMachine? m_parent;
        private PubSubState m_state;
        private StatusCode m_statusCode;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PubSubStateMachine"/> in the
        /// <see cref="PubSubState.Disabled"/> seed state.
        /// </summary>
        /// <param name="componentName">
        /// Human-readable name used for diagnostics and audit messages
        /// (e.g. the configuration <c>Name</c> of the owning component).
        /// </param>
        /// <param name="componentKind">Kind of component this machine tracks.</param>
        /// <param name="logger">Contextual logger; required.</param>
        /// <param name="initialState">Initial state to seed from a runtime-state store.</param>
        public PubSubStateMachine(
            string componentName,
            PubSubComponentKind componentKind,
            ILogger logger,
            PubSubState initialState = PubSubState.Disabled)
        {
            if (componentName is null)
            {
                throw new ArgumentNullException(nameof(componentName));
            }
            if (componentName.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(componentName));
            }
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            ComponentName = componentName;
            ComponentKind = componentKind;
            m_logger = logger;
            m_state = initialState;
            m_statusCode = DefaultStatusCodeFor(initialState);
        }

        /// <summary>
        /// Raised after every successful state transition. Subscribers should
        /// be lightweight; the event is invoked while the machine's internal
        /// lock is *not* held, but the new state has already been published.
        /// </summary>
        public event EventHandler<PubSubStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Human-readable name of the owning component.
        /// </summary>
        public string ComponentName { get; }

        /// <summary>
        /// Kind of component this machine tracks.
        /// </summary>
        public PubSubComponentKind ComponentKind { get; }

        /// <summary>
        /// The current <see cref="PubSubState"/> after the last accepted
        /// transition. Reads are lock-free; the field is updated as part of
        /// every <c>Try*</c> call before <see cref="StateChanged"/> fires.
        /// </summary>
        public PubSubState State
        {
            get
            {
                lock (m_lock)
                {
                    return m_state;
                }
            }
        }

        /// <summary>
        /// The current StatusCode reflecting the cause of <see cref="State"/>.
        /// </summary>
        public StatusCode StatusCode
        {
            get
            {
                lock (m_lock)
                {
                    return m_statusCode;
                }
            }
        }

        /// <summary>
        /// The parent state machine, if this is a child. Set automatically
        /// by <see cref="AttachChild"/>.
        /// </summary>
        public PubSubStateMachine? Parent
        {
            get
            {
                lock (m_lock)
                {
                    return m_parent;
                }
            }
        }

        /// <summary>
        /// Snapshot of currently attached children. Safe to enumerate by the
        /// caller without holding any locks.
        /// </summary>
        public IReadOnlyList<PubSubStateMachine> Children
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_children];
                }
            }
        }

        /// <summary>
        /// Attaches <paramref name="child"/> as a child of this machine and
        /// stores a back-reference on the child so parent-driven cascades
        /// (Disable, Pause) can reach it.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="child"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="child"/> already has a parent, is this instance,
        /// or this instance has been disposed.
        /// </exception>
        public void AttachChild(PubSubStateMachine child)
        {
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }
            if (ReferenceEquals(child, this))
            {
                throw new InvalidOperationException(
                    "A PubSubStateMachine cannot be its own child.");
            }
            lock (m_lock)
            {
                ThrowIfDisposedLocked();
                if (child.m_parent != null)
                {
                    throw new InvalidOperationException(
                        $"Child '{child.ComponentName}' already has a parent " +
                        $"('{child.m_parent.ComponentName}').");
                }
                m_children.Add(child);
                child.m_parent = this;
            }
        }

        /// <summary>
        /// Detaches a previously attached child. Has no effect if the child
        /// is not attached to this instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void DetachChild(PubSubStateMachine child)
        {
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }
            lock (m_lock)
            {
                if (m_children.Remove(child))
                {
                    child.m_parent = null;
                }
            }
        }

        /// <summary>
        /// Attempts to transition the machine from
        /// <see cref="PubSubState.Disabled"/> to <see cref="PubSubState.PreOperational"/>,
        /// or to <see cref="PubSubState.Paused"/> if its parent is not operational.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the transition succeeded;
        /// <see langword="false"/> if the current state is not
        /// <see cref="PubSubState.Disabled"/> (Part 14 §9.1.10.2 rejection).
        /// </returns>
        public bool TryEnable(PubSubStateTransitionReason reason = PubSubStateTransitionReason.ByMethod)
        {
            PubSubState target = ParentCanRun() ? PubSubState.PreOperational : PubSubState.Paused;
            return TryTransition(
                target,
                reason,
                DefaultStatusCodeFor(target),
                allowed: from => from == PubSubState.Disabled);
        }

        /// <summary>
        /// Attempts to mark the machine as <see cref="PubSubState.Operational"/>
        /// after its dependencies have become ready. Valid only from
        /// <see cref="PubSubState.PreOperational"/> or
        /// <see cref="PubSubState.Error"/> (recovery path).
        /// </summary>
        /// <param name="reason">
        /// Use <see cref="PubSubStateTransitionReason.DependenciesReady"/> on
        /// initial readiness, <see cref="PubSubStateTransitionReason.FromError"/>
        /// for recovery, or <see cref="PubSubStateTransitionReason.ByParent"/>
        /// when driven by an enclosing component.
        /// </param>
        public bool TryMarkOperational(
            PubSubStateTransitionReason reason = PubSubStateTransitionReason.DependenciesReady)
        {
            return TryTransition(
                PubSubState.Operational,
                reason,
                StatusCodes.Good,
                allowed: from => from is PubSubState.PreOperational or PubSubState.Error);
        }

        /// <summary>
        /// Attempts to pause the machine. Valid from
        /// <see cref="PubSubState.Operational"/>, <see cref="PubSubState.PreOperational"/>
        /// or <see cref="PubSubState.Error"/>; rejected from <see cref="PubSubState.Disabled"/>.
        /// </summary>
        public bool TryPause(PubSubStateTransitionReason reason = PubSubStateTransitionReason.ByMethod)
        {
            return TryTransition(
                PubSubState.Paused,
                reason,
                StatusCodes.GoodNoData,
                allowed: from => from is PubSubState.Operational or PubSubState.PreOperational or PubSubState.Error);
        }

        /// <summary>
        /// Attempts to resume a paused machine back to <see cref="PubSubState.PreOperational"/>.
        /// </summary>
        public bool TryResume(PubSubStateTransitionReason reason = PubSubStateTransitionReason.ByMethod)
        {
            return TryTransition(
                PubSubState.PreOperational,
                reason,
                StatusCodes.GoodCallAgain,
                allowed: from => from == PubSubState.Paused);
        }

        /// <summary>
        /// Forces the machine into <see cref="PubSubState.Error"/> with the
        /// given status code. Valid from every state except
        /// <see cref="PubSubState.Disabled"/> (a disabled component cannot
        /// fail). The transition reason defaults to
        /// <see cref="PubSubStateTransitionReason.Fatal"/>.
        /// </summary>
        public bool TryFault(
            StatusCode errorStatus,
            PubSubStateTransitionReason reason = PubSubStateTransitionReason.Fatal)
        {
            TryPauseChildrenCascade();
            return TryTransition(
                PubSubState.Error,
                reason,
                errorStatus,
                allowed: from => from is PubSubState.PreOperational or PubSubState.Operational or PubSubState.Error);
        }

        /// <summary>
        /// Disables the machine and *all* its children first, per Part 14
        /// §9.1.3.5 (children must transition to <see cref="PubSubState.Disabled"/>
        /// before the parent transitions). Returns <see langword="false"/> only
        /// when the machine is already <see cref="PubSubState.Disabled"/>
        /// (Part 14 §9.1.10.3 rejection).
        /// </summary>
        public bool TryDisable(PubSubStateTransitionReason reason = PubSubStateTransitionReason.ByMethod)
        {
            PubSubStateMachine[] childSnapshot;
            lock (m_lock)
            {
                if (m_state == PubSubState.Disabled)
                {
                    m_logger.DisableRejectedAlreadyDisabled(ComponentName, ComponentKind);
                    return false;
                }
                childSnapshot = [.. m_children];
            }
            foreach (PubSubStateMachine child in childSnapshot)
            {
                _ = child.TryDisable(
                    reason == PubSubStateTransitionReason.Removed
                        ? PubSubStateTransitionReason.Removed
                        : PubSubStateTransitionReason.ByParent);
            }
            return TryTransition(
                PubSubState.Disabled,
                reason,
                StatusCodes.BadInvalidState,
                allowed: from => from != PubSubState.Disabled);
        }

        /// <summary>
        /// Cascades a parent-driven <see cref="TryPause"/> to all children
        /// (recursively), then pauses this machine itself if it is currently
        /// in a pausable state.
        /// </summary>
        public bool TryPauseCascade()
        {
            PubSubStateMachine[] childSnapshot;
            lock (m_lock)
            {
                childSnapshot = [.. m_children];
            }
            foreach (PubSubStateMachine child in childSnapshot)
            {
                _ = child.TryPauseCascade();
            }
            return TryPause(PubSubStateTransitionReason.ByParent);
        }

        private void TryPauseChildrenCascade()
        {
            PubSubStateMachine[] childSnapshot;
            lock (m_lock)
            {
                childSnapshot = [.. m_children];
            }

            foreach (PubSubStateMachine child in childSnapshot)
            {
                _ = child.TryPauseCascade();
            }
        }

        /// <summary>
        /// Cascades a parent-driven resume to all paused children recursively.
        /// </summary>
        public bool TryResumeCascade()
        {
            bool changed = TryResume(PubSubStateTransitionReason.ByParent);
            PubSubStateMachine[] childSnapshot;
            lock (m_lock)
            {
                childSnapshot = [.. m_children];
            }
            foreach (PubSubStateMachine child in childSnapshot)
            {
                _ = child.TryResumeCascade();
            }
            return changed;
        }

        /// <summary>
        /// Marks the machine for removal: children are disabled first
        /// (Part 14 §9.1.3.5), then this machine is disabled, then detached
        /// from its parent. Idempotent.
        /// </summary>
        public void MarkRemoved()
        {
            _ = TryDisable(PubSubStateTransitionReason.Removed);
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_parent?.DetachChild(this);
            }
        }

        /// <summary>
        /// Restores a persisted state without raising a transition event.
        /// </summary>
        /// <param name="state">Persisted PubSub state.</param>
        public void Restore(PubSubState state)
        {
            lock (m_lock)
            {
                ThrowIfDisposedLocked();
                m_state = state;
                m_statusCode = DefaultStatusCodeFor(state);
            }
        }

        /// <summary>
        /// Returns the canonical Part 14 status code for a state.
        /// </summary>
        internal static StatusCode DefaultStatusCodeFor(PubSubState state)
        {
            return state switch
            {
                PubSubState.Operational => StatusCodes.Good,
                PubSubState.Paused => StatusCodes.GoodNoData,
                PubSubState.PreOperational => StatusCodes.GoodCallAgain,
                PubSubState.Error => StatusCodes.BadInternalError,
                PubSubState.Disabled => StatusCodes.BadInvalidState,
                _ => StatusCodes.BadUnexpectedError
            };
        }

        private bool TryTransition(
            PubSubState target,
            PubSubStateTransitionReason reason,
            StatusCode statusCode,
            Func<PubSubState, bool> allowed)
        {
            PubSubStateChangedEventArgs? evt;
            lock (m_lock)
            {
                ThrowIfDisposedLocked();
                PubSubState from = m_state;
                if (!allowed(from))
                {
                    m_logger.RejectedTransition(ComponentName, ComponentKind, from, target, reason);
                    return false;
                }
                if (from == target)
                {
                    // Same-state transition is accepted as a status-only
                    // update (e.g. fault-while-faulted refreshes the
                    // StatusCode but does not raise a state-changed event).
                    m_statusCode = statusCode;
                    return true;
                }
                m_state = target;
                m_statusCode = statusCode;
                evt = new PubSubStateChangedEventArgs(
                    ComponentName,
                    ComponentKind,
                    from,
                    target,
                    reason,
                    statusCode);
            }

            m_logger.Transitioned(
                evt.ComponentName,
                evt.ComponentKind,
                evt.PreviousState,
                evt.NewState,
                evt.Reason,
                evt.StatusCode);

            try
            {
                StateChanged?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                // Listener exceptions must never destabilise the state
                // machine. Log and swallow.
                m_logger.StateChangedHandlerThrew(ex, ComponentName, ComponentKind);
            }

            return true;
        }

        private bool ParentCanRun()
        {
            PubSubStateMachine? parent;
            lock (m_lock)
            {
                parent = m_parent;
            }
            return parent is null || parent.State is not (PubSubState.Disabled or PubSubState.Paused);
        }

        private void ThrowIfDisposedLocked()
        {
            if (m_disposed)
            {
                throw new InvalidOperationException(
                    $"PubSubStateMachine '{ComponentName}' has been removed.");
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PubSubStateMachine"/>.
    /// </summary>
    internal static partial class PubSubStateMachineLog
    {
        [LoggerMessage(EventId = PubSubEventIds.PubSubStateMachine + 0, Level = LogLevel.Debug,
            Message = "PubSubStateMachine '{Component}' ({Kind}) Disable rejected: already Disabled.")]
        public static partial void DisableRejectedAlreadyDisabled(
            this ILogger logger,
            string component,
            PubSubComponentKind kind);

        [LoggerMessage(EventId = PubSubEventIds.PubSubStateMachine + 1, Level = LogLevel.Debug,
            Message = "PubSubStateMachine '{Component}' ({Kind}) rejected transition {From} -> {To} " +
                "(reason {Reason}).")]
        public static partial void RejectedTransition(
            this ILogger logger,
            string component,
            PubSubComponentKind kind,
            PubSubState from,
            PubSubState to,
            PubSubStateTransitionReason reason);

        [LoggerMessage(EventId = PubSubEventIds.PubSubStateMachine + 2, Level = LogLevel.Information,
            Message = "PubSubStateMachine '{Component}' ({Kind}) transitioned {From} -> {To} " +
                "(reason {Reason}, status {Status}).")]
        public static partial void Transitioned(
            this ILogger logger,
            string component,
            PubSubComponentKind kind,
            PubSubState from,
            PubSubState to,
            PubSubStateTransitionReason reason,
            StatusCode status);

        [LoggerMessage(EventId = PubSubEventIds.PubSubStateMachine + 3, Level = LogLevel.Error,
            Message = "PubSubStateMachine '{Component}' ({Kind}) StateChanged handler threw.")]
        public static partial void StateChangedHandlerThrew(
            this ILogger logger,
            Exception exception,
            string component,
            PubSubComponentKind kind);
    }

}
