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
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

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
            X509Certificate2 clientCertificate,
            EndpointDescriptionCollection availableEndpoints = null,
            StringCollection discoveryProfileUris = null)
            : base(
                channel,
                configuration,
                endpoint,
                clientCertificate,
                null,
                availableEndpoints,
                discoveryProfileUris)
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
                Key = "SpanContext",
                Value = new Variant(new SpanContextDataType
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
            traceData = new AdditionalParametersType();
            traceData.Parameters.Add(spanContextParameter);
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
                else if (request.RequestHeader.AdditionalHeader.TryGetEncodeable(
                    out AdditionalParametersType existingParameters))
                {
                    // Merge the trace data into the existing parameters.
                    existingParameters.Parameters.AddRange(traceData.Parameters);
                }
            }
        }
    }
}
