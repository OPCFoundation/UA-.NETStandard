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

#if NET8_0_OR_GREATER
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi.Authentication;
using Opc.Ua.Schema.OpenApi;

namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Bridges the OPC UA REST Minimal-API endpoints to the
    /// <see cref="HttpsTransportListener"/> Kestrel host. Installed on
    /// every HTTPS / WSS listener factory by
    /// <c>AddWebApiTransport()</c>; ASP.NET Core invokes
    /// <see cref="Configure(IApplicationBuilder, HttpsTransportListener)"/>
    /// once per listener instance, between
    /// <c>UseWebSockets()</c> and the terminal binary / JSON dispatcher.
    /// </summary>
    /// <remarks>
    /// The contributor registers Minimal-API routing services and
    /// the OPC UA REST endpoints (see
    /// <c>MapWebApiEndpoints</c>) — no MVC reflection-based
    /// controller discovery — so the binding is fully
    /// NativeAOT-compatible.
    /// </remarks>
    internal sealed class WebApiHttpsStartupContributor :
        IHttpsListenerStartupContributor,
        IHttpsListenerServiceContributor
    {
        private readonly WebApiServer m_server;
        private readonly IServiceProvider? m_applicationServices;
        private readonly WebApiTransportOptions m_options;
        private readonly WebApiOpenApiGenerator? m_openApiGenerator;

        /// <summary>
        /// Creates a contributor that replays the REST authentication
        /// set up on the application container into each listener host.
        /// </summary>
        /// <param name="server">The REST dispatcher.</param>
        /// <param name="applicationServices">
        /// The application container holding the <c>AddWebApi*Auth()</c>
        /// registrations and the <see cref="ISessionlessIdentityProvider"/>.
        /// </param>
        /// <param name="options">
        /// The options that select the service set and the OpenAPI
        /// document; the defaults when <c>null</c>.
        /// </param>
        /// <param name="openApiGenerator">
        /// The generator of the OpenAPI document; the endpoint creates a
        /// default one when <c>null</c>.
        /// </param>
        public WebApiHttpsStartupContributor(
            WebApiServer server,
            IServiceProvider? applicationServices = null,
            WebApiTransportOptions? options = null,
            WebApiOpenApiGenerator? openApiGenerator = null)
        {
            ArgumentNullException.ThrowIfNull(server);
            m_server = server;
            m_applicationServices = applicationServices;
            m_options = options ?? new WebApiTransportOptions();
            m_openApiGenerator = openApiGenerator;
        }

        /// <inheritdoc/>
        public void ConfigureServices(IServiceCollection services, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(listener);

            services.TryAddSingleton(m_server);
            services.TryAddSingleton<IWebApiServer>(m_server);
            if (m_openApiGenerator != null)
            {
                services.TryAddSingleton(m_openApiGenerator);
            }
            // Minimal-API endpoint mapping needs routing services; no
            // MVC controllers / AddApplicationPart reflection scan.
            services.AddRouting();
            // RequireAuthorization() metadata is enforced by the
            // UseAuthorization() middleware (added conditionally below)
            // which depends on AuthorizationPolicy services. Register
            // them unconditionally so the contributor's no-op pipeline
            // remains valid even before an auth opt-in lands; this is
            // a cheap registration (no runtime cost when no policies).
            services.AddAuthorization();

            AddApplicationAuthentication(services);
        }

        /// <summary>
        /// The listener host has its own service container, so the
        /// authentication schemes and identity provider registered on the
        /// application container by the <c>AddWebApi*Auth()</c> opt-ins
        /// are replayed into it. Without this the listener sees no
        /// scheme, skips <c>UseAuthentication()</c> /
        /// <c>RequireAuthorization()</c> and serves the REST routes
        /// unauthenticated.
        /// </summary>
        /// <remarks>
        /// The identity provider is resolved per request from an
        /// application-container scope that lives as long as the
        /// listener's request scope, so scoped providers (and their
        /// request-scoped dependencies) keep their lifetime; singleton
        /// providers resolve to the application's instance.
        /// </remarks>
        private void AddApplicationAuthentication(IServiceCollection services)
        {
            if (m_applicationServices == null)
            {
                return;
            }

            IServiceProvider applicationServices = m_applicationServices;
            if (applicationServices.GetService<IServiceProviderIsService>()?
                .IsService(typeof(ISessionlessIdentityProvider)) != false)
            {
                services.TryAddScoped<ISessionlessIdentityProvider>(
                    _ => new ApplicationIdentityProvider(applicationServices));
            }

            foreach (WebApiListenerAuthRegistration registration in m_applicationServices
                .GetServices<WebApiListenerAuthRegistration>())
            {
                OpcUaWebApiAuthenticationBuilderExtensions.EnsureWebApiPolicyScheme(services);
                registration.Register(services.AddAuthentication());
            }
        }

        /// <summary>
        /// Verifies that every scheme recorded by an
        /// <c>AddWebApi*Auth()</c> opt-in on the application container is
        /// registered on the listener host's container.
        /// </summary>
        /// <param name="listenerServices">The listener host's services.</param>
        /// <returns><c>true</c> when the application opted into authentication.</returns>
        /// <exception cref="InvalidOperationException">
        /// An opted-in scheme is missing on the listener host.
        /// </exception>
        private bool EnsureApplicationAuthSchemes(IServiceProvider listenerServices)
        {
            if (m_applicationServices == null)
            {
                return false;
            }

            bool authRequired = false;
            AuthenticationOptions? options = null;
            foreach (WebApiListenerAuthRegistration registration in m_applicationServices
                .GetServices<WebApiListenerAuthRegistration>())
            {
                authRequired = true;
                options ??= listenerServices.GetService<IOptions<AuthenticationOptions>>()?.Value;
                if (options?.SchemeMap.ContainsKey(registration.SchemeName) != true)
                {
                    throw new InvalidOperationException(
                        $"The OPC UA REST authentication scheme '{registration.SchemeName}' is not " +
                        "registered on the HTTPS listener host; refusing to serve the REST routes " +
                        "without the configured authentication.");
                }
            }
            return authRequired;
        }

        /// <summary>
        /// Listener-scoped forwarder to the application's
        /// <see cref="ISessionlessIdentityProvider"/>, resolved from an
        /// application-container scope that the listener container
        /// disposes with the request. Forwarding (instead of handing out
        /// the application's instance) keeps the listener container from
        /// disposing an application-owned provider.
        /// </summary>
        private sealed class ApplicationIdentityProvider : ISessionlessIdentityProvider, IDisposable
        {
            private readonly IServiceProvider m_applicationServices;
            private IServiceScope? m_scope;

            public ApplicationIdentityProvider(IServiceProvider applicationServices)
            {
                m_applicationServices = applicationServices;
            }

            public IUserIdentity? Resolve(HttpContext context)
            {
                m_scope ??= m_applicationServices.CreateScope();
                return m_scope.ServiceProvider
                    .GetService<ISessionlessIdentityProvider>()?
                    .Resolve(context);
            }

            public void Dispose()
            {
                m_scope?.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
        {
            ArgumentNullException.ThrowIfNull(appBuilder);
            ArgumentNullException.ThrowIfNull(listener);

            // Fail closed: every scheme the application opted into must
            // be registered on the host that serves the routes.
            // Otherwise the auth middleware would be skipped silently
            // and the REST routes served without the credential.
            bool authRequired = EnsureApplicationAuthSchemes(appBuilder.ApplicationServices);

            // Late-bind the dispatcher to the listener's transport
            // callback. By the time Configure runs Kestrel has been
            // built but not started, and listener.Callback has already
            // been wired by HttpsTransportListener.Open(...).
            m_server.Attach(listener.Callback);
            if (listener.MessageContext is { } context)
            {
                m_server.UpdateMessageContext(context);
            }
            if (!string.IsNullOrEmpty(listener.ListenerId))
            {
                m_server.UpdateListenerId(listener.ListenerId);
            }

            // Pick the listener's OpenAPI endpoint description (added by
            // HttpsServiceHost discovery emission) as the default
            // invocation context. The server pipeline (e.g.
            // SessionManager.CreateSession) dereferences
            // SecureChannelContext.EndpointDescription, so a null endpoint
            // surfaces as a server-side NRE / BadUnexpectedError. Falling
            // back to the SM=None HTTPS-binary description keeps things
            // working for older HttpsServiceHost versions that didn't
            // emit the OpenAPI twin.
            if (listener.Descriptions is { } descriptions)
            {
                EndpointDescription? defaultEndpoint = null;
                foreach (EndpointDescription endpoint in descriptions)
                {
                    if (Profiles.IsHttpsOpenApi(endpoint.TransportProfileUri))
                    {
                        defaultEndpoint = endpoint;
                        break;
                    }
                }
                if (defaultEndpoint == null)
                {
                    foreach (EndpointDescription endpoint in descriptions)
                    {
                        if (endpoint.SecurityMode == MessageSecurityMode.None &&
                            !string.IsNullOrEmpty(endpoint.EndpointUrl) &&
                            Utils.IsUriHttpsScheme(endpoint.EndpointUrl))
                        {
                            defaultEndpoint = endpoint;
                            break;
                        }
                    }
                }
                defaultEndpoint ??= descriptions.Count > 0 ? descriptions[0] : null;
                m_server.UpdateDefaultEndpoint(defaultEndpoint);
            }

            // Mount routing + Minimal-API endpoints. Unmatched paths
            // fall through to the listener's terminal binary / JSON
            // dispatcher below, so the REST surface is additive —
            // existing routes and sub-protocols are unaffected.
            appBuilder.UseRouting();

            // app.UseAuthentication() must run after UseRouting() and
            // before UseEndpoints() for the auth handler resolved per
            // request to populate HttpContext.User in time for the
            // ISessionlessIdentityProvider hook. Only insert it when
            // at least one non-Anonymous auth scheme is registered;
            // bare AddWebApiTransport() (no auth) skips the
            // middleware entirely to preserve the historical anonymous
            // request flow.
            bool hasAuth = authRequired || HasNonAnonymousAuthScheme(appBuilder.ApplicationServices);
            if (hasAuth)
            {
                appBuilder.UseAuthentication();
                // UseAuthorization() enforces metadata produced by
                // RequireAuthorization() on the route group. Without
                // this middleware the authorization policy is silently
                // ignored.
                appBuilder.UseAuthorization();
            }
            appBuilder.UseEndpoints(endpoints =>
            {
                IEndpointConventionBuilder group = endpoints.MapWebApiEndpoints(m_options);
                if (hasAuth)
                {
                    // Require any successful authentication on every
                    // route, the OpenAPI document included; the discovery
                    // routes (FindServers / GetEndpoints) carry
                    // AllowAnonymous metadata so they remain reachable
                    // without a credential.
                    group.RequireAuthorization();
                }
            });

            // Wire WSS bearer-token validation so the
            // opcua+openapi+<accesstoken> sub-protocol no longer
            // accepts arbitrary tokens. The listener fail-closed
            // rejects when no validator is registered.
            listener.WssBearerTokenValidator = ValidateWssBearerTokenAsync;

            // The plain opcua+openapi upgrade is handled by the listener's
            // terminal dispatcher, not by a route, so RequireAuthorization()
            // never applies to it. Hold it to the same credential as the
            // REST routes.
            listener.WssOpenApiUpgradeAuthenticator = hasAuth ? AuthenticateWssOpenApiUpgradeAsync : null;
            listener.WssOpenApiIdentityResolver = ResolveWssOpenApiIdentity;
        }

        /// <summary>
        /// Authenticates and authorizes the plain <c>opcua+openapi</c>
        /// WebSocket upgrade with the default authorization policy, the
        /// one <c>RequireAuthorization()</c> applies to the REST routes, so
        /// Basic, Bearer (<c>Authorization</c> header) and the client
        /// certificate are honoured. On failure the request is answered
        /// by the authorization middleware result handler, with the
        /// challenge (401 and <c>WWW-Authenticate</c>) or forbid (403) of
        /// the policy's schemes.
        /// </summary>
        /// <param name="context">The upgrade request.</param>
        /// <returns><c>true</c> when the upgrade may be accepted.</returns>
        internal static async Task<bool> AuthenticateWssOpenApiUpgradeAsync(HttpContext context)
        {
            IServiceProvider services = context.RequestServices;
            AuthorizationPolicy policy = await services
                .GetRequiredService<IAuthorizationPolicyProvider>()
                .GetDefaultPolicyAsync()
                .ConfigureAwait(false);
            IPolicyEvaluator evaluator = services.GetRequiredService<IPolicyEvaluator>();
            AuthenticateResult authentication = await evaluator
                .AuthenticateAsync(policy, context)
                .ConfigureAwait(false);
            // The authorization middleware passes the HttpContext as the
            // resource; requirements of the default policy see the same.
            PolicyAuthorizationResult authorization = await evaluator
                .AuthorizeAsync(policy, authentication, context, resource: context)
                .ConfigureAwait(false);
            if (authorization.Succeeded)
            {
                return true;
            }
            // Challenge or forbid with the policy's schemes exactly as the
            // authorization middleware does for the REST routes.
            await services.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
                .HandleAsync(static _ => Task.CompletedTask, context, policy, authorization)
                .ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// Maps the principal of an <c>opcua+openapi</c> upgrade through
        /// the <see cref="ISessionlessIdentityProvider"/> the REST routes
        /// use.
        /// </summary>
        /// <param name="context">The upgrade request.</param>
        /// <returns>The mapped identity, or <c>null</c>.</returns>
        private static IUserIdentity? ResolveWssOpenApiIdentity(HttpContext context)
        {
            return context.RequestServices
                .GetService<ISessionlessIdentityProvider>()?
                .Resolve(context);
        }

        /// <summary>
        /// Validates the bearer token presented in the WSS
        /// opcua+openapi+{accesstoken} sub-protocol by delegating to the
        /// registered JwtBearer authentication scheme. Returns true only
        /// when the token authenticates; on success the resolved
        /// ClaimsPrincipal is published on HttpContext.User so downstream
        /// code (upstream-identity plumbing) can route it through the
        /// ISessionlessIdentityProvider hook.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        internal static async Task<bool> ValidateWssBearerTokenAsync(
            HttpContext context,
            string accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }
            IAuthenticationSchemeProvider schemes = context.RequestServices
                .GetRequiredService<IAuthenticationSchemeProvider>();
            AuthenticationScheme? bearerScheme = await schemes
                .GetSchemeAsync(WebApiAuthSchemes.Bearer)
                .ConfigureAwait(false);
            if (bearerScheme == null)
            {
                return false;
            }
            // Place the token in the Authorization header so JwtBearerHandler
            // resolves the credential from its standard location instead of
            // requiring a bespoke per-handler API.
            context.Request.Headers.Authorization = $"Bearer {accessToken}";
            IAuthenticationService authService = context.RequestServices
                .GetRequiredService<IAuthenticationService>();
            AuthenticateResult result = await authService
                .AuthenticateAsync(context, WebApiAuthSchemes.Bearer)
                .ConfigureAwait(false);
            if (result.Succeeded && result.Principal != null)
            {
                context.User = result.Principal;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves the AuthenticationOptions snapshot so that bindings
        /// composed without any AddWebApi*Auth() call don't pay the
        /// UseAuthentication() middleware cost (and don't change
        /// HttpContext.User semantics for the anonymous path). The
        /// options-based path keeps this purely synchronous — no
        /// sync-over-async on IAuthenticationSchemeProvider.
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        private static bool HasNonAnonymousAuthScheme(IServiceProvider services)
        {
            IOptions<AuthenticationOptions>? options = services
                .GetService<IOptions<AuthenticationOptions>>();
            return options?.Value.Schemes.Any() == true;
        }
    }
}
#endif
