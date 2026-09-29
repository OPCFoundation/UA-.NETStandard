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

namespace Opc.Ua.Gds.Server.Identity
{
    /// <summary>
    /// An access token provider that binds token requests to the identity the
    /// server authenticated on the calling session, so the configured access
    /// control applies and the token subject is the caller.
    /// </summary>
    internal interface ICallerIdentityAccessTokenProvider
    {
        /// <summary>
        /// Issues a token for the legacy RequestAccessToken method on behalf of the caller.
        /// </summary>
        [Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
        ValueTask<string> RequestAccessTokenAsync(
            UserIdentityToken identityToken,
            string resourceId,
            IUserIdentity? callerIdentity,
            CancellationToken ct = default);

        /// <summary>
        /// Begins a token request on behalf of the caller.
        /// </summary>
        ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            IUserIdentity? callerIdentity,
            CancellationToken ct = default);
    }
}
