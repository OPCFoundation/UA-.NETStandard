// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// A single error captured by <see cref="IPubSubDiagnostics"/> at
    /// <see cref="PubSubDiagnosticsLevel.Medium"/> or higher.
    /// </summary>
    /// <param name="Timestamp">
    /// Time the error was recorded, stamped from the diagnostics clock.
    /// </param>
    /// <param name="StatusCode">
    /// Status code summarising the error condition.
    /// </param>
    /// <param name="Message">
    /// Human-readable explanation of the error.
    /// </param>
    public readonly record struct PubSubErrorEntry(
        DateTimeUtc Timestamp,
        StatusCode StatusCode,
        string Message);
}
