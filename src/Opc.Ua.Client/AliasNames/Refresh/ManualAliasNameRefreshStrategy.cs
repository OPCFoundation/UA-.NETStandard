// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Trivial no-op refresh strategy — never triggers cache
    /// invalidation. Callers must invoke
    /// <see cref="AliasNameResolver.RefreshAsync"/> or
    /// <see cref="AliasNameResolver.Invalidate"/> explicitly.
    /// </summary>
    public sealed class ManualAliasNameRefreshStrategy : IAliasNameRefreshStrategy
    {
        /// <inheritdoc/>
        public ValueTask StartAsync(
            AliasNameClient client,
            Action onInvalidate,
            CancellationToken ct)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}
