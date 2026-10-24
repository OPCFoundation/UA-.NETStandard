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
using System.Runtime.CompilerServices;
using Opc.Ua.DataChannels;

namespace Opc.Ua.PubSub.Transports.DataChannels
{
    /// <summary>
    /// Resolves validated local flow policy for a PubSub connection without serializing it into PubSub config.
    /// </summary>
    internal interface IDataChannelPubSubConfigurationProvider
    {
        /// <summary>
        /// Returns the local flow policy associated with the selected connection.
        /// </summary>
        DataChannelParametersDataType GetParameters(PubSubConnectionDataType connection);
    }

    /// <summary>
    /// Creates complete-message PubSub transports over one selected DataChannel carrier profile.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelPubSubTransportFactory :
        IPubSubTransportFactory,
        IDataChannelPubSubConfigurationProvider
    {
        /// <summary>
        /// Selects a carrier and the application-owned provider of actual ready flows.
        /// </summary>
        public DataChannelPubSubTransportFactory(
            DataChannelCarrierKind carrierKind,
            IDataChannelPubSubFlowProvider flowProvider)
            : this(
                carrierKind,
                flowProvider,
                DataChannelPubSubConfiguration.GetTransportProfile(carrierKind),
                parametersResolver: null)
        {
        }

        /// <summary>
        /// Creates a factory with an explicit profile and local binding resolver.
        /// </summary>
        /// <param name="carrierKind">The underlying DataChannel carrier.</param>
        /// <param name="flowProvider">Provider that opens ready flows.</param>
        /// <param name="transportProfileUri">The exact PubSub transport profile to register.</param>
        /// <param name="parametersResolver">
        /// Resolves the stable, locally provisioned flow policy for each connection.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public DataChannelPubSubTransportFactory(
            DataChannelCarrierKind carrierKind,
            IDataChannelPubSubFlowProvider flowProvider,
            string transportProfileUri,
            Func<PubSubConnectionDataType, DataChannelParametersDataType>? parametersResolver)
        {
            _ = DataChannelPubSubConfiguration.GetTransportProfile(carrierKind);
            if (transportProfileUri != DataChannelPubSubConfiguration.GetTransportProfile(carrierKind) &&
                !(carrierKind == DataChannelCarrierKind.SharedUasc &&
                    transportProfileUri == DataChannelPubSubConfiguration.UafxTransportProfile))
            {
                throw new ArgumentException(
                    "The selected transport profile does not match the carrier.", nameof(transportProfileUri));
            }
            if (transportProfileUri == DataChannelPubSubConfiguration.UafxTransportProfile &&
                parametersResolver is null)
            {
                throw new ArgumentNullException(nameof(parametersResolver));
            }
            TransportProfileUri = transportProfileUri;
            m_flowProvider = flowProvider ?? throw new ArgumentNullException(nameof(flowProvider));
            m_parametersResolver = parametersResolver;
        }

        /// <inheritdoc/>
        public string TransportProfileUri { get; }

        /// <inheritdoc/>
        public IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (connection.TransportProfileUri != TransportProfileUri)
            {
                throw new ArgumentException(
                    "The factory and connection select different carriers.", nameof(connection));
            }
            return new DataChannelPubSubTransport(
                new DataChannelPubSubFlowRequest(connection, GetParameters(connection)),
                m_flowProvider,
                timeProvider);
        }

        DataChannelParametersDataType IDataChannelPubSubConfigurationProvider.GetParameters(
            PubSubConnectionDataType connection) => GetParameters(connection);

        private DataChannelParametersDataType GetParameters(PubSubConnectionDataType connection)
        {
            if (TransportProfileUri == DataChannelPubSubConfiguration.UafxTransportProfile)
            {
                Func<PubSubConnectionDataType, DataChannelParametersDataType> resolver = m_parametersResolver ??
                    throw new InvalidOperationException("The UAFX local binding resolver is not configured.");
                DataChannelParametersDataType parameters = m_uafxParameterSnapshots.GetValue(
                    connection,
                    key => new Lazy<DataChannelParametersDataType>(
                        () => DataChannelPubSubConfiguration.GetUafxParameters(key, resolver),
                        System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                return DataChannelPubSubConfiguration.CopyParameters(parameters);
            }
            return DataChannelPubSubConfiguration.GetParameters(connection);
        }

        private readonly IDataChannelPubSubFlowProvider m_flowProvider;
        private readonly Func<PubSubConnectionDataType, DataChannelParametersDataType>? m_parametersResolver;
        private readonly ConditionalWeakTable<
            PubSubConnectionDataType,
            Lazy<DataChannelParametersDataType>> m_uafxParameterSnapshots = new();
    }
}
