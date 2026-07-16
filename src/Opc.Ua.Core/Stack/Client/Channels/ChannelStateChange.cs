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
