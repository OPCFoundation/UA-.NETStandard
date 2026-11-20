/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
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
    /// Register replaces the existing token-type/profile registration. Issuer-qualified
    /// authenticators can coexist for distinct issuers; replacing an issuer never leaves
    /// its old verifier active. Remaining registrations are dispatched in registration
    /// order, stopping at the first Accepted or Rejected result.
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
            lock (m_lock)
            {
                string? issuer = GetIssuer(authenticator);
                m_order.RemoveAll(existing =>
                    existing.TokenType == authenticator.TokenType &&
                    string.Equals(
                        existing.IssuedTokenProfileUri,
                        authenticator.IssuedTokenProfileUri,
                        StringComparison.Ordinal) &&
                    (issuer == null ||
                        GetIssuer(existing) == null ||
                        string.Equals(GetIssuer(existing), issuer, StringComparison.Ordinal)));
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
            lock (m_lock)
            {
                return m_order.Remove(authenticator);
            }
        }

        /// <inheritdoc/>
        public void RegisterAugmenter(IIdentityAugmenter augmenter)
        {
            if (augmenter == null)
            {
                throw new ArgumentNullException(nameof(augmenter));
            }
            lock (m_lock)
            {
                m_augmenters.Add(augmenter);
            }
        }

        /// <inheritdoc/>
        public bool UnregisterAugmenter(IIdentityAugmenter augmenter)
        {
            if (augmenter == null)
            {
                throw new ArgumentNullException(nameof(augmenter));
            }
            lock (m_lock)
            {
                return m_augmenters.Remove(augmenter);
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
            string? issuedProfile = (context.TokenHandler as IssuedIdentityTokenHandler)?
                .IssuedTokenTypeProfileUri;

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
                    if (result.Outcome == AuthenticationOutcome.Accepted && result.Identity != null)
                    {
                        IIdentityAugmenter[] augSnapshot;
                        lock (m_lock)
                        {
                            augSnapshot = [.. m_augmenters];
                        }

                        IUserIdentity identity = result.Identity;
                        foreach (IIdentityAugmenter augmenter in augSnapshot)
                        {
                            AuthenticationResult augmentResult = await augmenter
                                .AugmentAsync(identity, context, ct)
                                .ConfigureAwait(false);
                            if (augmentResult.Outcome == AuthenticationOutcome.Rejected)
                            {
                                return augmentResult;
                            }
                            if (augmentResult.Outcome == AuthenticationOutcome.NotHandled)
                            {
                                continue;
                            }
                            if (augmentResult.Identity == null)
                            {
                                throw new InvalidOperationException(
                                    $"IIdentityAugmenter '{augmenter.GetType().Name}' returned Accepted " +
                                    "without an identity.");
                            }
                            identity = augmentResult.Identity;
                        }

                        return AuthenticationResult.Accept(identity);
                    }

                    return result;
                }
            }

            return AuthenticationResult.NotHandled;
        }

        private static string? GetIssuer(IUserTokenAuthenticator authenticator)
        {
            return authenticator.TokenType == UserTokenType.IssuedToken &&
                authenticator is IIssuerTokenAuthenticator issuerAuthenticator
                ? issuerAuthenticator.IssuerUri
                : null;
        }

        private readonly Lock m_lock = new();
        private readonly List<IUserTokenAuthenticator> m_order = [];
        private readonly List<IIdentityAugmenter> m_augmenters = [];
    }
}
