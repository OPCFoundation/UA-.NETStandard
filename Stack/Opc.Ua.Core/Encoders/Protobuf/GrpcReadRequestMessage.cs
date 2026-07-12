/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
namespace Opc.Ua
{
    /// <summary>
    /// Carries the fields of an OPC UA Read request for the experimental gRPC mapping.
    /// </summary>
    public sealed class GrpcReadRequestMessage
    {
        /// <summary>
        /// Gets or sets the OPC UA request header carried by the gRPC Read request.
        /// </summary>
        public RequestHeader? RequestHeader { get; set; }

        /// <summary>
        /// Gets or sets the maximum age, in milliseconds, accepted for returned values.
        /// </summary>
        public double MaxAge { get; set; }

        /// <summary>
        /// Gets or sets the timestamp selection encoded for the Read request.
        /// </summary>
        public uint TimestampsToReturn { get; set; }

        /// <summary>
        /// Gets or sets the nodes and attributes requested by the gRPC Read call.
        /// </summary>
        public ArrayOf<GrpcReadValueId> NodesToRead { get; set; }
    }
}
