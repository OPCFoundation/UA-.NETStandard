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

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Requester-side PubSub Action response.
    /// </summary>
    public sealed record PubSubActionResponse
    {
        /// <summary>
        /// Target that produced the response.
        /// </summary>
        public PubSubActionTarget Target { get; init; } = new();

        /// <summary>
        /// RequestId copied from the matching request.
        /// </summary>
        public ushort RequestId { get; init; }

        /// <summary>
        /// Correlation data copied from the matching request.
        /// </summary>
        public ByteString CorrelationData { get; init; } = ByteString.Empty;

        /// <summary>
        /// Action execution status.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.Good;

        /// <summary>
        /// Action lifecycle state reported by the responder.
        /// </summary>
        public ActionState ActionState { get; init; } = ActionState.Done;

        /// <summary>
        /// Named output fields returned by the Action handler.
        /// </summary>
        public ArrayOf<DataSetField> OutputFields { get; init; } = [];
    }
}
