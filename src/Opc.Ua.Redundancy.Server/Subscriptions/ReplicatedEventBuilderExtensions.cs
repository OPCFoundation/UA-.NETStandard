// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Redundancy;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Registers delivery of authoritative events for a mirrored redundant server set.
    /// </summary>
    public static class ReplicatedEventBuilderExtensions
    {
        /// <summary>
        /// Installs the hosted authoritative feed using the enrolled store and the distributed writer election.
        /// </summary>
        /// <remarks>
        /// Configure Transparent or HotAndMirrored server redundancy and the built-in leadership ServiceLevel
        /// policy. Register a provider for the application's own event data and call
        /// <see cref="ReplicatedEventFeed.BindNotifier"/> or <see cref="ReplicatedEventFeed.BindCondition"/>
        /// when constructing the stable source nodes, before startup replay.
        /// </remarks>
        public static IOpcUaServerBuilder UseReplicatedEventFeed(
            this IOpcUaServerBuilder builder,
            Uuid groupId,
            Func<IServiceProvider, IReplicatedEventSource> sourceFactory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (sourceFactory == null)
            {
                throw new ArgumentNullException(nameof(sourceFactory));
            }

            builder.Services.AddSingleton(sourceFactory);
            builder.Services.AddSingleton(sp => new ReplicatedEventFeed(
                sp.GetRequiredService<ServerRedundancyOptions>().Mode,
                groupId,
                sp.GetRequiredService<FreshnessProtectedKeyValueStore>(),
                sp.GetRequiredService<ILeaderElection>(),
                sp.GetRequiredService<IReplicatedEventSource>(),
                sp.GetRequiredService<IServiceLevelProvider>(),
                sp.GetRequiredService<ITelemetryContext>(),
                sp.GetService<TimeProvider>()));
            builder.Services.AddSingleton<IServerPreStartupTask>(sp => sp.GetRequiredService<ReplicatedEventFeed>());
            builder.Services.AddSingleton<IServerStartupTask>(sp => sp.GetRequiredService<ReplicatedEventFeed>());
            return builder;
        }

        /// <summary>
        /// Registers the group and condition method relay for <see cref="ReplicatedEventReporter"/>.
        /// </summary>
        /// <remarks>
        /// Also register <see cref="ServerRedundancyBuilderExtensions.AddServerRedundancy"/> with Transparent
        /// or HotAndMirrored mode. Each member needs the same group UUID and trusted authoritative feed.
        /// This registration does not replace the ordinary event pipeline or subscription queue store.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="groupId">The stable redundant-server-set UUID.</param>
        /// <param name="relayFactory">Factory for the application-authorized condition method relay.</param>
        /// <returns>The same builder for chaining.</returns>
        public static IOpcUaServerBuilder UseReplicatedEventReporting(
            this IOpcUaServerBuilder builder,
            Uuid groupId,
            Func<IServiceProvider, IReplicatedConditionMethodRelay> relayFactory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (relayFactory == null)
            {
                throw new ArgumentNullException(nameof(relayFactory));
            }

            builder.Services.AddSingleton(relayFactory);
            builder.Services.AddSingleton(sp => new ReplicatedEventReporter(
                sp.GetRequiredService<ServerRedundancyOptions>().Mode,
                groupId,
                sp.GetRequiredService<IReplicatedConditionMethodRelay>()));
            return builder;
        }
    }
}
