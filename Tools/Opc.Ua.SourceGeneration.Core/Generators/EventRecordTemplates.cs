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
    /// Templates for the <see cref="EventRecordGenerator"/>. Emits one
    /// <c>partial record {Type}Record : {Parent}Record</c> per OPC UA
    /// <c>ObjectType</c> that derives from <c>BaseEventType</c>. Each
    /// record declares only the fields introduced by its type;
    /// inherited fields come from the parent record class. The
    /// generated chain mirrors the OPC UA event-type hierarchy.
    /// </summary>
    /// <remarks>
    /// In addition to the record class, each emitted record contains
    /// a nested <c>public static class Decoder</c> with a positional
    /// <c>StandardFields</c> browse-path table and a
    /// <c>Decode(IReadOnlyList&lt;Variant&gt;)</c> method that
    /// populates every property (own + inherited) from positional
    /// variant reads. A per-file
    /// <c>Register{ModelPrefix}Decoders(this EventRecordDecoderRegistry)</c>
    /// extension method registers every emitted decoder against a
    /// caller-supplied <c>Opc.Ua.EventRecordDecoderRegistry</c>.
    /// </remarks>
    internal static class EventRecordTemplates
    {
        /// <summary>
        /// Single output file template.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #nullable enable annotations
            #nullable disable warnings

            namespace {{Tokens.Namespace}}
            {
                {{Tokens.ListOfTypes}}

                {{Tokens.ListOfActivatorRegistrations}}
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

                /// <summary>
                /// Source-generated positional decoder for
                /// <see cref="{{Tokens.ClassName}}"/>. Reads variant fields
                /// at indices fixed by <see cref="StandardFields"/> and
                /// populates every own + inherited init-only property.
                /// </summary>
                public {{Tokens.AccessModifier}}static class Decoder
                {
                    /// <summary>
                    /// Browse paths in positional order. The composed
                    /// registry layout (and any filter built from it)
                    /// uses the same positional convention; the
                    /// runtime remaps from the composed positions
                    /// before calling <see cref="Decode"/>.
                    /// </summary>
                    public static readonly global::Opc.Ua.QualifiedName[][] StandardFields =
                    [
                        {{Tokens.ListOfFields}}
                    ];

                    /// <summary>
                    /// Decodes <paramref name="fields"/> into a
                    /// <see cref="{{Tokens.ClassName}}"/>.
                    /// </summary>
                    /// <param name="fields">Positional variant array
                    /// matching <see cref="StandardFields"/>.</param>
                    /// <returns><c>null</c> when
                    /// <paramref name="fields"/> is null or empty;
                    /// a fully populated record otherwise.</returns>
                    public static {{Tokens.ClassName}}? Decode(
                        global::System.Collections.Generic.IReadOnlyList<global::Opc.Ua.Variant> fields)
                    {
                        if (fields == null || fields.Count == 0)
                        {
                            return null;
                        }
                        return new {{Tokens.ClassName}}
                        {
                            {{Tokens.ListOfDecodedFields}}
                        };
                    }
                }

                /// <summary>
                /// Source-generated event-filter factory for
                /// <see cref="{{Tokens.ClassName}}"/>. Produces an
                /// <see cref="global::Opc.Ua.EventFilter"/> whose
                /// where clause restricts events to
                /// <c>{{Tokens.SymbolicName}}</c> and whose select
                /// clauses come from a supplied
                /// <see cref="global::Opc.Ua.EventRecordDecoderRegistry"/>
                /// (or its <c>Default</c>).
                /// </summary>
                public {{Tokens.AccessModifier}}static class EventFilters
                {
                    /// <summary>
                    /// Builds the event filter.
                    /// </summary>
                    /// <param name="registry">Decoder registry whose
                    /// composed <c>StandardFields</c> form the select
                    /// clauses. When <c>null</c>,
                    /// <see cref="global::Opc.Ua.EventRecordDecoderRegistry.Default"/>
                    /// is used.</param>
                    public static global::Opc.Ua.EventFilter Build(
                        global::Opc.Ua.EventRecordDecoderRegistry? registry = null)
                        => global::Opc.Ua.EventFilterFactory.Create(
                            global::Opc.Ua.ObjectTypeIds.{{Tokens.SymbolicName}},
                            registry);
                }
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

        /// <summary>
        /// One <c>QualifiedName[]</c> entry per StandardFields row.
        /// For TwoStateVariable children the path has two segments
        /// (the variable + its <c>Id</c> property).
        /// </summary>
        public static readonly TemplateString StandardFieldEntry = TemplateString.Parse(
            $$"""
            {{Tokens.ChildPath}},
            """);

        /// <summary>
        /// One property assignment per StandardFields position.
        /// </summary>
        public static readonly TemplateString DecodedField = TemplateString.Parse(
            $$"""
            {{Tokens.PropertyName}} = global::Opc.Ua.EventRecordFieldReaders.{{Tokens.ClientMethod}}(fields, {{Tokens.FieldIndex}}),
            """);

        /// <summary>
        /// Per-file static class that registers every emitted decoder
        /// onto a caller-supplied <c>Opc.Ua.EventRecordDecoderRegistry</c>.
        /// </summary>
        public static readonly TemplateString RegistrationExtension = TemplateString.Parse(
            $$"""
            /// <summary>
            /// Registration extensions for the source-generated event
            /// record decoders in this file. The
            /// <c>{{Tokens.ClientMethod}}</c> extension method is idempotent
            /// via <c>EventRecordDecoderRegistry.TryRegister</c> so
            /// duplicate calls are safe.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            public static class {{Tokens.ClassName}}
            {
                /// <summary>
                /// Registers every event-record decoder in this model
                /// with <paramref name="registry"/>. Returns the
                /// registry for chaining.
                /// </summary>
                public static global::Opc.Ua.EventRecordDecoderRegistry {{Tokens.ClientMethod}}(
                    this global::Opc.Ua.EventRecordDecoderRegistry registry)
                {
                    {{Tokens.ListOfActivatorRegistrations}}
                    return registry;
                }
            }
            """);

        /// <summary>
        /// One <c>TryRegister</c> call per emitted record decoder.
        /// </summary>
        public static readonly TemplateString DecoderRegistration = TemplateString.Parse(
            $$"""
            registry.TryRegister(
                global::Opc.Ua.ObjectTypeIds.{{Tokens.SymbolicName}},
                {{Tokens.ClassName}}.Decoder.StandardFields,
                {{Tokens.ClassName}}.Decoder.Decode);
            """);
    }
}
