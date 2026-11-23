// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Augments an authenticated <see cref="IUserIdentity"/> after an
    /// <see cref="IUserTokenAuthenticator"/> returns
    /// <see cref="AuthenticationOutcome.Accepted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations may layer additional deployment-specific roles, claims,
    /// or application bindings by returning
    /// <see cref="AuthenticationResult.Accept(IUserIdentity)"/> with a new
    /// identity. Augmenters run in registration order and the output of one
    /// accepted augmenter is passed to the next.
    /// </para>
    /// <para>
    /// Implementations may reject the authenticated identity by returning
    /// <see cref="AuthenticationResult.Reject(ServiceResult)"/>, for example
    /// when enforcing channel-certificate binding or post-authentication
    /// policy. Returning <see cref="AuthenticationResult.NotHandled"/> is a
    /// no-op; the chain continues with the unchanged identity.
    /// </para>
    /// <para>
    /// Augmenters are not invoked when authentication returns
    /// <see cref="AuthenticationOutcome.NotHandled"/> or
    /// <see cref="AuthenticationOutcome.Rejected"/>.
    /// </para>
    /// </remarks>
    public interface IIdentityAugmenter
    {
        /// <summary>
        /// Augments an accepted identity.
        /// </summary>
        /// <param name="identity">The identity returned by the authenticator, or by the previous augmenter.</param>
        /// <param name="context">The authentication context associated with the inbound user token.</param>
        /// <param name="ct">A cancellation token for the authentication operation.</param>
        /// <returns>
        /// <see cref="AuthenticationResult.Accept(IUserIdentity)"/> with the
        /// unchanged or wrapped identity,
        /// <see cref="AuthenticationResult.Reject(ServiceResult)"/> to deny
        /// the accepted identity, or <see cref="AuthenticationResult.NotHandled"/>
        /// to leave the identity unchanged and continue the chain.
        /// </returns>
        ValueTask<AuthenticationResult> AugmentAsync(
            IUserIdentity identity,
            AuthenticationContext context,
            CancellationToken ct = default);
    }
}
