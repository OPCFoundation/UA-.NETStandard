/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
