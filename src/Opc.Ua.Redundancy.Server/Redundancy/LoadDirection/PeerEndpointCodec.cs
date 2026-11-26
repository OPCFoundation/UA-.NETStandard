// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Compact binary encoding for a peer's published <c>EndpointDescription</c> set (URL + certificate + security and
    /// user-token policies) used to return a peer's endpoints from a redirected <c>GetEndpoints</c> response.
    /// </summary>
    internal static class PeerEndpointCodec
    {
        private const byte Version = 2;

        /// <summary>
        /// Encodes an endpoint set and its publication timestamp.
        /// </summary>
        public static ByteString Encode(
            ArrayOf<EndpointDescription> endpoints,
            long timestampTicks,
            IServiceMessageContext context)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteByte(null, Version);
            encoder.WriteInt64(null, timestampTicks);
            encoder.WriteEncodeableArray(null, endpoints);
            byte[]? buffer = encoder.CloseAndReturnBuffer();
            return buffer is null ? ByteString.Empty : new ByteString(buffer);
        }

        /// <summary>
        /// Decodes an endpoint payload, returning <c>false</c> when it is malformed or of an unknown version.
        /// </summary>
        public static bool TryDecode(
            ByteString payload,
            IServiceMessageContext context,
            out ArrayOf<EndpointDescription> endpoints,
            out long timestampTicks)
        {
            endpoints = [];
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
                timestampTicks = decoder.ReadInt64(null);
                endpoints = decoder.ReadEncodeableArray<EndpointDescription>(null);
                return true;
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }
    }
}
