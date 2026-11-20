/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Client-side operations for an OPC UA <c>AcknowledgeableConditionType</c> instance.
    /// </summary>
    public interface IAcknowledgeableConditionOperations : IConditionOperations
    {
        /// <summary>
        /// Acknowledges a condition state notification identified by
        /// <paramref name="eventId"/>.
        /// </summary>
        ValueTask AcknowledgeAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default);

        /// <summary>
        /// Confirms a condition state notification identified by
        /// <paramref name="eventId"/>.
        /// </summary>
        ValueTask ConfirmAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken ct = default);
    }
}
