// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                global::Opc.Ua.RequestLifetime requestLifetime)
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
