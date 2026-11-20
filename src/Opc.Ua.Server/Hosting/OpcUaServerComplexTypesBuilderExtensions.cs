/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Schema;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions that configure building and
    /// registering dynamic stand-in encodeables for the custom DataTypes that
    /// a server loaded from a NodeSet at runtime.
    /// </summary>
    public static class OpcUaServerComplexTypesBuilderExtensions
    {
        /// <summary>
        /// Configures runtime complex type support for the server and exposes the
        /// primed encodeable factory as a DI-resolvable schema resolver. Complex
        /// type loading is on by default (<see cref="StandardServer.LoadComplexTypes"/>);
        /// use this to tune <see cref="ServerComplexTypeOptions"/> (including opting
        /// out via <see cref="ServerComplexTypeOptions.Enabled"/>) or to make the
        /// schema <see cref="IDataTypeDefinitionResolver"/> available through
        /// dependency injection. After the address space is available (and before
        /// the server starts accepting connections) the server builds stand-in
        /// encodeables for every custom DataType that carries a
        /// <c>DataTypeDefinition</c> attribute but is not already backed by a
        /// compiled .NET type, and registers them into the server's encodeable
        /// factory. This reuses exactly the same NativeAOT friendly path as the
        /// client complex type system.
        /// </summary>
        /// <param name="builder">The OPC UA server builder.</param>
        /// <param name="configure">An optional callback to configure the
        /// <see cref="ServerComplexTypeOptions"/>.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddComplexTypeSystem(
            this IOpcUaServerBuilder builder,
            Action<ServerComplexTypeOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var options = new ServerComplexTypeOptions();
            configure?.Invoke(options);

            IServiceCollection services = builder.Services;
            services.TryAddSingleton(options);
            services.TryAddSingleton<ServerDataTypeDefinitionResolver>();

            // Expose the server's runtime encodeable factory as the schema data
            // type definition resolver. The factory is the source of truth for
            // generated and runtime stand-in definitions; an optional
            // DataTypeDefinitionRegistry (for example registered by
            // AddSchemaGeneration for schema-only types) is composed as a
            // fallback when present. TryAddSingleton keeps the registration
            // idempotent when AddComplexTypeSystem() is called more than once.
            services.TryAddSingleton<IDataTypeDefinitionResolver>(sp =>
            {
                ServerDataTypeDefinitionResolver holder =
                    sp.GetRequiredService<ServerDataTypeDefinitionResolver>();
                DataTypeDefinitionRegistry? registry = sp.GetService<DataTypeDefinitionRegistry>();
                return registry != null
                    ? new CompositeDataTypeDefinitionResolver([holder, registry])
                    : holder;
            });

            return builder;
        }
    }
}
