/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Templates for the <see cref="EventRecordGenerator"/>. Emits one
    /// <c>partial record {Type}Record : {Parent}Record</c> per OPC UA
    /// <c>ObjectType</c> that derives from <c>BaseEventType</c>. Each
    /// record declares only the fields introduced by its type;
    /// inherited fields come from the parent record class. The
    /// generated chain mirrors the OPC UA event-type hierarchy.
    /// </summary>
    internal static class EventRecordTemplates
    {
        /// <summary>
        /// Single output file template.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #nullable enable

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfTypes}}
            }

            """);

        /// <summary>
        /// One record class per <c>ObjectType</c>. Generated as a
        /// <c>partial record</c> so consumers can add convenience
        /// properties without touching the generated source.
        /// </summary>
        public static readonly TemplateString RecordClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Decoded event record for OPC UA event type
            /// <c>{{Tokens.SymbolicName}}</c>. Add convenience members in
            /// a hand-written partial declaration of this same record.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public partial record {{Tokens.ClassName}} : {{Tokens.BaseClassName}}
            {
                {{Tokens.ListOfProperties}}
            }

            """);

        /// <summary>
        /// One init-only property per directly-declared field on the type.
        /// </summary>
        public static readonly TemplateString FieldProperty = TemplateString.Parse(
            $$"""

            /// <summary>{{Tokens.Description}}</summary>
            public {{Tokens.DataType}} {{Tokens.PropertyName}} { get; init; }
            """);
    }
}