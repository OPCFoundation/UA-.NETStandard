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

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Non-payload context for a single captured PubSub transport frame.
    /// Passed by reference alongside the frame bytes to an
    /// <see cref="IPubSubCaptureObserver"/> so the observer can record the
    /// datagram / broker message together with the metadata an offline
    /// dissector needs.
    /// </summary>
    public readonly struct PubSubCaptureContext
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubCaptureContext"/>.
        /// </summary>
        /// <param name="direction">
        /// Direction of the frame relative to the local node.
        /// </param>
        /// <param name="transportProfileUri">
        /// Transport profile URI the frame was observed on (UDP / MQTT).
        /// </param>
        /// <param name="timestamp">
        /// Capture timestamp from the transport clock.
        /// </param>
        /// <param name="endpoint">
        /// Wire endpoint the frame was sent to / received from, for
        /// example <c>239.0.0.1:4840</c> for a UDP datagram. May be
        /// <see langword="null"/> when unavailable.
        /// </param>
        /// <param name="topic">
        /// MQTT topic the frame was delivered on, or
        /// <see langword="null"/> for UDP datagrams.
        /// </param>
        public PubSubCaptureContext(
            PubSubCaptureDirection direction,
            string transportProfileUri,
            DateTimeUtc timestamp,
            string? endpoint = null,
            string? topic = null)
        {
            Direction = direction;
            TransportProfileUri = transportProfileUri ?? string.Empty;
            Timestamp = timestamp;
            Endpoint = endpoint;
            Topic = topic;
        }

        /// <summary>
        /// Direction of the frame relative to the local node.
        /// </summary>
        public PubSubCaptureDirection Direction { get; }

        /// <summary>
        /// Transport profile URI the frame was observed on.
        /// </summary>
        public string TransportProfileUri { get; }

        /// <summary>
        /// Capture timestamp taken from the transport clock.
        /// </summary>
        public DateTimeUtc Timestamp { get; }

        /// <summary>
        /// Wire endpoint the frame was sent to / received from, or
        /// <see langword="null"/> when unavailable.
        /// </summary>
        public string? Endpoint { get; }

        /// <summary>
        /// MQTT topic the frame was delivered on, or
        /// <see langword="null"/> for UDP datagrams.
        /// </summary>
        public string? Topic { get; }
    }
}
