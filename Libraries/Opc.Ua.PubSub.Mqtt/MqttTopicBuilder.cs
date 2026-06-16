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
using System.Globalization;
using System.Text;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Builds MQTT topic strings that follow the Part 14 §7.3.4.7.3
    /// data-topic and §7.3.4.7.4 metadata-topic schemas:
    /// <code>
    ///   &lt;Prefix&gt;/&lt;Encoding&gt;/data/&lt;PublisherId&gt;/&lt;WriterGroup&gt;[/&lt;DataSetWriter&gt;]
    ///   &lt;Prefix&gt;/&lt;Encoding&gt;/metadata/&lt;PublisherId&gt;/&lt;WriterGroup&gt;/&lt;DataSetWriter&gt;
    /// </code>
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.3">
    /// Part 14 §7.3.4.7.3 Data topic</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.4">
    /// §7.3.4.7.4 Metadata topic</see>. The builder rejects user input
    /// containing the MQTT topic wildcards (<c>#</c>, <c>+</c>) so a
    /// hostile or careless DataSetWriter name cannot accidentally
    /// widen subscription scope (research §5 supplement).
    /// </remarks>
    public static class MqttTopicBuilder
    {
        /// <summary>
        /// Topic level segment for data publications.
        /// </summary>
        public const string DataSegment = "data";

        /// <summary>
        /// Topic level segment for metadata publications.
        /// </summary>
        public const string MetaDataSegment = "metadata";

        /// <summary>
        /// Topic level segment for keep-alive publications.
        /// </summary>
        public const string KeepAliveSegment = "keepalive";

        /// <summary>
        /// Builds the writer-group or writer-specific data topic for a
        /// publication (Part 14 §7.3.4.7.3).
        /// </summary>
        /// <param name="prefix">
        /// Topic prefix (must not start or end with <c>/</c> and must
        /// not contain MQTT wildcards).
        /// </param>
        /// <param name="encoding">Encoding flavour.</param>
        /// <param name="publisherId">PublisherId (any Part 14 type).</param>
        /// <param name="writerGroupId">WriterGroup identifier.</param>
        /// <param name="dataSetWriterId">
        /// Optional DataSetWriter identifier. When provided, the topic
        /// becomes DataSetWriter-specific and the publisher MUST emit
        /// one DataSetMessage per NetworkMessage on it
        /// (<c>SingleNetworkMessage</c> mode per §7.3.4.7.3 /
        /// §A.3.3).
        /// </param>
        /// <returns>The constructed topic string.</returns>
        public static string BuildDataTopic(
            string prefix,
            MqttEncoding encoding,
            Variant publisherId,
            ushort writerGroupId,
            ushort? dataSetWriterId)
        {
            ValidatePrefix(prefix);
            string publisherToken = ToPublisherIdToken(publisherId);
            var sb = new StringBuilder(prefix.Length + 64);
            sb.Append(prefix);
            sb.Append('/').Append(encoding.ToTopicSegment());
            sb.Append('/').Append(DataSegment);
            sb.Append('/').Append(publisherToken);
            sb.Append('/').Append(writerGroupId.ToString(CultureInfo.InvariantCulture));
            if (dataSetWriterId is ushort writerId)
            {
                sb.Append('/').Append(writerId.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds the DataSetWriter-specific metadata topic
        /// (Part 14 §7.3.4.7.4).
        /// </summary>
        /// <param name="prefix">Topic prefix.</param>
        /// <param name="encoding">Encoding flavour.</param>
        /// <param name="publisherId">PublisherId.</param>
        /// <param name="writerGroupId">WriterGroup identifier.</param>
        /// <param name="dataSetWriterId">DataSetWriter identifier.</param>
        /// <returns>The constructed metadata topic string.</returns>
        public static string BuildMetaDataTopic(
            string prefix,
            MqttEncoding encoding,
            Variant publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId)
        {
            ValidatePrefix(prefix);
            string publisherToken = ToPublisherIdToken(publisherId);
            var sb = new StringBuilder(prefix.Length + 64);
            sb.Append(prefix);
            sb.Append('/').Append(encoding.ToTopicSegment());
            sb.Append('/').Append(MetaDataSegment);
            sb.Append('/').Append(publisherToken);
            sb.Append('/').Append(writerGroupId.ToString(CultureInfo.InvariantCulture));
            sb.Append('/').Append(dataSetWriterId.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Builds the writer-group keep-alive topic carried alongside
        /// the data topic (research §4 — KeepAlive).
        /// </summary>
        /// <param name="prefix">Topic prefix.</param>
        /// <param name="encoding">Encoding flavour.</param>
        /// <param name="publisherId">PublisherId.</param>
        /// <param name="writerGroupId">WriterGroup identifier.</param>
        /// <returns>The constructed keep-alive topic string.</returns>
        public static string BuildKeepAliveTopic(
            string prefix,
            MqttEncoding encoding,
            Variant publisherId,
            ushort writerGroupId)
        {
            ValidatePrefix(prefix);
            string publisherToken = ToPublisherIdToken(publisherId);
            var sb = new StringBuilder(prefix.Length + 64);
            sb.Append(prefix);
            sb.Append('/').Append(encoding.ToTopicSegment());
            sb.Append('/').Append(KeepAliveSegment);
            sb.Append('/').Append(publisherToken);
            sb.Append('/').Append(writerGroupId.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Converts a PublisherId <see cref="Variant"/> to the string
        /// token used as the <c>&lt;PublisherId&gt;</c> topic segment.
        /// Numeric variants use the invariant culture's <c>ToString</c>;
        /// strings are passed through after wildcard validation;
        /// Guid / Uuid use the <c>"N"</c> format (32 hex digits, no
        /// dashes) so the segment never embeds reserved MQTT
        /// characters.
        /// </summary>
        /// <param name="publisherId">PublisherId variant.</param>
        /// <returns>The topic segment string.</returns>
        /// <exception cref="ArgumentException">
        /// The variant holds a type not allowed by Part 14
        /// §7.2.4.5.2, or a string with a wildcard character.
        /// </exception>
        public static string ToPublisherIdToken(Variant publisherId)
        {
            if (publisherId.IsNull)
            {
                return "0";
            }
            if (publisherId.TryGetValue(out byte b))
            {
                return b.ToString(CultureInfo.InvariantCulture);
            }
            if (publisherId.TryGetValue(out ushort u16))
            {
                return u16.ToString(CultureInfo.InvariantCulture);
            }
            if (publisherId.TryGetValue(out uint u32))
            {
                return u32.ToString(CultureInfo.InvariantCulture);
            }
            if (publisherId.TryGetValue(out ulong u64))
            {
                return u64.ToString(CultureInfo.InvariantCulture);
            }
            if (publisherId.TryGetValue(out string str) && str != null)
            {
                ValidateNoWildcards(str, nameof(publisherId));
                ValidateNoTopicSeparator(str, nameof(publisherId));
                return str;
            }
            if (publisherId.TryGetValue(out Uuid uuid))
            {
                return ((Guid)uuid).ToString("N", CultureInfo.InvariantCulture);
            }
            throw new ArgumentException(
                "PublisherId must hold one of Byte, UInt16, UInt32, UInt64, String, or Guid.",
                nameof(publisherId));
        }

        private static void ValidatePrefix(string prefix)
        {
            if (prefix is null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }
            if (prefix.Length == 0)
            {
                throw new ArgumentException("Prefix cannot be empty.", nameof(prefix));
            }
            if (prefix[0] == '/' || prefix[prefix.Length - 1] == '/')
            {
                throw new ArgumentException(
                    "Prefix must not start or end with a '/' character.",
                    nameof(prefix));
            }
            ValidateNoWildcards(prefix, nameof(prefix));
        }

        private static void ValidateNoWildcards(string value, string paramName)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '#' || c == '+')
                {
                    throw new ArgumentException(
                        "MQTT topic wildcard characters '#' and '+' are not allowed in topic-builder inputs.",
                        paramName);
                }
                if (c == '\0')
                {
                    throw new ArgumentException(
                        "NUL character is not allowed in MQTT topic segments.",
                        paramName);
                }
            }
        }

        private static void ValidateNoTopicSeparator(string value, string paramName)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '/')
                {
                    throw new ArgumentException(
                        "PublisherId string must not contain the topic separator '/'.",
                        paramName);
                }
            }
        }
    }
}
