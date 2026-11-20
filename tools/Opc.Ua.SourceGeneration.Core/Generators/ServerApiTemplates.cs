/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class ServerApiTemplates
    {
        /// <summary>
        /// Server base template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.Prefix}}
            {
                {{Tokens.ServiceSets}}
            }
            """);

        /// <summary>
        /// Server API service set template
        /// </summary>
        public static readonly TemplateString ServiceSet = TemplateString.Parse(
            $$"""
            /// <summary>
            /// An interface to a UA server implementation.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public interface I{{Tokens.ServiceSet}}Server : global::Opc.Ua.IServerBase
            {
                {{Tokens.ServerApi}}
            }

            /// <summary>
            /// A basic implementation of the {{Tokens.ServiceSet}} server.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public partial class {{Tokens.ServiceSet}}ServerBase :
                global::Opc.Ua.ServerBase,
                global::Opc.Ua.I{{Tokens.ServiceSet}}Server
            {
                /// <summary>
                /// Create the {{Tokens.ServiceSet}} server.
                /// </summary>
                public {{Tokens.ServiceSet}}ServerBase(global::Opc.Ua.ITelemetryContext telemetry)
                    : base(telemetry)
                {
                }

                {{Tokens.ServerStubs}}
            }

            """);

        /// <summary>
        /// Server API interface method template
        /// </summary>
        public static readonly TemplateString InterfaceMethod = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service using async Task based request.
            /// </summary>
            {{Tokens.ServerMethodAsync}}

            """);

        /// <summary>
        /// Server API interface method template
        /// </summary>
        public static readonly TemplateString Method = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Invokes the {{Tokens.Name}} service using async Task based request.
            /// </summary>
            {{Tokens.ServerMethodAsync}}
            {
                ValidateRequest(requestHeader);

                // Insert implementation.
                await global::System.Threading.Tasks.Task.CompletedTask;

                throw new global::Opc.Ua.ServiceResultException(global::Opc.Ua.StatusCodes.BadServiceUnsupported);
            }

            """);
    }
}
