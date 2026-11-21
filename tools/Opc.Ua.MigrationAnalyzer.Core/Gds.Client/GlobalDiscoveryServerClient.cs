// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Migration shim &#8212; restores the synchronous <c>RegisterApplication</c>
    /// and <c>UnregisterApplication</c> methods on
    /// <see cref="GlobalDiscoveryServerClient"/> that were removed in 1.6.
    /// </summary>
    public static class GlobalDiscoveryServerClientShim
    {
        extension(GlobalDiscoveryServerClient client)
        {
            /// <summary>
            /// Registers the application synchronously.
            /// </summary>
            [Obsolete("Synchronous RegisterApplication was removed in 1.6. Use RegisterApplicationAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0015")]
            [OpcUaShim("UA0015")]
            public NodeId RegisterApplication(ApplicationRecordDataType application)
            {
                return client.RegisterApplicationAsync(application)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            /// <summary>
            /// Unregisters the application synchronously.
            /// </summary>
            [Obsolete("Synchronous UnregisterApplication was removed in 1.6. Use UnregisterApplicationAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0015")]
            [OpcUaShim("UA0015")]
            public void UnregisterApplication(NodeId applicationId)
            {
                client.UnregisterApplicationAsync(applicationId)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }
}
