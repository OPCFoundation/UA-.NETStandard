// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.ISA95.JobControl;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The individual bits of the ISA-95 Job Control <c>ReturnStatus</c>
    /// <see cref="ulong"/> bitmap defined in Annex B.2 of the V1 and V2 companion
    /// specifications. The value returned by a Job Control method is a bitwise
    /// combination of these flags; more than one bit can be set at once.
    /// </summary>
    public static class Isa95JobReturnStatus
    {
        /// <summary>
        /// No bits set. Used as a starting accumulator only; a successful call
        /// sets <see cref="Success"/>.
        /// </summary>
        public const ulong None = Isa95JobReturnStatusBits.None;

        /// <summary>
        /// The operation completed successfully (bit 0).
        /// </summary>
        public const ulong Success = Isa95JobReturnStatusBits.Success;

        /// <summary>
        /// The supplied job order identifier is unknown or invalid (bit 1).
        /// </summary>
        public const ulong UnknownJobOrderId = Isa95JobReturnStatusBits.UnknownJobOrderId;

        /// <summary>
        /// The supplied command is not valid (bit 2). Primarily used by V1.
        /// </summary>
        public const ulong InvalidCommand = Isa95JobReturnStatusBits.InvalidCommand;

        /// <summary>
        /// The job order is in a state that does not permit the operation
        /// (bit 3).
        /// </summary>
        public const ulong InvalidStatus = Isa95JobReturnStatusBits.InvalidStatus;

        /// <summary>
        /// The receiver is unable to accept the job order or response (bit 4).
        /// </summary>
        public const ulong UnableToAccept = Isa95JobReturnStatusBits.UnableToAccept;

        /// <summary>
        /// The request itself is invalid, for example a required field is missing
        /// (bit 32).
        /// </summary>
        public const ulong InvalidRequest = Isa95JobReturnStatusBits.InvalidRequest;
    }
}
