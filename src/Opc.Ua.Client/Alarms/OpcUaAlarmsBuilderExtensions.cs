// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client.Alarms</c>: register an
    /// <see cref="AlarmClientFactory"/> so that dependency-injected
    /// client hosts can resolve a Part 9 alarm/condition client per
    /// connected session.
    /// </summary>
    public static class OpcUaAlarmsBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="AlarmClientFactory"/> so
        /// consumers can resolve it and produce an
        /// <see cref="AlarmClient"/> per <see cref="ISessionClient"/>
        /// (typically the connected <c>ManagedSession</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent (<see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection)"/>):
        /// repeated calls do not add a second descriptor. The root
        /// <c>AddOpcUa()</c> call is invoked to ensure
        /// <see cref="ITelemetryContext"/> is registered even if the
        /// caller skipped chaining through it.
        /// </para>
        /// <para>
        /// The non-DI path
        /// (<c>AlarmClientSessionExtensions.GetAlarmClient</c> extension
        /// and the public <see cref="AlarmClient"/> constructor)
        /// remains available for callers that do not use the
        /// <c>Microsoft.Extensions.DependencyInjection</c> infrastructure.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance for
        /// fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// is <c>null</c>.</exception>
        public static IOpcUaBuilder AddAlarms(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<AlarmClientFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the singleton <see cref="AlarmClientFactory"/> and
        /// keeps the client builder chain flowing.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddAlarms(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddAlarms();
            return builder;
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
