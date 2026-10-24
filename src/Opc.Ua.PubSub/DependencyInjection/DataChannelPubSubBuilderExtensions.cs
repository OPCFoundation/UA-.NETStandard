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
using Opc.Ua;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Transports.DataChannels;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Adds independently selected flow carriers and payload mappings without native carrier dependencies.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public static class DataChannelPubSubBuilderExtensions
    {
        /// <summary>
        /// Registers a carrier with a caller-owned flow provider.
        /// </summary>
        public static IPubSubBuilder AddDataChannelTransport(
            this IPubSubBuilder builder,
            DataChannelCarrierKind carrierKind,
            IDataChannelPubSubFlowProvider flowProvider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (flowProvider is null)
            {
                throw new ArgumentNullException(nameof(flowProvider));
            }
            _ = DataChannelPubSubConfiguration.GetTransportProfile(carrierKind);
            builder.Services.AddPubSubTransportFactory(_ =>
                new DataChannelPubSubTransportFactory(carrierKind, flowProvider));
            return builder;
        }

        /// <summary>
        /// Registers a DI-owned flow provider keyed by carrier and a decorated transport factory.
        /// The factory can resolve the host's connector or resource router without a base PubSub dependency on it.
        /// </summary>
        public static IPubSubBuilder AddDataChannelTransport(
            this IPubSubBuilder builder,
            DataChannelCarrierKind carrierKind,
            Func<IServiceProvider, IDataChannelPubSubFlowProvider> flowProviderFactory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (flowProviderFactory is null)
            {
                throw new ArgumentNullException(nameof(flowProviderFactory));
            }
            _ = DataChannelPubSubConfiguration.GetTransportProfile(carrierKind);
            builder.Services.AddKeyedSingleton<IDataChannelPubSubFlowProvider>(
                carrierKind, (provider, _) => flowProviderFactory(provider));
            builder.Services.AddPubSubTransportFactory(provider => new DataChannelPubSubTransportFactory(
                carrierKind, provider.GetRequiredKeyedService<IDataChannelPubSubFlowProvider>(carrierKind)));
            return builder;
        }

        /// <summary>
        /// Registers the optional UAFX PubSub-over-UASC binding with a caller-owned flow provider and local policy
        /// resolver. The provider must route the configured resource over an existing shared UASC context.
        /// </summary>
        public static IPubSubBuilder AddUafxDataChannelTransport(
            this IPubSubBuilder builder,
            IDataChannelPubSubFlowProvider flowProvider,
            Func<PubSubConnectionDataType, DataChannelParametersDataType> parametersResolver)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (flowProvider is null)
            {
                throw new ArgumentNullException(nameof(flowProvider));
            }
            if (parametersResolver is null)
            {
                throw new ArgumentNullException(nameof(parametersResolver));
            }
            builder.Services.AddPubSubTransportFactory(_ =>
                new DataChannelPubSubTransportFactory(
                    DataChannelCarrierKind.SharedUasc,
                    flowProvider,
                    DataChannelPubSubConfiguration.UafxTransportProfile,
                    parametersResolver));
            return builder;
        }

        /// <summary>
        /// Registers a DI-owned provider and local policy resolver for the optional UAFX PubSub-over-UASC binding.
        /// </summary>
        public static IPubSubBuilder AddUafxDataChannelTransport(
            this IPubSubBuilder builder,
            Func<IServiceProvider, IDataChannelPubSubFlowProvider> flowProviderFactory,
            Func<IServiceProvider, PubSubConnectionDataType, DataChannelParametersDataType> parametersResolver)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (flowProviderFactory is null)
            {
                throw new ArgumentNullException(nameof(flowProviderFactory));
            }
            if (parametersResolver is null)
            {
                throw new ArgumentNullException(nameof(parametersResolver));
            }
            builder.Services.AddKeyedSingleton<IDataChannelPubSubFlowProvider>(
                UafxProviderKey,
                (provider, _) => flowProviderFactory(provider));
            builder.Services.AddPubSubTransportFactory(provider =>
                new DataChannelPubSubTransportFactory(
                    DataChannelCarrierKind.SharedUasc,
                    provider.GetRequiredKeyedService<IDataChannelPubSubFlowProvider>(
                        UafxProviderKey),
                    DataChannelPubSubConfiguration.UafxTransportProfile,
                    connection => parametersResolver(provider, connection)));
            return builder;
        }

        private const string UafxProviderKey = "UafxDataChannelPubSub";
    }
}
