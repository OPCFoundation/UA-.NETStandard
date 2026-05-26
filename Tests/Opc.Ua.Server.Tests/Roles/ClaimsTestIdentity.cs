/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
            IEnumerable<string> groups = null,
            IEnumerable<string> roles = null,
            string issuer = null,
            string subject = null,
            IEnumerable<NodeId> grantedRoleIds = null)
        {
            TokenType = tokenType;
            DisplayName = displayName;
            Groups = groups?.ToArray() ?? [];
            Roles = roles?.ToArray() ?? [];
            Issuer = issuer;
            Subject = subject ?? displayName;
            Claims = BuildClaims(Groups, Roles, Issuer, Subject);
            GrantedRoleIds = grantedRoleIds == null
                ? ArrayOf.Empty<NodeId>()
                : ArrayOf.Wrapped(grantedRoleIds.ToArray());
        }

        public string DisplayName { get; }

        public string PolicyId => string.Empty;

        public UserTokenType TokenType { get; }

        public XmlQualifiedName IssuedTokenType => XmlQualifiedName.Empty;

        public bool SupportsSignatures => false;

        public ArrayOf<NodeId> GrantedRoleIds { get; }

        public IUserIdentityTokenHandler TokenHandler => s_tokenHandler;

        public IReadOnlyDictionary<string, object> Claims { get; }

        public IReadOnlyList<string> Groups { get; }

        public IReadOnlyList<string> Roles { get; }

        public string Issuer { get; }

        public string Subject { get; }

        private static IReadOnlyDictionary<string, object> BuildClaims(
            IReadOnlyList<string> groups,
            IReadOnlyList<string> roles,
            string issuer,
            string subject)
        {
            var claims = new Dictionary<string, object>();
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
