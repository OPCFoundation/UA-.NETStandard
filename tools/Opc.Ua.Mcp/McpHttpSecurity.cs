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
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HttpStatus = Microsoft.AspNetCore.Http.StatusCodes;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Settings that protect the Streamable HTTP endpoint: the bearer token every request must
    /// carry, the identity that token maps to, and the Host and Origin values a request may use.
    /// </summary>
    internal sealed class McpHttpSecurityOptions
    {
        /// <summary>
        /// The shortest configured token accepted, in characters.
        /// </summary>
        public const int MinimumTokenLength = 32;

        /// <summary>
        /// The issuer of every identity established by the bearer token.
        /// Caller grants for HTTP clients use this issuer.
        /// </summary>
        public const string Issuer = "urn:opcfoundation:mcp:http-token";

        /// <summary>
        /// Gets the secret every request must present as <c>Authorization: Bearer &lt;token&gt;</c>.
        /// </summary>
        public required string Token { get; init; }

        /// <summary>
        /// Gets whether the token was generated for this run rather than configured.
        /// </summary>
        public bool TokenGenerated { get; init; }

        /// <summary>
        /// Gets the subject of the identity a valid token establishes.
        /// </summary>
        public string Subject { get; init; } = "http-token";

        /// <summary>
        /// Gets the host names a request's Host header may name (port is not compared).
        /// </summary>
        public IReadOnlyList<string> AllowedHosts { get; init; } = s_loopbackHosts;

        /// <summary>
        /// Gets the exact origins (scheme://host[:port]) allowed to send requests. A request
        /// without an Origin header is not affected; browsers always send one.
        /// </summary>
        public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

        /// <summary>
        /// Reads <c>McpServer:Http</c> from configuration, generating a token when none is set.
        /// <c>OPCUA_MCP_HTTP_TOKEN</c> is accepted as an alternative to <c>McpServer:Http:Token</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">A configured value is unusable.</exception>
        public static McpHttpSecurityOptions Read(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            IConfigurationSection section = configuration.GetSection("McpServer:Http");
            string? token = section["Token"];
            if (string.IsNullOrEmpty(token))
            {
                token = Environment.GetEnvironmentVariable("OPCUA_MCP_HTTP_TOKEN");
            }
            bool generated = string.IsNullOrEmpty(token);
            if (generated)
            {
                token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            }
            else if (token!.Length < MinimumTokenLength || token.Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"McpServer:Http:Token must be at least {MinimumTokenLength} characters without whitespace.");
            }

            string subject = section["Subject"] ?? "http-token";
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 4096)
            {
                throw new InvalidOperationException("McpServer:Http:Subject must be a non-empty identifier.");
            }

            string[] hosts = Strings(section.GetSection("AllowedHosts"));
            string[] origins = Strings(section.GetSection("AllowedOrigins"));
            foreach (string origin in origins)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme is not ("http" or "https") ||
                    uri.GetLeftPart(UriPartial.Authority) != origin.TrimEnd('/'))
                {
                    throw new InvalidOperationException(
                        $"McpServer:Http:AllowedOrigins entry '{origin}' must be scheme://host[:port].");
                }
            }

            return new McpHttpSecurityOptions
            {
                Token = token!,
                TokenGenerated = generated,
                Subject = subject,
                AllowedHosts = hosts.Length > 0 ? hosts : s_loopbackHosts,
                AllowedOrigins = [.. origins.Select(origin => origin.TrimEnd('/'))]
            };
        }

        /// <summary>
        /// Checks the Host header against the allowed host names, blocking DNS rebinding.
        /// </summary>
        public bool IsAllowedHost(HostString host)
        {
            if (!host.HasValue)
            {
                return false;
            }
            string name = host.Host;
            foreach (string allowed in AllowedHosts)
            {
                if (string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks an Origin header. Absent is allowed (non-browser clients); anything else,
        /// including the opaque origin <c>null</c>, must be configured explicitly.
        /// </summary>
        public bool IsAllowedOrigin(string? origin)
        {
            if (origin is null)
            {
                return true;
            }
            foreach (string allowed in AllowedOrigins)
            {
                if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Compares a presented bearer token with the configured one in constant time.
        /// </summary>
        public bool TokenMatches(string? presented)
        {
            if (presented is null)
            {
                return false;
            }
            byte[] expected = Encoding.UTF8.GetBytes(Token);
            byte[] actual = Encoding.UTF8.GetBytes(presented);
            return actual.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static string[] Strings(IConfigurationSection section)
        {
            if (section.Value is { } single)
            {
                // Allow "a;b;c" as well as an array, matching ASP.NET Core's AllowedHosts convention.
                return single.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            return [.. section.GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())];
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static readonly string[] s_loopbackHosts = ["localhost", "127.0.0.1", "[::1]"];
    }

    /// <summary>
    /// Authenticates <c>Authorization: Bearer &lt;token&gt;</c> against the host's token and maps it to
    /// a stable subject/issuer identity that MCP caller grants can name.
    /// </summary>
    internal sealed class McpBearerTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        /// The authentication scheme name.
        /// </summary>
        public const string SchemeName = "OpcUaMcpBearer";

        /// <summary>
        /// Creates the handler.
        /// </summary>
        public McpBearerTokenHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            McpHttpSecurityOptions security)
            : base(options, logger, encoder)
        {
            m_security = security ?? throw new ArgumentNullException(nameof(security));
        }

        /// <inheritdoc/>
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? header = Request.Headers.Authorization;
            const string prefix = "Bearer ";
            if (string.IsNullOrEmpty(header) ||
                !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            if (!m_security.TokenMatches(header[prefix.Length..].Trim()))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
            }
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, m_security.Subject, ClaimValueTypes.String,
                    McpHttpSecurityOptions.Issuer)],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }

        /// <inheritdoc/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = HttpStatus.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }

        private readonly McpHttpSecurityOptions m_security;
    }

    /// <summary>
    /// Pipeline helpers for the HTTP host.
    /// </summary>
    internal static class McpHttpSecurity
    {
        /// <summary>
        /// Registers the security options, the bearer-token scheme as default and authorization.
        /// </summary>
        public static IServiceCollection AddMcpHttpSecurity(
            this IServiceCollection services,
            McpHttpSecurityOptions security)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(security);
            services.AddSingleton(security);
            services
                .AddAuthentication(McpBearerTokenHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, McpBearerTokenHandler>(
                    McpBearerTokenHandler.SchemeName, null);
            services.AddAuthorization();
            if (security.AllowedOrigins.Count > 0)
            {
                // Browsers preflight the Authorization header; answer only for configured origins.
                services.AddCors(cors => cors.AddPolicy(CorsPolicyName, policy => policy
                    .WithOrigins([.. security.AllowedOrigins])
                    .WithMethods("GET", "POST", "DELETE")
                    .WithHeaders(
                        "Authorization", "Content-Type", "Accept", "Last-Event-ID",
                        "Mcp-Session-Id", "Mcp-Protocol-Version")
                    .WithExposedHeaders("Mcp-Session-Id", "WWW-Authenticate")));
            }
            return services;
        }

        /// <summary>
        /// The CORS policy registered when browser origins are allowed.
        /// </summary>
        public const string CorsPolicyName = "OpcUaMcpAllowedOrigins";

        /// <summary>
        /// Adds Host/Origin validation, CORS for the allowed origins (so their unauthenticated
        /// preflight succeeds), then authentication and authorization. Endpoints still need
        /// <c>RequireAuthorization()</c> to refuse unauthenticated requests.
        /// </summary>
        public static IApplicationBuilder UseMcpHttpSecurity(
            this IApplicationBuilder app,
            McpHttpSecurityOptions security)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(security);
            app.UseMcpHostAndOriginValidation(security);
            if (security.AllowedOrigins.Count > 0)
            {
                app.UseCors(CorsPolicyName);
            }
            app.UseAuthentication();
            app.UseAuthorization();
            return app;
        }

        /// <summary>
        /// Rejects requests whose Host header is not an allowed host name (400) or whose Origin
        /// header is not an allowed origin (403), before authentication or MCP handling runs.
        /// This blocks DNS rebinding and cross-site browser requests even when a token leaks.
        /// </summary>
        public static IApplicationBuilder UseMcpHostAndOriginValidation(
            this IApplicationBuilder app,
            McpHttpSecurityOptions security)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(security);
            return app.Use(async (context, next) =>
            {
                if (!security.IsAllowedHost(context.Request.Host))
                {
                    context.Response.StatusCode = HttpStatus.Status400BadRequest;
                    return;
                }
                string? origin = context.Request.Headers.Origin;
                if (!security.IsAllowedOrigin(string.IsNullOrEmpty(origin) ? null : origin))
                {
                    context.Response.StatusCode = HttpStatus.Status403Forbidden;
                    return;
                }
                await next(context).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Builds the startup notice that tells the user how to connect. The token is printed only
        /// when it was generated for this run; a configured token is never echoed.
        /// </summary>
        public static string StartupNotice(McpHttpSecurityOptions security, int port)
        {
            ArgumentNullException.ThrowIfNull(security);
            string url = $"http://localhost:{port}/mcp";
            if (!security.TokenGenerated)
            {
                return $"HTTP clients must send 'Authorization: Bearer <configured token>' to {url}.";
            }
            return
                $"HTTP clients must send 'Authorization: Bearer {security.Token}' to {url}." + Environment.NewLine +
                "This token was generated for this run. Set McpServer__Http__Token or OPCUA_MCP_HTTP_TOKEN " +
                $"(at least {McpHttpSecurityOptions.MinimumTokenLength} characters) to keep a fixed one." + Environment.NewLine +
                $"Example: claude mcp add --transport http opcua {url} --header \"Authorization: Bearer {security.Token}\"";
        }
    }
}
