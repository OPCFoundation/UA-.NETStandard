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
using System.Collections.Generic;
using System.Xml;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// User identity backed by a validated JWT issued identity token.
    /// </summary>
    public sealed class JwtUserIdentity : IUserIdentity, IIdentityClaims
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
        {
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
