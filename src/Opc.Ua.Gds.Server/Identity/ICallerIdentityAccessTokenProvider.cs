// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
