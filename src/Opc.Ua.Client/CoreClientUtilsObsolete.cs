/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Defines numerous re-useable utility functions for clients.
    /// </summary>
    /// <remarks>
    /// All overloads in this file are obsolete bridges that forward to modern overloads
    /// requiring a non-nullable <see cref="ITelemetryContext"/> argument. Each call site
    /// passes <c>null!</c> to preserve the pre-nullable behavior of these legacy entry
    /// points; downstream code is expected to fall back to ambient/default telemetry.
    /// </remarks>
    public static partial class CoreClientUtils
    {
        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        [Obsolete("Use DiscoverServersAsync instead.")]
        public static IList<string> DiscoverServers(
            ApplicationConfiguration configuration)
        {
            return DiscoverServersAsync(
                configuration,
                DefaultDiscoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Discovers the servers on the local machine.
        /// </summary>
        [Obsolete("Use DiscoverServersAsync instead.")]
        public static IList<string> DiscoverServers(
            ApplicationConfiguration configuration,
            int discoverTimeout)
        {
            return DiscoverServersAsync(
                configuration,
                discoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription? SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                DefaultDiscoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription? SelectEndpoint(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            int discoverTimeout)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                discoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription? SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                DefaultDiscoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync instead.")]
        public static EndpointDescription? SelectEndpoint(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                discoverTimeout,
                null!).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync with ITelemetryContext parameter instead.")]
        public static ValueTask<EndpointDescription?> SelectEndpointAsync(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            CancellationToken ct = default)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                DefaultDiscoverTimeout,
                null!,
                ct);
        }

        /// <summary>
        /// Finds the endpoint that best matches the current settings.
        /// </summary>
        [Obsolete("Use SelectEndpointAsync with ITelemetryContext parameter instead.")]
        public static ValueTask<EndpointDescription?> SelectEndpointAsync(
            ApplicationConfiguration application,
            ITransportWaitingConnection connection,
            bool useSecurity,
            int discoverTimeout,
            CancellationToken ct = default)
        {
            return SelectEndpointAsync(
                application,
                connection,
                useSecurity,
                discoverTimeout,
                null!,
                ct);
        }

        /// <summary>
        /// Selects the endpoint that best matches the current settings.
        /// </summary>
        /// <param name="application"></param>
        /// <param name="discoveryUrl"></param>
        /// <param name="useSecurity"></param>
        /// <param name="discoverTimeout"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        [Obsolete("Use SelectEndpointAsync with ITelemetryContext parameter instead.")]
        public static ValueTask<EndpointDescription?> SelectEndpointAsync(
            ApplicationConfiguration application,
            string discoveryUrl,
            bool useSecurity,
            int discoverTimeout,
            CancellationToken ct = default)
        {
            return SelectEndpointAsync(
                application,
                discoveryUrl,
                useSecurity,
                discoverTimeout,
                null!,
                ct);
        }
    }
}
