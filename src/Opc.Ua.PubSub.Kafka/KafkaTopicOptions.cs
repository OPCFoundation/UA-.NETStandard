/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
