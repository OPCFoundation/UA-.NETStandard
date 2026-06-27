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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    public sealed class DefaultServerRedundancyHandler : IServerRedundancyHandler
    {
        /// <summary>
        /// The default maintenance retry backoff when the server does not provide a future return time.
        /// </summary>
        public static readonly TimeSpan DefaultMaintenanceBackoff = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultServerRedundancyHandler"/> class.
        /// </summary>
        public DefaultServerRedundancyHandler(
            IRedundantServerEndpointResolver? endpointResolver = null,
            TimeProvider? timeProvider = null)
        {
            m_endpointResolver = endpointResolver ?? new DefaultRedundantServerEndpointResolver();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            ISession session,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            // Read redundancy support mode, service level, and return time in one call.
            ArrayOf<NodeId> nodeIds =
            [
                VariableIds.Server_ServerRedundancy_RedundancySupport,
                VariableIds.Server_ServiceLevel,
                VariableIds.Server_EstimatedReturnTime
            ];

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                await session.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);

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
            if (StatusCode.IsGood(errors[2].StatusCode))
            {
                estimatedReturnTime = values[2].GetValue(DateTime.MinValue);
            }

            var redundantServers = new ArrayOf<RedundantServer>();
            string currentServerId = string.Empty;
            if (mode != RedundancySupport.None)
            {
                redundantServers = await ReadRedundantServersAsync(
                    session, mode, ct).ConfigureAwait(false);
                currentServerId = mode == RedundancySupport.Transparent
                    ? await ReadCurrentServerIdAsync(session, ct).ConfigureAwait(false)
                    : string.Empty;
                redundantServers = await ResolveEndpointsAsync(
                    redundantServers,
                    session.ConfiguredEndpoint,
                    ct).ConfigureAwait(false);
            }

            return new ServerRedundancyInfo
            {
                Mode = mode,
                ServiceLevel = serviceLevel,
                ServiceLevelAccessible = serviceLevelAccessible,
                ServiceLevelSubrange = ServiceLevels.GetSubrange(serviceLevel),
                EstimatedReturnTime = estimatedReturnTime,
                CurrentServerId = currentServerId,
                RedundantServers = redundantServers
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

            if (!ShouldFailover(redundancyInfo, currentEndpoint).IsFailoverWarranted)
            {
                return null;
            }

            return SelectBestPeer(redundancyInfo, currentEndpoint)?.Endpoint;
        }

        private async ValueTask<ArrayOf<RedundantServer>> ResolveEndpointsAsync(
            ArrayOf<RedundantServer> redundantServers,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct)
        {
            var result = new List<RedundantServer>();
            for (int ii = 0; ii < redundantServers.Count; ii++)
            {
                RedundantServer server = redundantServers[ii];
                ConfiguredEndpoint? endpoint = await ResolveEndpointAsync(
                    server.ServerUri,
                    currentEndpoint,
                    ct).ConfigureAwait(false);
                result.Add(new RedundantServer
                {
                    ServerUri = server.ServerUri,
                    ServiceLevel = server.ServiceLevel,
                    ServiceLevelKnown = server.ServiceLevelKnown,
                    ServerState = server.ServerState,
                    Endpoint = endpoint
                });
            }

            return new ArrayOf<RedundantServer>(result.ToArray());
        }

        private async ValueTask<ConfiguredEndpoint?> ResolveEndpointAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct)
        {
            if (m_resolvedEndpoints.TryGetValue(serverUri, out ConfiguredEndpoint? cachedEndpoint))
            {
                return cachedEndpoint;
            }

            ConfiguredEndpoint? endpoint = await m_endpointResolver
                .ResolveAsync(serverUri, currentEndpoint, ct)
                .ConfigureAwait(false);
            if (endpoint != null)
            {
                m_resolvedEndpoints[serverUri] = endpoint;
            }

            return endpoint;
        }

        private static async Task<ArrayOf<RedundantServer>> ReadRedundantServersAsync(
            ISession session,
            RedundancySupport mode,
            CancellationToken ct)
        {
            ArrayOf<RedundantServer> redundantServers =
                await ReadRedundantServerArrayAsync(session, ct).ConfigureAwait(false);

            if (mode != RedundancySupport.Transparent)
            {
                ArrayOf<string> serverUris = await ReadServerUriArrayAsync(session, ct)
                    .ConfigureAwait(false);
                redundantServers = AddMissingServerUris(redundantServers, serverUris);
            }

            return redundantServers;
        }

        private static async Task<ArrayOf<RedundantServer>> ReadRedundantServerArrayAsync(
            ISession session,
            CancellationToken ct)
        {
            var result = new List<RedundantServer>();

            try
            {
                DataValue dataValue = await session.ReadValueAsync(
                    VariableIds.Server_ServerRedundancy_RedundantServerArray,
                    ct).ConfigureAwait(false);

                if (StatusCode.IsBad(dataValue.StatusCode))
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
            }
            catch (ServiceResultException)
            {
                // Node may not exist; return empty list.
            }

            return new ArrayOf<RedundantServer>(result.ToArray());
        }

        private static async Task<ArrayOf<string>> ReadServerUriArrayAsync(
            ISession session,
            CancellationToken ct)
        {
            var result = new ArrayOf<string>();

            try
            {
                DataValue dataValue = await session.ReadValueAsync(
                    VariableIds.Server_ServerRedundancy_ServerUriArray,
                    ct).ConfigureAwait(false);

                if (StatusCode.IsBad(dataValue.StatusCode))
                {
                    return result;
                }

                if (dataValue.WrappedValue.TryGetValue(out ArrayOf<string> serverUris))
                {
                    return serverUris;
                }
            }
            catch (ServiceResultException)
            {
                // Node may not exist; return empty list.
            }

            return result;
        }

        private static async Task<string> ReadCurrentServerIdAsync(
            ISession session,
            CancellationToken ct)
        {
            try
            {
                DataValue dataValue = await session.ReadValueAsync(
                    VariableIds.Server_ServerRedundancy_CurrentServerId,
                    ct).ConfigureAwait(false);

                if (StatusCode.IsGood(dataValue.StatusCode) &&
                    dataValue.WrappedValue.TryGetValue(out string currentServerId))
                {
                    return currentServerId;
                }
            }
            catch (ServiceResultException)
            {
                // Node may not exist; return empty string.
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
        private readonly Dictionary<string, ConfiguredEndpoint> m_resolvedEndpoints = new(StringComparer.Ordinal);
        private readonly TimeProvider m_timeProvider;
    }
}
