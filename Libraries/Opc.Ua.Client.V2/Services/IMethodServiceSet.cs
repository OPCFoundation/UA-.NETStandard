// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Services
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Method services
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.12"/>
    /// </summary>
    public interface IMethodServiceSet
    {
        /// <summary>
        /// Call service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="methodsToCall"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<CallResponse> CallAsync(RequestHeader? requestHeader,
            ArrayOf<CallMethodRequest> methodsToCall,
            CancellationToken ct = default);
    }
}
