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
using Opc.Ua.Schema;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions that enable building and
    /// registering dynamic stand-in encodeables for the custom DataTypes that
    /// a server loaded from a NodeSet at runtime.
    /// </summary>
    public static class OpcUaServerComplexTypesBuilderExtensions
    {
        /// <summary>
        /// Enables runtime complex type support for the server. After the
        /// address space is available (and before the server starts accepting
        /// connections) the server builds stand-in encodeables for every custom
        /// DataType that carries a <c>DataTypeDefinition</c> attribute but is not
        /// already backed by a compiled .NET type, and registers them into the
        /// server's encodeable factory. This reuses exactly the same NativeAOT
        /// friendly path as the client complex type system.
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
            // fallback when present.
            services.AddSingleton<IDataTypeDefinitionResolver>(sp =>
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
