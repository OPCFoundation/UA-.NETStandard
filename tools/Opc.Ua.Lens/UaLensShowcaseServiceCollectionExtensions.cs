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
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using UaLens.Plugins.Alarms;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Plugins.Companions.Providers;
using UaLens.Plugins.Continuity;
using UaLens.Plugins.EventView;
using UaLens.Plugins.GdsManagement;
using UaLens.Plugins.GdsPush;
using UaLens.Plugins.Historian;
using UaLens.Plugins.Models;
using UaLens.Plugins.PubSub;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Samples;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens
{
    /// <summary>
    /// Registers native-safe, replaceable document factories without starting network or sample workloads.
    /// </summary>
    internal static class UaLensShowcaseServiceCollectionExtensions
    {
        public static IServiceCollection AddUaLensShowcases(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
            {
                return services;
            }
            services.AddSingleton(new RegistrationMarker());
            AddFactory(services, PluginKind.Alarms, static host => new AlarmsPlugin(host));
            AddFactory(services, PluginKind.Models, static host => new ModelInspectorPlugin(host));
            services.TryAddSingleton<Func<PluginHost, ContinuityPlugin>>(provider =>
            {
                IContinuityBackendFactory? backend = provider.GetService<IContinuityBackendFactory>();
                IContinuitySessionFactory? sessions = provider.GetService<IContinuitySessionFactory>();
                return host => new ContinuityPlugin(
                    host, backend ?? ContinuityPlugin.CreateDefaultFactory(host, sessions));
            });
            services.AddUaLensPluginFactory<Func<PluginHost, ContinuityPlugin>>(
                PluginKind.Continuity, static (factory, host) => factory(host), isDefault: true);
            services.TryAddSingleton<IGdsCertificateIssuance>(_ => new GdsCertificateIssuance());
            services.TryAddSingleton<Func<PluginHost, GdsManagementPlugin>>(provider =>
            {
                IGdsCertificateIssuance issuance = provider.GetRequiredService<IGdsCertificateIssuance>();
                IGdsManagementClient? client = provider.GetService<IGdsManagementClient>();
                IGdsCertificateDelivery? delivery = provider.GetService<IGdsCertificateDelivery>();
                return host => new GdsManagementPlugin(host, issuance, client, delivery);
            });
            services.AddUaLensPluginFactory<Func<PluginHost, GdsManagementPlugin>>(
                PluginKind.GdsManagement, static (factory, host) => factory(host), isDefault: true);
            services.TryAddSingleton<Func<PluginHost, GdsPushPlugin>>(provider =>
            {
                IGdsPushClient? client = provider.GetService<IGdsPushClient>();
                return host => new GdsPushPlugin(host, client);
            });
            services.AddUaLensPluginFactory<Func<PluginHost, GdsPushPlugin>>(
                PluginKind.GdsPush, static (factory, host) => factory(host), isDefault: true);

            services.TryAddSingleton(provider =>
                new VariablePoolBrowser(provider.GetRequiredService<ITelemetryContext>()));
            services.AddUaLensPluginFactory<VariablePoolBrowser>(
                PluginKind.SubscriptionBench,
                static (browser, host) => new SubscriptionBenchPlugin(host, browser),
                isDefault: true);
            services.AddUaLensPluginFactory<IWorkspaceDispatcher>(
                PluginKind.Historian,
                static (dispatcher, host) => new HistorianPlugin(host, dispatcher),
                isDefault: true);
            services.AddUaLensPluginFactory<IWorkspaceDispatcher>(
                PluginKind.Subscription,
                static (dispatcher, host) => PluginRegistry.CreateSubscription(host, dispatcher),
                isDefault: true);
            services.AddUaLensPluginFactory<WriteValueOperationFactory>(
                PluginKind.EventView,
                static (operations, host) => new EventViewPlugin(host, operations),
                isDefault: true);

            services.TryAddSingleton<IPubSubRuntimeFactory>(provider => new PubSubRuntimeFactory(
                provider.GetRequiredService<ITelemetryContext>(),
                provider.GetService<TimeProvider>(),
                [.. provider.GetServices<IPubSubTransportProvider>()],
                [.. provider.GetServices<IPubSubKeyProviderResolver>()],
                [.. provider.GetServices<IPubSubAdapterProvider>()],
                captures: provider.GetService<Diagnostics.Capture.DiagnosticCaptureWorkspace>()));
            services.AddUaLensPluginFactory<IPubSubRuntimeFactory>(
                PluginKind.PubSub, static (factory, host) => new PubSubPlugin(host, factory), isDefault: true);

            services.TryAddSingleton<IOpenUsdViewportAssetCache>(_ => new OpenUsdViewportAssetCache());
            services.TryAddSingleton<IRenderedVisionCameraSource>(_ => new RenderedVisionCameraSource());
            services.TryAddSingleton<ICompanionPackageReader, CompanionPackageReader>();
            services.TryAddSingleton(provider => new RepositorySampleCompanionPolicy(
                provider.GetService<IRepositorySampleService>(),
                [.. provider.GetServices<CompanionDeploymentRule>()], provider.GetService<TimeProvider>()));
            services.TryAddSingleton<ICompanionDeploymentPolicy>(
                provider => provider.GetRequiredService<RepositorySampleCompanionPolicy>());
            services.TryAddSingleton<Plugins.Companions.Providers.IWeighingSimulationPolicy>(
                provider => provider.GetRequiredService<RepositorySampleCompanionPolicy>());
            services.TryAddSingleton<IMachineryDataSource, MachineryDataSource>();
            services.TryAddSingleton<IMachineryResultSource, MachineryResultSource>();
            services.TryAddSingleton(provider => new MachineryPluginFactory(
                provider.GetRequiredService<IMachineryDataSource>(),
                provider.GetRequiredService<IMachineryResultSource>(),
                provider.GetRequiredService<ICompanionDeploymentPolicy>(),
                provider.GetService<TimeProvider>(),
                ResolveMachineryJobs(provider)));
            services.AddUaLensPluginFactory<MachineryPluginFactory>(
                PluginKind.Machinery, static (factory, host) => factory.Create(host), isDefault: true);
            services.TryAddSingleton(provider =>
            {
                ICompanionProvider[] companions = [.. provider.GetServices<ICompanionProvider>()];
                return new CompanionPluginFactory(
                    providers: companions.Length == 0 ? default : new ArrayOf<ICompanionProvider>(companions),
                    timeProvider: provider.GetService<TimeProvider>(),
                    packages: provider.GetRequiredService<ICompanionPackageReader>(),
                    deploymentPolicy: provider.GetRequiredService<ICompanionDeploymentPolicy>(),
                    viewportFactory: provider.GetService<OpenUsdViewportFactory>(),
                    visionCameraSource: provider.GetRequiredService<IRenderedVisionCameraSource>(),
                    viewportAssets: provider.GetRequiredService<IOpenUsdViewportAssetCache>());
            });
            services.AddUaLensPluginFactory<CompanionPluginFactory>(
                PluginKind.Companions, static (factory, host) => factory.Create(host), isDefault: true);
            services.TryAddSingleton(_ => new Plugins.Companions.Providers.PumpsCompanionProvider());
            services.TryAddSingleton(provider => new Plugins.Companions.Providers.AmbCompanionProvider(
                provider.GetRequiredService<ICompanionDeploymentPolicy>(), provider.GetService<TimeProvider>()));
            services.TryAddSingleton(provider => new Plugins.Companions.Providers.PackMlCompanionProvider(
                provider.GetRequiredService<ICompanionDeploymentPolicy>(), provider.GetService<TimeProvider>()));
            services.TryAddSingleton(provider => new Plugins.Companions.Providers.PositioningCompanionProvider(
                provider.GetService<TimeProvider>()));
            services.AddSingleton(provider =>
            {
                CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                PositioningCompanionProvider positioning = provider.GetRequiredService<Plugins.Companions.Providers.PositioningCompanionProvider>();
                return new PluginFactoryRegistration(
                    PluginKind.Positioning,
                    host => factory.CreateSpecification(host, PluginKind.Positioning, positioning),
                    IsDefault: true);
            });
            services.AddSingleton(provider =>
            {
                CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                PackMlCompanionProvider packml = provider.GetRequiredService<Plugins.Companions.Providers.PackMlCompanionProvider>();
                return new PluginFactoryRegistration(
                    PluginKind.PackML,
                    host => factory.CreateSpecification(host, PluginKind.PackML, packml),
                    IsDefault: true);
            });
            services.AddSingleton(provider =>
            {
                CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                AmbCompanionProvider assets = provider.GetRequiredService<Plugins.Companions.Providers.AmbCompanionProvider>();
                return new PluginFactoryRegistration(
                    PluginKind.AssetManagement,
                    host => factory.CreateSpecification(host, PluginKind.AssetManagement, assets),
                    IsDefault: true);
            });
            services.AddSingleton(provider =>
            {
                CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                PumpsCompanionProvider pumps = provider.GetRequiredService<Plugins.Companions.Providers.PumpsCompanionProvider>();
                return new PluginFactoryRegistration(
                    PluginKind.Pumps,
                    host => factory.CreateSpecification(host, PluginKind.Pumps, pumps),
                    IsDefault: true);
            });
            services.TryAddSingleton(provider => new Plugins.Companions.Providers.WeighingCompanionProvider(
                provider.GetRequiredService<Plugins.Companions.Providers.IWeighingSimulationPolicy>(),
                provider.GetService<TimeProvider>()));
            services.AddSingleton(provider =>
            {
                CompanionPluginFactory factory = provider.GetRequiredService<CompanionPluginFactory>();
                WeighingCompanionProvider weighing = provider.GetRequiredService<Plugins.Companions.Providers.WeighingCompanionProvider>();
                return new PluginFactoryRegistration(
                    PluginKind.Weighing,
                    host => factory.CreateSpecification(host, PluginKind.Weighing, weighing),
                    IsDefault: true);
            });
            return services;
        }

        private static IPreparedCompanionProvider? ResolveMachineryJobs(IServiceProvider provider)
        {
            ICompanionProvider? jobs = provider.GetServices<ICompanionProvider>()
                .SingleOrDefault(candidate => candidate.Descriptor.Id == "isa95");
            return jobs switch
            {
                null => null,
                IPreparedCompanionProvider prepared => prepared,
                _ => throw new InvalidOperationException(
                    "The configured ISA-95 provider must support prepared tasks for Machinery.")
            };
        }

        private static void AddFactory<TPlugin>(
            IServiceCollection services,
            PluginKind kind,
            Func<PluginHost, TPlugin> create)
            where TPlugin : class, IPlugin
        {
            services.TryAddSingleton(_ => create);
            services.AddUaLensPluginFactory<Func<PluginHost, TPlugin>>(
                kind, static (factory, host) => factory(host), isDefault: true);
        }

        private sealed class RegistrationMarker;
    }
}
