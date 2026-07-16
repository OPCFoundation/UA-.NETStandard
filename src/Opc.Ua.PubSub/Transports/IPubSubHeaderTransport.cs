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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Optional capability implemented by transports that can attach
    /// per-message key / value properties to an emitted frame (e.g. MQTT 5.0
    /// User Properties). Transports without a header channel (datagram
    /// transports) do not implement this interface; callers detect the
    /// capability with a type check and fall back to the plain
    /// <see cref="IPubSubTransport.SendAsync"/> when it is absent.
    /// </summary>
    /// <remarks>
    /// Used by the transcoding pipeline to promote selected DataSet fields
    /// into broker message properties per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.3">
    /// Part 14 §7.2.3</see>, so downstream consumers can filter on those
    /// values without decoding the payload. On MQTT 3.1.1 (which has no User
    /// Property support) the properties are silently dropped.
    /// </remarks>
    public interface IPubSubHeaderTransport
    {
        /// <summary>
        /// Emits a single frame on the transport together with the supplied
        /// message properties.
        /// </summary>
        /// <param name="payload">
        /// Frame bytes — typically the output of an
        /// <see cref="Encoding.INetworkMessageEncoder.EncodeAsync"/>
        /// invocation.
        /// </param>
        /// <param name="topic">
        /// Broker topic to publish the frame on.
        /// </param>
        /// <param name="properties">
        /// Message properties to attach. An empty set is equivalent to a
        /// plain send.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic,
            ArrayOf<PubSubMessageProperty> properties,
            CancellationToken cancellationToken = default);
    }
}
