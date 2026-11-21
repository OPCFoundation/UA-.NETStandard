// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;
using UaLens.ViewModels;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Typed composition for the built-in task adapters, with explicit provider replacement
    /// and a fresh operation workspace for every document.
    /// </summary>
    internal sealed class CompanionPluginFactory
    {
        public CompanionPluginFactory(
            ArrayOf<ICompanionProvider> providers = default,
            TimeProvider? timeProvider = null,
            ICompanionPackageReader? packages = null,
            ICompanionDeploymentPolicy? deploymentPolicy = null,
            OpenUsdViewportFactory? viewportFactory = null,
            IRenderedVisionCameraSource? visionCameraSource = null,
            IOpenUsdViewportAssetCache? viewportAssets = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_deploymentPolicy = deploymentPolicy;
            m_providers = providers.IsNull
                ? [
                    new DeviceCompanionProvider(packages, m_timeProvider),
                    new Isa95CompanionProvider(),
                    new WotCompanionProvider(),
                    new RegistryCompanionProvider(),
                    new RoboticsCompanionProvider(deploymentPolicy, m_timeProvider),
                    new RobotIntentCompanionProvider(deploymentPolicy, m_timeProvider),
                    new VisionCompanionProvider(),
                    new AICompanionProvider(),
                    new OpenUsdCompanionProvider(),
                    new LadsCompanionProvider(timeProvider: m_timeProvider)
                ]
                : providers;
            m_viewportFactory = viewportFactory ?? CreateViewportFactory(m_providers, viewportAssets);
            m_visionCameraSource = visionCameraSource ?? new RenderedVisionCameraSource();
        }

        public CompanionPlugin Create(PluginHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            return new CompanionPlugin(
                host, providers: m_providers, timeProvider: m_timeProvider, deploymentPolicy: m_deploymentPolicy,
                viewportFactory: m_viewportFactory, visionCameraSource: m_visionCameraSource);
        }

        /// <summary>
        /// Creates a dedicated document with one specification provider and the configured task policy.
        /// </summary>
        public CompanionPlugin CreateSpecification(
            PluginHost host,
            PluginKind kind,
            ICompanionProvider provider)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(provider);
            if (kind == PluginKind.Companions)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            foreach (ICompanionProvider registered in m_providers)
            {
                if (registered.Descriptor.Id == provider.Descriptor.Id)
                {
                    provider = registered;
                    break;
                }
            }
            return new CompanionPlugin(
                host, providers: [provider], timeProvider: m_timeProvider, deploymentPolicy: m_deploymentPolicy,
                kind: kind, documentName: provider.Descriptor.DisplayName,
                viewportFactory: m_viewportFactory, visionCameraSource: m_visionCameraSource);
        }

        /// <summary>
        /// Preserves the selected provider for both direct and dependency-injected document construction.
        /// </summary>
        internal static OpenUsdViewportFactory CreateViewportFactory(
            ArrayOf<ICompanionProvider> providers, IOpenUsdViewportAssetCache? assets = null)
        {
            return new OpenUsdViewportFactory(ResolveViewportSource(providers), assets);
        }

        private static IOpenUsdViewportSourceFactory ResolveViewportSource(ArrayOf<ICompanionProvider> providers)
        {
            foreach (ICompanionProvider provider in providers)
            {
                if (provider.Descriptor.Id == "openusd" && provider is IOpenUsdViewportSourceFactory source)
                {
                    return source;
                }
            }
            return new UnavailableViewportSource();
        }

        private readonly ArrayOf<ICompanionProvider> m_providers;
        private readonly TimeProvider m_timeProvider;
        private readonly ICompanionDeploymentPolicy? m_deploymentPolicy;
        private readonly OpenUsdViewportFactory m_viewportFactory;
        private readonly IRenderedVisionCameraSource m_visionCameraSource;

        /// <summary>
        /// Keeps inspection-only provider overrides usable without bypassing them with a default viewport source.
        /// </summary>
        private sealed class UnavailableViewportSource : IOpenUsdViewportSourceFactory
        {
            /// <inheritdoc/>
            public Task<OpenUsdViewportSource> OpenViewportAsync(
                CompanionContext context, CompanionTarget target, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new NotSupportedException(
                    "The configured OpenUSD provider does not support viewport source capture. " +
                    "Configure an OpenUsdViewportFactory for this provider.");
            }
        }
    }
}
