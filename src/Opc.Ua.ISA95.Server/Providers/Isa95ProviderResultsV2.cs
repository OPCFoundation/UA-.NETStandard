// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The outcome of a Job Control V2 job order operation.
    /// </summary>
    public readonly record struct Isa95JobOrderReceiptV2
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The ISA-95 UInt64 return-status bitmap.
        /// </summary>
        public required ulong ReturnStatus { get; init; }
    }

    /// <summary>
    /// The outcome of a Job Control V2 response request by job-order ID.
    /// </summary>
    public readonly record struct Isa95JobResponseByIdResultV2
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The ISA-95 UInt64 return-status bitmap.
        /// </summary>
        public required ulong ReturnStatus { get; init; }

        /// <summary>
        /// The latest matching job response, or <c>null</c> when unavailable.
        /// </summary>
        public V2.ISA95JobResponseDataType? Response { get; init; }
    }

    /// <summary>
    /// The outcome of a Job Control V2 response request by job-order state.
    /// </summary>
    public readonly record struct Isa95JobResponsesByStateResultV2
    {
        /// <summary>
        /// The diagnostic result.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The ISA-95 UInt64 return-status bitmap.
        /// </summary>
        public required ulong ReturnStatus { get; init; }

        /// <summary>
        /// The matching job responses.
        /// </summary>
        public required ArrayOf<V2.ISA95JobResponseDataType> Responses { get; init; }
    }

    /// <summary>
    /// The outcome of a Job Control V2 receive-job-response call.
    /// </summary>
    public readonly record struct Isa95JobResponseReceiptV2
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The ISA-95 UInt64 return-status bitmap.
        /// </summary>
        public required ulong ReturnStatus { get; init; }
    }
}
