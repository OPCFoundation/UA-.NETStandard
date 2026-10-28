// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Validates the ordered frames received on one HTTP/3 control stream.
    /// </summary>
    internal sealed class Http3ControlStreamState
    {
        /// <summary>
        /// Creates a validator for the control stream received by the local endpoint.
        /// </summary>
        internal Http3ControlStreamState(Http3EndpointRole localRole)
        {
            if (!Enum.IsDefined(localRole))
            {
                throw new ArgumentOutOfRangeException(nameof(localRole));
            }

            LocalRole = localRole;
        }

        /// <summary>
        /// Gets the local HTTP/3 endpoint role.
        /// </summary>
        internal Http3EndpointRole LocalRole { get; }

        /// <summary>
        /// Gets the peer settings after the initial SETTINGS frame has been accepted.
        /// </summary>
        internal Http3Settings PeerSettings { get; private set; }

        /// <summary>
        /// Gets whether the initial SETTINGS frame has been accepted.
        /// </summary>
        internal bool HasPeerSettings { get; private set; }

        /// <summary>
        /// Gets the latest peer GOAWAY request stream identifier, when present.
        /// </summary>
        internal ulong? PeerGoAwayStreamId { get; private set; }

        /// <summary>
        /// Accepts one complete control-stream frame and validates its ordering and payload.
        /// </summary>
        internal Http3ControlStreamFrameResult AcceptFrame(ulong frameType, ReadOnlySequence<byte> payload)
        {
            if (!HasPeerSettings)
            {
                if (frameType != 0x4)
                {
                    return Http3ControlStreamFrameResult.FrameUnexpected;
                }

                Http3SettingsReadStatus settingsStatus = Http3Settings.TryParse(payload, out Http3Settings settings);
                if (settingsStatus != Http3SettingsReadStatus.Success)
                {
                    return Http3ControlStreamFrameResult.MalformedSettings;
                }
                if (LocalRole == Http3EndpointRole.Server && settings.PeerEnablesConnectProtocol)
                {
                    return Http3ControlStreamFrameResult.MalformedSettings;
                }

                PeerSettings = settings;
                HasPeerSettings = true;
                return Http3ControlStreamFrameResult.Accepted;
            }

            if (frameType == 0x4)
            {
                return Http3ControlStreamFrameResult.FrameUnexpected;
            }

            if (frameType is 0x0 or 0x1 or 0x5)
            {
                return Http3ControlStreamFrameResult.FrameUnexpected;
            }

            if (frameType != 0x7)
            {
                return Http3ControlStreamFrameResult.Accepted;
            }

            SequenceReader<byte> reader = new(payload);
            if (!Http3VarInt.TryRead(ref reader, out ulong streamId) || reader.Remaining != 0 ||
                !IsValidGoAwayStreamId(streamId))
            {
                return Http3ControlStreamFrameResult.InvalidGoAway;
            }

            if (PeerGoAwayStreamId is ulong previous && streamId > previous)
            {
                return Http3ControlStreamFrameResult.InvalidGoAway;
            }

            PeerGoAwayStreamId = streamId;
            return Http3ControlStreamFrameResult.Accepted;
        }

        private bool IsValidGoAwayStreamId(ulong streamId)
        {
            if (LocalRole == Http3EndpointRole.Server)
            {
                return streamId == 0;
            }

            return (streamId & 0x3) == 0;
        }
    }

    /// <summary>
    /// Identifies the local endpoint role in an HTTP/3 connection.
    /// </summary>
    internal enum Http3EndpointRole
    {
        /// <summary>
        /// The local endpoint initiated the HTTP/3 connection.
        /// </summary>
        Client,

        /// <summary>
        /// The local endpoint accepted the HTTP/3 connection.
        /// </summary>
        Server
    }

    /// <summary>
    /// Describes the result of accepting a control-stream frame.
    /// </summary>
    internal enum Http3ControlStreamFrameResult
    {
        /// <summary>
        /// The frame was valid for the control stream state.
        /// </summary>
        Accepted,

        /// <summary>
        /// The frame is invalid or out of order for a control stream.
        /// </summary>
        FrameUnexpected,

        /// <summary>
        /// The SETTINGS frame payload is malformed or exceeds local bounds.
        /// </summary>
        MalformedSettings,

        /// <summary>
        /// The GOAWAY payload or monotonic stream identifier is invalid.
        /// </summary>
        InvalidGoAway
    }
}
