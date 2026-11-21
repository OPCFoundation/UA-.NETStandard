// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Hosting registration for the strong distributed historian.
    /// </summary>
    public static class DistributedHistorianBuilderExtensions
    {
        /// <summary>
        /// Registers a protected, leader-write shared historian.
        /// </summary>
        /// <remarks>
        /// The call deliberately provides no process-local store, unprotected
        /// record, or static-leader fallback. The application must register a
        /// cross-process linearizable <see cref="ISharedKeyValueStore"/>, an
        /// <see cref="IRecordProtector"/>, and an <see cref="ILeaderElection"/>.
        /// An explicitly registered <see cref="IHistorianProvider"/> remains
        /// authoritative and is not replaced.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseDistributedHistorian(
            this IOpcUaServerBuilder builder,
            Action<SharedKeyValueHistorianOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new SharedKeyValueHistorianOptions();
            configure?.Invoke(options);
            options.Validate();

            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton<IHistorianFencingAuthority>(
                services => new SharedKeyValueHistorianFencingAuthority(
                    services.GetRequiredService<ISharedKeyValueStore>(),
                    services.GetRequiredService<IRecordProtector>(),
                    services.GetRequiredService<ILeaderElection>(),
                    services.GetRequiredService<SharedKeyValueHistorianOptions>()
                        .WriterFenceLeaseDuration,
                    services.GetService<TimeProvider>()));
            builder.Services.TryAddSingleton(
                services => new SharedKeyValueHistorianProvider(
                    services.GetRequiredService<ISharedKeyValueStore>(),
                    services.GetRequiredService<IRecordProtector>(),
                    services.GetRequiredService<ILeaderElection>(),
                    services.GetRequiredService<SharedKeyValueHistorianOptions>(),
                    services.GetService<TimeProvider>(),
                    services.GetRequiredService<
                        IHistorianFencingAuthority>()));
            builder.Services.TryAddSingleton<IHistorianProvider>(
                services => services.GetRequiredService<
                    SharedKeyValueHistorianProvider>());
            builder.AddHistorian<IHistorianProvider>();
            builder.Services.TryAddSingleton(
                    services =>
                        new SharedKeyValueHistoryContinuationStore(
                            services.GetRequiredService<
                                ISharedKeyValueStore>(),
                            services.GetRequiredService<
                                IRecordProtector>(),
                            maxPayloadBytes: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationMaxPayloadBytes,
                            maxEnvelopesPerSession: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationMaxEnvelopesPerSession,
                            retentionTime: services.GetRequiredService<
                                SharedKeyValueHistorianOptions>()
                                .ContinuationRetentionTime,
                            timeProvider: services.GetService<
                                TimeProvider>(),
                            logger: services.GetService<ILogger<
                                SharedKeyValueHistoryContinuationStore>>()));
            builder.Services.TryAddSingleton<
                IHistoryContinuationPointStore>(
                    services => services.GetRequiredService<
                        SharedKeyValueHistoryContinuationStore>());
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStrongKeyspaceProvider>(
                    DistributedHistorianStrongKeyspaceProvider.Instance));
            builder.Services.TryAddSingleton<DistributedHistorianStartupTask>();
            builder.Services.AddSingleton<IServerPreStartupTask>(
                services => services.GetRequiredService<
                    DistributedHistorianStartupTask>());
            return builder;
        }
    }

    /// <summary>
    /// Declares historian and continuation-store key prefixes that require strong consistency.
    /// </summary>
    internal sealed class DistributedHistorianStrongKeyspaceProvider :
        IStrongKeyspaceProvider
    {
        /// <summary>
        /// Shared provider for the distributed historian's strong-consistency keyspaces.
        /// </summary>
        public static DistributedHistorianStrongKeyspaceProvider Instance { get; }
            = new();

        /// <summary>
        /// Returns the historian and history-continuation key prefixes requiring strong consistency.
        /// </summary>
        public ArrayOf<string> GetStrongKeyPrefixes()
        {
            return
            [
                "historian/v1/",
                "history-continuation/v1/"
            ];
        }
    }
}
