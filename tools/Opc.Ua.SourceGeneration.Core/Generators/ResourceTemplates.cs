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
