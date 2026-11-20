/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using V1 = Opc.Ua.ISA95.JobControl.V1;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// The outcome of a Job Control V1 <c>ReceiveJobOrder</c> call. Carries the
    /// diagnostic <see cref="ServiceResult"/> together with the spec method
    /// output bitmap.
    /// </summary>
    public readonly record struct Isa95JobOrderReceiptV1
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The raw <c>UInt64</c> return status defined by the method.
        /// </summary>
        public required ulong ReturnStatus { get; init; }
    }

    /// <summary>
    /// The outcome of a Job Control V1 <c>RequestJobResponse</c> call. Carries the
    /// diagnostic <see cref="ServiceResult"/>, the spec return status and the
    /// matching job responses.
    /// </summary>
    public readonly record struct Isa95JobResponseQueryV1
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The raw <c>UInt64</c> return status defined by the method.
        /// </summary>
        public required ulong ReturnStatus { get; init; }

        /// <summary>
        /// The matching job responses. Empty when none match.
        /// </summary>
        public required ArrayOf<V1.ISA95JobResponseDataType> Responses { get; init; }
    }

    /// <summary>
    /// The outcome of a Job Control V1 <c>ReceiveJobResponse</c> call.
    /// </summary>
    public readonly record struct Isa95JobResponseReceiptV1
    {
        /// <summary>
        /// The diagnostic result. <see cref="ServiceResult.Good"/> on success.
        /// </summary>
        public required ServiceResult Result { get; init; }

        /// <summary>
        /// The raw <c>UInt64</c> return status defined by the method.
        /// </summary>
        public required ulong ReturnStatus { get; init; }
    }
}
