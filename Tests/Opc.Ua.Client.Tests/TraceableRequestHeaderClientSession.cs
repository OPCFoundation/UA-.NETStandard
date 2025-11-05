/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
            traceData = new AdditionalParametersType();

            // Determine the trace flag based on the 'Recorded' status.
            string traceFlags = (context.TraceFlags & ActivityTraceFlags.Recorded) != 0
                ? "01"
                : "00";

            // Construct the traceparent header, adhering to the W3C Trace Context format.
            string traceparent = $"00-{context.TraceId}-{context.SpanId}-{traceFlags}";
            traceData.Parameters
                .Add(new KeyValuePair { Key = "traceparent", Value = new Variant(traceparent) });
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

                if (request.RequestHeader.AdditionalHeader == null)
                {
                    request.RequestHeader.AdditionalHeader = new ExtensionObject(traceData);
                }
                else if (request.RequestHeader.AdditionalHeader
                    .Body is AdditionalParametersType existingParameters)
                {
                    // Merge the trace data into the existing parameters.
                    existingParameters.Parameters.AddRange(traceData.Parameters);
                }
            }
        }
    }
}
