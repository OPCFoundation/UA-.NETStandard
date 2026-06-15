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
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Bindings.WebApi.Authentication;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Authentication-mode opt-ins for the OPC UA REST binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each extension installs an ASP.NET Core authentication handler
    /// for one of the four spec-supported auth models (Anonymous,
    /// Bearer JWT, HTTP Basic, Mutual TLS) and registers a
    /// <see cref="ISessionlessIdentityProvider"/> that maps the
    /// resulting <see cref="ClaimsPrincipal"/> to an OPC UA
    /// <see cref="IUserIdentity"/>. The actual cryptographic
    /// validation is delegated to the standard ASP.NET Core handlers
    /// (<c>Microsoft.AspNetCore.Authentication.JwtBearer</c>,
    /// <c>Microsoft.AspNetCore.Authentication.Certificate</c>, the
    /// in-package <see cref="BasicAuthenticationHandler"/>).
    /// </para>
    /// <para>
    /// Call <c>AddWebApiTransport()</c> first; then opt in to as many
    /// auth modes as desired. They compose (a single request can
    /// satisfy any of the registered schemes via ASP.NET Core's
    /// multi-scheme policy).
    /// </para>
    /// </remarks>
    public static class OpcUaWebApiAuthenticationBuilderExtensions
    {
        /// <summary>
        /// Registers the default sessionless identity provider
        /// (<see cref="DefaultSessionlessIdentityProvider"/>) and the
        /// anonymous authentication scheme. This is the no-op baseline
        /// for the REST binding — requests without an
        /// <c>Authorization</c> header succeed without challenge.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        public static IOpcUaBuilder AddWebApiAnonymousAuth(this IOpcUaBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Services.TryAddSingleton<ISessionlessIdentityProvider, DefaultSessionlessIdentityProvider>();
            return builder;
        }

        /// <summary>
        /// Registers the Bearer JWT authentication scheme
        /// (<see cref="WebApiAuthSchemes.Bearer"/>) using the standard
        /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>
        /// middleware.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">
        /// Callback that configures the JWT validation parameters
        /// (issuer, audience, signing keys, etc.).
        /// </param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiBearerAuth(
            this IOpcUaBuilder builder,
            Action<JwtBearerOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            builder.Services.TryAddSingleton<ISessionlessIdentityProvider, DefaultSessionlessIdentityProvider>();
            builder.Services
                .AddAuthentication()
                .AddJwtBearer(WebApiAuthSchemes.Bearer, configure);
            return builder;
        }

        /// <summary>
        /// Replaces the default <see cref="ISessionlessIdentityProvider"/>
        /// with a <see cref="JwtClaimSessionlessIdentityProvider"/> that
        /// projects standard JWT claims (<c>sub</c> / <c>scope</c> /
        /// <c>roles</c>) onto the OPC UA identity model and carries the
        /// raw bearer token as a <see cref="Profiles.JwtUserToken"/>
        /// issued-token payload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call after <see cref="AddWebApiBearerAuth(IOpcUaBuilder, Action{JwtBearerOptions})"/>
        /// (or another bearer-style auth registration that populates
        /// <see cref="System.Net.Http.HttpRequestMessage.Headers"/>
        /// with the raw token). The replacement is DI-scoped to a
        /// singleton — last call wins. JWT validation (signing-key
        /// resolution, issuer / audience / expiry checks) is delegated
        /// to the upstream bearer middleware and is NOT re-performed
        /// here; this provider is purely a claims-to-identity mapper.
        /// </para>
        /// <para>
        /// Subsequent server-side code can recover the projected
        /// subject, scopes, and roles via the
        /// <see cref="JwtClaimSessionlessIdentityProvider.GetSubject"/>,
        /// <see cref="JwtClaimSessionlessIdentityProvider.GetScopes"/>,
        /// and
        /// <see cref="JwtClaimSessionlessIdentityProvider.GetRoles"/>
        /// helpers passing the <c>WebApiInvocationContext.Identity</c>
        /// handed to <c>IWebApiServer.InvokeAsync</c>.
        /// RoleBasedUserManagement integration is a separate, larger
        /// design and not part of this minimal helper.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">
        /// Optional callback that customises the JWT claim-name
        /// projection (defaults to the OAuth 2.0 / OpenID Connect
        /// standard names <c>"sub"</c> / <c>"scope"</c> /
        /// <c>"roles"</c>).
        /// </param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder UseJwtClaimIdentityProvider(
            this IOpcUaBuilder builder,
            Action<JwtClaimSessionlessIdentityProviderOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var options = new JwtClaimSessionlessIdentityProviderOptions();
            configure?.Invoke(options);

            // Replace any earlier registration (TryAddSingleton + manual
            // override) so the JWT provider becomes the singleton
            // resolved by IWebApiServer dispatch.
            builder.Services.RemoveAll<ISessionlessIdentityProvider>();
            builder.Services.AddSingleton<ISessionlessIdentityProvider>(
                new JwtClaimSessionlessIdentityProvider(options));
            return builder;
        }

        /// <summary>
        /// Registers the HTTP Basic authentication scheme
        /// (<see cref="WebApiAuthSchemes.Basic"/>) using the in-package
        /// <see cref="BasicAuthenticationHandler"/>. The supplied
        /// <paramref name="validate"/> callback verifies credentials.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="validate">
        /// Async callback that receives the (username, password) pair
        /// and returns the authenticated principal on success, or
        /// <c>null</c> to reject.
        /// </param>
        /// <param name="configure">
        /// Optional callback that customises the handler options
        /// (realm, require-HTTPS, etc.).
        /// </param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="validate"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiBasicAuth(
            this IOpcUaBuilder builder,
            Func<string, string, Task<ClaimsPrincipal?>> validate,
            Action<BasicAuthenticationOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(validate);

            builder.Services.TryAddSingleton<ISessionlessIdentityProvider, DefaultSessionlessIdentityProvider>();
            builder.Services
                .AddAuthentication()
                .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
                    WebApiAuthSchemes.Basic,
                    options =>
                    {
                        options.ValidateCredentials = validate;
                        configure?.Invoke(options);
                    });
            return builder;
        }

        /// <summary>
        /// Registers the mutual-TLS client-certificate authentication
        /// scheme (<see cref="WebApiAuthSchemes.MutualTls"/>) using
        /// <c>Microsoft.AspNetCore.Authentication.Certificate</c>. The
        /// caller is responsible for configuring Kestrel's
        /// <c>ClientCertificateMode</c> (typically
        /// <c>RequireCertificate</c> or <c>AllowCertificate</c>) — the
        /// existing HTTPS binding already enables MTLS when
        /// <c>HttpsSettings.HttpsMutualTls</c> is set, which is the
        /// expected path.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">
        /// Optional callback that customises certificate validation
        /// (allowed certificate types, chain trust, etc.).
        /// </param>
        /// <returns>The same <paramref name="builder"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaBuilder AddWebApiMutualTlsAuth(
            this IOpcUaBuilder builder,
            Action<CertificateAuthenticationOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.TryAddSingleton<ISessionlessIdentityProvider, DefaultSessionlessIdentityProvider>();

            AuthenticationBuilder authBuilder = builder.Services.AddAuthentication();
            if (configure != null)
            {
                authBuilder.AddCertificate(WebApiAuthSchemes.MutualTls, configure);
            }
            else
            {
                authBuilder.AddCertificate(WebApiAuthSchemes.MutualTls);
            }
            return builder;
        }
    }
}
