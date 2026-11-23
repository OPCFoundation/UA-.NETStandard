// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Xml;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// User identity backed by a validated JWT issued identity token.
    /// </summary>
    public sealed class JwtUserIdentity : IUserIdentity, IIdentityClaims, IIdentityExpiration
    {
        /// <summary>
        /// Creates a JWT user identity.
        /// </summary>
        public JwtUserIdentity(
            IssuedIdentityTokenHandler tokenHandler,
            IReadOnlyDictionary<string, object?> claims,
            IReadOnlyList<string> groups,
            IReadOnlyList<string> roles,
            string? issuer,
            string? subject)
            : this(tokenHandler, claims, groups, roles, issuer, subject, expiresAt: null)
        {
        }

        /// <summary>
        /// Creates a JWT user identity whose credentials expire at
        /// <paramref name="expiresAt"/> (UTC).
        /// </summary>
        public JwtUserIdentity(
            IssuedIdentityTokenHandler tokenHandler,
            IReadOnlyDictionary<string, object?> claims,
            IReadOnlyList<string> groups,
            IReadOnlyList<string> roles,
            string? issuer,
            string? subject,
            DateTime? expiresAt)
        {
            ExpiresAt = expiresAt;
            TokenHandler = tokenHandler ?? throw new ArgumentNullException(nameof(tokenHandler));
            Claims = claims ?? throw new ArgumentNullException(nameof(claims));
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
            Roles = roles ?? throw new ArgumentNullException(nameof(roles));
            Issuer = issuer;
            Subject = subject;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object?> Claims { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Groups { get; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Roles { get; }

        /// <inheritdoc/>
        public string? Issuer { get; }

        /// <inheritdoc/>
        public string? Subject { get; }

        /// <inheritdoc/>
        public DateTime? ExpiresAt { get; }

        /// <inheritdoc/>
        public string DisplayName => Subject ?? TokenHandler.DisplayName;

        /// <inheritdoc/>
        public string PolicyId => TokenHandler.Token.PolicyId ?? string.Empty;

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.IssuedToken;

        /// <inheritdoc/>
        public XmlQualifiedName IssuedTokenType => new(
            string.Empty,
            TokenHandler.IssuedTokenTypeProfileUri ?? string.Empty);

        /// <inheritdoc/>
        public bool SupportsSignatures => false;

        /// <inheritdoc/>
        public ArrayOf<NodeId> GrantedRoleIds => [];

        /// <inheritdoc/>
        public IssuedIdentityTokenHandler TokenHandler { get; }

        IUserIdentityTokenHandler IUserIdentity.TokenHandler => TokenHandler;
    }
}
