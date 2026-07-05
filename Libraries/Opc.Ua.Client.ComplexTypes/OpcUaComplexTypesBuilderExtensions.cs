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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client.ComplexTypes</c>: register a
    /// <see cref="ComplexTypeSystemFactory"/> so that dependency-injected
    /// client hosts (e.g. <c>ManagedSession</c> consumers) can resolve
    /// typed complex-type loaders.
    /// </summary>
    public static class OpcUaComplexTypesBuilderExtensions
    {
        /// <summary>
        /// Registers the singleton <see cref="ComplexTypeSystemFactory"/>
        /// service so consumers can resolve it and produce a
        /// <see cref="ComplexTypeSystem"/> per <see cref="ISession"/>.
        /// </summary>
        /// <remarks>
        /// Idempotent (<see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection)"/>):
        /// repeated calls do not add a second descriptor. The root
        /// <c>AddOpcUa()</c> call is invoked to ensure
        /// <see cref="ITelemetryContext"/> is registered even if the
        /// caller skipped chaining through it.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddComplexTypes(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<IComplexTypeSystemFactory, ComplexTypeSystemFactory>();
            builder.Services.TryAddSingleton<ComplexTypeSystemFactory>();
            return builder;
        }

        /// <summary>
        /// Registers the singleton <see cref="ComplexTypeSystemFactory"/>
        /// service and keeps the client builder chain flowing.
        /// </summary>
        public static IOpcUaClientBuilder AddComplexTypes(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new BuilderAdapter(builder.Services).AddComplexTypes();
            return builder;
        }

        /// <summary>
        /// Registers client services, reconnect defaults, and complex-type
        /// loading in one call.
        /// </summary>
        public static IOpcUaClientBuilder AddManagedClient(
            this IOpcUaBuilder builder,
            Action<OpcUaClientOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            IOpcUaClientBuilder clientBuilder = builder.AddClient(options =>
            {
                configure(options);
                options.Session = options.Session with { LoadComplexTypes = true };
            });
            return clientBuilder.AddComplexTypes();
        }


        /// <summary>
        /// Registers a managed client from the default configuration section and enables complex-type loading.
        /// </summary>
        public static IOpcUaClientBuilder AddManagedClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return builder.AddManagedClient(
                configuration.GetSection(OpcUaClientBuilderExtensions.DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers a managed client from configuration and enables complex-type loading.
        /// </summary>
        public static IOpcUaClientBuilder AddManagedClient(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            IOpcUaClientBuilder clientBuilder = builder.AddClient(section);
            EnableComplexTypeLoading(clientBuilder.Services);
            return clientBuilder.AddComplexTypes();
        }


        private static void EnableComplexTypeLoading(IServiceCollection services)
        {
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(OpcUaClientOptions) &&
                    services[i].ImplementationInstance is OpcUaClientOptions options)
                {
                    options.Session = options.Session with { LoadComplexTypes = true };
                    return;
                }
            }

            throw new InvalidOperationException(
                "AddManagedClient requires AddClient to register OpcUaClientOptions.");
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
