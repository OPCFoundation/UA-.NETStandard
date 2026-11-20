// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration for cross-replica session mirroring.
    /// </summary>
    public static class DistributedSessionMirroringBuilderExtensions
    {
        /// <summary>
        /// Registers distributed session mirroring for HotAndMirrored or Transparent failover.
        /// </summary>
        /// <remarks>
        /// The mirrored session record is keyed by a digest of the session authentication token and is protected
        /// with the configured <see cref="IRecordProtector"/>. Enable
        /// <see cref="DistributedSessionOptions.EnableFastReconnect"/> to allow a backup replica to materialize the
        /// session during <c>ActivateSession</c>; validation still runs through the core activation path and consumes
        /// the mirrored server nonce once across the replica set.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Optional session mirroring options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedSessionMirroring(
            this IOpcUaServerBuilder builder,
            Action<DistributedSessionOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new DistributedSessionOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            builder.Services.TryAddSingleton<ISessionManagerFactory>(sp =>
                new DistributedSessionManagerFactory(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp),
                    options));

            return builder;
        }
    }
}
