// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Compact binary encoding for a single gossiped direction signal: a (ServerUri, byte value, UTC-ticks timestamp)
    /// triple. The <c>ServiceLevel</c> and load-weight keyspaces both use this format; the byte carries the
    /// <c>ServiceLevel</c> or the load weight respectively.
    /// </summary>
    internal static class PeerDirectionCodec
    {
        private const byte Version = 1;

        /// <summary>
        /// Encodes a signal into a self-describing payload.
        /// </summary>
        public static ByteString Encode(
            string serverUri,
            byte value,
            long timestampTicks,
            IServiceMessageContext context)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteByte(null, Version);
            encoder.WriteString(null, serverUri);
            encoder.WriteByte(null, value);
            encoder.WriteInt64(null, timestampTicks);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        /// <summary>
        /// Decodes a signal payload, returning <c>false</c> when it is malformed or of an unknown version.
        /// </summary>
        public static bool TryDecode(
            ByteString payload,
            IServiceMessageContext context,
            out string serverUri,
            out byte value,
            out long timestampTicks)
        {
            serverUri = string.Empty;
            value = 0;
            timestampTicks = 0;

            if (payload.IsNull || payload.Length == 0)
            {
                return false;
            }

            try
            {
                using var decoder = new BinaryDecoder(payload.ToArray(), context);
                if (decoder.ReadByte(null) != Version)
                {
                    return false;
                }
                serverUri = decoder.ReadString(null) ?? string.Empty;
                value = decoder.ReadByte(null);
                timestampTicks = decoder.ReadInt64(null);
                return serverUri.Length != 0;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }
    }
}
