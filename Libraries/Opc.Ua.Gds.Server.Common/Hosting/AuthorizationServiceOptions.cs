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

using System;
using System.Collections.Generic;

#nullable enable

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Options for the GDS AuthorizationService JWT issuer.
    /// </summary>
    public sealed class AuthorizationServiceOptions
    {
        /// <summary>
        /// Gets or sets the certificate used to sign issued JWTs. When not
        /// set, the hosted GDS falls back to its application instance
        /// certificate.
        /// </summary>
        public CertificateIdentifier? SigningCertificate { get; set; }

        /// <summary>
        /// Gets or sets the JWT <c>iss</c> value. When empty, the hosted
        /// GDS uses its application URI.
        /// </summary>
        public string IssuerUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the lifetime used when the request does not ask for
        /// a shorter lifetime.
        /// </summary>
        public TimeSpan DefaultTokenLifetime { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Gets or sets a value indicating whether the in-memory provider issues
        /// refresh tokens during FinishRequestToken and accepts them on RefreshToken.
        /// Defaults to <c>true</c>. Setting to <c>false</c> preserves the legacy
        /// behaviour where Finish returns no refresh token and Refresh throws
        /// BadNotSupported.
        /// </summary>
        public bool EnableRefreshTokens { get; set; } = true;

        /// <summary>
        /// Gets or sets the lifetime applied to each newly-issued refresh token.
        /// Refresh tokens slide — each successful refresh extends the lifetime
        /// of the new token by this value. OPC 10000-12 does not mandate a
        /// value; the default is 7 days per OAuth2 norms.
        /// </summary>
        public TimeSpan DefaultRefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Gets the audience URIs that may receive tokens. When empty, the
        /// default implementation accepts the requested resource URI.
        /// </summary>
        public IList<string> AllowedAudiences { get; } = new List<string>();

        /// <summary>
        /// Gets the scopes granted when the caller did not request scopes.
        /// </summary>
        public IList<string> DefaultScopes { get; } = new List<string>();

        /// <summary>
        /// Gets or sets an optional authorization predicate over caller,
        /// audience and requested scopes.
        /// </summary>
        public Func<IUserIdentity?, string, IReadOnlyList<string>, bool>? AccessControl { get; set; }
    }
}
