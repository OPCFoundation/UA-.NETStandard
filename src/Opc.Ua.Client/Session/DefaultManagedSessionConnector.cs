// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Client
{
    internal sealed class DefaultManagedSessionConnector : IManagedSessionConnector
    {
        public Task<ManagedSession> ConnectAsync(
            IServiceProvider serviceProvider,
            ManagedSessionOptions sessionOptions,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct)
        {
            return serviceProvider.ConnectManagedSessionAsync(
                sessionOptions,
                configure,
                ct);
        }
    }
}
