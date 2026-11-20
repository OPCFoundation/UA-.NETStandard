// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET8_0_OR_GREATER
using System.Net;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Bindings.WebApi.Authentication
{
    /// <summary>
    /// Fallback target of the WebApi policy scheme for requests that
    /// carry no credential of a registered type. It authenticates
    /// nobody, so <c>RequireAuthorization()</c> rejects the request, and
    /// it answers the resulting challenge with 401 and the
    /// <c>WWW-Authenticate</c> challenges of the registered Basic /
    /// Bearer schemes.
    /// </summary>
    internal sealed class WebApiNoCredentialsAuthenticationHandler :
        AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        /// Name of the no-credentials scheme.
        /// </summary>
        public const string SchemeName = "OpcUaWebApi.NoCredentials";

        /// <summary>
        /// Schemes whose challenge is issued on behalf of the request.
        /// Basic replaces the <c>WWW-Authenticate</c> header while the
        /// JwtBearer handler appends to it, so Basic goes first.
        /// </summary>
        private static readonly string[] s_challengeSchemes =
        [
            WebApiAuthSchemes.Basic,
            WebApiAuthSchemes.Bearer
        ];

        /// <inheritdoc/>
        public WebApiNoCredentialsAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <inheritdoc/>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        /// <inheritdoc/>
        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // Set the status first: a JwtBearerEvents.OnChallenge handler
            // may write the response, after which it can no longer change.
            Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            IAuthenticationSchemeProvider schemes = Context.RequestServices
                .GetRequiredService<IAuthenticationSchemeProvider>();
            foreach (string scheme in s_challengeSchemes)
            {
                if (Response.HasStarted)
                {
                    return;
                }
                if (await schemes.GetSchemeAsync(scheme).ConfigureAwait(false) != null)
                {
                    await Context.ChallengeAsync(scheme, properties).ConfigureAwait(false);
                }
            }
        }
    }
}
#endif
