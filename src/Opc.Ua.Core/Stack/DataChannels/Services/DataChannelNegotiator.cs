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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// What a data channel source will accept, read from
    /// IDataChannelSourceType in the AddressSpace or supplied directly by
    /// a server that hosts its endpoints in code.
    /// </summary>
    public sealed record DataChannelSourceCapabilities
    {
        /// <summary>
        /// The directions this endpoint can carry data in, from the point
        /// of view of the server as the source. Not revisable: a
        /// direction the source does not support is rejected.
        /// </summary>
        public DataChannelDirection Direction { get; init; }
            = DataChannelDirection.SourceToSink;

        /// <summary>
        /// The delivery modes this endpoint accepts. A mode that is not
        /// listed is rejected rather than silently substituted.
        /// </summary>
        public IReadOnlyList<DataChannelDeliveryMode> SupportedDeliveryModes { get; init; }
            = [DataChannelDeliveryMode.ReliableOrdered];

        /// <summary>
        /// The IANA media type of the byte stream this endpoint produces
        /// or consumes.
        /// </summary>
        public string ContentType { get; init; } = "application/octet-stream";

        /// <summary>
        /// Content specific parameters that qualify
        /// <see cref="ContentType"/>. Opaque to the data channel layer.
        /// </summary>
        public IReadOnlyList<KeyValuePair> ContentParameters { get; init; } = [];

        /// <summary>
        /// The largest frame payload this endpoint will emit or accept,
        /// or zero when only the server and transport bounds apply.
        /// </summary>
        public uint MaxFrameSize { get; init; }

        /// <summary>
        /// The peak rate this endpoint may produce, in bits per second,
        /// or zero when unconstrained.
        /// </summary>
        public uint MaxBitrate { get; init; }

        /// <summary>
        /// The priority applied when the client requests the no
        /// preference encoding.
        /// </summary>
        public byte Priority { get; init; }

        /// <summary>
        /// The most channels that may be open on this endpoint at once,
        /// or zero for no endpoint specific limit.
        /// </summary>
        public ushort MaxChannels { get; init; }
    }

    /// <summary>
    /// The server wide limits a client reads from
    /// ServerCapabilities.DataChannelCapabilities before it negotiates.
    /// </summary>
    public sealed record DataChannelServerCapabilities
    {
        /// <summary>
        /// The most channels the server keeps open on one SecureChannel.
        /// </summary>
        public ushort MaxDataChannels { get; init; } = 16;

        /// <summary>
        /// The largest frame payload the server will emit or accept on
        /// any endpoint, before the transport bound is applied.
        /// </summary>
        public uint MaxFrameSize { get; init; } = 65536;

        /// <summary>
        /// The delivery modes the server implements. A mode absent here
        /// is unsupported everywhere on this server.
        /// </summary>
        public IReadOnlyList<DataChannelDeliveryMode> SupportedDeliveryModes { get; init; }
            = [DataChannelDeliveryMode.ReliableOrdered, DataChannelDeliveryMode.ReliableUnordered];

        /// <summary>
        /// The TransportProfileUris over which this server carries data
        /// channels.
        /// </summary>
        public IReadOnlyList<string> SupportedTransportProfileUris { get; init; }
            = [Profiles.UaTcpTransport];

        /// <summary>
        /// The largest flow control window the server will grant to one
        /// channel. Mandatory in the model because the connection level
        /// bootstrap is bounded by it multiplied by the channel count.
        /// </summary>
        public uint MaxCreditPerChannel { get; init; } = 1024 * 1024;

        /// <summary>
        /// The aggregate rate the server will emit across all data
        /// channels of one SecureChannel, or zero when unconstrained.
        /// </summary>
        public uint MaxTotalBitrate { get; init; }

        /// <summary>
        /// True when the server can carry Unreliable channels over a
        /// genuinely lossy path. False on a server reachable only over
        /// opc.tcp or opc.wss, where Unreliable degrades to sender side
        /// discard.
        /// </summary>
        public bool SupportsUnreliableDatagrams { get; init; }

        /// <summary>
        /// True only where the server permits a data channel on a
        /// SecureChannel whose SecurityMode is None. Absence is read as
        /// false, and the default is false, because on such a channel a
        /// frame carries neither signature nor encryption and every
        /// protection rests on bytes anyone on the path can rewrite.
        /// </summary>
        public bool AllowInsecureDataChannels { get; init; }
    }

    /// <summary>
    /// Applies the parameter revision rules of Part 4 errata 5.1.1 and
    /// 5.1 to an OpenDataChannel or ModifyDataChannel request.
    /// </summary>
    /// <remarks>
    /// The server revises rather than rejects wherever it can, because a
    /// client that asked for more than it can have usually wants the
    /// largest amount available. Direction and DeliveryMode are the two
    /// exceptions: silently downgrading to a stronger guarantee would add
    /// unbounded latency to a media channel, and silently downgrading to
    /// a weaker one would lose data.
    /// </remarks>
    public static class DataChannelNegotiator
    {
        /// <summary>
        /// Revises the requested parameters against what the source, the
        /// server and the transport will actually do.
        /// </summary>
        /// <param name="requested">The parameters the client asked for. A
        /// zero in a numeric member means no preference.</param>
        /// <param name="source">What the endpoint accepts.</param>
        /// <param name="server">The server wide limits.</param>
        /// <param name="transportMaxFrameSize">The bound the negotiated
        /// buffer size imposes on a frame payload.</param>
        /// <param name="transportIsReliable">True when the transport
        /// itself provides reliability, in which case MaxRetransmits and
        /// FrameDeadline are returned as zero so the client can see they
        /// had no effect.</param>
        /// <param name="revised">The parameters actually in force.</param>
        /// <param name="error">Why the request was refused.</param>
        /// <returns>True when a channel can be opened on these terms.</returns>
        public static bool TryRevise(
            DataChannelParametersDataType? requested,
            DataChannelSourceCapabilities source,
            DataChannelServerCapabilities server,
            uint transportMaxFrameSize,
            bool transportIsReliable,
            out DataChannelParametersDataType revised,
            out StatusCode error)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            revised = new DataChannelParametersDataType();
            requested ??= new DataChannelParametersDataType();

            // Direction is not revisable.
            if (!IsDirectionSupported(requested.Direction, source.Direction))
            {
                error = StatusCodes.BadDataChannelDirectionUnsupported;
                return false;
            }

            // DeliveryMode is not revisable either.
            if (!Contains(source.SupportedDeliveryModes, requested.DeliveryMode) ||
                !Contains(server.SupportedDeliveryModes, requested.DeliveryMode))
            {
                error = StatusCodes.BadDeliveryModeUnsupported;
                return false;
            }

            if (!string.IsNullOrEmpty(requested.ContentType) &&
                !string.Equals(requested.ContentType, source.ContentType, StringComparison.OrdinalIgnoreCase) &&
                !IsWildcardMatch(requested.ContentType, source.ContentType))
            {
                error = StatusCodes.BadContentTypeUnsupported;
                return false;
            }

            revised.Direction = requested.Direction;
            revised.DeliveryMode = requested.DeliveryMode;

            // The server may narrow ContentType to a more specific type it
            // will actually produce.
            revised.ContentType = source.ContentType;

            // Entries the server does not understand are omitted from the
            // response rather than echoed, so a client can see what took
            // effect.
            revised.ContentParameters = [.. source.ContentParameters];

            uint maxFrameSize = Least(
                requested.MaxFrameSize,
                source.MaxFrameSize,
                server.MaxFrameSize,
                transportMaxFrameSize);

            if (maxFrameSize == 0)
            {
                error = StatusCodes.BadDataChannelLimitsExceeded;
                return false;
            }

            revised.MaxFrameSize = maxFrameSize;
            revised.MaxBitrate = ReviseMaxBitrate(requested.MaxBitrate, source.MaxBitrate);

            uint initialCredit = requested.InitialCredit == 0
                ? server.MaxCreditPerChannel
                : requested.InitialCredit;

            if (initialCredit > server.MaxCreditPerChannel)
            {
                initialCredit = server.MaxCreditPerChannel;
            }

            // A window smaller than one frame is an immediate deadlock:
            // the channel opens Paused and the first frame can never be
            // sent.
            if (initialCredit < maxFrameSize)
            {
                initialCredit = maxFrameSize;
            }

            revised.InitialCredit = initialCredit;

            // Zero is a real priority, so the no preference encoding is
            // 255. Any other value above seven is revised down.
            revised.Priority = requested.Priority == DataChannelConstants.NoPriorityPreference
                ? source.Priority
                : Math.Min(requested.Priority, DataChannelConstants.MaxPriority);

            if (transportIsReliable ||
                requested.DeliveryMode is DataChannelDeliveryMode.ReliableOrdered
                    or DataChannelDeliveryMode.ReliableUnordered)
            {
                revised.MaxRetransmits = 0;
                revised.FrameDeadline = 0;
            }
            else
            {
                revised.MaxRetransmits = requested.MaxRetransmits;
                revised.FrameDeadline = requested.FrameDeadline < 0
                    ? 0
                    : requested.FrameDeadline;
            }

            error = StatusCodes.Good;
            return true;
        }

        /// <summary>
        /// Checks that a ModifyDataChannel request leaves the immutable
        /// parameters alone. Both change what the receiver's pipeline is,
        /// so neither may be altered on a live channel.
        /// </summary>
        /// <param name="inForce">The parameters currently in force.</param>
        /// <param name="requested">The requested parameters.</param>
        public static bool IsMutation(
            DataChannelParametersDataType inForce,
            DataChannelParametersDataType? requested)
        {
            if (inForce == null)
            {
                throw new ArgumentNullException(nameof(inForce));
            }

            return requested != null &&
                (requested.Direction != inForce.Direction ||
                 requested.DeliveryMode != inForce.DeliveryMode);
        }

        private static bool IsDirectionSupported(
            DataChannelDirection requested,
            DataChannelDirection supported)
        {
            // A Bidirectional source can carry either half; a
            // unidirectional source carries only its own direction.
            return supported == DataChannelDirection.Bidirectional || requested == supported;
        }

        private static bool Contains(
            IReadOnlyList<DataChannelDeliveryMode>? modes,
            DataChannelDeliveryMode mode)
        {
            if (modes == null)
            {
                return false;
            }

            for (int ii = 0; ii < modes.Count; ii++)
            {
                if (modes[ii] == mode)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWildcardMatch(string requested, string offered)
        {
            int slash = requested.IndexOf('/', StringComparison.Ordinal);

            if (slash < 0 || slash == requested.Length - 1)
            {
                return false;
            }

            return requested.AsSpan(slash + 1).SequenceEqual("*".AsSpan()) &&
                offered.AsSpan(0, Math.Min(slash + 1, offered.Length))
                    .SequenceEqual(requested.AsSpan(0, slash + 1));
        }

        private static uint Least(uint requested, uint source, uint server, uint transport)
        {
            uint value = requested == 0 ? uint.MaxValue : requested;

            if (source != 0 && source < value)
            {
                value = source;
            }

            if (server != 0 && server < value)
            {
                value = server;
            }

            if (transport != 0 && transport < value)
            {
                value = transport;
            }

            return value == uint.MaxValue ? 0 : value;
        }

        private static uint ReviseMaxBitrate(uint requested, uint source)
        {
            if (requested == 0)
            {
                return source;
            }

            return source != 0 && source < requested ? source : requested;
        }

    }
}
