// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.PubSub.Transports;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Decorates PubSub transport factories at resolution time.
    /// </summary>
    public interface IPubSubTransportFactoryDecorator
    {
        /// <summary>
        /// Decorates the supplied transport factory.
        /// </summary>
        /// <param name="provider">The service provider.</param>
        /// <param name="factory">The transport factory to decorate.</param>
        /// <returns>The decorated factory.</returns>
        IPubSubTransportFactory Decorate(
            IServiceProvider provider,
            IPubSubTransportFactory factory);
    }

    /// <summary>
    /// Shared registration helpers for PubSub transport factories.
    /// </summary>
    public static class PubSubTransportFactoryServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton PubSub transport factory that applies registered
        /// <see cref="IPubSubTransportFactoryDecorator"/> services when resolved.
        /// </summary>
        /// <typeparam name="TFactory">The transport factory type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection TryAddPubSubTransportFactory<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>(
            this IServiceCollection services)
            where TFactory : class, IPubSubTransportFactory
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            for (int i = 0; i < services.Count; i++)
            {
                ServiceDescriptor descriptor = services[i];
                if (descriptor.ServiceType == typeof(IPubSubTransportFactory) &&
                    descriptor.ImplementationFactory?.Target?.GetType() ==
                    typeof(PubSubTransportFactoryRegistration<TFactory>))
                {
                    return services;
                }
            }

            services.Add(ServiceDescriptor.Singleton(
                new PubSubTransportFactoryRegistration<TFactory>().Resolve));
            return services;
        }

        /// <summary>
        /// Registers a singleton PubSub transport factory that applies registered
        /// <see cref="IPubSubTransportFactoryDecorator"/> services when resolved.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="factory">Factiry of a transport factory</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection AddPubSubTransportFactory(
            this IServiceCollection services,
            Func<IServiceProvider, IPubSubTransportFactory> factory)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            services.Add(ServiceDescriptor.Singleton(
                new PubSubTransportFactoryRegistration(factory).Resolve));
            return services;
        }

        /// <summary>
        /// Applies registered PubSub transport factory decorators.
        /// </summary>
        /// <param name="provider">The service provider.</param>
        /// <param name="factory">The transport factory to decorate.</param>
        /// <returns>The decorated factory.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IPubSubTransportFactory DecoratePubSubTransportFactory(
            this IServiceProvider provider,
            IPubSubTransportFactory factory)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            IPubSubTransportFactory current = factory;
            foreach (IPubSubTransportFactoryDecorator decorator
                in provider.GetServices<IPubSubTransportFactoryDecorator>())
            {
                current = decorator.Decorate(provider, current);
            }
            return current;
        }
    }

    /// <summary>
    /// Marker used by diagnostics extensions to detect deferred transport factory registrations.
    /// </summary>
    public interface IPubSubTransportFactoryRegistration
    {
        /// <summary>
        /// Gets a value indicating whether the registration applies deferred decorators.
        /// </summary>
        bool AppliesDecorators { get; }
    }

    internal sealed class PubSubTransportFactoryRegistration : IPubSubTransportFactoryRegistration
    {
        private readonly Func<IServiceProvider, IPubSubTransportFactory> m_factory;

        public PubSubTransportFactoryRegistration(Func<IServiceProvider, IPubSubTransportFactory> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public bool AppliesDecorators => true;

        public IPubSubTransportFactory Resolve(IServiceProvider provider)
        {
            return provider.DecoratePubSubTransportFactory(m_factory(provider));
        }
    }

    internal sealed class PubSubTransportFactoryRegistration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>
        : IPubSubTransportFactoryRegistration
        where TFactory : class, IPubSubTransportFactory
    {
        public bool AppliesDecorators => true;

        public IPubSubTransportFactory Resolve(IServiceProvider provider)
        {
            return provider.DecoratePubSubTransportFactory(
                ActivatorUtilities.CreateInstance<TFactory>(provider));
        }
    }
}
