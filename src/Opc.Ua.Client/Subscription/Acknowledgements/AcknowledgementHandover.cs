/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// An acknowledgement released by a previous session during subscription transfer.
    /// </summary>
    /// <param name="Handle">The identity of the acknowledgement.</param>
    /// <param name="Confirmed">Whether the application confirmed the acknowledgement.</param>
    internal readonly record struct AcknowledgementHandover(
        AcknowledgementHandle Handle,
        bool Confirmed);
}
