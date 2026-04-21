/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
    /// Templates used by <see cref="ModelDependencyGenerator"/> to emit the
    /// per-assembly <c>[assembly: Opc.Ua.ModelDependencyAttribute(...)]</c>
    /// closure file that downstream source-generator consumers scan to
    /// discover referenced models without re-walking AdditionalFiles.
    /// </summary>
    internal static class ModelDependencyTemplates
    {
        /// <summary>
        /// File shell. Hosts the shared code header followed by one Entry
        /// template invocation per unique model dependency.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            // Lists every OPC UA model the assembly emits or transitively consumes.
            // Generated from the model design and the closure of referenced assemblies.

            {{Tokens.ListOfModelDependencies}}
            """);

        /// <summary>
        /// One assembly-attribute line. The Version and PublicationDate
        /// replacements are pre-formatted by the generator as either the
        /// literal token <c>null</c> or a quoted string, so that the template
        /// itself does not need to encode that decision.
        /// </summary>
        public static readonly TemplateString Entry = TemplateString.Parse(
            $$"""
            [assembly: global::Opc.Ua.ModelDependencyAttribute("{{Tokens.ModelUri}}", "{{Tokens.Prefix}}", {{Tokens.ModelVersion}}, {{Tokens.ModelPublicationDate}})]

            """);
    }
}
