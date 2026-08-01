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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register replaceable WoT
    /// protocol binders and executors and wire them into the WoT Connectivity 1.1
    /// materialization coordinator. Binders are injected independently and are
    /// selected by pinned identification rules; concrete executors are registered
    /// separately so a protocol can be validated without being executable and can
    /// be upgraded to executable once its executor is present. The bundled HTTP,
    /// Modbus TCP, and OPC UA registration helpers call these extensions directly;
    /// the optional MQTT package does the same from its own assembly.
    /// </summary>
    public static class OpcUaWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner / validator binders (HTTP, CoAP,
        /// MQTT, Modbus TCP, BACnet, PROFINET, LoRaWAN and OPC UA) and replaces the
        /// binder registry with the aggregating
        /// <see cref="WotProtocolBinderRegistry"/>. Without any executor these
        /// binders validate and compile plans but materialize non-executable nodes.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotProtocolBinders(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            foreach (IWotProtocolBinder binder in WotBuiltInBinders.CreateAll())
            {
                builder.Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton(binder));
            }
            EnsureRegistry(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers a single, custom protocol binder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotBinder(this IOpcUaBuilder builder, IWotProtocolBinder binder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (binder is null)
            {
                throw new ArgumentNullException(nameof(binder));
            }
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton(binder));
            EnsureRegistry(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers a runtime executor for a binder identity.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotBindingExecutor(
            this IOpcUaBuilder builder, IWotBindingExecutor executor)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (executor is null)
            {
                throw new ArgumentNullException(nameof(executor));
            }
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton(executor));
            EnsureRegistry(builder.Services);
            return builder;
        }

        /// <summary>
        /// Registers the credential provider used to resolve secret-free references.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotCredentialProvider(
            this IOpcUaBuilder builder, IWotCredentialProvider provider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            builder.Services.AddSingleton(provider);
            return builder;
        }

        /// <summary>
        /// Registers the endpoint policy enforced before a WoT binding executor opens an outbound channel.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddWotEndpointPolicy(
            this IOpcUaBuilder builder, WotEndpointPolicy policy)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            builder.Services.AddSingleton(policy);
            return builder;
        }

        private static void EnsureRegistry(IServiceCollection services)
        {
            services.EnsureWotBinderRegistry();
        }
    }

    /// <summary>
    /// <see cref="IServiceCollection"/> extension shared by every registration
    /// path (the Bindings builder extensions above and
    /// <c>AddWotRegistryServer</c>) that needs the aggregating
    /// <see cref="WotProtocolBinderRegistry"/>. It is safe to call any number
    /// of times, in any order relative to <see cref="OpcUaWotBindingBuilderExtensions"/>
    /// registrations: the registry is registered once via <c>TryAddSingleton</c>
    /// as its own concrete type, and both <see cref="IWotBinderRegistry"/> and
    /// <see cref="IWotBindingChannelFactory"/> are wired to resolve that same
    /// singleton instance, so exactly one registry is ever constructed
    /// regardless of registration order.
    /// </summary>
    public static class WotBinderRegistryServiceCollectionExtensions
    {
        /// <summary>
        /// Ensures the aggregating <see cref="WotProtocolBinderRegistry"/> is
        /// registered exactly once and exposed as both
        /// <see cref="IWotBinderRegistry"/> and
        /// <see cref="IWotBindingChannelFactory"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IServiceCollection EnsureWotBinderRegistry(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton(sp => new WotProtocolBinderRegistry(
                sp.GetServices<IWotProtocolBinder>(),
                sp.GetServices<IWotBindingExecutor>(),
                sp.GetService<IWotCredentialProvider>(),
                sp.GetService<IWotCodecRegistry>(),
                sp.GetService<WotBindingBounds>(),
                sp.GetService<WotEndpointPolicy>()));
            services.TryAddSingleton<IWotBinderRegistry>(
                sp => sp.GetRequiredService<WotProtocolBinderRegistry>());
            services.TryAddSingleton<IWotBindingChannelFactory>(
                sp => sp.GetRequiredService<WotProtocolBinderRegistry>());
            return services;
        }
    }
}
