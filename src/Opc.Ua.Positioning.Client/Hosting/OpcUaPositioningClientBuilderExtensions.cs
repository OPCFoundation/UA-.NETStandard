// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Positioning.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers Positioning clients over the managed OPC UA session.
    /// </summary>
    public static class OpcUaPositioningClientBuilderExtensions
    {
        /// <summary>
        /// Registers RSL and GPOS client factories.
        /// </summary>
        public static IOpcUaClientBuilder AddPositioningClient(
            this IOpcUaClientBuilder builder)
        {
            return AddPositioningClientCore(builder, null);
        }

        /// <summary>
        /// Registers RSL and GPOS client factories with strict bounded reads.
        /// The immutable options apply to both clients without taking session ownership.
        /// </summary>
        public static IOpcUaClientBuilder AddPositioningClient(
            this IOpcUaClientBuilder builder,
            PositioningClientReadOptions options)
        {
            options.ThrowIfNull(nameof(options));
            options.Validate();
            return AddPositioningClientCore(builder, options);
        }

        private static IOpcUaClientBuilder AddPositioningClientCore(
            IOpcUaClientBuilder builder,
            PositioningClientReadOptions? options)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<RelativeSpatialLocationClient>>>(
                    sp =>
                {
                    Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                        sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                        throw new InvalidOperationException(
                            "AddPositioningClient requires AddClient to be called first.");
                    ITelemetryContext telemetry =
                        sp.GetRequiredService<ITelemetryContext>();
                    return async cancellationToken =>
                    {
                        ManagedSession session = await sessionFactory(cancellationToken)
                            .ConfigureAwait(false);
                        return options is null
                            ? new RelativeSpatialLocationClient(session, telemetry)
                            : new RelativeSpatialLocationClient(session, telemetry, options);
                    };
                });

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<GlobalPositioningClient>>>(
                    sp =>
                {
                    Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                        sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                        throw new InvalidOperationException(
                            "AddPositioningClient requires AddClient to be called first.");
                    ITelemetryContext telemetry =
                        sp.GetRequiredService<ITelemetryContext>();
                    return async cancellationToken =>
                    {
                        ManagedSession session = await sessionFactory(cancellationToken)
                            .ConfigureAwait(false);
                        return options is null
                            ? new GlobalPositioningClient(session, telemetry)
                            : new GlobalPositioningClient(session, telemetry, options);
                    };
                });

            return builder;
        }
    }
}
