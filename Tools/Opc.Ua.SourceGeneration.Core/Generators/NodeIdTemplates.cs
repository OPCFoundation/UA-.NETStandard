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
    internal static class NodeIdTemplates
    {
        /// <summary>
        /// Identifiers file template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfIdentifiers}}

                {{Tokens.ListOfNodeIds}}
            }
            """);

        /// <summary>
        /// Identifiers per node class
        /// </summary>
        public static readonly TemplateString IdsPerNodeClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// A class that declares constants for all {{Tokens.NodeClass}}
            /// symbolic names in the {{Tokens.Namespace}} namespace.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public static partial class {{Tokens.NodeClass}}s
            {
                {{Tokens.ListOfIdentifiers}}

                {{Tokens.IdentifierReflection}}
            }

            """);

        /// <summary>
        /// NodeIds per node class
        /// </summary>
        public static readonly TemplateString NodeIdPerNodeClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// A class that declares constants for all {{Tokens.NodeClass}}
            /// NodeIds in the {{Tokens.Namespace}} namespace.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public static partial class {{Tokens.NodeClass}}Ids
            {
                {{Tokens.ListOfIdentifiers}}

                {{Tokens.IdentifierReflection}}
            }

            """);

        /// <summary>
        /// Identifier declaration
        /// </summary>
        public static readonly TemplateString IdDeclaration = TemplateString.Parse(
            $$"""
            public const {{Tokens.IdType}} {{Tokens.SymbolicName}} = {{Tokens.Identifier}};

            """);

        /// <summary>
        /// NodeId declaration with namespace URI
        /// </summary>
        public static readonly TemplateString NodeIdDeclarationAbsolute = TemplateString.Parse(
            $$"""
            public static readonly global::Opc.Ua.ExpandedNodeId {{Tokens.SymbolicName}} =
                new global::Opc.Ua.ExpandedNodeId({{Tokens.NamespacePrefix}}.{{Tokens.NodeClass}}s.{{Tokens.SymbolicName}}, {{Tokens.NamespaceUri}});

            """);

        /// <summary>
        /// NodeId declaration
        /// </summary>
        public static readonly TemplateString NodeIdDeclaration = TemplateString.Parse(
            $$"""
            public static global::Opc.Ua.NodeId {{Tokens.SymbolicName}} =>
                new global::Opc.Ua.NodeId({{Tokens.NamespacePrefix}}.{{Tokens.NodeClass}}s.{{Tokens.SymbolicName}});

            """);

        /// <summary>
        /// Reflection methods for identifiers
        /// </summary>
        public static readonly TemplateString Reflection = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Returns the browse names for all {{Tokens.ClassName}}
            /// </summary>
            public static global::System.Collections.Generic.IEnumerable<string> BrowseNames
                => s_nameToId.Value.Keys;

            /// <summary>
            /// Returns the ids for all {{Tokens.ClassName}}.
            /// </summary>
            public static global::System.Collections.Generic.IEnumerable<{{Tokens.IdType}}> Identifiers
                => s_idToName.Value.Keys;

            /// <summary>
            /// Returns the browse name for a {{Tokens.ClassName}} id.
            /// </summary>
            public static string GetBrowseName({{Tokens.IdType}} identifier)
            {
                return s_idToName.Value.TryGetValue(identifier, out string name) ?  name : string.Empty;
            }

            /// <summary>
            /// Returns the id for a {{Tokens.ClassName}} string.
            /// </summary>
            public static {{Tokens.IdType}} GetIdentifier(string browseName)
            {
                return s_nameToId.Value.TryGetValue(browseName, out {{Tokens.IdType}} id) ? id : 0;
            }

            /// <summary>
            /// Returns the browse name for a {{Tokens.ClassName}} id.
            /// </summary>
            public static bool TryGetBrowseName(
                {{Tokens.IdType}} identifier,
                /*[global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]*/ out string? name)
            {
                return s_idToName.Value.TryGetValue(identifier, out name);
            }

            /// <summary>
            /// Returns the id for a {{Tokens.ClassName}} string.
            /// </summary>
            public static bool TryGetIdentifier(string browseName, out {{Tokens.IdType}} id)
            {
                return s_nameToId.Value.TryGetValue(browseName, out id);
            }

            /// <summary>
            /// Lazy id to name lookup.
            /// </summary>
            private static readonly global::System.Lazy<
                global::System.Collections.Generic.IReadOnlyDictionary<{{Tokens.IdType}}, string>> s_idToName =
                new global::System.Lazy<
                    global::System.Collections.Generic.IReadOnlyDictionary<{{Tokens.IdType}}, string>>(() =>
                {
                    var lookup = new global::System.Collections.Generic.Dictionary<{{Tokens.IdType}}, string>();
                    {{Tokens.ListOfIdentifersToNames}}
            #if NET8_0_OR_GREATER
                    return global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(lookup);
            #else
                    return new global::System.Collections.ObjectModel.ReadOnlyDictionary<{{Tokens.IdType}}, string>(lookup);
            #endif
                });

            /// <summary>
            /// Lazy name to id lookup.
            /// </summary>
            private static readonly global::System.Lazy<
                global::System.Collections.Generic.IReadOnlyDictionary<string, {{Tokens.IdType}}>> s_nameToId =
                new  global::System.Lazy<
                    global::System.Collections.Generic.IReadOnlyDictionary<string, {{Tokens.IdType}}>>(() =>
                {
                    var lookup = new global::System.Collections.Generic.Dictionary<string, {{Tokens.IdType}}>();
                    {{Tokens.ListOfNamesToIdentifiers}}
            #if NET8_0_OR_GREATER
                    return global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(lookup);
            #else
                    return new global::System.Collections.ObjectModel.ReadOnlyDictionary<string, {{Tokens.IdType}}>(lookup);
            #endif
                });
            """);
    }
}
