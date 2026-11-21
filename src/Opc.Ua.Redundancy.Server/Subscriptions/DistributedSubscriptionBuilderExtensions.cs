// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fluent registration for OPC 10000-4 §6.6.2.2 cross-replica Subscription definition mirroring.
    /// </summary>
    public static class DistributedSubscriptionBuilderExtensions
    {
        /// <summary>
        /// Registers the shared-store backed Subscription definition mirror.
        /// </summary>
        /// <remarks>
        /// The registered <see cref="ISubscriptionStore"/> persists subscription and monitored-item definitions,
        /// retransmission state for <c>Republish</c>, and continuation-point envelopes. This provides the
        /// HighAvailability capability boundary for continuation points: transferred sessions can identify and release
        /// mirrored opaque continuation tokens, but the local Browse/Query/History continuation enumerator state is not
        /// rebuilt on another replica. The registration also installs a
        /// <see cref="SharedKeyValueMonitoredItemQueueFactory"/> as the <see cref="IMonitoredItemQueueFactory"/>, so
        /// per-monitored-item data/event queues are continuously mirrored and restored on promotion, letting
        /// queued-but-unpublished notifications survive a failover.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedSubscriptionMirroring(this IOpcUaServerBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<ISharedKeyValueStore>(_ => new InMemorySharedKeyValueStore());
            builder.Services.TryAddSingleton(sp =>
                new SharedKeyValueMonitoredItemQueueFactory(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetRequiredService<IServiceMessageContext>(),
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp),
                    sp.GetRequiredService<ITelemetryContext>()));
            builder.Services.TryAddSingleton<IMonitoredItemQueueFactory>(
                sp => sp.GetRequiredService<SharedKeyValueMonitoredItemQueueFactory>());
            builder.Services.TryAddSingleton<ISubscriptionStore>(sp =>
                new SharedKeyValueSubscriptionStore(
                    sp.GetRequiredService<ISharedKeyValueStore>(),
                    sp.GetRequiredService<IServiceMessageContext>(),
                    RecordProtectionGuard.ResolveProtectorOrThrow(sp),
                    sp.GetService<ILogger<SharedKeyValueSubscriptionStore>>(),
                    sp.GetRequiredService<SharedKeyValueMonitoredItemQueueFactory>()));

            return builder;
        }
    }
}
