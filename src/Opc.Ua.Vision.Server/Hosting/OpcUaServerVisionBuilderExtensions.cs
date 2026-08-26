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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC UA Vision servers.
    /// </summary>
    public static class OpcUaServerVisionBuilderExtensions
    {
        /// <summary>
        /// Registers the standalone Vision node manager and its default
        /// hosting glue.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddVision(
            this IOpcUaServerBuilder builder,
            Action<VisionServerOptions>? configure = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<VisionServerOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IVisionModelProvider, VisionModelProvider>());
            builder.Services.TryAddSingleton<IVisionPostSetupRunner, VisionPostSetupRunner>();
            builder.Services.AddSingleton(static services =>
            {
                VisionServerOptions options = services
                    .GetRequiredService<IOptions<VisionServerOptions>>()
                    .Value;
                IVisionModelProvider[] providers = [.. services.GetServices<IVisionModelProvider>()];
                return new VisionNodeManagerFactory(
                    providers,
                    options,
                    services.GetService<IVisionPostSetupRunner>());
            });
            builder.Services.AddSingleton(static services =>
            {
                VisionServerOptions options = services
                    .GetRequiredService<IOptions<VisionServerOptions>>()
                    .Value;
                IVisionModelProvider[] providers = [.. services.GetServices<IVisionModelProvider>()];
                return new VisionHostedNodeManagerFactory(
                    providers,
                    options,
                    services.GetService<IVisionPostSetupRunner>(),
                    services);
            });
            builder.Services.AddSingleton(static services =>
                new OpcUaServerNodeManagerRegistration(
                    services.GetRequiredService<VisionHostedNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers a media provider for one sensor browse name.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The media provider type. Resolved via the DI container.
        /// </typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionMediaProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaServerBuilder builder,
            string sensorBrowseName)
            where TProvider : class, IVisionMediaProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(sensorBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(sensorBrowseName));
            }
            builder.Services.AddSingleton<TProvider>();
            builder.Services.AddSingleton(services =>
                new VisionMediaProviderRegistration(
                    sensorBrowseName,
                    services.GetRequiredService<TProvider>()));
            return builder;
        }

        /// <summary>
        /// Registers a media provider instance for one sensor browse name.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionMediaProvider(
            this IOpcUaServerBuilder builder,
            string sensorBrowseName,
            IVisionMediaProvider provider)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(sensorBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(sensorBrowseName));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            builder.Services.AddSingleton(new VisionMediaProviderRegistration(sensorBrowseName, provider));
            return builder;
        }

        /// <summary>
        /// Registers an inference provider for one pipeline browse name.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The inference provider type. Resolved via the DI container.
        /// </typeparam>
        /// <remarks>
        /// The <paramref name="onServer"/> flag controls whether the
        /// server advertises the on-server or off-server inference facet
        /// (§8.2).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionInferenceProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaServerBuilder builder,
            string pipelineBrowseName,
            bool onServer = true)
            where TProvider : class, IVisionInferenceProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(pipelineBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(pipelineBrowseName));
            }
            builder.Services.AddSingleton<TProvider>();
            builder.Services.AddSingleton(services =>
                new VisionInferenceProviderRegistration(
                    pipelineBrowseName,
                    services.GetRequiredService<TProvider>(),
                    onServer));
            return builder;
        }

        /// <summary>
        /// Registers an inference provider instance for one pipeline browse name.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionInferenceProvider(
            this IOpcUaServerBuilder builder,
            string pipelineBrowseName,
            IVisionInferenceProvider provider,
            bool onServer = true)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(pipelineBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(pipelineBrowseName));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            builder.Services.AddSingleton(
                new VisionInferenceProviderRegistration(pipelineBrowseName, provider, onServer));
            return builder;
        }

        /// <summary>
        /// Registers a feedback sink for one pipeline browse name.
        /// </summary>
        /// <typeparam name="TSink">
        /// The feedback sink type. Resolved via the DI container.
        /// </typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionFeedbackSink<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSink>(
            this IOpcUaServerBuilder builder,
            string pipelineBrowseName)
            where TSink : class, IVisionFeedbackSink
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(pipelineBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(pipelineBrowseName));
            }
            builder.Services.AddSingleton<TSink>();
            builder.Services.AddSingleton(services =>
                new VisionFeedbackSinkRegistration(
                    pipelineBrowseName,
                    services.GetRequiredService<TSink>()));
            return builder;
        }

        /// <summary>
        /// Registers a feedback sink instance for one pipeline browse name.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static IOpcUaServerBuilder AddVisionFeedbackSink(
            this IOpcUaServerBuilder builder,
            string pipelineBrowseName,
            IVisionFeedbackSink sink)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (string.IsNullOrEmpty(pipelineBrowseName))
            {
                throw new ArgumentException("A non-empty value is required.", nameof(pipelineBrowseName));
            }
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }
            builder.Services.AddSingleton(new VisionFeedbackSinkRegistration(pipelineBrowseName, sink));
            return builder;
        }

        /// <summary>
        /// Registers a Vision configurator for the standalone manager.
        /// </summary>
        public static IOpcUaServerBuilder ConfigureVision(
            this IOpcUaServerBuilder builder,
            Func<IVisionBuildContext, CancellationToken, ValueTask> configure)
        {
            return builder.ConfigureVisionFor<VisionNodeManager>(configure);
        }

        /// <summary>
        /// Registers a Vision configurator for the standalone manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureVision(
            this IOpcUaServerBuilder builder,
            Action<IVisionBuildContext> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.ConfigureVision((context, _) =>
            {
                configure(context);
                return default;
            });
        }

        /// <summary>
        /// Registers a Vision configurator for a specific Vision node
        /// manager type.
        /// </summary>
        /// <typeparam name="TNodeManager">
        /// The target Vision node manager type. Only
        /// <see cref="VisionNodeManager"/> is currently supported.
        /// </typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="NotSupportedException"></exception>
        public static IOpcUaServerBuilder ConfigureVisionFor<TNodeManager>(
            this IOpcUaServerBuilder builder,
            Func<IVisionBuildContext, CancellationToken, ValueTask> configure)
            where TNodeManager : AsyncCustomNodeManager
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (typeof(TNodeManager) != typeof(VisionNodeManager))
            {
                throw new NotSupportedException(
                    "ConfigureVisionFor<TNodeManager> is supported only for VisionNodeManager. " +
                    "Use ConfigureVision for the standalone manager.");
            }
            builder.Services.TryAddSingleton<IVisionPostSetupRunner, VisionPostSetupRunner>();
            builder.Services.AddSingleton<IVisionPostSetupConfigurator>(
                new DelegateVisionConfigurator(typeof(TNodeManager), configure));
            return builder;
        }

        private sealed class DelegateVisionConfigurator : IVisionPostSetupConfigurator
        {
            public DelegateVisionConfigurator(
                Type targetManagerType,
                Func<IVisionBuildContext, CancellationToken, ValueTask> configure)
            {
                TargetManagerType = targetManagerType;
                m_configure = configure;
            }

            public Type TargetManagerType { get; }

            public ValueTask RunAsync(IVisionBuildContext context)
            {
                return m_configure(context, context.CancellationToken);
            }

            private readonly Func<IVisionBuildContext, CancellationToken, ValueTask> m_configure;
        }
    }
}
