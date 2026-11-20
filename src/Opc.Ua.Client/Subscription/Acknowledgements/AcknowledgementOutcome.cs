/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// The terminal state of one acknowledgement.
    /// </summary>
    /// <param name="Handle">The acknowledgement.</param>
    /// <param name="Disposition">How the acknowledgement ended.</param>
    /// <param name="Status">The server's result, or the reason it ended without one.</param>
    public readonly record struct AcknowledgementOutcome(
        AcknowledgementHandle Handle,
        AcknowledgementDisposition Disposition,
        StatusCode Status);
}
