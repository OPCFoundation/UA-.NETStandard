/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
                    ContentIdProvider = serviceProvider.GetService<IResourceContentIdProvider>(),
                    FederationProvider = serviceProvider.GetService<IXRegistryFederationProvider>()
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
