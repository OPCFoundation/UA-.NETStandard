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

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Event payload raised by an <see cref="IPubSubTransport"/>
    /// whenever its connection state changes. Carries enough detail
    /// for the owning PubSubConnection state machine to decide
    /// between fault and recovery transitions.
    /// </summary>
    /// <remarks>
    /// Implements the transport-state notification surface required
    /// by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.5">
    /// Part 14 §9.1.5 PubSubConnection address space model</see> so
    /// the connection's <c>PubSubStatusType</c> can mirror the
    /// underlying transport state.
    /// </remarks>
    public sealed class PubSubTransportStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubTransportStateChangedEventArgs"/>.
        /// </summary>
        /// <param name="isConnected">
        /// <see langword="true"/> after a successful connect /
        /// reconnect; <see langword="false"/> after a disconnect.
        /// </param>
        /// <param name="status">
        /// Status code summarising the cause of the change.
        /// </param>
        /// <param name="reason">
        /// Optional human-readable explanation. Must not contain
        /// sensitive data.
        /// </param>
        public PubSubTransportStateChangedEventArgs(bool isConnected, StatusCode status, string? reason)
        {
            IsConnected = isConnected;
            Status = status;
            Reason = reason;
        }

        /// <summary>
        /// Whether the transport is currently connected.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Status code summarising the cause of the state change.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Optional human-readable description.
        /// </summary>
        public string? Reason { get; }
    }
}
