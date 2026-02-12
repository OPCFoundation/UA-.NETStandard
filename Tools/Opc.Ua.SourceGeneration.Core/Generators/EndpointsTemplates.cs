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
    internal static class EndpointsTemplates
    {
        /// <summary>
        /// Endpoints file for stack generator
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
        /// Service set endpoint class
        /// </summary>
        public static readonly TemplateString ServiceSet = TemplateString.Parse(
            $$"""
            /// <summary>
            /// A endpoint object used by clients to access a UA service.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ServiceSet}}Endpoint : global::Opc.Ua.EndpointBase
            {
                /// <summary>
                /// Initializes the object.
                /// </summary>
                [Obsolete("WCF not supported")]
                public {{Tokens.ServiceSet}}Endpoint()
                {
                    this.CreateKnownTypes();
                }

                /// <summary>
                /// Initializes the when it is created directly.
                /// </summary>
                public {{Tokens.ServiceSet}}Endpoint(global::Opc.Ua.IServiceHostBase host)
                    : base(host)
                {
                    this.CreateKnownTypes();
                }

                /// <summary>
                /// Initializes a new instance of the <see cref="{{Tokens.ServiceSet}}Endpoint"/> class.
                /// </summary>
                /// <param name="server">The server.</param>
                public {{Tokens.ServiceSet}}Endpoint(global::Opc.Ua.ServerBase server)
                    : base(server)
                {
                    this.CreateKnownTypes();
                }

                /// <summary>
                /// The UA server instance that the endpoint is connected to.
                /// </summary>
                protected I{{Tokens.ServiceSet}}Server? ServerInstance
                {
                    get
                    {
                        if (global::Opc.Ua.ServiceResult.IsBad(ServerError))
                        {
                            throw new global::Opc.Ua.ServiceResultException(ServerError);
                        }

                        return ServerForContext as I{{Tokens.ServiceSet}}Server;
                    }
                }

                {{Tokens.MethodList}}

                /// <summary>
                /// Populates the known types table.
                /// </summary>
                protected virtual void CreateKnownTypes()
                {
                    {{Tokens.AddKnownType}}
                }
            }

            """);

        /// <summary>
        /// Endpoints method template
        /// </summary>
        public static readonly TemplateString Method = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service.
            /// </summary>
            public async global::System.Threading.Tasks.ValueTask<global::Opc.Ua.IServiceResponse> {{Tokens.Name}}Async(
                global::Opc.Ua.IServiceRequest incoming,
                global::Opc.Ua.SecureChannelContext secureChannelContext,
                global::System.Threading.CancellationToken cancellationToken = default)
            {
                {{Tokens.Name}}Response? response = null;

                try
                {
                    OnRequestReceived(incoming);

                    {{Tokens.Name}}Request request = ({{Tokens.Name}}Request)incoming;

                    {{Tokens.InvokeServiceAsync}}
                }
                finally
                {
                    OnResponseSent(response);
                }

                return response;
            }

            """);
    }
}
