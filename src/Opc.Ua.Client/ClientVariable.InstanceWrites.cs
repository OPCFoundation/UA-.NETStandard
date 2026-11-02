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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    public sealed partial class ClientVariable<T>
    {
        /// <summary>
        /// Writes an explicitly instance-writable, already resolved node using this variable's generated codec.
        /// </summary>
        /// <remarks>
        /// The caller resolves the instance node and decides whether to permit an instance override of the
        /// model's static access declaration. This method neither resolves another node nor changes Writable.
        /// The server authorizes the actual request. A missing, uncertain or bad response is not accepted.
        /// </remarks>
        /// <param name="resolvedNode">The bounded, session-local variable NodeId resolved by the caller.</param>
        /// <param name="value">The value encoded with the generated variable codec.</param>
        /// <param name="cancellationToken">Cancels before encoding or dispatch and accompanies the request.</param>
        public async ValueTask WriteInstanceAsync(
            NodeId resolvedNode,
            T value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resolvedNode.IsNull)
            {
                throw new ArgumentException("A resolved instance variable NodeId is required.", nameof(resolvedNode));
            }
            ArrayOf<WriteValue> writes =
            [
                new WriteValue
                {
                    NodeId = resolvedNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(m_encode(value))
                }
            ];
            cancellationToken.ThrowIfCancellationRequested();
            WriteResponse response = await m_owner.Session.WriteAsync(
                null, writes, cancellationToken).ConfigureAwait(false);
            if (response.ResponseHeader is null || !StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(
                    response.ResponseHeader?.ServiceResult ?? StatusCodes.BadDecodingError);
            }
            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, writes);
            if (!StatusCode.IsGood(response.Results[0]))
            {
                throw new ServiceResultException(response.Results[0]);
            }
        }
    }
}
