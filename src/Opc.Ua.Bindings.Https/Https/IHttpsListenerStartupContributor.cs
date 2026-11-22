// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Extension hook into <see cref="HttpsTransportListener"/>'s Kestrel
    /// pipeline. Companion bindings (such as the REST binding in
    /// <c>OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi</c>) register a
    /// contributor on <see cref="HttpsServiceHost.StartupContributors"/>
    /// to attach additional middleware (typically routing + MVC
    /// controllers) to the same Kestrel host that already serves the
    /// HTTPS-binary (Part 6 §7.4.4) and HTTPS-JSON (Part 6 §7.4.5)
    /// sub-profiles. Contributor middleware runs BEFORE the terminal
    /// binary / JSON dispatcher, so unmatched requests still fall
    /// through to the existing transport handlers.
    /// </summary>
    public interface IHttpsListenerStartupContributor
    {
        /// <summary>
        /// Invoked by <c>HttpsTransportListener</c>'s <c>Startup.Configure</c>
        /// after <see cref="WebSocketMiddlewareExtensions.UseWebSockets(IApplicationBuilder)"/>
        /// and before the terminal binary / JSON dispatcher
        /// <see cref="RunExtensions.Run"/>.
        /// </summary>
        /// <param name="appBuilder">The Kestrel pipeline builder.</param>
        /// <param name="listener">The listener whose pipeline is being
        /// configured. Contributors typically read the listener's
        /// transport callback / message context out of this instance to
        /// wire their request handlers to the host server.</param>
        void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener);
    }

    /// <summary>
    /// Optional service-registration companion for
    /// <see cref="IHttpsListenerStartupContributor"/> implementations.
    /// </summary>
    public interface IHttpsListenerServiceContributor
    {
        /// <summary>
        /// Invoked while the listener's Kestrel host services are being built.
        /// </summary>
        /// <param name="services">The web-host service collection.</param>
        /// <param name="listener">The listener whose host is being configured.</param>
        void ConfigureServices(IServiceCollection services, HttpsTransportListener listener);
    }
}
