// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Topic filter installed against the broker by the MQTT broker
    /// transport when opening a subscriber.
    /// </summary>
    /// <remarks>
    /// Implements the topic-subscription envelope used by the MQTT
    /// transport per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. MQTT wildcards
    /// (<c>+</c>, <c>#</c>) are intentionally accepted in topic
    /// filters but the PubSub layer should only ever supply the
    /// narrowest topics that match a reader's expected writer-group
    /// and writer-id (research §5 supplement).
    /// </remarks>
    /// <param name="Topic">
    /// Topic filter pattern (MQTT v3.1.1 / v5 syntax).
    /// </param>
    /// <param name="Qos">
    /// Maximum delivery guarantee to negotiate with the broker.
    /// </param>
    public readonly record struct MqttTopicFilter(string Topic, MqttQualityOfService Qos);
}
