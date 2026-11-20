/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class ResourceTemplates
    {
        /// <summary>
        /// Resources file template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfResourceGroups}}
            }
            """);

        /// <summary>
        /// Resources class template
        /// </summary>
        public static readonly TemplateString Class = TemplateString.Parse(
            $$"""
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            {{Tokens.AccessModifier}} static class {{Tokens.ClassName}}
            {
                {{Tokens.ListOfResourceDeclarations}}
            }

            """);

        /// <summary>
        /// ReadOnlySpan resource declaration
        /// </summary>
        public static readonly TemplateString Declaration_ReadOnlySpan = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The embedded {{Tokens.ResourceName}} resource as span
            /// </summary>
            public static global::System.ReadOnlySpan<byte> {{Tokens.ResourceName}} =>
                {{Tokens.Resource}}
                ;

            /// <summary>
            /// The embedded {{Tokens.ResourceName}} resource as stream
            /// </summary>
            public static global::System.IO.Stream {{Tokens.ResourceName}}AsStream
            {
                // Copy operation
                get => new global::System.IO.MemoryStream({{Tokens.ResourceName}}.ToArray(), false);
            }

            """);

        /// <summary>
        /// Byte array resource declaration
        /// </summary>
        public static readonly TemplateString Declaration_ByteArray = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The embedded {{Tokens.ResourceName}} resource
            /// </summary>
            public static byte[] {{Tokens.ResourceName}} =>
                {{Tokens.Resource}}
                ;

            /// <summary>
            /// The embedded {{Tokens.ResourceName}} resource as stream
            /// </summary>
            public static global::System.IO.Stream {{Tokens.ResourceName}}AsStream
            {
                get => new global::System.IO.MemoryStream({{Tokens.ResourceName}}, false);
            }

            """);

        /// <summary>
        /// String resource declaration
        /// </summary>
        public static readonly TemplateString Declaration_ConstString = TemplateString.Parse(
            $$"""
            public const string {{Tokens.ResourceName}} = {{Tokens.Resource}};

            """);
    }
}
