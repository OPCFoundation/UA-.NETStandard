// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Schema;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.Xsd;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency injection extensions that register the OPC UA schema
    /// generation services.
    /// </summary>
    public static class SchemaServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the schema generation services (the
        /// <see cref="ISchemaProvider"/>, the default
        /// <see cref="DataTypeDefinitionRegistry"/> resolver and the built-in
        /// schema generators).
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddSchemaGeneration(this IOpcUaBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            IServiceCollection services = builder.Services;
            services.TryAddSingleton<DataTypeDefinitionRegistry>();
            services.TryAddSingleton<IDataTypeDefinitionResolver>(
                static sp => sp.GetRequiredService<DataTypeDefinitionRegistry>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IUaSchemaGenerator, JsonSchemaGenerator>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IUaSchemaGenerator, XsdSchemaGenerator>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IUaSchemaGenerator, BsdSchemaGenerator>());
            services.TryAddSingleton<ISchemaProvider, DefaultSchemaProvider>();
            return builder;
        }
    }
}
