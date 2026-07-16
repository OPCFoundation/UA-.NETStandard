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
    /// Direction of flow an <see cref="IPubSubTransport"/> instance
    /// services. A PubSubConnection may publish, subscribe, or do both;
    /// the transport reports the configured direction so the dispatcher
    /// can skip wiring for the unused side.
    /// </summary>
    /// <remarks>
    /// Implements the publisher / subscriber binding selector from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7 PubSubConnection</see>.
    /// </remarks>
    [Flags]
    public enum PubSubTransportDirection
    {
        /// <summary>
        /// Connection is disabled or otherwise carries no traffic.
        /// </summary>
        None = 0,

        /// <summary>
        /// Publisher direction — the transport sends frames.
        /// </summary>
        Send = 1,

        /// <summary>
        /// Subscriber direction — the transport receives frames.
        /// </summary>
        Receive = 2,

        /// <summary>
        /// Convenience for connections that publish and subscribe over
        /// the same socket.
        /// </summary>
        SendReceive = Send | Receive
    }
}
