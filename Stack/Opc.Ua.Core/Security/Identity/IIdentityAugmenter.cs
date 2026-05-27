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

#nullable enable

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
    /// Implementations may return the unchanged identity as a no-op or wrap it
    /// with additional deployment-specific roles,
    /// claims, or application bindings. Augmenters run in registration order
    /// and the output of one augmenter is passed to the next.
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
        /// The unchanged identity or a wrapped identity. Returning <see langword="null"/>
        /// is invalid and causes the registry to reject the augmenter result.
        /// </returns>
        ValueTask<IUserIdentity> AugmentAsync(
            IUserIdentity identity,
            AuthenticationContext context,
            CancellationToken ct = default);
    }
}
