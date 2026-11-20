// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// A single transport-level message property (key / value pair)
    /// carried out-of-band alongside a NetworkMessage frame. Maps to an
    /// MQTT 5.0 User Property; transports without a header channel ignore
    /// these.
    /// </summary>
    /// <remarks>
    /// Implements the "promoted field" idea of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.3">
    /// Part 14 §7.2.3 PublishedDataSets</see> at the broker layer: selected
    /// DataSet fields are surfaced as broker message properties so consumers
    /// can filter without decoding the payload.
    /// </remarks>
    /// <param name="Name">
    /// Property key. For MQTT this becomes the User Property name.
    /// </param>
    /// <param name="Value">
    /// Property value formatted as an invariant string.
    /// </param>
    public readonly record struct PubSubMessageProperty(string Name, string Value);
}
