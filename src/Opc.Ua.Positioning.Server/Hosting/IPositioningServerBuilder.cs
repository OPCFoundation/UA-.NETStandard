// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Opc.Ua.Positioning.Server.Hosting
{
    /// <summary>
    /// Fluent registration surface for Positioning providers and configurators.
    /// </summary>
    public interface IPositioningServerBuilder
    {
        /// <summary>
        /// Underlying service collection.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Registers a geo location provider.
        /// </summary>
        /// <typeparam name="T">Provider implementation type.</typeparam>
        IPositioningServerBuilder AddGeoLocationProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IGeoLocationProvider;

        /// <summary>
        /// Registers a geo location provider factory.
        /// </summary>
        IPositioningServerBuilder AddGeoLocationProvider(
            Func<IServiceProvider, IGeoLocationProvider> factory);

        /// <summary>
        /// Registers a relative spatial location provider.
        /// </summary>
        /// <typeparam name="T">Provider implementation type.</typeparam>
        IPositioningServerBuilder AddRelativeSpatialLocationProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IRelativeSpatialLocationProvider;

        /// <summary>
        /// Registers a relative spatial location provider factory.
        /// </summary>
        IPositioningServerBuilder AddRelativeSpatialLocationProvider(
            Func<IServiceProvider, IRelativeSpatialLocationProvider> factory);
    }
}
