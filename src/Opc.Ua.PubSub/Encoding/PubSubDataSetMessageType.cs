// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Kind of a single DataSetMessage. Selected by the JSON
    /// <c>MessageType</c> field and the UADP DataSetFlags2
    /// message-type bits, common to both mappings.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.3">
    /// Part 14 §7.2.5.3 JSON DataSetMessage</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 UADP DataSetMessage header / DataSetFlags2</see>.
    /// </remarks>
    public enum PubSubDataSetMessageType
    {
        /// <summary>
        /// Key-frame: every configured field is present. Emitted
        /// periodically (see DataSetWriter <c>KeyFrameCount</c>) and
        /// after subscriber reconnect so receivers can rebuild a
        /// complete snapshot.
        /// </summary>
        KeyFrame,

        /// <summary>
        /// Delta-frame: only fields whose value or status changed since
        /// the last KeyFrame are present.
        /// </summary>
        DeltaFrame,

        /// <summary>
        /// Event: payload carries one OPC UA Event (Part 5 EventType
        /// instance) instead of variable values.
        /// </summary>
        Event,

        /// <summary>
        /// KeepAlive: no field payload; emitted at the configured
        /// <c>KeepAliveTime</c> to refresh subscriber receive-timeout
        /// timers.
        /// </summary>
        KeepAlive
    }
}
