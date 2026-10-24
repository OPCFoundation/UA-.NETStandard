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
        /// Client API service set template (interface + class)
        /// </summary>
        public static readonly TemplateString ServiceSet = TemplateString.Parse(
            $$"""
            /// <summary>
            /// An interface used by by clients to access a UA server.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public interface I{{Tokens.ServiceSet}}ClientMethods{{Tokens.BaseInterfaces}}
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
        /// Client API service set template (interface only, no class)
        /// </summary>
        public static readonly TemplateString ServiceSetInterfaceOnly = TemplateString.Parse(
            $$"""
            /// <summary>
            /// An interface used by by clients to access a UA server.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public interface I{{Tokens.ServiceSet}}ClientMethods{{Tokens.BaseInterfaces}}
            {
                {{Tokens.ClientMethod}}
            }

            """);

        /// <summary>
        /// Client API interface method template
        /// </summary>
        public static readonly TemplateString InterfaceMethods = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service.
            /// </summary>
            {{Tokens.ClientMethodAsync}}

            """);

        /// <summary>
        /// Client API method implementations template
        /// </summary>
        public static readonly TemplateString Methods = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service.
            /// </summary>
            /// <remarks>
            /// The state machine is pooled, so the returned task must be awaited once,
            /// like any value task. A completed call keeps no reference to its response:
            /// a call that waited long enough to be promoted (a Publish waits for the
            /// publishing interval) would otherwise keep every later response alive
            /// until the next full collection.
            /// </remarks>
            #if NET6_0_OR_GREATER
            [global::System.Runtime.CompilerServices.AsyncMethodBuilder(
                typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]
            #endif
            {{Tokens.ClientMethodAsync}}
            {
                {{Tokens.Name}}Request {{Tokens.ClientRequestVariable}} = new {{Tokens.Name}}Request();
                {{Tokens.Name}}Response {{Tokens.ClientResponseVariable}} = null;

                {{Tokens.RequestParameters}}

                UpdateRequestHeader({{Tokens.ClientRequestVariable}}, requestHeader == null, "{{Tokens.Name}}");

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse =
                        await TransportChannel.SendRequestAsync({{Tokens.ClientRequestVariable}}, ct)
                            .ConfigureAwait(false);

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    {{Tokens.ClientResponseVariable}} = ({{Tokens.Name}}Response)genericResponse;
                }
                finally
                {
                    RequestCompleted(
                        {{Tokens.ClientRequestVariable}}, {{Tokens.ClientResponseVariable}}, "{{Tokens.Name}}");
                }

                return {{Tokens.ClientResponseVariable}};
            }

            {{Tokens.LegacyClientMethods}}

            """);

        /// <summary>
        /// Compatibility methods emitted only for Services that already have a legacy Client API.
        /// </summary>
        public static readonly TemplateString LegacyMethods = TemplateString.Parse(
            $$"""

            /// <summary>
            /// Invokes the {{Tokens.Name}} service synchronously.
            /// </summary>
            [global::System.Obsolete("Sync methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodSync}}
            {
                {{Tokens.Name}}Request {{Tokens.ClientRequestVariable}} = new {{Tokens.Name}}Request();
                {{Tokens.Name}}Response? {{Tokens.ClientResponseVariable}} = null;

                {{Tokens.RequestParameters}}

                UpdateRequestHeader({{Tokens.ClientRequestVariable}}, requestHeader == null, "{{Tokens.Name}}");

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse = TransportChannel
                        .SendRequestAsync(
                            {{Tokens.ClientRequestVariable}}, global::System.Threading.CancellationToken.None)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    {{Tokens.ClientResponseVariable}} = ({{Tokens.Name}}Response)genericResponse;

                    {{Tokens.ResponseParameters}}
                }
                finally
                {
                    RequestCompleted(
                        {{Tokens.ClientRequestVariable}}, {{Tokens.ClientResponseVariable}}, "{{Tokens.Name}}");
                }

                return {{Tokens.ClientResponseVariable}}.ResponseHeader;
            }

            /// <summary>
            /// Begins an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodBegin}}
            {
                {{Tokens.Name}}Request {{Tokens.ClientRequestVariable}} = new {{Tokens.Name}}Request();

                {{Tokens.RequestParameters}}

                UpdateRequestHeader({{Tokens.ClientRequestVariable}}, requestHeader == null, "{{Tokens.Name}}");

                global::System.Threading.Tasks.Task<global::Opc.Ua.IServiceResponse> task = TransportChannel
                    .SendRequestAsync({{Tokens.ClientRequestVariable}}, global::System.Threading.CancellationToken.None)
                    .AsTask();
                global::System.Threading.Tasks.TaskCompletionSource<global::Opc.Ua.IServiceResponse> tcs =
                    new global::System.Threading.Tasks.TaskCompletionSource<global::Opc.Ua.IServiceResponse>(asyncState);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        tcs.TrySetException(t.Exception!.InnerExceptions);
                    }
                    else if (t.IsCanceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(t.Result);
                    }
                    callback?.Invoke(tcs.Task);
                }, global::System.Threading.Tasks.TaskScheduler.Default);
                return tcs.Task;
            }

            /// <summary>
            /// Finishes an asynchronous invocation of the {{Tokens.Name}} service.
            /// </summary>
            [global::System.Obsolete("Begin/End methods are deprecated in this version. Use {{Tokens.Name}}Async instead.")]
            {{Tokens.ClientMethodEnd}}
            {
                {{Tokens.Name}}Response {{Tokens.ClientResponseVariable}} = null;

                try
                {
                    global::Opc.Ua.IServiceResponse? genericResponse =
                        ((global::System.Threading.Tasks.Task<global::Opc.Ua.IServiceResponse>)result)
                        .GetAwaiter()
                        .GetResult();

                    if (genericResponse == null)
                    {
                        throw new global::Opc.Ua.ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    ValidateResponse(genericResponse.ResponseHeader);
                    {{Tokens.ClientResponseVariable}} = ({{Tokens.Name}}Response)genericResponse;

                    {{Tokens.ResponseParameters}}
                }
                finally
                {
                    RequestCompleted(null, {{Tokens.ClientResponseVariable}}, "{{Tokens.Name}}");
                }

                return {{Tokens.ClientResponseVariable}}.ResponseHeader;
            }

            """);
    }
}
