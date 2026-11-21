// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Migration shim &#8212; restores the synchronous <c>ApplyChanges</c>
    /// method on <see cref="ServerPushConfigurationClient"/> that was
    /// removed in 1.6.
    /// </summary>
    public static class ServerPushConfigurationClientShim
    {
        extension(ServerPushConfigurationClient client)
        {
            /// <summary>
            /// Applies the staged configuration changes synchronously.
            /// </summary>
            [Obsolete("Synchronous ApplyChanges was removed in 1.6. Use ApplyChangesAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0015")]
            [OpcUaShim("UA0015")]
            public void ApplyChanges()
            {
                client.ApplyChangesAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }
}
