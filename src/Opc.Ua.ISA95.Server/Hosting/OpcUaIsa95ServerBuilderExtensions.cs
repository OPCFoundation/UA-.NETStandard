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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua.ISA95.Server;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers ISA-95 server support on the regular OPC UA server builder.
    /// </summary>
    public static class OpcUaIsa95ServerBuilderExtensions
    {
        public static IIsa95ServerBuilder AddIsa95Server(
            this IOpcUaServerBuilder builder,
            Action<Isa95ServerOptions>? configure = null,
            Action<Isa95JobControlProviderOptions>? configureJobControl = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            EnsureFirstRegistration(builder.Services);

            if (configure == null)
            {
                builder.Services.AddOptions<Isa95ServerOptions>();
            }
            else
            {
                builder.Services.AddOptions<Isa95ServerOptions>().Configure(configure);
            }

            if (HasJobControlProviderRegistration(builder.Services))
            {
                if (configureJobControl != null)
                {
                    throw new InvalidOperationException(
                        "Job Control options cannot configure a custom provider registration.");
                }
            }
            else
            {
                builder.Services.AddInMemoryIsa95JobControlProvider(configureJobControl);
            }
            builder.Services.TryAddSingleton(serviceProvider =>
            {
                Isa95ServerOptions options =
                    serviceProvider.GetRequiredService<IOptions<Isa95ServerOptions>>().Value;
                options.Validate();
                return options;
            });
            builder.Services.TryAddSingleton(CreateServerProviders);
            builder.AddNodeManager<Isa95NodeManagerFactory>();
            return new Isa95ServerBuilder(builder.Services);
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(Isa95ServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddIsa95Server has already been called.");
                }
            }
            services.AddSingleton<Isa95ServerRegistrationMarker>();
        }

        private static bool HasJobControlProviderRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                Type serviceType = descriptor.ServiceType;
                if (serviceType == typeof(InMemoryIsa95JobControlProvider) ||
                    serviceType == typeof(IIsa95JobOrderReceiverV1) ||
                    serviceType == typeof(IIsa95JobResponseProviderV1) ||
                    serviceType == typeof(IIsa95JobResponseReceiverV1) ||
                    serviceType == typeof(IIsa95JobOrderReceiverV2) ||
                    serviceType == typeof(IIsa95JobResponseProviderV2) ||
                    serviceType == typeof(IIsa95JobResponseReceiverV2) ||
                    serviceType == typeof(IIsa95JobStatusSourceV2) ||
                    serviceType == typeof(IIsa95JobExecutionController) ||
                    serviceType == typeof(IIsa95JobOrderCatalog) ||
                    serviceType == typeof(IIsa95JobOrderCatalogChangeSource))
                {
                    return true;
                }
            }
            return false;
        }

        private static Isa95ServerProviders CreateServerProviders(
            IServiceProvider serviceProvider)
        {
            IIsa95JobOrderReceiverV1? orderReceiverV1 =
                serviceProvider.GetService<IIsa95JobOrderReceiverV1>();
            IIsa95JobResponseProviderV1? responseProviderV1 =
                serviceProvider.GetService<IIsa95JobResponseProviderV1>();
            IIsa95JobResponseReceiverV1? responseReceiverV1 =
                serviceProvider.GetService<IIsa95JobResponseReceiverV1>();
            IIsa95JobOrderReceiverV2? orderReceiverV2 =
                serviceProvider.GetService<IIsa95JobOrderReceiverV2>();
            IIsa95JobResponseProviderV2? responseProviderV2 =
                serviceProvider.GetService<IIsa95JobResponseProviderV2>();
            IIsa95JobResponseReceiverV2? responseReceiverV2 =
                serviceProvider.GetService<IIsa95JobResponseReceiverV2>();
            IIsa95JobStatusSourceV2? statusSourceV2 =
                serviceProvider.GetService<IIsa95JobStatusSourceV2>();
            IIsa95JobExecutionController? executionController =
                serviceProvider.GetService<IIsa95JobExecutionController>();
            IIsa95JobOrderCatalog? orderCatalog =
                serviceProvider.GetService<IIsa95JobOrderCatalog>();
            IIsa95JobOrderCatalogChangeSource? catalogChangeSource =
                serviceProvider.GetService<IIsa95JobOrderCatalogChangeSource>();
            InMemoryIsa95JobControlProvider? defaultProvider =
                serviceProvider.GetService<InMemoryIsa95JobControlProvider>();

            bool hasDefaultProvider = false;
            bool hasCustomProvider = false;
            ClassifyProvider(orderReceiverV1, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(responseProviderV1, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(responseReceiverV1, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(orderReceiverV2, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(responseProviderV2, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(responseReceiverV2, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(statusSourceV2, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(executionController, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(orderCatalog, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            ClassifyProvider(catalogChangeSource, defaultProvider, ref hasDefaultProvider, ref hasCustomProvider);
            if (hasDefaultProvider && hasCustomProvider)
            {
                throw new InvalidOperationException(
                    "Default and custom ISA-95 Job Control providers cannot be combined.");
            }

            return new Isa95ServerProviders
            {
                JobOrderReceiverV1 = orderReceiverV1,
                JobResponseProviderV1 = responseProviderV1,
                JobResponseReceiverV1 = responseReceiverV1,
                JobOrderReceiverV2 = orderReceiverV2,
                JobResponseProviderV2 = responseProviderV2,
                JobResponseReceiverV2 = responseReceiverV2,
                JobStatusSourceV2 = statusSourceV2,
                JobOrderCatalog = orderCatalog,
                JobOrderCatalogChangeSource = catalogChangeSource
            };
        }

        private static void ClassifyProvider<T>(
            T? provider,
            InMemoryIsa95JobControlProvider? defaultProvider,
            ref bool hasDefaultProvider,
            ref bool hasCustomProvider)
            where T : class
        {
            if (provider == null)
            {
                return;
            }
            if (ReferenceEquals(provider, defaultProvider))
            {
                hasDefaultProvider = true;
            }
            else
            {
                hasCustomProvider = true;
            }
        }

        private sealed class Isa95ServerBuilder : IIsa95ServerBuilder
        {
            public Isa95ServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IIsa95ServerBuilder ConfigureModel(
                Func<IIsa95ModelBuilder, CancellationToken, ValueTask> configure)
            {
                if (configure == null)
                {
                    throw new ArgumentNullException(nameof(configure));
                }
                Services.AddSingleton<IIsa95ModelConfigurator>(
                    new DelegateModelConfigurator(configure));
                return this;
            }
        }

        private sealed class DelegateModelConfigurator : IIsa95ModelConfigurator
        {
            public DelegateModelConfigurator(
                Func<IIsa95ModelBuilder, CancellationToken, ValueTask> configure)
            {
                m_configure = configure;
            }

            public ValueTask ConfigureAsync(
                IIsa95ModelBuilder builder,
                CancellationToken cancellationToken)
            {
                return m_configure(builder, cancellationToken);
            }

            private readonly Func<IIsa95ModelBuilder, CancellationToken, ValueTask>
                m_configure;
        }

        private sealed class Isa95ServerRegistrationMarker;
    }
}
