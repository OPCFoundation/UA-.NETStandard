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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Every provisional identifier and default the OPC UA Data Channels
    /// errata defines, collected in one place so that a future assignment
    /// by the OPC Foundation is a one line change.
    /// </summary>
    /// <remarks>
    /// Clause references in this type are to the <i>OPC UA Part 6 - Data
    /// Channel Transport</i> and <i>Part 4 - Data Channel Services</i>
    /// errata.
    /// </remarks>
    public static class DataChannelConstants
    {
        /// <summary>
        /// The diagnostic id suppressed by a consumer that opts in to the
        /// experimental data channel surface.
        /// </summary>
        public const string ExperimentalDiagnosticId = DataChannelFeature.ExperimentalDiagnosticId;

        /// <summary>
        /// The documentation url reported alongside
        /// <see cref="ExperimentalDiagnosticId"/>.
        /// </summary>
        public const string ExperimentalUrlFormat = DataChannelFeature.ExperimentalUrlFormat;

        /// <summary>
        /// The connection control channel (Part 6 errata 5.6). Never
        /// assigned by OpenDataChannel; carries only CREDIT, PING and PONG.
        /// </summary>
        public const uint ConnectionControlChannelId = 0;

        /// <summary>
        /// The first ChannelId a server may assign. ChannelIds are
        /// allocated monotonically from this value and are never reused
        /// while the owning SecureChannel is open (Part 6 errata 5.11).
        /// </summary>
        public const uint FirstChannelId = 1;

        /// <summary>
        /// The size of the stream header that begins the secured body of
        /// every frame (Part 6 errata 5.2).
        /// </summary>
        public const int StreamHeaderSize = 12;

        /// <summary>
        /// The size of the optional Deadline field that follows the stream
        /// header when the DeadlinePresent flag is set.
        /// </summary>
        public const int DeadlineSize = 8;

        /// <summary>
        /// Fixed overhead of a frame carried by inline framing: 12 byte
        /// message header, 4 byte symmetric security header, 8 byte
        /// sequence header and the 12 byte stream header
        /// (Part 6 errata 5.5).
        /// </summary>
        public const int InlineFrameOverhead = 12 + 4 + 8 + StreamHeaderSize;

        /// <summary>
        /// Fixed overhead of a frame carried by QUIC framing: the 12 byte
        /// message header and the 12 byte stream header
        /// (Part 6 errata 5.5, 7.4).
        /// </summary>
        public const int QuicFrameOverhead = 12 + StreamHeaderSize;

        /// <summary>
        /// The flag bits held back for a future revision. A receiver
        /// rejects a frame that sets any of them (Part 6 errata 5.4).
        /// </summary>
        public const byte ReservedFlagMask = 0xE0;

        /// <summary>
        /// The RequestId every frame carries. A data channel frame is not
        /// a Service invocation, so any other value is a protocol error
        /// (Part 6 errata 5.1).
        /// </summary>
        public const uint FrameRequestId = 0;

        /// <summary>
        /// The lowest FrameSequenceNumber. Zero is excluded so that an
        /// unset field is unambiguous (Part 6 errata 5.2).
        /// </summary>
        public const uint FirstFrameSequenceNumber = 1;

        /// <summary>
        /// The modulus of the serial number arithmetic used to compare
        /// FrameSequenceNumbers. It is 2^32-1 rather than 2^32 because
        /// zero is excluded from the value space, which is what makes the
        /// wrap from 4294967295 to 1 a distance of one
        /// (Part 6 errata 5.2.1).
        /// </summary>
        public const uint FrameSequenceModulus = uint.MaxValue;

        /// <summary>
        /// The minimum replay window a receiver maintains, in frames, at
        /// or below HighestReceived (Part 6 errata 5.2.1).
        /// </summary>
        public const int MinReplayWindow = 64;

        /// <summary>
        /// The default absolute bound on retained GAP runs per channel and
        /// direction (Part 6 errata 5.2.1).
        /// </summary>
        public const int DefaultMaxGapRuns = 64;

        /// <summary>
        /// The highest defined scheduling priority. Values above it are
        /// revised down, except the no preference encoding
        /// (Part 4 errata 5.1.1).
        /// </summary>
        public const byte MaxPriority = 7;

        /// <summary>
        /// The no preference encoding of Priority. Zero cannot serve as
        /// the sentinel because it is the lowest real priority
        /// (Part 4 errata 5.1.1).
        /// </summary>
        public const byte NoPriorityPreference = 255;

        /// <summary>
        /// The interval in which the OpenDataChannel response must arrive
        /// before the channel faults, in milliseconds
        /// (Part 6 errata 5.14).
        /// </summary>
        public const int DefaultOpenTimeout = 10000;

        /// <summary>
        /// The interval between a peer deciding to close a direction and
        /// emitting END in it, in milliseconds (Part 6 errata 5.14).
        /// </summary>
        public const int DefaultDrainTimeout = 5000;

        /// <summary>
        /// The floor applied to the PING timeout, in milliseconds
        /// (Part 6 errata 5.14).
        /// </summary>
        public const int MinPingTimeout = 1000;

        /// <summary>
        /// The cap applied to the PING timeout, in milliseconds
        /// (Part 6 errata 5.14).
        /// </summary>
        public const int MaxPingTimeout = 30000;

        /// <summary>
        /// The multiple of the measured round trip that bounds an
        /// unanswered PING (Part 6 errata 5.14).
        /// </summary>
        public const int PingTimeoutRoundTripMultiple = 3;

        /// <summary>
        /// The minimum interval between PING frames on one ChannelId, in
        /// milliseconds (Part 6 errata 5.11).
        /// </summary>
        public const int MinPingInterval = 1000;

        /// <summary>
        /// The share of <see cref="MinPingInterval"/> a receiver waits before
        /// it answers another PING on the same ChannelId (Part 6 errata 5.11).
        /// </summary>
        /// <remarks>
        /// Below 1.0 deliberately. The sender's bound is measured on the
        /// sender's clock and the receiver's on the receiver's, so a peer that
        /// honours the one-per-second rule still delivers the occasional PING
        /// a few milliseconds early. Answering slightly sooner than the
        /// nominal interval keeps ordinary jitter from being mistaken for a
        /// violation, and still bounds the work a peer can compel.
        /// </remarks>
        public const double PingResponseIntervalTolerance = 0.8;

        /// <summary>
        /// How many consecutive PING frames breaching the rate bound are
        /// discarded before the channel is reset (Part 6 errata 5.11).
        /// </summary>
        /// <remarks>
        /// The errata permits a receiver to discard an over-rate PING and to
        /// reset the channel once the violation "persists". A single burst is
        /// therefore absorbed silently; only a peer that keeps flooding after
        /// being ignored this many times is treated as hostile.
        /// </remarks>
        public const int MaxPingRateViolations = 16;

        /// <summary>
        /// The minimum interval between Open to Paused state change
        /// Events for one channel, in milliseconds (Part 6 errata 5.13).
        /// </summary>
        public const int PausedEventInterval = 1000;

        /// <summary>
        /// How often an open channel's authorization is re-evaluated even
        /// when nothing observable changed, in milliseconds
        /// (Part 4 errata 7.2).
        /// </summary>
        public const int DefaultAuthorizationRecheckInterval = 60000;

        /// <summary>
        /// The number of 100 nanosecond intervals in one millisecond, used
        /// to convert the FrameDeadline Duration to the on wire Deadline
        /// (Part 6 errata 5.4).
        /// </summary>
        public const long DeadlineTicksPerMillisecond = 10000;

        /// <summary>
        /// The multiple of MaxFrameSize a receiver buffers for ChannelIds
        /// it does not yet know (Part 6 errata 7.4).
        /// </summary>
        public const int UnknownChannelBufferFrames = 4;

        /// <summary>
        /// The SequenceNumber headroom below which a sender initiates
        /// channel token renewal (Part 6 errata 5.1.1).
        /// </summary>
        public const uint SequenceNumberRenewalHeadroom = 1u << 30;

        /// <summary>
        /// The window, in seconds, of expected chunk emission that also
        /// triggers renewal when it exceeds the remaining SequenceNumber
        /// space (Part 6 errata 5.1.1).
        /// </summary>
        public const int SequenceNumberRenewalSeconds = 60;

        /// <summary>
        /// The provisional ALPN protocol identifier for opc.quic
        /// (Part 6 errata 7.2).
        /// </summary>
        public const string QuicAlpnProtocol = "opcua/1";

        /// <summary>
        /// The url scheme of the QUIC transport (Part 6 errata 7.2).
        /// </summary>
        public const string QuicScheme = Utils.UriSchemeOpcQuic;

        /// <summary>
        /// The TransportProfileUri of the QUIC transport
        /// (Part 6 errata 7.2).
        /// </summary>
        public const string QuicTransportProfileUri = Profiles.UaQuicTransport;

        /// <summary>
        /// The TLS exporter label that binds the QUIC key schedule to the
        /// SecureChannel thumbprint (Part 6 errata 7.6.1).
        /// </summary>
        public const string QuicExporterLabel = "EXPORTER-opcua-quic";

        /// <summary>
        /// The default port of the QUIC transport. It does not collide
        /// with TCP 4840 because it is a UDP port (Part 6 errata 7.2).
        /// </summary>
        public const int QuicDefaultPort = 4840;
    }
}
