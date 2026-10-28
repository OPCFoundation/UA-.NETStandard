// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Tracks the unique HTTP/3 critical unidirectional streams received from one peer.
    /// </summary>
    internal sealed class Http3CriticalStreamRegistry
    {
        /// <summary>
        /// Creates a registry for streams initiated by the remote endpoint.
        /// </summary>
        internal Http3CriticalStreamRegistry(Http3EndpointRole localRole)
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
        /// Gets whether the peer control stream has been registered.
        /// </summary>
        internal bool HasControlStream => m_controlStreamId.HasValue;

        /// <summary>
        /// Gets whether the peer QPACK encoder stream has been registered.
        /// </summary>
        internal bool HasQpackEncoderStream => m_qpackEncoderStreamId.HasValue;

        /// <summary>
        /// Gets whether the peer QPACK decoder stream has been registered.
        /// </summary>
        internal bool HasQpackDecoderStream => m_qpackDecoderStreamId.HasValue;

        /// <summary>
        /// Gets whether all HTTP/3 critical unidirectional streams have been registered.
        /// </summary>
        internal bool HasAllCriticalStreams =>
            HasControlStream && HasQpackEncoderStream && HasQpackDecoderStream;

        /// <summary>
        /// Registers a peer-initiated unidirectional stream after reading its stream-type prefix.
        /// </summary>
        internal Http3CriticalStreamResult Register(ulong streamId, ulong streamType)
        {
            ulong expectedStreamIdType = LocalRole == Http3EndpointRole.Client ? 0x3UL : 0x2UL;
            if ((streamId & 0x3) != expectedStreamIdType)
            {
                return Http3CriticalStreamResult.InvalidStreamId;
            }

            switch (streamType)
            {
                case 0:
                    return RegisterCriticalStream(streamId, ref m_controlStreamId);
                case 2:
                    return RegisterCriticalStream(streamId, ref m_qpackEncoderStreamId);
                case 3:
                    return RegisterCriticalStream(streamId, ref m_qpackDecoderStreamId);
                case 1:
                    return Http3CriticalStreamResult.UnsupportedPushStream;
                default:
                    if (m_ignoredStreams == MaximumIgnoredStreams)
                    {
                        return Http3CriticalStreamResult.IgnoredStreamLimitExceeded;
                    }

                    m_ignoredStreams++;
                    return Http3CriticalStreamResult.IgnoredExtensionStream;
            }
        }

        /// <summary>
        /// Reports closure of a peer stream, failing the connection if it is critical.
        /// </summary>
        internal Http3CriticalStreamResult Close(ulong streamId)
        {
            if (m_controlStreamId == streamId ||
                m_qpackEncoderStreamId == streamId ||
                m_qpackDecoderStreamId == streamId)
            {
                return Http3CriticalStreamResult.ClosedCriticalStream;
            }

            return Http3CriticalStreamResult.IgnoredExtensionStream;
        }

        private static Http3CriticalStreamResult RegisterCriticalStream(ulong streamId, ref ulong? registeredId)
        {
            if (registeredId.HasValue)
            {
                return Http3CriticalStreamResult.DuplicateCriticalStream;
            }

            registeredId = streamId;
            return Http3CriticalStreamResult.CriticalStreamRegistered;
        }

        private const int MaximumIgnoredStreams = 64;
        private int m_ignoredStreams;
        private ulong? m_controlStreamId;
        private ulong? m_qpackEncoderStreamId;
        private ulong? m_qpackDecoderStreamId;
    }

    /// <summary>
    /// Describes the result of registering or closing a peer HTTP/3 stream.
    /// </summary>
    internal enum Http3CriticalStreamResult
    {
        /// <summary>
        /// A critical stream was registered.
        /// </summary>
        CriticalStreamRegistered,

        /// <summary>
        /// A critical stream type was already registered.
        /// </summary>
        DuplicateCriticalStream,

        /// <summary>
        /// A critical stream was closed.
        /// </summary>
        ClosedCriticalStream,

        /// <summary>
        /// A stream identifier does not identify a peer-initiated unidirectional stream.
        /// </summary>
        InvalidStreamId,

        /// <summary>
        /// A server push stream was received without push support.
        /// </summary>
        UnsupportedPushStream,

        /// <summary>
        /// An unknown extension stream was accepted for bounded discard.
        /// </summary>
        IgnoredExtensionStream,

        /// <summary>
        /// The configured count of unknown extension streams was exceeded.
        /// </summary>
        IgnoredStreamLimitExceeded
    }
}
