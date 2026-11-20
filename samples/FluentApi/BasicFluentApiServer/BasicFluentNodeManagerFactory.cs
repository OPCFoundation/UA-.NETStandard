// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

using Opc.Ua;
using Opc.Ua.Server;

namespace BasicFluentApiServer
{
    public class BasicFluentNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public ArrayOf<string> NamespacesUris =>
        [
            BasicFluentNodeManager.NamespaceUri
        ];

        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            IAsyncNodeManager nodeManager =
                new BasicFluentNodeManager(server, configuration);
#pragma warning restore CA2000

            return ValueTask.FromResult(nodeManager);
        }
    }
}
