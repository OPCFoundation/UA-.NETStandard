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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default implementation of <see cref="IServerRedundancyHandler"/>
    /// that reads redundancy information from the OPC UA server address
    /// space and selects the best failover target based on service level
    /// and server state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the following nodes:
    /// <list type="bullet">
    ///   <item><c>Server.ServerRedundancy.RedundancySupport</c></item>
    ///   <item><c>Server.ServerRedundancy.RedundantServerArray</c></item>
    ///   <item><c>Server.ServiceLevel</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// For transparent redundancy the handler returns <see langword="null"/>
    /// from <see cref="SelectFailoverTarget"/> because the server
    /// infrastructure handles failover transparently.
    /// </para>
    /// <para>
    /// For non-transparent modes the handler picks the server with the
    /// highest <c>ServiceLevel</c> that is in
    /// <see cref="ServerState.Running"/> state and whose URI differs
    /// from the current endpoint.
    /// </para>
    /// </remarks>
    public sealed class DefaultServerRedundancyHandler : IServerRedundancyHandler, IServerRedundancyEndpointCache
    {
        /// <summary>
        /// The default maintenance retry backoff when the server does not provide a future return time.
        /// </summary>
        public static readonly TimeSpan DefaultMaintenanceBackoff = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultServerRedundancyHandler"/> class.
        /// </summary>
        /// <param name="endpointResolver">
        /// Resolves a peer URI to an endpoint. Defaults to a
        /// <see cref="ReverseConnectRedundantServerEndpointResolver"/> when
        /// <see cref="ServerRedundancyOptions.MemberConnections"/> is set and to a
        /// <see cref="DefaultRedundantServerEndpointResolver"/> otherwise.
        /// </param>
        /// <param name="timeProvider">Clock used for the discovery bound.</param>
        /// <param name="options">
        /// Bounds applied to peer discovery. Defaults to
        /// <see cref="ServerRedundancyOptions.DefaultTimeout"/> per peer.
        /// </param>
        public DefaultServerRedundancyHandler(
            IRedundantServerEndpointResolver? endpointResolver = null,
            TimeProvider? timeProvider = null,
            ServerRedundancyOptions? options = null)
        {
            ServerRedundancyOptions effective = options ?? new ServerRedundancyOptions();
            effective.Validate();
            m_endpointResolver = endpointResolver ?? CreateEndpointResolver(effective, null);
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_peerDiscoveryTimeout = effective.PeerDiscoveryTimeout;
        }

        /// <summary>
        /// Creates the endpoint resolver that matches how the members of the
        /// set are reached.
        /// </summary>
        internal static IRedundantServerEndpointResolver CreateEndpointResolver(
            ServerRedundancyOptions? options,
            ITelemetryContext? telemetry)
        {
            return options?.MemberConnections is IReverseConnectionAvailability connections
                ? new ReverseConnectRedundantServerEndpointResolver(connections, telemetry)
                : new DefaultRedundantServerEndpointResolver(telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            ISession session,
            CancellationToken ct = default)
        {
            ServerRedundancyInfo snapshot = await ReadRedundancyInfoAsync(session, ct).ConfigureAwait(false);
            return await ((IServerRedundancyEndpointCache)this).ResolveCachedEndpointsAsync(
                snapshot, session.ConfiguredEndpoint, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        ValueTask<ServerRedundancyInfo> IServerRedundancyEndpointCache.ReadRedundancyInfoAsync(
            ISession session,
            CancellationToken ct)
        {
            return ReadRedundancyInfoAsync(session, ct);
        }

        private async ValueTask<ServerRedundancyInfo> ReadRedundancyInfoAsync(
            ISession session,
            CancellationToken ct)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            ct.ThrowIfCancellationRequested();

            // Read redundancy status and peer discovery nodes in one call.
            ArrayOf<NodeId> nodeIds =
            [
                VariableIds.Server_ServerRedundancy_RedundancySupport,
                VariableIds.Server_ServiceLevel,
                VariableIds.Server_ServerRedundancy_RedundantServerArray,
                VariableIds.Server_ServerRedundancy_ServerUriArray,
                VariableIds.Server_EstimatedReturnTime
            ];

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                await session.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var readStatuses = new Dictionary<NodeId, StatusCode>(nodeIds.Count + 1);
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                readStatuses[nodeIds[ii]] = GetReadStatus(values[ii], errors[ii]);
            }

            RedundancySupport mode = RedundancySupport.None;
            if (StatusCode.IsGood(errors[0].StatusCode))
            {
                mode = ToRedundancySupport(values[0].GetValue(0));
            }

            byte serviceLevel = 0;
            bool serviceLevelAccessible = StatusCode.IsGood(errors[1].StatusCode);
            if (StatusCode.IsGood(errors[1].StatusCode))
            {
                serviceLevel = values[1].GetValue<byte>(0);
            }

            DateTime estimatedReturnTime = DateTime.MinValue;
            if (StatusCode.IsGood(errors[4].StatusCode))
            {
                estimatedReturnTime = values[4].GetValue(DateTime.MinValue);
            }

            var redundantServers = new ArrayOf<RedundantServer>();
            string currentServerId = string.Empty;
            if (mode != RedundancySupport.None)
            {
                redundantServers = ReadRedundantServers(
                    mode,
                    values[2],
                    errors[2],
                    values[3],
                    errors[3]);
                currentServerId = mode == RedundancySupport.Transparent
                    ? await ReadCurrentServerIdAsync(session, readStatuses, ct).ConfigureAwait(false)
                    : string.Empty;
                redundantServers = redundantServers.ConvertAll(server => WithEndpoint(
                    server, m_resolvedEndpoints.TryGetValue(
                        CreateCacheKey(server.ServerUri, session.ConfiguredEndpoint),
                        out ConfiguredEndpoint? endpoint)
                        ? endpoint
                        : null));
            }

            ct.ThrowIfCancellationRequested();
            return new ServerRedundancyInfo
            {
                Mode = mode,
                ServiceLevel = serviceLevel,
                ServiceLevelAccessible = serviceLevelAccessible,
                ServiceLevelSubrange = ServiceLevels.GetSubrange(serviceLevel),
                EstimatedReturnTime = estimatedReturnTime,
                CurrentServerId = currentServerId,
                RedundantServers = redundantServers,
                ReadStatuses = readStatuses
            };
        }

        /// <inheritdoc/>
        public ServerFailoverDecision ShouldFailover(
            ServerRedundancyInfo redundancyInfo,
            ConfiguredEndpoint currentEndpoint)
        {
            if (redundancyInfo is null)
            {
                throw new ArgumentNullException(nameof(redundancyInfo));
            }

            if (currentEndpoint is null)
            {
                throw new ArgumentNullException(nameof(currentEndpoint));
            }

            if (redundancyInfo.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return NoFailover("Redundancy mode does not require client-side failover.");
            }

            if (redundancyInfo.ServiceLevelAccessible &&
                ServiceLevels.IsHealthy(redundancyInfo.ServiceLevel))
            {
                return NoFailover("Current server remains in the Healthy service level subrange.");
            }

            RedundantServer? best = SelectBestPeer(redundancyInfo, currentEndpoint);

            if (redundancyInfo.ServiceLevelAccessible &&
                ServiceLevels.IsMaintenance(redundancyInfo.ServiceLevel))
            {
                if (best != null)
                {
                    return new ServerFailoverDecision(
                        isFailoverWarranted: true,
                        DateTime.MinValue,
                        "Current server is in Maintenance and an operational redundant server is available.");
                }

                DateTime retryAfter = GetMaintenanceRetryTime(redundancyInfo.EstimatedReturnTime);
                return new ServerFailoverDecision(
                    isFailoverWarranted: false,
                    retryAfter,
                    "Current server is in Maintenance and no operational redundant server is available.");
            }

            if (best == null)
            {
                return NoFailover("No operational redundant server is available.");
            }

            if (redundancyInfo.ServiceLevelAccessible &&
                ServiceLevels.IsDegraded(redundancyInfo.ServiceLevel) &&
                best.ServiceLevelKnown &&
                !ServiceLevels.IsHealthy(best.ServiceLevel))
            {
                return NoFailover("Current server is Degraded and no Healthy peer is available.");
            }

            return new ServerFailoverDecision(
                isFailoverWarranted: true,
                DateTime.MinValue,
                "A better redundant server is available.");
        }

        /// <inheritdoc/>
        public ConfiguredEndpoint? SelectFailoverTarget(
            ServerRedundancyInfo redundancyInfo,
            ConfiguredEndpoint currentEndpoint)
        {
            if (redundancyInfo is null)
            {
                throw new ArgumentNullException(nameof(redundancyInfo));
            }

            if (currentEndpoint is null)
            {
                throw new ArgumentNullException(nameof(currentEndpoint));
            }

            if (redundancyInfo.Mode is RedundancySupport.None or RedundancySupport.Transparent)
            {
                return null;
            }

            // ShouldFailover evaluates proactive switching while the current
            // channel is usable. This method is also called after reconnect
            // exhaustion, when the cached current ServiceLevel may still be
            // Healthy even though the endpoint is unreachable. At that point
            // target selection must use the viable peer set directly.
            return SelectBestPeer(redundancyInfo, currentEndpoint)?.Endpoint;
        }

        /// <inheritdoc/>
        async ValueTask<ServerRedundancyInfo> IServerRedundancyEndpointCache.ResolveCachedEndpointsAsync(
            ServerRedundancyInfo snapshot,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct,
            ConfiguredEndpoint? initialEndpoint)
        {
            ArrayOf<RedundantServer> servers = await ResolveEndpointsAsync(
                snapshot.RedundantServers, currentEndpoint, initialEndpoint, ct).ConfigureAwait(false);
            return new ServerRedundancyInfo
            {
                Mode = snapshot.Mode,
                RedundantServers = servers,
                ServiceLevel = snapshot.ServiceLevel,
                ServiceLevelAccessible = snapshot.ServiceLevelAccessible,
                ServiceLevelSubrange = snapshot.ServiceLevelSubrange,
                EstimatedReturnTime = snapshot.EstimatedReturnTime,
                CurrentServerId = snapshot.CurrentServerId,
                ReadStatuses = snapshot.ReadStatuses
            };
        }

        /// <inheritdoc/>
        void IServerRedundancyEndpointCache.InvalidateEndpoint(ConfiguredEndpoint endpoint)
        {
            foreach (KeyValuePair<EndpointCacheKey, ConfiguredEndpoint> cached in m_resolvedEndpoints)
            {
                if (ReferenceEquals(cached.Value, endpoint) ||
                    Equals(cached.Value.EndpointUrl, endpoint.EndpointUrl))
                {
                    ((ICollection<KeyValuePair<EndpointCacheKey, ConfiguredEndpoint>>)m_resolvedEndpoints)
                        .Remove(cached);
                }
            }
        }

        private async ValueTask<ArrayOf<RedundantServer>> ResolveEndpointsAsync(
            ArrayOf<RedundantServer> redundantServers,
            ConfiguredEndpoint currentEndpoint,
            ConfiguredEndpoint? initialEndpoint,
            CancellationToken ct)
        {
            var tasks = new Task<RedundantServer>[redundantServers.Count];
            string? initialUri = initialEndpoint?.ReverseConnect?.ServerUri ??
                initialEndpoint?.Description?.Server?.ApplicationUri;
            for (int ii = 0; ii < redundantServers.Count; ii++)
            {
                ct.ThrowIfCancellationRequested();
                RedundantServer server = redundantServers[ii];
                tasks[ii] = !string.IsNullOrEmpty(initialUri) &&
                    string.Equals(server.ServerUri, initialUri, StringComparison.Ordinal)
                    ? Task.FromResult(WithEndpoint(server, initialEndpoint))
                    : ResolveServerAsync(server, currentEndpoint, ct);
            }

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task<RedundantServer> ResolveServerAsync(
            RedundantServer server,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ConfiguredEndpoint? endpoint = null;
            ServiceResult? error = null;
            if (!string.IsNullOrEmpty(server.ServerUri))
            {
                using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(
                    m_peerDiscoveryTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                try
                {
                    // Wait on the linked token rather than the caller's token: the wait must not end before
                    // the discovery token observes the caller's cancellation, or disposing the linked source
                    // below would unhook it from the caller's token and leave the discovery running.
                    endpoint = await ResolveEndpointAsync(server.ServerUri, currentEndpoint, linked.Token)
                        .WaitAsync(m_peerDiscoveryTimeout, m_timeProvider, linked.Token).ConfigureAwait(false);
                    if (endpoint == null)
                    {
                        error = new ServiceResult(
                            StatusCodes.BadNotFound,
                            new LocalizedText(
                                "No endpoint of the Server matches the security and transport of the endpoint in use."));
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is ServiceResultException or TimeoutException
                    or OperationCanceledException or IOException or SocketException)
                {
                    ct.ThrowIfCancellationRequested();
                    m_logger.ManagedSessionRedundancyDiscoveryFailed(exception);
                    error = ToServiceResult(exception);
                }
                finally
                {
                    // The bounded wait can finish before linked cancellation is delivered.
                    await linked.CancelAsync().ConfigureAwait(false);
                }
            }
            return WithEndpoint(server, endpoint, error);
        }

        private static RedundantServer WithEndpoint(
            RedundantServer server,
            ConfiguredEndpoint? endpoint,
            ServiceResult? resolutionError = null)
        {
            return new RedundantServer
            {
                ServerUri = server.ServerUri,
                ServiceLevel = server.ServiceLevel,
                ServiceLevelKnown = server.ServiceLevelKnown,
                ServerState = server.ServerState,
                Endpoint = endpoint,
                ResolutionError = resolutionError
            };
        }

        private static ServiceResult ToServiceResult(Exception exception)
        {
            return exception switch
            {
                ServiceResultException sre => sre.Result,
                TimeoutException or OperationCanceledException => new ServiceResult(
                    StatusCodes.BadTimeout, new LocalizedText(exception.Message)),
                _ => new ServiceResult(exception)
            };
        }

        private static StatusCode GetReadStatus(DataValue value, ServiceResult error)
        {
            return StatusCode.IsBad(error.StatusCode) ? error.StatusCode : value.StatusCode;
        }

        private async Task<ConfiguredEndpoint?> ResolveEndpointAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct)
        {
            EndpointCacheKey key = CreateCacheKey(serverUri, currentEndpoint);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (m_resolvedEndpoints.TryGetValue(key, out ConfiguredEndpoint? cachedEndpoint))
                {
                    return cachedEndpoint;
                }
                var proposed = new Lazy<Task<ConfiguredEndpoint?>>(() =>
                {
                    Task<ConfiguredEndpoint?> resolution = DiscoverEndpointAsync(key, currentEndpoint, ct);
                    _ = ObserveEndpointResolutionAsync(resolution);
                    return resolution;
                });
                Lazy<Task<ConfiguredEndpoint?>> pending = m_pendingResolutions.GetOrAdd(key, proposed);
                try
                {
                    return await pending.Value.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !ct.IsCancellationRequested && !ReferenceEquals(proposed, pending))
                {
                    // A prior caller's cancelled lookup has drained; this caller can now retry without overlap.
                }
            }
        }

        private async Task<ConfiguredEndpoint?> DiscoverEndpointAsync(
            EndpointCacheKey key,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct)
        {
            try
            {
                ConfiguredEndpoint? endpoint;
                if (m_endpointResolver is DefaultRedundantServerEndpointResolver resolver)
                {
                    (endpoint, ServiceResult? discoveryError) = await resolver
                        .ResolveWithDiscoveryErrorAsync(key.ServerUri, currentEndpoint, ct).ConfigureAwait(false);
                    if (endpoint == null && discoveryError != null)
                    {
                        // A failed discovery is reported as such, not as a missing endpoint.
                        throw new ServiceResultException(discoveryError);
                    }
                }
                else
                {
                    endpoint = await m_endpointResolver
                        .ResolveAsync(key.ServerUri, currentEndpoint, ct).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
                if (endpoint != null)
                {
                    m_resolvedEndpoints[key] = endpoint;
                }
                return endpoint;
            }
            finally
            {
                m_pendingResolutions.TryRemove(key, out _);
            }
        }

        private static EndpointCacheKey CreateCacheKey(string serverUri, ConfiguredEndpoint currentEndpoint)
        {
            return new EndpointCacheKey(serverUri, ManagedChannelKey.FromEndpoint(currentEndpoint));
        }

        private async Task ObserveEndpointResolutionAsync(Task<ConfiguredEndpoint?> resolution)
        {
            try
            {
                await resolution.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller observes cancellation or the bounded peer-discovery timeout.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ManagedSessionRedundancyDiscoveryFailed(exception);
            }
        }

        private static ArrayOf<RedundantServer> ReadRedundantServers(
            RedundancySupport mode,
            DataValue redundantServerArrayValue,
            ServiceResult redundantServerArrayError,
            DataValue serverUriArrayValue,
            ServiceResult serverUriArrayError)
        {
            ArrayOf<RedundantServer> redundantServers = ReadRedundantServerArray(
                redundantServerArrayValue,
                redundantServerArrayError);

            if (mode != RedundancySupport.Transparent)
            {
                ArrayOf<string> serverUris = ReadServerUriArray(
                    serverUriArrayValue,
                    serverUriArrayError);
                redundantServers = AddMissingServerUris(redundantServers, serverUris);
            }

            return redundantServers;
        }

        private static ArrayOf<RedundantServer> ReadRedundantServerArray(
            DataValue dataValue,
            ServiceResult error)
        {
            var result = new List<RedundantServer>();

            if (StatusCode.IsBad(error.StatusCode) ||
                StatusCode.IsBad(dataValue.StatusCode))
            {
                return new ArrayOf<RedundantServer>(result.ToArray());
            }

            if (dataValue.WrappedValue.TryGetValue(
                out ArrayOf<ExtensionObject> extensionObjects))
            {
                foreach (ExtensionObject extensionObject in extensionObjects)
                {
                    if (extensionObject.TryGetValue(
                        out RedundantServerDataType? serverData))
                    {
                        result.Add(new RedundantServer
                        {
                            ServerUri = serverData.ServerId
                                ?? string.Empty,
                            ServiceLevel = serverData.ServiceLevel,
                            ServerState = serverData.ServerState
                        });
                    }
                }
            }

            return new ArrayOf<RedundantServer>(result.ToArray());
        }

        private static ArrayOf<string> ReadServerUriArray(
            DataValue dataValue,
            ServiceResult error)
        {
            var result = new ArrayOf<string>();

            if (StatusCode.IsBad(error.StatusCode) ||
                StatusCode.IsBad(dataValue.StatusCode))
            {
                return result;
            }

            if (dataValue.WrappedValue.TryGetValue(out ArrayOf<string> serverUris))
            {
                return serverUris;
            }

            return result;
        }

        private static async Task<string> ReadCurrentServerIdAsync(
            ISession session,
            Dictionary<NodeId, StatusCode> readStatuses,
            CancellationToken ct)
        {
            try
            {
                DataValue dataValue = await session.ReadValueAsync(
                    VariableIds.Server_ServerRedundancy_CurrentServerId,
                    ct).ConfigureAwait(false);
                readStatuses[VariableIds.Server_ServerRedundancy_CurrentServerId] = dataValue.StatusCode;

                if (StatusCode.IsGood(dataValue.StatusCode) &&
                    dataValue.WrappedValue.TryGetValue(out string currentServerId))
                {
                    return currentServerId;
                }
            }
            catch (ServiceResultException sre)
            {
                // The node may not exist; the status reports why the identifier is empty.
                readStatuses[VariableIds.Server_ServerRedundancy_CurrentServerId] = sre.StatusCode;
            }

            return string.Empty;
        }

        private static ArrayOf<RedundantServer> AddMissingServerUris(
            ArrayOf<RedundantServer> redundantServers,
            ArrayOf<string> serverUris)
        {
            var result = new List<RedundantServer>();
            for (int ii = 0; ii < redundantServers.Count; ii++)
            {
                result.Add(redundantServers[ii]);
            }

            for (int ii = 0; ii < serverUris.Count; ii++)
            {
                string serverUri = serverUris[ii];
                if (ContainsServerUri(result, serverUri))
                {
                    continue;
                }

                result.Add(new RedundantServer
                {
                    ServerUri = serverUri,
                    ServerState = ServerState.Running,
                    ServiceLevel = ServiceLevels.NoData,
                    ServiceLevelKnown = false
                });
            }

            return new ArrayOf<RedundantServer>(result.ToArray());
        }

        private RedundantServer? SelectBestPeer(
            ServerRedundancyInfo redundancyInfo,
            ConfiguredEndpoint currentEndpoint)
        {
            string? currentUri = currentEndpoint.Description?.Server?.ApplicationUri;
            RedundantServer? best = null;
            for (int ii = 0; ii < redundancyInfo.RedundantServers.Count; ii++)
            {
                RedundantServer server = redundancyInfo.RedundantServers[ii];
                if (server.ServerState == ServerState.Running &&
                    server.Endpoint != null &&
                    (!server.ServiceLevelKnown || ServiceLevels.IsOperational(server.ServiceLevel)) &&
                    !string.Equals(server.ServerUri, currentUri, StringComparison.Ordinal) &&
                    (best == null || server.ServiceLevel > best.ServiceLevel))
                {
                    best = server;
                }
            }

            return best;
        }

        private static bool ContainsServerUri(
            List<RedundantServer> redundantServers,
            string serverUri)
        {
            foreach (RedundantServer redundantServer in redundantServers)
            {
                if (string.Equals(redundantServer.ServerUri, serverUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private ServerFailoverDecision NoFailover(string reason)
        {
            return new ServerFailoverDecision(false, GetNow(), reason);
        }

        private DateTime GetMaintenanceRetryTime(DateTime estimatedReturnTime)
        {
            DateTime now = GetNow();
            return estimatedReturnTime > now
                ? estimatedReturnTime
                : now.Add(DefaultMaintenanceBackoff);
        }

        private DateTime GetNow()
        {
            return m_timeProvider.GetUtcNow().UtcDateTime;
        }

        private static RedundancySupport ToRedundancySupport(int value)
        {
            return value switch
            {
                0 => RedundancySupport.None,
                1 => RedundancySupport.Cold,
                2 => RedundancySupport.Warm,
                3 => RedundancySupport.Hot,
                4 => RedundancySupport.Transparent,
                5 => RedundancySupport.HotAndMirrored,
                _ => RedundancySupport.None
            };
        }

        private readonly IRedundantServerEndpointResolver m_endpointResolver;

        private readonly ConcurrentDictionary<EndpointCacheKey, ConfiguredEndpoint> m_resolvedEndpoints = new();

        private readonly TimeProvider m_timeProvider;

        private readonly TimeSpan m_peerDiscoveryTimeout;

        private readonly ILogger m_logger =
            AmbientMessageContext.Telemetry.CreateLogger<DefaultServerRedundancyHandler>();

        private readonly ConcurrentDictionary<EndpointCacheKey, Lazy<Task<ConfiguredEndpoint?>>> m_pendingResolutions =
            new();

        private readonly record struct EndpointCacheKey(string ServerUri, ManagedChannelKey Channel);
    }
}
