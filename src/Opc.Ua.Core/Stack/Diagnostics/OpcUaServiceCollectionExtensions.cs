/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Root entry point for the OPC UA dependency injection surface.
    /// </summary>
    /// <remarks>
    /// All OPC UA feature libraries expose extension methods on
    /// <see cref="IOpcUaBuilder"/> (e.g. <c>.AddServer(...)</c>,
    /// <c>.AddClient(...)</c>, <c>.AddGdsServer(...)</c>) and any
    /// <see cref="IServiceCollection"/>
    /// consumer starts from <see cref="AddOpcUa(IServiceCollection)"/>.
    /// </remarks>
    public static class OpcUaServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the shared OPC UA dependency-injection services and
        /// returns an <see cref="IOpcUaBuilder"/> for chaining feature
        /// registrations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotent: calling this method multiple times only registers the
        /// shared <see cref="ITelemetryContext"/> once. A user-supplied
        /// <see cref="ITelemetryContext"/> registration that exists in the
        /// service collection before <c>AddOpcUa()</c> is invoked is
        /// preserved (<see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider, TService})"/>).
        /// </para>
        /// <para>
        /// The registered <see cref="ITelemetryContext"/> resolves the
        /// host's <see cref="ILoggerFactory"/> on first use, so callers
        /// should configure logging via the standard
        /// <see cref="LoggingServiceCollectionExtensions.AddLogging(IServiceCollection, Action{ILoggingBuilder})"/>
        /// pattern (or call <see cref="AddLogging(IOpcUaBuilder)"/> below).
        /// </para>
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <returns>An <see cref="IOpcUaBuilder"/> for further chained
        /// registration of OPC UA feature services.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddOpcUa(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.TryAddSingleton<BufferManagerFactoryOptions>();
            services.TryAddSingleton<IBufferManagerFactory, DefaultBufferManagerFactory>();

            // Always install the transport binding registry seeded with the
            // mandatory raw-socket opc.tcp secure channel listener and tcp
            // connection channel factories. This makes opc.tcp available to
            // every client and server without an explicit AddOpcTcpTransport()
            // call; optional transports (Kestrel / HTTPS / WSS) still override
            // the seeded defaults via their own Add*Transport() extensions.
            services.AddTransportBindingRegistry();

            return new OpcUaBuilder(services);
        }

        /// <summary>
        /// Adds the default <see cref="ILoggerFactory"/> service registration
        /// (equivalent to <see cref="LoggingServiceCollectionExtensions.AddLogging(IServiceCollection)"/>)
        /// and returns the same <see cref="IOpcUaBuilder"/> for fluent
        /// chaining into feature methods.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddLogging(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddLogging();
            return builder;
        }

        /// <summary>
        /// Adds the default <see cref="ILoggerFactory"/> service registration
        /// configured by <paramref name="configure"/> and returns the same
        /// <see cref="IOpcUaBuilder"/> for fluent chaining.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Configuration callback for the logging
        /// builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddLogging(this IOpcUaBuilder builder,
            Action<ILoggingBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddLogging(configure);
            return builder;
        }

        /// <summary>
        /// Adds the default metrics service registration and returns the
        /// same <see cref="IOpcUaBuilder"/> for fluent chaining.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddMetrics(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddMetrics();
            return builder;
        }

        /// <summary>
        /// Adds the default metrics service registration configured by
        /// <paramref name="configure"/> and returns the same
        /// <see cref="IOpcUaBuilder"/> for fluent chaining.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Configuration callback for the metrics
        /// builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddMetrics(this IOpcUaBuilder builder,
            Action<IMetricsBuilder> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddMetrics(configure);
            return builder;
        }

        private sealed class OpcUaBuilder : IOpcUaBuilder
        {
            public OpcUaBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
