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

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Topic-level options that apply to every publish / subscribe on
    /// the connection. Individual data / metadata topics are taken from
    /// the connection's broker transport settings (QueueName /
    /// MetaDataQueueName); this bag only carries the fallback prefix used
    /// when a writer / reader does not specify an explicit Kafka topic.
    /// </summary>
    /// <remarks>
    /// Implements the topic addressing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Apache Kafka topic
    /// names are restricted to the character set
    /// <c>[a-zA-Z0-9._-]</c>, so the fallback naming scheme uses
    /// <c>.</c> as the segment separator rather than the MQTT <c>/</c>.
    /// </remarks>
    public sealed class KafkaTopicOptions
    {
        /// <summary>
        /// Topic prefix used as the first segment of every fallback data
        /// / metadata topic when the connection does not carry an
        /// explicit Kafka topic (QueueName). Defaults to <c>opcua</c>.
        /// </summary>
        public string Prefix { get; set; } = "opcua";

        /// <summary>
        /// Creates a deep copy of these topic options so per-connection
        /// changes never leak back into a shared default options instance.
        /// </summary>
        /// <returns>A new, independent <see cref="KafkaTopicOptions"/>.</returns>
        public KafkaTopicOptions Clone()
        {
            return new KafkaTopicOptions
            {
                Prefix = Prefix
            };
        }
    }
}
