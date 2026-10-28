// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents HTTP/3 settings relevant to the capsule WebTransport profile.
    /// </summary>
    internal readonly record struct Http3Settings(
        ulong QpackMaxTableCapacity,
        ulong QpackBlockedStreams,
        bool PeerEnablesConnectProtocol,
        bool PeerAdvertisesH3Datagrams)
    {
        /// <summary>
        /// Parses a complete SETTINGS payload without retaining unknown setting values.
        /// </summary>
        internal static Http3SettingsReadStatus TryParse(
            ReadOnlySequence<byte> payload,
            out Http3Settings settings)
        {
            Span<ulong> seenSettings = stackalloc ulong[MaximumSettings];
            int settingCount = 0;
            ulong qpackMaxTableCapacity = 0;
            ulong qpackBlockedStreams = 0;
            bool peerEnablesConnectProtocol = false;
            bool peerAdvertisesH3Datagrams = false;
            SequenceReader<byte> reader = new(payload);

            while (reader.Remaining > 0)
            {
                if (!Http3VarInt.TryRead(ref reader, out ulong identifier) ||
                    !Http3VarInt.TryRead(ref reader, out ulong value))
                {
                    settings = default;
                    return Http3SettingsReadStatus.Malformed;
                }

                if (settingCount == MaximumSettings)
                {
                    settings = default;
                    return Http3SettingsReadStatus.TooManySettings;
                }

                for (int index = 0; index < settingCount; index++)
                {
                    if (seenSettings[index] == identifier)
                    {
                        settings = default;
                        return Http3SettingsReadStatus.Malformed;
                    }
                }
                seenSettings[settingCount++] = identifier;

                if (identifier is >= 2 and <= 5)
                {
                    settings = default;
                    return Http3SettingsReadStatus.Malformed;
                }

                switch (identifier)
                {
                    case 1:
                        qpackMaxTableCapacity = value;
                        break;
                    case 7:
                        qpackBlockedStreams = value;
                        break;
                    case 8:
                        if (value > 1)
                        {
                            settings = default;
                            return Http3SettingsReadStatus.Malformed;
                        }
                        peerEnablesConnectProtocol = value == 1;
                        break;
                    case 0x33:
                        if (value > 1)
                        {
                            settings = default;
                            return Http3SettingsReadStatus.Malformed;
                        }
                        peerAdvertisesH3Datagrams = value == 1;
                        break;
                }
            }

            settings = new Http3Settings(
                qpackMaxTableCapacity,
                qpackBlockedStreams,
                peerEnablesConnectProtocol,
                peerAdvertisesH3Datagrams);
            return Http3SettingsReadStatus.Success;
        }

        /// <summary>
        /// Writes SETTINGS values for literal-only QPACK and Extended CONNECT support.
        /// </summary>
        internal static bool TryWriteLocalPayload(
            Http3EndpointRole localRole,
            Span<byte> destination,
            out int bytesWritten)
        {
            if (!Enum.IsDefined(localRole))
            {
                throw new ArgumentOutOfRangeException(nameof(localRole));
            }

            int requiredLength = localRole == Http3EndpointRole.Server ? 6 : 4;
            if (destination.Length < requiredLength)
            {
                bytesWritten = 0;
                return false;
            }

            Http3VarInt.TryWrite(destination, 1, out int identifierLength);
            Http3VarInt.TryWrite(destination[identifierLength..], 0, out int valueLength);
            int offset = identifierLength + valueLength;
            Http3VarInt.TryWrite(destination[offset..], 7, out identifierLength);
            offset += identifierLength;
            Http3VarInt.TryWrite(destination[offset..], 0, out valueLength);
            offset += valueLength;
            if (localRole == Http3EndpointRole.Server)
            {
                Http3VarInt.TryWrite(destination[offset..], 8, out identifierLength);
                offset += identifierLength;
                Http3VarInt.TryWrite(destination[offset..], 1, out valueLength);
                offset += valueLength;
            }
            bytesWritten = offset;
            return true;
        }

        private const int MaximumSettings = 64;
    }

    /// <summary>
    /// Describes the result of parsing an HTTP/3 SETTINGS payload.
    /// </summary>
    internal enum Http3SettingsReadStatus
    {
        /// <summary>
        /// The complete payload is valid.
        /// </summary>
        Success,

        /// <summary>
        /// The payload is truncated or violates a setting rule.
        /// </summary>
        Malformed,

        /// <summary>
        /// The payload exceeds the configured setting count.
        /// </summary>
        TooManySettings
    }
}
