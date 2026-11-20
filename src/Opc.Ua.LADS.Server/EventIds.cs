// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The base event identifiers of the LADS server runtime, one block of ten per log class.
    /// </summary>
    internal static class LadsServerEventIds
    {
        /// <summary>
        /// The block of <see cref="LadsServerLog"/>.
        /// </summary>
        public const int LadsRuntime = 17100;

        /// <summary>
        /// The block of the program messages of <see cref="LadsServerLog"/>.
        /// </summary>
        public const int LadsPrograms = 17110;

        /// <summary>
        /// The block of the maintenance messages of <see cref="LadsServerLog"/>.
        /// </summary>
        public const int LadsMaintenance = 17120;
    }
}
