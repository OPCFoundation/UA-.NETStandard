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
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Server startup task that populates the live
    /// <c>Server.ServerRedundancy</c> nodes from
    /// <see cref="ServerRedundancyOptions"/>.
    /// </summary>
    public sealed class ServerRedundancyStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="options">The server redundancy options.</param>
        public ServerRedundancyStartupTask(ServerRedundancyOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            ServerObjectState? serverObject = server.ServerObject;
            ServerRedundancyState? redundancy = serverObject?.ServerRedundancy;
            if (redundancy == null)
            {
                return default;
            }

            ISystemContext context = server.DefaultSystemContext;
            if (redundancy.RedundancySupport != null)
            {
                redundancy.RedundancySupport.Value = m_options.Mode;
                redundancy.RedundancySupport.ClearChangeMasks(context, false);
            }

            if (m_options.Mode != RedundancySupport.None)
            {
                ApplyRedundantServerArray(server, redundancy, context);
                if (m_options.Mode == RedundancySupport.Transparent)
                {
                    ApplyCurrentServerId(server, redundancy, context);
                }
                else
                {
                    ApplyServerUriArray(server, redundancy, context);
                }
            }

            return default;
        }

        private void ApplyRedundantServerArray(
            IServerInternal server,
            ServerRedundancyState redundancy,
            ISystemContext context)
        {
            PropertyState<ArrayOf<RedundantServerDataType>>? redundantServerArray =
                server.DiagnosticsNodeManager?.FindPredefinedNode<PropertyState<ArrayOf<RedundantServerDataType>>>(
                    VariableIds.Server_ServerRedundancy_RedundantServerArray) ??
                redundancy.RedundantServerArray;
            if (redundantServerArray == null)
            {
                return;
            }

            ArrayOf<string> peerApplicationUris = m_options.GetPeerApplicationUris();
            var servers = new List<RedundantServerDataType>(peerApplicationUris.Count);
            foreach (string peerServerUri in peerApplicationUris)
            {
                servers.Add(new RedundantServerDataType
                {
                    ServerId = peerServerUri,
                    ServiceLevel = m_options.PeerServiceLevel,
                    ServerState = ServerState.Running
                });
            }

            redundantServerArray.Value = new ArrayOf<RedundantServerDataType>(servers.ToArray());
            redundantServerArray.ClearChangeMasks(context, false);
        }

        private void ApplyCurrentServerId(
            IServerInternal server,
            ServerRedundancyState redundancy,
            ISystemContext context)
        {
            PropertyState<string>? currentServerId =
                server.DiagnosticsNodeManager?.FindPredefinedNode<PropertyState<string>>(
                    VariableIds.Server_ServerRedundancy_CurrentServerId) ??
                FindProperty<string>(redundancy, context, BrowseNames.CurrentServerId);
            if (currentServerId != null)
            {
                currentServerId.Value = m_options.CurrentServerId;
                currentServerId.ClearChangeMasks(context, false);
            }
        }

        private void ApplyServerUriArray(
            IServerInternal server,
            ServerRedundancyState redundancy,
            ISystemContext context)
        {
            PropertyState<ArrayOf<string>>? serverUriArray =
                server.DiagnosticsNodeManager?.FindPredefinedNode<PropertyState<ArrayOf<string>>>(
                    VariableIds.Server_ServerRedundancy_ServerUriArray) ??
                FindProperty<ArrayOf<string>>(redundancy, context, BrowseNames.ServerUriArray);
            if (serverUriArray != null)
            {
                serverUriArray.Value = m_options.GetPeerApplicationUris();
                serverUriArray.ClearChangeMasks(context, false);
            }
        }

        private static PropertyState<T>? FindProperty<T>(
            ServerRedundancyState redundancy,
            ISystemContext context,
            string browseName)
        {
            NodeState? child = redundancy.FindChild(context, new QualifiedName(browseName, 0));
            return child as PropertyState<T>;
        }

        private readonly ServerRedundancyOptions m_options;
    }
}
