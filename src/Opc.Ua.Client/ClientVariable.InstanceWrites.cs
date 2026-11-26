// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            if (response is null || response.ResponseHeader is null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
            {
                throw new ServiceResultException(response.ResponseHeader.ServiceResult);
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
