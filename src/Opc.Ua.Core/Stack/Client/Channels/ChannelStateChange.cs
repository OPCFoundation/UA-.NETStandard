/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Notification published by an <see cref="IManagedTransportChannel"/>
    /// when its <see cref="IManagedTransportChannel.State"/> changes.
    /// </summary>
    /// <param name="PreviousState">The state the channel transitioned out of.</param>
    /// <param name="NewState">The state the channel transitioned into.</param>
    /// <param name="Error">Optional <see cref="ServiceResult"/> describing
    /// the cause of the transition (typically populated for transitions
    /// into <see cref="ChannelState.TransportReconnecting"/> or
    /// <see cref="ChannelState.Faulted"/>).</param>
    /// <param name="ReconnectAttempt">Attempt counter for in-progress
    /// reconnect cycles. Reset to 0 at the start of every fresh cycle.</param>
    /// <remarks>
    /// Subscribers receive the notification synchronously from the manager's
    /// reconnect fiber. Handlers must not block, take locks held elsewhere,
    /// or call back into the manager. Use the notification to post work
    /// to a separate worker or to set flags consumed elsewhere.
    /// </remarks>
    public readonly record struct ChannelStateChange(
        ChannelState PreviousState,
        ChannelState NewState,
        ServiceResult? Error,
        int ReconnectAttempt);
}
