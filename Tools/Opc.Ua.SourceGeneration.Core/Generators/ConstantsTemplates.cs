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
            public const string {{Tokens.SymbolicName}} = "{{Tokens.BrowseName}}";

            """);
    }
}
