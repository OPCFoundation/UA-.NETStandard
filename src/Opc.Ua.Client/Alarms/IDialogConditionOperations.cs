// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Client-side operations for an OPC UA <c>DialogConditionType</c> instance.
    /// </summary>
    public interface IDialogConditionOperations : IConditionOperations
    {
        /// <summary>
        /// Responds to an active dialog with the selected response index.
        /// </summary>
        ValueTask RespondAsync(
            NodeId conditionId,
            int selectedResponse,
            CancellationToken ct = default);

        /// <summary>
        /// Responds to an active dialog with a comment.
        /// </summary>
        ValueTask Respond2Async(
            NodeId conditionId,
            int selectedResponse,
            LocalizedText comment,
            CancellationToken ct = default);
    }
}
