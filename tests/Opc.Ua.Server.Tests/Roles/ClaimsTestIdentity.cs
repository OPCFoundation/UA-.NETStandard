// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Tests
{
    internal sealed class ClaimsTestIdentity : IUserIdentity, IIdentityClaims
    {
        private static readonly IUserIdentityTokenHandler s_tokenHandler = new AnonymousIdentityTokenHandler();

        public ClaimsTestIdentity(
            UserTokenType tokenType = UserTokenType.IssuedToken,
            string displayName = "claims-user",
            IEnumerable<string>? groups = null,
            IEnumerable<string>? roles = null,
            string? issuer = null,
            string? subject = null,
            IEnumerable<NodeId>? grantedRoleIds = null)
        {
            TokenType = tokenType;
            DisplayName = displayName;
            Groups = groups?.ToArray() ?? [];
            Roles = roles?.ToArray() ?? [];
            Issuer = issuer!;
            Subject = subject ?? displayName;
            Claims = BuildClaims(Groups, Roles, Issuer!, Subject);
            GrantedRoleIds = grantedRoleIds == null
                ? []
                : ArrayOf.Wrapped(grantedRoleIds.ToArray());
        }

        public string DisplayName { get; }

        public string PolicyId => string.Empty;

        public UserTokenType TokenType { get; }

        public XmlQualifiedName IssuedTokenType => XmlQualifiedName.Empty;

        public bool SupportsSignatures => false;

        public ArrayOf<NodeId> GrantedRoleIds { get; }

        public IUserIdentityTokenHandler TokenHandler => s_tokenHandler;

        public IReadOnlyDictionary<string, object?> Claims { get; }

        public IReadOnlyList<string> Groups { get; }

        public IReadOnlyList<string> Roles { get; }

        public string Issuer { get; }

        public string Subject { get; }

        private static Dictionary<string, object?> BuildClaims(
            IReadOnlyList<string> groups,
            IReadOnlyList<string> roles,
            string issuer,
            string subject)
        {
            var claims = new Dictionary<string, object?>();
            if (groups.Count > 0)
            {
                claims["groups"] = groups;
            }
            if (roles.Count > 0)
            {
                claims["roles"] = roles;
            }
            if (!string.IsNullOrEmpty(issuer))
            {
                claims["iss"] = issuer;
            }
            if (!string.IsNullOrEmpty(subject))
            {
                claims["sub"] = subject;
            }
            return claims;
        }
    }
}
