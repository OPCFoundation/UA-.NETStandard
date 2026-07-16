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
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaServerBuilder"/> extensions for loading runtime
    /// NodeSet2 documents into the OPC UA server's address space.
    /// </summary>
    public static class RuntimeNodeSetBuilderExtensions
    {
        /// <summary>
        /// Registers a <see cref="RuntimeNodeSetNodeManagerFactory"/> that
        /// loads the NodeSet2 document at <paramref name="filePath"/> into
        /// the server's address space at startup.
        /// </summary>
        /// <remarks>
        /// The file is opened twice: once during this call to extract
        /// model metadata (which populates the factory's
        /// <see cref="IAsyncNodeManagerFactory.NamespacesUris"/>), and once
        /// more at server startup for the full import.
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="filePath">
        /// Path to a NodeSet2 XML file. May be absolute or relative to
        /// the current working directory.
        /// </param>
        /// <param name="configure">
        /// Optional fluent configuration callback invoked after the
        /// NodeSet2 nodes have been added. When non-<c>null</c> and the
        /// loaded model set has exactly one leaf model, that model's
        /// namespace is used as the default browse-path prefix;
        /// otherwise set
        /// <see cref="RuntimeNodeSetOptions.DefaultNamespaceUri"/> via the
        /// <see cref="AddRuntimeNodeSet(IOpcUaServerBuilder,RuntimeNodeSetOptions)"/> overload.
        /// </param>
        /// <returns>
        /// The same <see cref="IOpcUaServerBuilder"/> for chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="filePath"/> is
        /// <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder AddRuntimeNodeSet(
            this IOpcUaServerBuilder builder,
            string filePath,
            Action<INodeManagerBuilder>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            var options = new RuntimeNodeSetOptions
            {
                Sources = [RuntimeNodeSetSource.FromFile(filePath)],
                Configure = configure
            };

            return builder.AddRuntimeNodeSet(options);
        }

        /// <summary>
        /// Registers a <see cref="RuntimeNodeSetNodeManagerFactory"/> built
        /// from the supplied <paramref name="options"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sources listed in <see cref="RuntimeNodeSetOptions.Sources"/>
        /// are imported in dependency order at server startup. Missing
        /// external dependencies are allowed; cycles among the included
        /// sources cause server startup to fail with a clear error.
        /// </para>
        /// <para>
        /// The factory is registered both as
        /// <see cref="IAsyncNodeManagerFactory"/> (for direct DI access)
        /// and as <see cref="OpcUaServerNodeManagerRegistration"/> (so the
        /// hosted server discovers it consistently with all other
        /// node-manager registrations).
        /// </para>
        /// </remarks>
        /// <param name="builder">The server builder.</param>
        /// <param name="options">
        /// Configuration describing the NodeSet2 sources, optional default
        /// namespace URI, and optional fluent <c>Configure</c> callback.
        /// </param>
        /// <returns>
        /// The same <see cref="IOpcUaServerBuilder"/> for chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="options"/> is
        /// <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder AddRuntimeNodeSet(
            this IOpcUaServerBuilder builder,
            RuntimeNodeSetOptions options)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var factory = new RuntimeNodeSetNodeManagerFactory(options);
            builder.Services.AddSingleton<IAsyncNodeManagerFactory>(factory);
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));

            return builder;
        }

        /// <summary>
        /// Registers a <see cref="RuntimeNodeSetNodeManagerFactory"/> built
        /// from the options configured by <paramref name="configure"/>.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="configure">
        /// Callback that populates a new <see cref="RuntimeNodeSetOptions"/>
        /// instance.
        /// </param>
        /// <returns>
        /// The same <see cref="IOpcUaServerBuilder"/> for chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="configure"/> is
        /// <c>null</c>.
        /// </exception>
        public static IOpcUaServerBuilder AddRuntimeNodeSet(
            this IOpcUaServerBuilder builder,
            Action<RuntimeNodeSetOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new RuntimeNodeSetOptions();
            configure(options);

            return builder.AddRuntimeNodeSet(options);
        }
    }
}
