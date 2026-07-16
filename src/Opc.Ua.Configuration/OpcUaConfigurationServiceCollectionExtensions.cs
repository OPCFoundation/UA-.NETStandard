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
using Opc.Ua.Configuration;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Configuration</c>.
    /// </summary>
    public static class OpcUaConfigurationServiceCollectionExtensions
    {
        /// <summary>
        /// Configures the common OPC UA application identity and security
        /// settings used by subsequently registered client and server features.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">The application-options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder ConfigureApplication(
            this IOpcUaBuilder builder,
            Action<OpcUaApplicationOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaApplicationOptions();
            configure(options);
            builder.Services.AddSingleton(options);
            builder.AddApplicationInstance();
            builder.Services.TryAddSingleton(sp =>
                new OpcUaApplicationConfigurationProvider(
                    sp.GetRequiredService<OpcUaApplicationOptions>(),
                    sp.GetRequiredService<IApplicationInstanceFactory>(),
                    sp.GetRequiredService<ITelemetryContext>(),
                    [.. sp.GetServices<IOpcUaApplicationConfigurationFeature>()],
                    sp.GetService<ICertificateManager>()));
            builder.Services.TryAddSingleton<IOpcUaApplicationConfigurationProvider>(
                sp => sp.GetRequiredService<OpcUaApplicationConfigurationProvider>());
            return builder;
        }

        /// <summary>
        /// Registers the default
        /// <see cref="IApplicationInstanceFactory"/> implementation so
        /// hosted OPC UA servers (regular, GDS, LDS, WotCon) can each
        /// create their own <see cref="IApplicationInstance"/>.
        /// </summary>
        /// <remarks>
        /// Idempotent (<c>TryAddSingleton</c>). A consumer that registers
        /// a custom <see cref="IApplicationInstanceFactory"/> before this
        /// call wins.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddApplicationInstance(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<IApplicationInstanceFactory,
                DefaultApplicationInstanceFactory>();
            return builder;
        }
    }
}
