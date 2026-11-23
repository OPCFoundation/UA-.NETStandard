// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Selects the running server's resource admission and scheduling profile.
    /// </summary>
    public enum ServerResourceIsolationMode
    {
        /// <summary>
        /// Existing shared aggregate capacity without reserved floors or fairness guarantees.
        /// </summary>
        SharedOnly,

        /// <summary>
        /// Work-conserving shared capacity with owner ceilings and weighted scheduling.
        /// </summary>
        FairShare,

        /// <summary>
        /// Separate non-borrowable bootstrap and reconnect floors. The default runtime profile.
        /// </summary>
        Balanced,

        /// <summary>
        /// Balanced isolation plus explicitly provisioned, non-borrowable trusted-owner floors.
        /// </summary>
        TrustedReservations
    }
}
