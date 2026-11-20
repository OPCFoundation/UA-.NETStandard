/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.StateMachine
{
    /// <summary>
    /// Classifies *why* a <see cref="PubSubStateMachine"/> transitioned. The
    /// reason is propagated to <see cref="PubSubStateChangedEventArgs.Reason"/>
    /// and surfaced via the matching diagnostics counter so operators can
    /// distinguish e.g. an operator-initiated <c>Enable</c> from a
    /// parent-driven cascade.
    /// </summary>
    /// <remarks>
    /// Reason values mirror the standard
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see> counter classifications
    /// (<c>StateOperationalByMethod</c>, <c>StateOperationalByParent</c>,
    /// <c>StateOperationalFromError</c>, <c>StatePausedByParent</c>,
    /// <c>StateDisabledByMethod</c>) so a state change can be attributed to
    /// a single counter without ambiguity.
    /// </remarks>
    public enum PubSubStateTransitionReason
    {
        /// <summary>
        /// Default / unspecified. Should not normally appear on a successful
        /// transition; used as a placeholder when an event source is unknown.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// The transition was initiated by a configuration method call
        /// (typically <c>Enable</c> or <c>Disable</c> on the standard
        /// <c>PubSubStatusType</c>).
        /// </summary>
        ByMethod,

        /// <summary>
        /// The transition was initiated by a parent component cascading its
        /// own state change to its children (e.g. parent <c>Pause</c> or parent
        /// <c>Disable</c>).
        /// </summary>
        ByParent,

        /// <summary>
        /// The component's own dependencies are now satisfied (transport
        /// ready, metadata available, security keys obtained), allowing a
        /// transition from <c>PreOperational</c> to <c>Operational</c>.
        /// </summary>
        DependenciesReady,

        /// <summary>
        /// The component has recovered from an error condition (e.g. transport
        /// re-connected, security keys refreshed, valid DataSetMessage received
        /// after a receive-timeout).
        /// </summary>
        FromError,

        /// <summary>
        /// The component encountered a fatal condition (transport failure,
        /// signature/decryption failure, unresolvable metadata-version
        /// mismatch, receive timeout, decoder error).
        /// </summary>
        Fatal,

        /// <summary>
        /// The component is being removed from the configuration; per Part 14
        /// §9.1.3.5 children must transition to <c>Disabled</c> before the
        /// component itself is removed.
        /// </summary>
        Removed,

        /// <summary>
        /// The component is being constructed; transition is from the
        /// initial implicit <c>Disabled</c> seed state.
        /// </summary>
        Initial
    }
}
