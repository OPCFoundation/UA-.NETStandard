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
 *
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

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Optional probe interface for an authenticated <see cref="IUserIdentity"/>
    /// whose credentials stop being valid at a known time, for example an
    /// identity created from an IssuedIdentityToken with an expiration claim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented alongside <see cref="IUserIdentity"/> (not inherited) and
    /// probed via <c>identity as IIdentityExpiration</c>. An
    /// <see cref="IIdentityAugmenter"/> that wraps an identity should forward
    /// this interface so the expiration is not lost.
    /// </para>
    /// <para>
    /// OPC 10000-4 7.40.6: once the token has expired the Server invalidates
    /// the credentials of the Session; the Client must call ActivateSession
    /// with a new token. The server rejects other requests of the Session with
    /// <see cref="StatusCodes.BadIdentityTokenInvalid"/> until then.
    /// </para>
    /// </remarks>
    public interface IIdentityExpiration
    {
        /// <summary>
        /// The UTC time after which the credentials are no longer valid,
        /// including any clock skew tolerance the authenticator grants, or
        /// <see langword="null"/> when they do not expire.
        /// </summary>
        DateTime? ExpiresAt { get; }
    }
}
