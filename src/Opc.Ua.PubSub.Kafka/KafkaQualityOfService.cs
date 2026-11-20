/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Kafka producer delivery guarantee mapped onto the Part 14
    /// <c>BrokerTransportQualityOfService</c> enumeration.
    /// </summary>
    /// <remarks>
    /// Implements the delivery-guarantee mapping of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. Unlike MQTT, where
    /// the guarantee is expressed as a per-message QoS level, Kafka
    /// realises the guarantee through producer configuration
    /// (<c>acks</c> and <c>enable.idempotence</c>); the mapping is
    /// therefore applied once at producer creation.
    /// </remarks>
    public enum KafkaQualityOfService
    {
        /// <summary>
        /// Fire-and-forget. The producer does not wait for any broker
        /// acknowledgement (<c>acks=0</c>). Maps to
        /// <c>BrokerTransportQualityOfService.BestEffort</c>.
        /// </summary>
        BestEffort,

        /// <summary>
        /// The producer waits for the partition leader to acknowledge
        /// the write (<c>acks=1</c>) without idempotence. Maps to
        /// <c>BrokerTransportQualityOfService.AtMostOnce</c>.
        /// </summary>
        AtMostOnce,

        /// <summary>
        /// The producer waits for all in-sync replicas to acknowledge the
        /// write (<c>acks=all</c>) with the idempotent producer enabled
        /// (<c>enable.idempotence=true</c>), so retries do not create
        /// duplicate records. Maps to
        /// <c>BrokerTransportQualityOfService.AtLeastOnce</c>.
        /// </summary>
        AtLeastOnce,

        /// <summary>
        /// The producer enables the idempotent producer
        /// (<c>enable.idempotence=true</c>, implying <c>acks=all</c>) so
        /// retries do not create duplicate records. Maps to
        /// <c>BrokerTransportQualityOfService.ExactlyOnce</c>.
        /// </summary>
        ExactlyOnce
    }

    /// <summary>
    /// Broker acknowledgement level requested from the Kafka producer.
    /// Numeric values match the Kafka <c>acks</c> setting.
    /// </summary>
    /// <remarks>
    /// Backs the <c>acks</c> selector described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>.
    /// </remarks>
    public enum KafkaAcks
    {
        /// <summary>
        /// No acknowledgement is requested from the broker
        /// (<c>acks=0</c>).
        /// </summary>
        None = 0,

        /// <summary>
        /// Only the partition leader must acknowledge the write
        /// (<c>acks=1</c>).
        /// </summary>
        Leader = 1,

        /// <summary>
        /// All in-sync replicas must acknowledge the write
        /// (<c>acks=all</c>).
        /// </summary>
        All = -1
    }

    /// <summary>
    /// Resolved Kafka producer delivery settings derived from a
    /// <see cref="KafkaQualityOfService"/> value.
    /// </summary>
    /// <remarks>
    /// Carries the two producer knobs that realise the Part 14 Annex B.2
    /// delivery guarantee so Kafka client adapters can apply them without
    /// re-deriving the mapping.
    /// </remarks>
    /// <param name="Acks">Broker acknowledgement level.</param>
    /// <param name="EnableIdempotence">
    /// <see langword="true"/> to enable the idempotent producer for
    /// exactly-once semantics on retry.
    /// </param>
    public readonly record struct KafkaDeliveryGuarantee(KafkaAcks Acks, bool EnableIdempotence);

    /// <summary>
    /// Mapping helpers for <see cref="KafkaQualityOfService"/>.
    /// </summary>
    public static class KafkaQualityOfServiceExtensions
    {
        /// <summary>
        /// Resolves the producer <c>acks</c> / <c>enable.idempotence</c>
        /// settings for the given delivery guarantee per Part 14 Annex
        /// B.2.
        /// </summary>
        /// <param name="qualityOfService">Delivery guarantee value.</param>
        /// <returns>The resolved producer settings.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="qualityOfService"/> is not a defined value.
        /// </exception>
        public static KafkaDeliveryGuarantee ToDeliveryGuarantee(
            this KafkaQualityOfService qualityOfService)
        {
            return qualityOfService switch
            {
                KafkaQualityOfService.BestEffort =>
                    new KafkaDeliveryGuarantee(KafkaAcks.None, EnableIdempotence: false),
                KafkaQualityOfService.AtMostOnce =>
                    new KafkaDeliveryGuarantee(KafkaAcks.Leader, EnableIdempotence: false),
                KafkaQualityOfService.AtLeastOnce =>
                    new KafkaDeliveryGuarantee(KafkaAcks.All, EnableIdempotence: true),
                KafkaQualityOfService.ExactlyOnce =>
                    new KafkaDeliveryGuarantee(KafkaAcks.All, EnableIdempotence: true),
                _ => throw new ArgumentOutOfRangeException(nameof(qualityOfService))
            };
        }

        /// <summary>
        /// Maps a Part 14 <see cref="BrokerTransportQualityOfService"/>
        /// value to the equivalent <see cref="KafkaQualityOfService"/>.
        /// </summary>
        /// <param name="guarantee">Requested delivery guarantee.</param>
        /// <param name="fallback">
        /// Value returned when <paramref name="guarantee"/> is
        /// <see cref="BrokerTransportQualityOfService.NotSpecified"/> or
        /// an unrecognised value.
        /// </param>
        /// <returns>The mapped delivery guarantee.</returns>
        public static KafkaQualityOfService FromBrokerGuarantee(
            this BrokerTransportQualityOfService guarantee,
            KafkaQualityOfService fallback)
        {
            return guarantee switch
            {
                BrokerTransportQualityOfService.BestEffort => KafkaQualityOfService.BestEffort,
                BrokerTransportQualityOfService.AtMostOnce => KafkaQualityOfService.AtMostOnce,
                BrokerTransportQualityOfService.AtLeastOnce => KafkaQualityOfService.AtLeastOnce,
                BrokerTransportQualityOfService.ExactlyOnce => KafkaQualityOfService.ExactlyOnce,
                _ => fallback
            };
        }
    }
}
