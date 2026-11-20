/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A subclass of a session to override some implementations from CleintBase
    /// </summary>
    public class TraceableRequestHeaderClientSession : Session
    {
        /// <inheritdoc/>
        public TraceableRequestHeaderClientSession(
            ITransportChannel channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            Certificate clientCertificate,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default,
            ISubscriptionEngineFactory? engineFactory = null)
            : base(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                null,
                availableEndpoints,
                discoveryProfileUris,
                engineFactory)
        {
        }

        /// <inheritdoc/>
        public TraceableRequestHeaderClientSession(
            ITransportChannel channel,
            Session template,
            bool copyEventHandlers)
            : base(channel, template, copyEventHandlers)
        {
        }

        /// <summary>
        /// Populates AdditionalParameters with details from the ActivityContext
        /// </summary>
        public static void InjectTraceIntoAdditionalParameters(
            ActivityContext context,
            out AdditionalParametersType traceData)
        {
            // https://reference.opcfoundation.org/Core/Part26/v105/docs/5.5.4
            Span<byte> spanId = stackalloc byte[8];
            Span<byte> traceId = stackalloc byte[16];
            context.SpanId.CopyTo(spanId);
            context.TraceId.CopyTo(traceId);
            var spanContextParameter = new KeyValuePair
            {
                Key = QualifiedName.From("SpanContext"),
                Value = Variant.FromStructure(new SpanContextDataType
                {
#if NET8_0_OR_GREATER
                    SpanId = BitConverter.ToUInt64(spanId),
                    TraceId = new Guid(traceId)
#else
                    SpanId = BitConverter.ToUInt64(spanId.ToArray(), 0),
                    TraceId = new Uuid(traceId.ToArray())
#endif
                })
            };
            traceData = new AdditionalParametersType
            {
                Parameters = [spanContextParameter]
            };
        }

        ///<inheritdoc/>
        protected override void UpdateRequestHeader(IServiceRequest request, bool useDefaults)
        {
            base.UpdateRequestHeader(request, useDefaults);

            if (Activity.Current != null)
            {
                InjectTraceIntoAdditionalParameters(
                    Activity.Current.Context,
                    out AdditionalParametersType traceData);

                if (request.RequestHeader.AdditionalHeader.IsNull)
                {
                    request.RequestHeader.AdditionalHeader = new ExtensionObject(traceData);
                }
                else if (request.RequestHeader.AdditionalHeader.TryGetValue(
                    out AdditionalParametersType? existingParameters))
                {
                    // Merge the trace data into the existing parameters.
                    existingParameters.Parameters =
                        existingParameters.Parameters.AddItems(traceData.Parameters);
                }
            }
        }
    }
}
