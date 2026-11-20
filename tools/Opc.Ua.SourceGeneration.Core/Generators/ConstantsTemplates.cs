// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class ConstantsTemplates
    {
        /// <summary>
        /// Constants file template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            {{Tokens.ListOfImports}}

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfIdentifiers}}

                {{Tokens.ListOfNodeIds}}

                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                public static partial class BrowseNames
                {
                    {{Tokens.ListOfBrowseNames}}
                }

                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                public static partial class Namespaces
                {
                    {{Tokens.ListOfNamespaceUris}}
                }

                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                public static partial class ModelVersions
                {
                    /// <summary>
                    /// The version of the target OPC UA model.
                    /// </summary>
                    public const string Target = "{{Tokens.ModelVersion}}";
                }
            }
            """);

        /// <summary>
        /// Namespace Uris
        /// </summary>
        public static readonly TemplateString NamespaceUri = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The URI for the {{Tokens.Name}} namespace (.NET code namespace is '{{Tokens.CodeName}}').
            /// </summary>
            public const string {{Tokens.Name}} = "{{Tokens.NamespaceUri}}";

            """);

        /// <summary>
        /// Browse names
        /// </summary>
        public static readonly TemplateString BrowseName = TemplateString.Parse(
            $$"""
            public const string {{Tokens.SymbolicName}} = "{{Tokens.BrowseNameLiteral}}";

            """);
    }
}
