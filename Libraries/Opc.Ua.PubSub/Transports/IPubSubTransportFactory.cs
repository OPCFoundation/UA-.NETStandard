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
    /// DI-resolvable factory that creates an <see cref="IPubSubTransport"/>
    /// for a given <see cref="PubSubConnectionDataType"/>. The
    /// application's transport registry holds one factory per supported
    /// <see cref="TransportProfileUri"/> and picks the matching entry at
    /// connection-enable time.
    /// </summary>
    /// <remarks>
    /// Implements the transport-factory contract described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3">
    /// Part 14 §7.3 PubSub transport mappings</see>. Each transport
    /// library (Opc.Ua.PubSub.Udp, Opc.Ua.PubSub.Mqtt) registers one
    /// implementation via DI.
    /// </remarks>
    public interface IPubSubTransportFactory
    {
        /// <summary>
        /// Transport profile URI handled by this factory (e.g.
        /// <see cref="Profiles.PubSubUdpUadpTransport"/>).
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Creates a transport bound to <paramref name="connection"/>.
        /// The returned transport is not yet open; callers invoke
        /// <see cref="IPubSubTransport.OpenAsync"/> after wiring the
        /// transport into the connection state machine.
        /// </summary>
        /// <param name="connection">PubSubConnection configuration.</param>
        /// <param name="telemetry">Telemetry context for logging and metrics.</param>
        /// <param name="timeProvider">Clock used by the transport.</param>
        /// <returns>A transport ready to be opened.</returns>
        IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
