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
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Bindings.WebApi.Authentication
{
    /// <summary>
    /// Authentication-scheme constants for the REST binding. Concrete
    /// auth handlers wire up under these scheme names so policies can
    /// reference them by stable identifier.
    /// </summary>
    public static class WebApiAuthSchemes
    {
        /// <summary>HTTP Basic (RFC 7617).</summary>
        public const string Basic = "OpcUaWebApi.Basic";

        /// <summary>Bearer JWT (RFC 6750).</summary>
        public const string Bearer = "OpcUaWebApi.Bearer";

        /// <summary>Mutual TLS client certificate.</summary>
        public const string MutualTls = "OpcUaWebApi.MutualTls";

        /// <summary>Anonymous (no authentication).</summary>
        public const string Anonymous = "OpcUaWebApi.Anonymous";
    }

    /// <summary>
    /// Options for the <see cref="BasicAuthenticationHandler"/>. Hooks
    /// supply the password-verification callback; the binding does not
    /// store passwords.
    /// </summary>
    public sealed class BasicAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>
        /// Realm advertised on the <c>WWW-Authenticate</c> challenge
        /// header. Defaults to <c>OPC UA REST</c>.
        /// </summary>
        public string Realm { get; set; } = "OPC UA REST";

        /// <summary>
        /// Callback that validates a (username, password) pair and
        /// returns a <see cref="ClaimsPrincipal"/> on success or
        /// <c>null</c> to reject. <c>null</c> here causes every
        /// request to fail authentication.
        /// </summary>
        public Func<string, string, Task<ClaimsPrincipal?>>? ValidateCredentials { get; set; }

        /// <summary>
        /// When <c>true</c>, the handler refuses authentication unless
        /// the connection is HTTPS. Defaults to <c>true</c> — sending
        /// HTTP Basic credentials over plain HTTP is a security
        /// vulnerability and we follow the existing HTTPS-JSON
        /// binding's posture.
        /// </summary>
        public bool RequireHttps { get; set; } = true;
    }

    /// <summary>
    /// ASP.NET Core authentication handler for HTTP Basic
    /// (<see href="https://tools.ietf.org/html/rfc7617"/>) used by the
    /// OPC UA REST binding. Decodes <c>Authorization: Basic base64</c>,
    /// invokes
    /// <see cref="BasicAuthenticationOptions.ValidateCredentials"/>,
    /// and emits a <see cref="ClaimsPrincipal"/> with
    /// <see cref="ClaimTypes.Name"/> populated.
    /// </summary>
    public sealed class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
    {
        /// <inheritdoc/>
        public BasicAuthenticationHandler(
            IOptionsMonitor<BasicAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <inheritdoc/>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.RequireHttps && !Request.IsHttps)
            {
                return AuthenticateResult.Fail(
                    "Basic authentication requires HTTPS — refusing to accept credentials over plain HTTP.");
            }

            if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            {
                return AuthenticateResult.NoResult();
            }

            string? header = headerValues.Count > 0 ? headerValues[0] : null;
            if (string.IsNullOrEmpty(header))
            {
                return AuthenticateResult.NoResult();
            }

            if (!AuthenticationHeaderValue.TryParse(header, out var parsed) ||
                !string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(parsed.Parameter))
            {
                return AuthenticateResult.NoResult();
            }

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
            }
            catch (FormatException ex)
            {
                return AuthenticateResult.Fail(ex);
            }

            int separator = decoded.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return AuthenticateResult.Fail(
                    "Malformed Basic credentials (expected 'user:password').");
            }

            string username = decoded[..separator];
            string password = decoded[(separator + 1)..];

            if (Options.ValidateCredentials is null)
            {
                return AuthenticateResult.Fail(
                    "BasicAuthenticationOptions.ValidateCredentials is not configured.");
            }

            ClaimsPrincipal? principal;
            try
            {
                principal = await Options.ValidateCredentials(username, password).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return AuthenticateResult.Fail(ex);
            }

            if (principal is null)
            {
                return AuthenticateResult.Fail("Invalid username or password.");
            }

            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(ticket);
        }

        /// <inheritdoc/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{Options.Realm}\", charset=\"UTF-8\"";
            return base.HandleChallengeAsync(properties);
        }
    }
}
