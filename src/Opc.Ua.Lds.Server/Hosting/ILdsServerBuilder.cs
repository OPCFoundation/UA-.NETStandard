// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Lds.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <see cref="OpcUaLdsServerBuilderExtensions.AddLdsServer(IOpcUaBuilder, Action{LdsServerOptions})"/>;
    /// exposes the underlying <see cref="IServiceCollection"/> so callers can
    /// register additional services that the hosted Local Discovery Server
    /// may need (custom telemetry, certificate password providers, etc.).
    /// </summary>
    /// <remarks>
    /// Unlike <c>IOpcUaServerBuilder</c>, an LDS has no node-manager surface
    /// to attach. This interface intentionally exposes only
    /// <see cref="Services"/>; future LDS-specific extensions can be added
    /// as extension methods without breaking existing consumers.
    /// </remarks>
    public interface ILdsServerBuilder
    {
        /// <summary>
        /// Underlying service collection. Use it to register additional
        /// services the hosted LDS may need.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers the LDS registration store implementation.
        /// </summary>
        /// <typeparam name="T">The store implementation type.</typeparam>
        ILdsServerBuilder AddRegistrationStore<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IRegisteredServerStore;

        /// <summary>
        /// Registers the LDS registration store factory.
        /// </summary>
        ILdsServerBuilder AddRegistrationStore(Func<IServiceProvider, IRegisteredServerStore> factory);

        /// <summary>
        /// Registers the LDS-ME multicast discovery factory.
        /// </summary>
        /// <typeparam name="T">The multicast discovery factory type.</typeparam>
        ILdsServerBuilder AddMulticastDiscovery<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, ILdsMulticastDiscoveryFactory;

        /// <summary>
        /// Registers the LDS-ME multicast discovery factory delegate.
        /// </summary>
        ILdsServerBuilder AddMulticastDiscovery(Func<IServiceProvider, ILdsMulticastDiscoveryFactory> factory);
    }
}
