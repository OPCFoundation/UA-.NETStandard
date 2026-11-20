/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Robotics.Client;
using Opc.Ua.Robotics.Client.Intent;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Robotics client over the managed OPC UA session.
    /// </summary>
    public static class OpcUaRoboticsClientBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="RoboticsClient"/> factory. The Robotics
        /// client extends the Device Integration client, so the DI client
        /// services are registered as well.
        /// </summary>
        /// <param name="builder">
        /// The client builder returned by <c>AddClient</c>.
        /// </param>
        public static IOpcUaClientBuilder AddRoboticsClient(
            this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.AddOpcUaDi();
            builder.AddRobotIntentClient();
            builder.Services.TryAddSingleton<RoboticsClientFactory>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddRoboticsClient requires AddClient to be called first.");
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                return new RoboticsClientFactory(sessionFactory, telemetry);
            });

            builder.Services.TryAddSingleton<
               Func<CancellationToken, Task<RoboticsClient>>>(sp =>
            {
                RoboticsClientFactory factory =
                    sp.GetRequiredService<RoboticsClientFactory>();
                return factory.CreateAsync;
            });

            return builder;
        }

        /// <summary>
        /// Registers a <see cref="RobotIntentClient"/> factory over the managed session.
        /// </summary>
        /// <param name="builder">
        /// The client builder returned by <c>AddClient</c>.
        /// </param>
        public static IOpcUaClientBuilder AddRobotIntentClient(
            this IOpcUaClientBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));

            builder.Services.TryAddSingleton<
                Func<CancellationToken, Task<RobotIntentClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> sessionFactory =
                    sp.GetService<Func<CancellationToken, Task<ManagedSession>>>() ??
                    throw new InvalidOperationException(
                        "AddRobotIntentClient requires AddClient to be called first.");
                ITelemetryContext telemetry =
                    sp.GetRequiredService<ITelemetryContext>();
                return async ct =>
                {
                    ManagedSession session = await sessionFactory(ct).ConfigureAwait(false);
                    return new RobotIntentClient(session, telemetry);
                };
            });

            return builder;
        }
    }
}
