/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
