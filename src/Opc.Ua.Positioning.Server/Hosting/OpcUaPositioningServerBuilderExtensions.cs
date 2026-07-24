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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC UA Positioning servers.
    /// </summary>
    public static class OpcUaPositioningServerBuilderExtensions
    {
        /// <summary>
        /// Registers the standalone Positioning node manager.
        /// </summary>
        public static IPositioningServerBuilder AddPositioningServer(
            this IOpcUaServerBuilder builder)
        {
            builder.ThrowIfNull(nameof(builder));
            RegisterCommonServices(builder.Services);
            EnsureStandaloneManagerNotRegistered(builder.Services);
            builder.Services.AddSingleton<PositioningNodeManagerRegistrationMarker>();
            builder.AddNodeManager<PositioningNodeManagerFactory>();
            return new PositioningServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers Positioning services for an existing composite node manager.
        /// </summary>
        /// <typeparam name="TNodeManager">Composite node manager type.</typeparam>
        public static IPositioningServerBuilder AddPositioningFor<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNodeManager>(
            this IOpcUaServerBuilder builder)
            where TNodeManager : AsyncCustomNodeManager
        {
            builder.ThrowIfNull(nameof(builder));
            RegisterCommonServices(builder.Services);
            return new PositioningServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an asynchronous Positioning configuration callback.
        /// </summary>
        /// <typeparam name="TNodeManager">Target node manager type.</typeparam>
        public static IPositioningServerBuilder ConfigurePositioningFor<TNodeManager>(
            this IPositioningServerBuilder builder,
            Func<PositioningServerContext, ValueTask> configure)
            where TNodeManager : AsyncCustomNodeManager
        {
            builder.ThrowIfNull(nameof(builder));
            configure.ThrowIfNull(nameof(configure));
            builder.Services.AddSingleton<IPositioningPostSetupConfigurator>(
                new DelegatePositioningConfigurator(
                    typeof(TNodeManager),
                    configure));
            return builder;
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<IPositioningPostSetupRunner, PositioningPostSetupRunner>();
        }

        private static void EnsureStandaloneManagerNotRegistered(
            IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType ==
                    typeof(PositioningNodeManagerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddPositioningServer has already been called.");
                }
            }
        }

        private sealed class PositioningServerBuilder : IPositioningServerBuilder
        {
            public PositioningServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IPositioningServerBuilder AddGlobalPositionProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IGlobalPositionProvider
            {
                Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IGlobalPositionProvider, T>());
                return this;
            }

            public IPositioningServerBuilder AddGlobalPositionProvider(
                Func<IServiceProvider, IGlobalPositionProvider> factory)
            {
                factory.ThrowIfNull(nameof(factory));
                Services.AddSingleton(factory);
                return this;
            }

            public IPositioningServerBuilder AddRelativeSpatialLocationProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IRelativeSpatialLocationProvider
            {
                Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IRelativeSpatialLocationProvider, T>());
                return this;
            }

            public IPositioningServerBuilder AddRelativeSpatialLocationProvider(
                Func<IServiceProvider, IRelativeSpatialLocationProvider> factory)
            {
                factory.ThrowIfNull(nameof(factory));
                Services.AddSingleton(factory);
                return this;
            }
        }

        private sealed class DelegatePositioningConfigurator :
            IPositioningPostSetupConfigurator
        {
            private readonly Func<PositioningServerContext, ValueTask> m_configure;

            public DelegatePositioningConfigurator(
                Type targetManagerType,
                Func<PositioningServerContext, ValueTask> configure)
            {
                TargetManagerType = targetManagerType;
                m_configure = configure;
            }

            public Type TargetManagerType { get; }

            public ValueTask RunAsync(PositioningServerContext context)
            {
                return m_configure(context);
            }
        }

        private sealed class PositioningNodeManagerRegistrationMarker;
    }
}
