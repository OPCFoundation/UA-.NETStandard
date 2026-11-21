// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Non-payload context for a single captured PubSub transport frame.
    /// Passed by reference alongside the frame bytes to an
    /// <see cref="IPubSubCaptureObserver"/> so the observer can record the
    /// datagram / broker message together with the metadata an offline
    /// dissector needs.
    /// </summary>
    public readonly record struct PubSubCaptureContext
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
