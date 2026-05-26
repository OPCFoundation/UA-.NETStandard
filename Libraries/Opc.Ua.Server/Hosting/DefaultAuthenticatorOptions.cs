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
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Options for registering the built-in identity authenticators.
    /// </summary>
    public sealed class DefaultAuthenticatorOptions
    {
        /// <summary>
        /// Register <see cref="AnonymousAuthenticator"/>.
        /// </summary>
        public bool EnableAnonymous { get; set; } = true;

        /// <summary>
        /// Register <see cref="UserNamePasswordAuthenticator"/> when user services are available.
        /// </summary>
        public bool EnableUserNamePassword { get; set; } = true;

        /// <summary>
        /// Register <see cref="X509Authenticator"/> when a certificate validator is available.
        /// </summary>
        public bool EnableX509 { get; set; } = true;

        /// <summary>
        /// Register <see cref="JwtAuthenticator"/> when issuer options are supplied.
        /// </summary>
        public bool EnableJwt { get; set; } = true;

        /// <summary>
        /// Optional user database for username/password authentication.
        /// </summary>
        public IUserDatabase? UserDatabase { get; set; }

        /// <summary>
        /// Optional user-management facade for username/password authentication.
        /// </summary>
        public IUserManagement? UserManagement { get; set; }

        /// <summary>
        /// Optional certificate validator for X.509 user tokens.
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// Trust list used for X.509 user tokens.
        /// </summary>
        public TrustListIdentifier UserCertificateTrustList { get; set; } = TrustListIdentifier.Users;

        /// <summary>
        /// Optional issuer key resolver for JWT issued identity tokens.
        /// </summary>
        public IIssuerKeyResolver? IssuerKeyResolver { get; set; }

        /// <summary>
        /// Expected JWT audience for this server.
        /// </summary>
        public string? ExpectedAudience { get; set; }

        /// <summary>
        /// Clock skew tolerated while validating JWT lifetime claims.
        /// </summary>
        public TimeSpan ClockSkewTolerance { get; set; } = TimeSpan.FromSeconds(60);
    }
}
