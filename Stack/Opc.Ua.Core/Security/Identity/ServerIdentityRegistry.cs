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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Default <see cref="IServerIdentityRegistry"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authenticators are dispatched in registration order: this
    /// preserves predictable precedence when more than one
    /// authenticator claims the same
    /// <see cref="IUserTokenAuthenticator.TokenType"/> (e.g. two
    /// different JWT authenticators for two different issuers — the
    /// first to <see cref="AuthenticationOutcome.Accepted"/> or
    /// <see cref="AuthenticationOutcome.Rejected"/> wins; a
    /// <see cref="AuthenticationOutcome.NotHandled"/> moves on).
    /// </para>
    /// </remarks>
    public sealed class ServerIdentityRegistry : IServerIdentityRegistry
    {
        /// <summary>
        /// Creates an empty registry.
        /// </summary>
        public ServerIdentityRegistry()
        {
        }

        /// <summary>
        /// Creates a registry pre-populated with the supplied
        /// authenticators.
        /// </summary>
        public ServerIdentityRegistry(params IUserTokenAuthenticator[] authenticators)
        {
            if (authenticators == null)
            {
                throw new ArgumentNullException(nameof(authenticators));
            }
            foreach (IUserTokenAuthenticator authenticator in authenticators)
            {
                Register(authenticator);
            }
        }

        /// <inheritdoc/>
        public void Register(IUserTokenAuthenticator authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator));
            }
            string key = Key(authenticator.TokenType, authenticator.IssuedTokenProfileUri);
            lock (m_lock)
            {
                m_byKey[key] = authenticator;
                m_order.Add(authenticator);
            }
        }

        /// <inheritdoc/>
        public bool Unregister(IUserTokenAuthenticator authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator));
            }
            string key = Key(authenticator.TokenType, authenticator.IssuedTokenProfileUri);
            lock (m_lock)
            {
                if (!m_byKey.TryGetValue(key, out IUserTokenAuthenticator? existing) ||
                    !ReferenceEquals(existing, authenticator))
                {
                    return false;
                }
                m_byKey.Remove(key);
                m_order.Remove(authenticator);
                return true;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            IUserTokenAuthenticator[] snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_order];
            }

            UserTokenType tokenType = context.TokenHandler.TokenType;
            string? issuedProfile = (context.TokenHandler as IssuedIdentityTokenHandler)
                ?.IssuedTokenTypeProfileUri;

            foreach (IUserTokenAuthenticator authenticator in snapshot)
            {
                if (authenticator.TokenType != tokenType)
                {
                    continue;
                }
                if (tokenType == UserTokenType.IssuedToken &&
                    !string.IsNullOrEmpty(authenticator.IssuedTokenProfileUri) &&
                    !string.Equals(
                        authenticator.IssuedTokenProfileUri,
                        issuedProfile,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                AuthenticationResult result =
                    await authenticator.AuthenticateAsync(context, ct).ConfigureAwait(false);

                if (result.Outcome != AuthenticationOutcome.NotHandled)
                {
                    return result;
                }
            }

            return AuthenticationResult.NotHandled;
        }

        private static string Key(UserTokenType type, string? profileUri)
        {
            return profileUri == null
                ? type.ToString()
                : $"{type}|{profileUri}";
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, IUserTokenAuthenticator> m_byKey = new(StringComparer.Ordinal);
        private readonly List<IUserTokenAuthenticator> m_order = [];
    }
}
