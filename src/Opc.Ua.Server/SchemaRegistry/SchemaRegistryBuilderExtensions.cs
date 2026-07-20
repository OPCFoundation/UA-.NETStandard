/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.SchemaRegistry;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions that add the optional, dependency-injectable
    /// in-server Schema Registry feature: the abstract xRegistry base and Schema Registry companion
    /// NodeSets (loaded through the runtime NodeSet import path) plus the fast-path, registration and
    /// federation node managers that serve the content-addressed Opaque <c>SchemaId</c> nodes.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public static class SchemaRegistryBuilderExtensions
    {
        /// <summary>
        /// Registers the in-server Schema Registry feature with default options.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddSchemaRegistry(this IOpcUaServerBuilder builder)
        {
            return builder.AddSchemaRegistry(new SchemaRegistryOptions());
        }

        /// <summary>
        /// Registers the in-server Schema Registry feature configured by <paramref name="configure"/>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">Callback that populates a new <see cref="SchemaRegistryOptions"/>.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configure"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder AddSchemaRegistry(
            this IOpcUaServerBuilder builder,
            Action<SchemaRegistryOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new SchemaRegistryOptions();
            configure(options);
            return builder.AddSchemaRegistry(options);
        }

        /// <summary>
        /// Registers the in-server Schema Registry feature built from the supplied
        /// <paramref name="options"/>. The xRegistry base and Schema Registry companion NodeSets are
        /// imported in dependency order at startup, and the fast-path, registration and federation
        /// node managers are attached to the Schema Registry namespace.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="options">The Schema Registry feature options.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder AddSchemaRegistry(
            this IOpcUaServerBuilder builder,
            SchemaRegistryOptions options)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // 1) The companion NodeSets (xRegistry base + Schema Registry) loaded at startup.
            var nodeSetOptions = new RuntimeNodeSetOptions
            {
                Sources = SchemaRegistryNodeSets.CreateSources(options)
            };
            var nodeSetFactory = new RuntimeNodeSetNodeManagerFactory(nodeSetOptions);
            builder.Services.AddSingleton<IAsyncNodeManagerFactory>(nodeSetFactory);
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(nodeSetFactory));

            // 2) The Schema-Registry-specific node managers serving the content-addressed nodes.
            INodeManagerFactory[] factories =
            [
                new SchemaRegistryFastPathNodeManagerFactory(options),
                new SchemaRegistryRegistrationNodeManagerFactory(options),
                new SchemaRegistryFederationNodeManagerFactory(options),
            ];
            foreach (INodeManagerFactory factory in factories)
            {
                builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            }

            return builder;
        }
    }
}
