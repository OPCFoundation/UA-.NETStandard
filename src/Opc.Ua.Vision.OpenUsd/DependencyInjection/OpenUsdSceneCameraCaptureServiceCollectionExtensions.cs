// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Vision.OpenUsd;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// DI extension for registering the OpenUSD-backed implementation of
    /// <see cref="ISceneCameraCaptureProvider"/>. Follows the same
    /// convention as
    /// <c>Opc.Ua.OpenUsd.Client.Fluent.OpcUaOpenUsdConnectorBuilderExtensions</c>:
    /// live in the
    /// <c>Microsoft.Extensions.DependencyInjection</c> namespace so the
    /// call surfaces on any <see cref="IServiceCollection"/>.
    /// </summary>
    public static class OpenUsdSceneCameraCaptureServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="OpenUsdSceneCameraCaptureProvider"/> as the
        /// singleton <see cref="ISceneCameraCaptureProvider"/>. The device
        /// probe runs when the provider is first resolved, not at
        /// registration time, so this call is safe on hosts where no
        /// graphics backend is available - the resolved provider will
        /// simply report <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>
        /// on every capture.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
        public static IServiceCollection AddOpenUsdSceneCameraCaptureProvider(
            this IServiceCollection services,
            Action<OpenUsdSceneCaptureOptions>? configure = null)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            var options = new OpenUsdSceneCaptureOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<ISceneCameraCaptureProvider>(sp =>
                new OpenUsdSceneCameraCaptureProvider(
                    sp.GetRequiredService<OpenUsdSceneCaptureOptions>(),
                    sp.GetService<ITelemetryContext>()));
            return services;
        }
    }
}
