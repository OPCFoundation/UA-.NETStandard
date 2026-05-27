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

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Registry of <see cref="IUserTokenAuthenticator"/> instances.
    /// Dispatches an inbound <c>ActivateSession</c> identity token to the
    /// authenticator whose
    /// <see cref="IUserTokenAuthenticator.TokenType"/> (plus, for issued
    /// tokens, <see cref="IUserTokenAuthenticator.IssuedTokenProfileUri"/>)
    /// matches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Precedence rules (verified by P2 server wiring):
    /// </para>
    /// <list type="number">
    ///   <item>Explicit authenticators registered in this registry are
    ///         tried first, in the order they were registered.</item>
    ///   <item>If no authenticator returns
    ///         <see cref="AuthenticationOutcome.Accepted"/> or
    ///         <see cref="AuthenticationOutcome.Rejected"/>, the
    ///         <c>SessionManager</c> falls back to the legacy
    ///         <c>ImpersonateUser</c> event.</item>
    ///   <item>If the legacy event is not subscribed either, the
    ///         <c>SessionManager</c> falls back to wrapping the raw
    ///         <c>UserIdentity</c> from the token.</item>
    /// </list>
    /// <para>
    /// Registry membership is mutable but writes are infrequent; reads
    /// happen on every <c>ActivateSession</c>. Implementations must be
    /// safe for concurrent dispatch.
    /// </para>
    /// </remarks>
    public interface IServerIdentityRegistry
    {
        /// <summary>
        /// Registers an authenticator. If an authenticator with the same
        /// <see cref="IUserTokenAuthenticator.TokenType"/> +
        /// <see cref="IUserTokenAuthenticator.IssuedTokenProfileUri"/>
        /// is already registered, it is replaced.
        /// </summary>
        void Register(IUserTokenAuthenticator authenticator);

        /// <summary>
        /// Removes an authenticator. Returns <see langword="true"/> when
        /// an entry was removed.
        /// </summary>
        bool Unregister(IUserTokenAuthenticator authenticator);

        /// <summary>
        /// Register an augmenter to run after a successful authenticator returns Accepted.
        /// </summary>
        void RegisterAugmenter(IIdentityAugmenter augmenter);

        /// <summary>
        /// Removes a previously-registered augmenter. Returns true when removed.
        /// </summary>
        bool UnregisterAugmenter(IIdentityAugmenter augmenter);

        /// <summary>
        /// Dispatch the supplied <paramref name="context"/> to the
        /// matching authenticator. Returns
        /// <see cref="AuthenticationResult.NotHandled"/> when no
        /// authenticator matched.
        /// </summary>
        ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default);
    }
}
