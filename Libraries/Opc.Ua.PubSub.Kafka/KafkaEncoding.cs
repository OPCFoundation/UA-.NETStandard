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

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Encoding tier carried by the Kafka NetworkMessage body and
    /// advertised through the record <c>content-type</c> header.
    /// </summary>
    /// <remarks>
    /// Implements the message body encoding selector defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. The wire segment
    /// is the lowercase enum name; the content-type strings are shared
    /// with the MQTT broker transport.
    /// </remarks>
    public enum KafkaEncoding
    {
        /// <summary>
        /// UADP binary NetworkMessage body
        /// (<see cref="KafkaProfiles.PubSubKafkaUadpTransport"/>).
        /// </summary>
        Uadp,

        /// <summary>
        /// JSON NetworkMessage body
        /// (<see cref="KafkaProfiles.PubSubKafkaJsonTransport"/>).
        /// </summary>
        Json
    }

    /// <summary>
    /// Extension helpers for <see cref="KafkaEncoding"/>.
    /// </summary>
    public static class KafkaEncodingExtensions
    {
        /// <summary>
        /// Returns the lowercase topic segment for the given encoding.
        /// </summary>
        /// <param name="encoding">Encoding value.</param>
        /// <returns>
        /// <c>"uadp"</c> for <see cref="KafkaEncoding.Uadp"/>,
        /// <c>"json"</c> for <see cref="KafkaEncoding.Json"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="encoding"/> is not a defined value.
        /// </exception>
        public static string ToTopicSegment(this KafkaEncoding encoding)
        {
            return encoding switch
            {
                KafkaEncoding.Uadp => "uadp",
                KafkaEncoding.Json => "json",
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
        }

        /// <summary>
        /// Returns the record <c>content-type</c> header value for the
        /// given encoding per Part 14 Annex B.2.
        /// </summary>
        /// <param name="encoding">Encoding value.</param>
        /// <returns>
        /// <c>"application/json"</c> for <see cref="KafkaEncoding.Json"/>,
        /// <c>"application/opcua+uadp"</c> for
        /// <see cref="KafkaEncoding.Uadp"/>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="encoding"/> is not a defined value.
        /// </exception>
        public static string ToContentType(this KafkaEncoding encoding)
        {
            return encoding switch
            {
                KafkaEncoding.Uadp => "application/opcua+uadp",
                KafkaEncoding.Json => "application/json",
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
        }
    }
}
