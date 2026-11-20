// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// PubSub transport profile identifiers defined by the Apache Kafka
    /// transport, kept with the transport implementation rather than in
    /// the core profile constants (mirrors <c>EthProfiles</c>).
    /// </summary>
    public static class KafkaProfiles
    {
        /// <summary>
        /// Uri for the "PubSub Kafka UADP" Profile. This PubSub transport
        /// Facet combines the OPC UA Part 14 Annex B.2 Apache Kafka
        /// transport protocol mapping with UADP message mapping and is used
        /// for broker-based messaging.
        /// </summary>
        public const string PubSubKafkaUadpTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-kafka-uadp";

        /// <summary>
        /// Uri for the "PubSub Kafka JSON" Profile. This PubSub transport
        /// Facet combines the OPC UA Part 14 Annex B.2 Apache Kafka
        /// transport protocol mapping with JSON message mapping and is used
        /// for broker-based messaging.
        /// </summary>
        public const string PubSubKafkaJsonTransport
            = "http://opcfoundation.org/UA-Profile/Transport/pubsub-kafka-json";
    }
}
