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
