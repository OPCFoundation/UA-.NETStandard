/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Directs a <c>GetEndpoints</c> request to the best member of a <c>RedundantServerSet</c> when it arrives on the
    /// dedicated balancing discovery URL, and publishes the local Server's own endpoints (observed from normal
    /// discovery requests) so peers can direct Clients to it. Inactive until <see cref="Configure"/> runs at startup;
    /// a stale/unknown view or an unresolved target fails safe to the local Server.
    /// </summary>
    public sealed class ServerLoadDirector : IGetEndpointsDirector
    {
        /// <summary>
        /// Creates the director with the always-available collaborators.
        /// </summary>
        /// <param name="serviceLevelProvider">The local health service-level source.</param>
        /// <param name="loadWeightProvider">The local load-weight source.</param>
        /// <param name="options">The load-direction options (balancing URL).</param>
        /// <param name="logger">Optional logger for best-effort publish failures.</param>
        public ServerLoadDirector(
            IServiceLevelProvider serviceLevelProvider,
            ILoadWeightProvider loadWeightProvider,
            LoadDirectionOptions options,
            ILogger<ServerLoadDirector>? logger = null)
        {
            m_serviceLevelProvider = serviceLevelProvider
                ?? throw new ArgumentNullException(nameof(serviceLevelProvider));
            m_loadWeightProvider = loadWeightProvider ?? throw new ArgumentNullException(nameof(loadWeightProvider));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = logger;
        }

        /// <summary>
        /// Activates the director with the context-dependent collaborators built at server startup.
        /// </summary>
        /// <param name="policy">The direction policy.</param>
        /// <param name="endpointDirectory">Resolves a peer's published endpoints.</param>
        /// <param name="endpointPublisher">Publishes the local Server's endpoints for peers.</param>
        /// <param name="localServerUri">The local ServerUri.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void Configure(
            IServerDirectionPolicy policy,
            IPeerEndpointDirectory endpointDirectory,
            IPeerEndpointPublisher endpointPublisher,
            string localServerUri)
        {
            m_endpointDirectory = endpointDirectory ?? throw new ArgumentNullException(nameof(endpointDirectory));
            m_endpointPublisher = endpointPublisher ?? throw new ArgumentNullException(nameof(endpointPublisher));
            if (string.IsNullOrEmpty(localServerUri))
            {
                throw new ArgumentException("The local ServerUri must be provided.", nameof(localServerUri));
            }
            m_localServerUri = localServerUri;
            m_policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Redirect, ArrayOf<EndpointDescription> Endpoints)> TryGetDirectedEndpointsAsync(
            string? endpointUrl,
            ArrayOf<EndpointDescription> localEndpoints,
            CancellationToken cancellationToken = default)
        {
            IServerDirectionPolicy? policy = m_policy;
            if (policy == null)
            {
                return (false, default);
            }

            if (!IsBalancingEndpoint(endpointUrl))
            {
                // Normal discovery request: publish our own endpoints for peers, then serve locally.
                await MaybePublishLocalEndpointsAsync(localEndpoints, cancellationToken).ConfigureAwait(false);
                return (false, default);
            }

            string? localServerUri = m_localServerUri;
            if (string.IsNullOrEmpty(localServerUri))
            {
                return (false, default);
            }

            string? target;
            try
            {
                target = await policy.SelectTargetServerUriAsync(
                    localServerUri!,
                    m_serviceLevelProvider.GetServiceLevel(),
                    m_loadWeightProvider.GetLoadWeight(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.LoadDirectionPolicyFailed(ex);
                return (false, default);
            }

            if (target == null)
            {
                return (false, default);
            }

            ArrayOf<EndpointDescription> peerEndpoints;
            try
            {
                peerEndpoints = await m_endpointDirectory!
                    .GetEndpointsAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.FailedToResolvePeerEndpoints(ex);
                return (false, default);
            }

            // Fail safe: without the target's endpoints we cannot direct the Client, so serve locally.
            return peerEndpoints.Count == 0 ? (false, default) : (true, peerEndpoints);
        }

        private async ValueTask MaybePublishLocalEndpointsAsync(
            ArrayOf<EndpointDescription> localEndpoints,
            CancellationToken cancellationToken)
        {
            IPeerEndpointPublisher? publisher = m_endpointPublisher;
            if (publisher == null || localEndpoints.Count == 0)
            {
                return;
            }

            string signature = ComputeSignature(localEndpoints);
            if (string.Equals(Volatile.Read(ref m_lastPublishedSignature), signature, StringComparison.Ordinal))
            {
                return;
            }
            Volatile.Write(ref m_lastPublishedSignature, signature);

            try
            {
                await publisher.PublishAsync(localEndpoints, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m_logger?.FailedToPublishLocalEndpointsForLoadDirection(ex);
            }
        }

        private bool IsBalancingEndpoint(string? endpointUrl)
        {
            string balancing = m_options.BalancingEndpointUrl;
            if (string.IsNullOrEmpty(balancing) || string.IsNullOrEmpty(endpointUrl))
            {
                return false;
            }
            return string.Equals(Normalize(endpointUrl!), Normalize(balancing), StringComparison.Ordinal);
        }

        private static string Normalize(string url)
        {
            return url.Trim().TrimEnd('/').ToLowerInvariant();
        }

        private static string ComputeSignature(ArrayOf<EndpointDescription> endpoints)
        {
            var builder = new StringBuilder();
            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                EndpointDescription endpoint = endpoints[ii];
                builder.Append(endpoint.EndpointUrl)
                    .Append('|')
                    .Append(endpoint.SecurityPolicyUri)
                    .Append('|')
                    .Append(((int)endpoint.SecurityMode).ToString(CultureInfo.InvariantCulture))
                    .Append(';');
            }
            return builder.ToString();
        }

        private readonly IServiceLevelProvider m_serviceLevelProvider;
        private readonly ILoadWeightProvider m_loadWeightProvider;
        private readonly LoadDirectionOptions m_options;
        private readonly ILogger<ServerLoadDirector>? m_logger;
        private IServerDirectionPolicy? m_policy;
        private IPeerEndpointDirectory? m_endpointDirectory;
        private IPeerEndpointPublisher? m_endpointPublisher;
        private string? m_localServerUri;
        private string? m_lastPublishedSignature;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ServerLoadDirector"/>.
    /// </summary>
    internal static partial class ServerLoadDirectorLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.ServerLoadDirector + 0, Level = LogLevel.Debug,
            Message = "Load direction policy failed; serving local endpoints.")]
        public static partial void LoadDirectionPolicyFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.ServerLoadDirector + 1, Level = LogLevel.Debug,
            Message = "Failed to resolve peer endpoints; serving local endpoints.")]
        public static partial void FailedToResolvePeerEndpoints(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = RedundancyServerEventIds.ServerLoadDirector + 2, Level = LogLevel.Debug,
            Message = "Failed to publish local endpoints for load direction.")]
        public static partial void FailedToPublishLocalEndpointsForLoadDirection(
            this ILogger logger,
            Exception exception);
    }

}
