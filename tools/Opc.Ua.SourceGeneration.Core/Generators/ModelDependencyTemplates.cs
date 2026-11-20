/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// One assembly-attribute line. The Version, PublicationDate, Name
        /// and Payload replacements are pre-formatted by the generator as
        /// either the literal token <c>null</c> or a quoted string, so that
        /// the template itself does not need to encode that decision. The
        /// Payload is non-null only on the self-declaration entry — it
        /// carries the base64 <c>ModelDependencyV1</c> blob downstream
        /// generators can use to resolve cross-assembly type references
        /// without re-adding upstream NodeSet2/ModelDesign XML.
        /// </summary>
        public static readonly TemplateString Entry = TemplateString.Parse(
            $$"""
            [assembly: global::Opc.Ua.ModelDependencyAttribute(
                "{{Tokens.ModelUri}}",
                "{{Tokens.Prefix}}",
                {{Tokens.ModelVersion}},
                {{Tokens.ModelPublicationDate}},
                {{Tokens.ModelName}},
                {{Tokens.ModelPayload}})]

            """);
    }
}
