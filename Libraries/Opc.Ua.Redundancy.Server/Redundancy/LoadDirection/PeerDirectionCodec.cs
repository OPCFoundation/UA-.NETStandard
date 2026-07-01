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
