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
