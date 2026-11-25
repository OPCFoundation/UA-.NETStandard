// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.JobControl
{
    /// <summary>
    /// The Job Control ReturnStatus bits defined by Annex B.
    /// </summary>
    public static class Isa95JobReturnStatusBits
    {
        /// <summary>No bits set.</summary>
        public const ulong None = 0;

        /// <summary>The operation succeeded.</summary>
        public const ulong Success = 1UL << 0;

        /// <summary>The job order is unknown.</summary>
        public const ulong UnknownJobOrderId = 1UL << 1;

        /// <summary>The command is invalid.</summary>
        public const ulong InvalidCommand = 1UL << 2;

        /// <summary>The state does not permit the operation.</summary>
        public const ulong InvalidStatus = 1UL << 3;

        /// <summary>The receiver could not accept the request.</summary>
        public const ulong UnableToAccept = 1UL << 4;

        /// <summary>The request is invalid.</summary>
        public const ulong InvalidRequest = 1UL << 32;
    }
}
