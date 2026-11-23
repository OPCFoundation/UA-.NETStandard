// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    internal interface IManagedSessionConnector
    {
        Task<ManagedSession> ConnectAsync(
            IServiceProvider serviceProvider,
            ManagedSessionOptions sessionOptions,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct);
    }
}
