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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class ClientApiTemplates
    {
        /// <summary>
        /// Client API file template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            using System;

            namespace {{Tokens.Prefix}}
            {
                {{Tokens.ServiceSets}}
            }
            """);

        /// <summary>
        /// Client API service set template
        /// </summary>
        public static readonly TemplateString ServiceSet = TemplateString.Parse(
            $$"""
            /// <summary>
            /// An interface used by by clients to access a UA server.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public interface I{{Tokens.ServiceSet}}ClientMethods
            {
                {{Tokens.ClientMethod}}
            }

            /// <summary>
            /// The client side interface for a UA server.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ServiceSet}}Client :
                global::Opc.Ua.ClientBase,
                global::Opc.Ua.I{{Tokens.ServiceSet}}ClientMethods
            {
                /// <summary>
                /// Intializes the object with a channel and a message context.
                /// </summary>
                public {{Tokens.ServiceSet}}Client(
                    global::Opc.Ua.ITransportChannel channel,
                    global::Opc.Ua.ITelemetryContext telemetry)
                    : base(channel, telemetry)
                {
                }

                {{Tokens.ClientApi}}
            }

            """);

        /// <summary>
        /// Client API interface method template
        /// </summary>
        public static readonly TemplateString InterfaceMethods = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service using async Task based request.
            /// </summary>
            {{Tokens.ClientMethodAsync}}

            /// <summary>
            /// Invokes the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Sync methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodSync}}

            /// <summary>
            /// Begins an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodBegin}}

            /// <summary>
            /// Finishes an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodEnd}}

            """);

        /// <summary>
        /// Client API method implementations template
        /// </summary>
        public static readonly TemplateString Methods = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service.
            /// </summary>
            {{Tokens.ClientMethodAsync}}
            {
                {{Tokens.Name}}Request request = new {{Tokens.Name}}Request();
                {{Tokens.Name}}Response response = null;

                {{Tokens.RequestParameters}}

                UpdateRequestHeader(request, requestHeader == null, "{{Tokens.Name}}");

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse =
                        await TransportChannel.SendRequestAsync(request, ct).ConfigureAwait(false);

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    response = ({{Tokens.Name}}Response)genericResponse;
                }
                finally
                {
                    RequestCompleted(request, response, "{{Tokens.Name}}");
                }

                return response;
            }

            /// <summary>
            /// Invokes the {{Tokens.Name}} service synchronously.
            /// </summary>
            [global::System.Obsolete("Sync methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodSync}}
            {
                {{Tokens.Name}}Request request = new {{Tokens.Name}}Request();
                {{Tokens.Name}}Response? response = null;

                {{Tokens.RequestParameters}}

                UpdateRequestHeader(request, requestHeader == null, "{{Tokens.Name}}");

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse = TransportChannel.SendRequest(request);

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    response = ({{Tokens.Name}}Response)genericResponse;

                    {{Tokens.ResponseParameters}}
                }
                finally
                {
                    RequestCompleted(request, response, "{{Tokens.Name}}");
                }

                return response.ResponseHeader;
            }

            /// <summary>
            /// Begins an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodBegin}}
            {
                {{Tokens.Name}}Request request = new {{Tokens.Name}}Request();

                {{Tokens.RequestParameters}}

                UpdateRequestHeader(request, requestHeader == null, "{{Tokens.Name}}");

                return TransportChannel.BeginSendRequest(request, callback, asyncState);
            }

            /// <summary>
            /// Finishes an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodEnd}}
            {
                {{Tokens.Name}}Response response = null;

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse = TransportChannel.EndSendRequest(result);

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    response = ({{Tokens.Name}}Response)genericResponse;

                    {{Tokens.ResponseParameters}}
                }
                finally
                {
                    RequestCompleted(null, response, "{{Tokens.Name}}");
                }

                return response.ResponseHeader;
            }

            """);
    }
}
