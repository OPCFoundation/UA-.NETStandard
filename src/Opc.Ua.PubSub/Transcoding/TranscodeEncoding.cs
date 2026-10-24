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

using System;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Selects the target NetworkMessage mapping produced by a
    /// transcoder. Mirrors the two payload encodings implemented by the
    /// stack (UADP and JSON) so a route can request cross-encoding
    /// independently of the concrete transport profile URI.
    /// </summary>
    /// <remarks>
    /// Implements the encoding selection implied by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP NetworkMessage mapping</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5 JSON NetworkMessage mapping</see>.
    /// </remarks>
    public enum TranscodeEncoding
    {
        /// <summary>
        /// UADP binary NetworkMessage mapping (Part 14 §7.2.4).
        /// </summary>
        Uadp,

        /// <summary>
        /// JSON NetworkMessage mapping (Part 14 §7.2.5).
        /// </summary>
        Json,

        /// <summary>
        /// Experimental Apache Avro NetworkMessage mapping (OPC UA Part 14 draft).
        /// </summary>
        Avro
    }

    /// <summary>
    /// Helper conversions between <see cref="TranscodeEncoding"/> and the
    /// transport profile URIs used to key the pluggable
    /// <see cref="Encoding.INetworkMessageEncoder"/> /
    /// <see cref="Encoding.INetworkMessageDecoder"/> registries.
    /// </summary>
    public static class TranscodeEncodingExtensions
    {
        /// <summary>
        /// Returns the canonical transport profile URI for the encoding
        /// family (UADP over UDP, JSON over MQTT). The value is only used
        /// to resolve the matching encoder by encoding family; the
        /// concrete wire transport is chosen by the egress.
        /// </summary>
        /// <param name="encoding">Target encoding.</param>
        /// <returns>Transport profile URI.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string ToTransportProfileUri(this TranscodeEncoding encoding)
        {
            return encoding switch
            {
                TranscodeEncoding.Uadp => Profiles.PubSubUdpUadpTransport,
                TranscodeEncoding.Json => Profiles.PubSubMqttJsonTransport,
                TranscodeEncoding.Avro => Encoding.AvroNetworkMessage.PubSubMqttAvroTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
        }

        /// <summary>
        /// Classifies a transport profile URI into the encoding family it
        /// belongs to. Profiles whose identifier contains <c>Json</c> map
        /// to <see cref="TranscodeEncoding.Json"/>; every other profile
        /// maps to <see cref="TranscodeEncoding.Uadp"/>.
        /// </summary>
        /// <param name="transportProfileUri">Transport profile URI.</param>
        /// <returns>The encoding family.</returns>
        public static TranscodeEncoding FromTransportProfileUri(this string transportProfileUri)
        {
            if (transportProfileUri is null)
            {
                return TranscodeEncoding.Uadp;
            }
            if (transportProfileUri.Contains("avro", StringComparison.OrdinalIgnoreCase))
            {
                return TranscodeEncoding.Avro;
            }
            return transportProfileUri.Contains("Json", StringComparison.OrdinalIgnoreCase)
                ? TranscodeEncoding.Json
                : TranscodeEncoding.Uadp;
        }

        /// <summary>
        /// Returns the encoding family a decoded NetworkMessage belongs
        /// to based on its concrete record type, falling back to the
        /// transport profile URI classification.
        /// </summary>
        /// <param name="message">Decoded NetworkMessage.</param>
        /// <returns>The encoding family.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static TranscodeEncoding EncodingOf(this Encoding.PubSubNetworkMessage message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            return message switch
            {
                Encoding.Uadp.UadpNetworkMessage => TranscodeEncoding.Uadp,
                Encoding.Json.JsonNetworkMessage => TranscodeEncoding.Json,
                Encoding.AvroNetworkMessage => TranscodeEncoding.Avro,
                _ => FromTransportProfileUri(message.TransportProfileUri)
            };
        }
    }
}
