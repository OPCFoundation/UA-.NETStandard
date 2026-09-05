/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency injection extensions that register the generic xRegistry server services.
    /// </summary>
    public static class XRegistryServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the xRegistry server services: the options, the resource store and the
        /// registration, fast-path and federation node managers. Every registration uses
        /// <c>TryAdd</c>, so an application that has already supplied its own
        /// <see cref="IXRegistryResourceStore"/> or <see cref="IResourceContentIdProvider"/> keeps
        /// it. Constructing the node managers directly remains supported.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional configuration of the registry options.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddXRegistryServer(
            this IServiceCollection services,
            Action<XRegistryServerOptions>? configure = null)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IXRegistryResourceStore, InMemoryResourceStore>();
            services.TryAddSingleton(serviceProvider =>
            {
                var options = new XRegistryServerOptions
                {
                    ResourceStore = serviceProvider.GetRequiredService<IXRegistryResourceStore>(),
                    ContentIdProvider = serviceProvider.GetService<IResourceContentIdProvider>()
                };
                configure?.Invoke(options);
                return options;
            });

            return services;
        }

        /// <summary>
        /// Registers a file-backed <see cref="IXRegistryResourceStore"/> so resource documents
        /// outlive the server process and a shared volume can back a distributed deployment.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="rootPath">The directory that holds the resource documents.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="rootPath"/> is null or empty.</exception>
        public static IServiceCollection AddXRegistryFileSystemResourceStore(
            this IServiceCollection services,
            string rootPath)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrEmpty(rootPath))
            {
                throw new ArgumentException("A root path is required.", nameof(rootPath));
            }

            services.TryAddSingleton<IXRegistryResourceStore>(
                serviceProvider => new FileSystemResourceStore(
                    rootPath, serviceProvider.GetService<IFileSystem>()));
            return services;
        }

        /// <summary>
        /// Registers the document content-key provider a concrete registry supplies.
        /// </summary>
        /// <typeparam name="TProvider">The provider implementation.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddXRegistryContentIdProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IServiceCollection services)
            where TProvider : class, IResourceContentIdProvider
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<IResourceContentIdProvider, TProvider>();
            return services;
        }
    }
}
