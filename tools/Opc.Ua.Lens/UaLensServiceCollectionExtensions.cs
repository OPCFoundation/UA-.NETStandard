// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.Connection;
using UaLens.Diagnostics;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;
using UaLens.Samples;
using UaLens.Storage;
using UaLens.Telemetry;
using UaLens.Themes;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens
{
    /// <summary>
    /// Typed composition for the desktop modules. Explicit factories keep construction
    /// compatible with NativeAOT; callers can replace real seams before registering defaults.
    /// </summary>
    internal static class UaLensServiceCollectionExtensions
    {
        public static IServiceCollection AddUaLens(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton(_ => new LogRingBuffer(capacity: 4096));
            services.TryAddSingleton(provider => new AppTelemetryContext(provider.GetRequiredService<LogRingBuffer>()));
            services.TryAddSingleton<ITelemetryContext>(provider => provider.GetRequiredService<AppTelemetryContext>());
            services.AddUaLensRepositorySamples();
            services.TryAddSingleton(_ => new PublishLogObserver());
            services.TryAddSingleton(_ => new AppearancePreferences());
            services.TryAddSingleton(_ => new InspectorPreferences());
            services.TryAddSingleton<IWorkspaceDispatcher>(_ => new AvaloniaWorkspaceDispatcher());
            services.TryAddSingleton<WriteValueOperationFactory>(_ =>
                static (nodeId, session) => new WriteValueOperation(nodeId, session));
            services.TryAddSingleton<Func<ApplicationConfiguration, CertificateStoreOperations>>(provider =>
            {
                ITelemetryContext telemetry = provider.GetRequiredService<ITelemetryContext>();
                ICertificateStoreAccess? store = provider.GetService<ICertificateStoreAccess>();
                TimeProvider? timeProvider = provider.GetService<TimeProvider>();
                return configuration => new CertificateStoreOperations(
                    store ?? new CertificateStoreService(configuration, telemetry), timeProvider);
            });
            services.AddUaLensConnection();
            services.TryAddSingleton(provider => new Diagnostics.Capture.DiagnosticCaptureWorkspace(
                provider.GetRequiredService<ITelemetryContext>(),
                provider.GetRequiredService<ConnectionService>().ConfiguredBackend?.Transports.CaptureRegistry,
                provider.GetRequiredService<ConnectionService>(),
                provider.GetService<TimeProvider>()));
            services.TryAddSingleton<INodeSetLoader>(_ => new NodeSetLoader());
            services.TryAddSingleton<INodeSetRepository>(_ => new UaNodeSetRepository());
            services.TryAddSingleton<INodeSetAddressSpaceFactory>(provider =>
                new NodeSetAddressSpaceFactory(provider.GetRequiredService<ITelemetryContext>()));
            services.AddUaLensShowcases();
            services.TryAddSingleton<ICapabilityProbe>(_ => new SessionCapabilityProbe());
            services.TryAddSingleton<ICapabilityService>(provider => new CapabilityService(
                provider.GetRequiredService<ConnectionService>(),
                provider.GetRequiredService<ICapabilityProbe>()));
            services.TryAddSingleton<IPluginFactory>(
                provider => new PluginFactory([.. provider.GetServices<PluginFactoryRegistration>()]));
            services.TryAddSingleton(
                provider => new PluginDocumentOperations(provider.GetRequiredService<ConnectionService>()));
            services.TryAddSingleton(provider => new DocumentWorkspace<IPlugin>(
                provider.GetRequiredService<AppTelemetryContext>().CreateLogger("Documents"),
                provider.GetRequiredService<IWorkspaceDispatcher>(),
                provider.GetRequiredService<PluginDocumentOperations>().SynchronizeConnectionAsync));
            services.TryAddSingleton(_ => new CommandRegistry());
            services.TryAddSingleton<Func<CancellationToken, Task<ResourceMonitorHost>>>(
                provider => cancellationToken => ResourceMonitorHost.StartAsync(
                    provider.GetRequiredService<AppTelemetryContext>(), cancellationToken));
            services.TryAddSingleton(provider => new MainViewModel(
                provider.GetRequiredService<AppTelemetryContext>(),
                provider.GetRequiredService<ConnectionService>(),
                provider.GetRequiredService<DocumentWorkspace<IPlugin>>(),
                provider.GetRequiredService<CommandRegistry>(),
                provider.GetRequiredService<PluginDocumentOperations>(),
                provider.GetRequiredService<IWorkspaceDispatcher>(),
                provider.GetRequiredService<Func<CancellationToken, Task<ResourceMonitorHost>>>(),
                provider.GetRequiredService<ICapabilityService>(),
                provider.GetRequiredService<IPluginFactory>(),
                provider.GetRequiredService<INodeSetLoader>(),
                provider.GetRequiredService<INodeSetAddressSpaceFactory>(),
                provider.GetRequiredService<Diagnostics.Capture.DiagnosticCaptureWorkspace>()));
            services.TryAddSingleton(provider => provider.GetRequiredService<MainViewModel>().CreatePluginHost());
            return services;
        }

        /// <summary>
        /// Binds a registered, typed feature factory at composition time.
        /// The feature and its document never resolve services.
        /// Register the feature factory itself using an explicit construction delegate for NativeAOT.
        /// </summary>
        /// <typeparam name="TFactory"></typeparam>
        public static IServiceCollection AddUaLensPluginFactory<TFactory>(
            this IServiceCollection services,
            PluginKind kind,
            Func<TFactory, PluginHost, IPlugin> create,
            bool isDefault = false)
            where TFactory : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(create);
            services.AddSingleton(provider =>
            {
                TFactory factory = provider.GetRequiredService<TFactory>();
                return new PluginFactoryRegistration(kind, host => create(factory, host), isDefault);
            });
            return services;
        }
    }
}
