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
using System.Linq;
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
        /// <inheritdoc/>
        public async ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            ISession session,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            // Read redundancy support mode and service level in one call.
            ArrayOf<NodeId> nodeIds =
            [
                VariableIds.Server_ServerRedundancy_RedundancySupport,
                VariableIds.Server_ServiceLevel
            ];

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                await session.ReadValuesAsync(nodeIds, ct).ConfigureAwait(false);

            RedundancyMode mode = RedundancyMode.None;
            if (StatusCode.IsGood(errors[0].StatusCode))
            {
                mode = ToRedundancyMode(values[0].GetValue(0));
            }

            byte serviceLevel = 0;
            if (StatusCode.IsGood(errors[1].StatusCode))
            {
                serviceLevel = values[1].GetValue<byte>(0);
            }

            // Read the redundant server array (may not exist when mode is None).
            var redundantServers = new List<RedundantServer>();
            if (mode != RedundancyMode.None)
            {
                redundantServers = await ReadRedundantServerArrayAsync(
                    session, ct).ConfigureAwait(false);
            }

            return new ServerRedundancyInfo
            {
                Mode = mode,
                ServiceLevel = serviceLevel,
                RedundantServers = redundantServers
            };
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

            if (redundancyInfo.Mode is RedundancyMode.None or RedundancyMode.Transparent)
            {
                return null;
            }

            string? currentUri = currentEndpoint.Description?.Server?.ApplicationUri;

            // Pick the running server with the highest service level
            // that is not the current server.
            RedundantServer? best = redundancyInfo.RedundantServers
                .Where(s => s.ServerState == ServerState.Running &&
                    !string.Equals(s.ServerUri, currentUri, StringComparison.Ordinal))
                .OrderByDescending(s => s.ServiceLevel)
                .FirstOrDefault();

            if (best == null)
            {
                return null;
            }

            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = best.ServerUri,
                Server = new ApplicationDescription
                {
                    ApplicationUri = best.ServerUri
                }
            };

            return new ConfiguredEndpoint(null, endpointDescription);
        }

        private static async Task<List<RedundantServer>> ReadRedundantServerArrayAsync(
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
                    return result;
                }

                if (dataValue.WrappedValue.TryGet(
                    out ArrayOf<ExtensionObject> extensionObjects))
                {
                    foreach (ExtensionObject extensionObject in extensionObjects)
                    {
                        if (extensionObject.TryGetEncodeable(
                            out RedundantServerDataType serverData))
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

            return result;
        }

        private static RedundancyMode ToRedundancyMode(int value)
        {
            return value switch
            {
                0 => RedundancyMode.None,
                1 => RedundancyMode.Cold,
                2 => RedundancyMode.Warm,
                3 => RedundancyMode.Hot,
                4 => RedundancyMode.Transparent,
                5 => RedundancyMode.HotAndMirrored,
                _ => RedundancyMode.None
            };
        }
    }
}
