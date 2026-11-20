// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    public class DefaultAuthenticatorOptions
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
        /// The validator is resolved from the configured
        /// <see cref="ApplicationConfiguration.CertificateManager"/> (an
        /// <see cref="ICertificateManager"/> that implements
        /// <see cref="ICertificateValidatorEx"/>) or from any
        /// <see cref="ICertificateValidatorEx"/> registered in dependency injection.
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
