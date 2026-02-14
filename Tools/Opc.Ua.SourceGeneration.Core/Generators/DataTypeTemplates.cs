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
    internal static class DataTypeTemplates
    {
        /// <summary>
        /// Base data type and node state files
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            {{Tokens.ListOfImports}}

            namespace {{Tokens.NamespacePrefix}}
            {
                {{Tokens.ListOfTypes}}

                {{Tokens.ListOfTypeActivators}}

                /// <summary>
                /// Data type definitions for all classes in the {{Tokens.NamespaceUri}} namespace.
                /// </summary>
                [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                public static partial class DataTypeDefinitions
                {
                    {{Tokens.ListOfDataTypeDefinitions}}
                }

                /// <summary>
                /// Extensions that add functionality from the {{Tokens.NamespaceUri}} namespace.
                /// </summary>
                public static partial class {{Tokens.Namespace}}Extensions
                {
                    /// <summary>
                    /// Adds all encodeables of the {{Tokens.NamespaceUri}} namespace to the
                    /// IEncodeableFactoryBuilder.
                    /// </summary>
                    /// <param name="builder">The factory builder.</param>
                    /// <returns>The factory builder passed as parameter.</returns>
                    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
                    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
                    public static global::Opc.Ua.IEncodeableFactoryBuilder Add{{Tokens.Namespace}}(
                        this global::Opc.Ua.IEncodeableFactoryBuilder builder)
                    {
                        {{Tokens.ListOfActivatorRegistrations}}

                        return builder;
                    }
                }
            }
            """);

        /// <summary>
        /// Structure definition declaration
        /// </summary>
        public static readonly TemplateString StructureDefinition = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The structure definition for the {{Tokens.BrowseName}} DataType.
            /// </summary>
            public static global::Opc.Ua.StructureDefinition Create{{Tokens.ClassName}}(
                global::Opc.Ua.NamespaceTable namespaceUris)
            {
                return new global::Opc.Ua.StructureDefinition
                {
                    BaseDataType = {{Tokens.BaseType}},
                    StructureType = global::Opc.Ua.StructureType.{{Tokens.StructureType}},
                    FirstExplicitFieldIndex = {{Tokens.FirstExplicitFieldIndex}},
                    Fields = new global::Opc.Ua.StructureField[]
                    {
                        {{Tokens.ListOfFields}}
                    }
                };
            }
            """);

        /// <summary>
        /// Structure definition field
        /// </summary>
        public static readonly TemplateString StructureField = TemplateString.Parse(
            $$"""
            new global::Opc.Ua.StructureField
            {
                Name = {{Tokens.FieldName}},
                DataType = {{Tokens.DataType}},
                ValueRank = {{Tokens.ValueRank}},
                ArrayDimensions = {{Tokens.ArrayDimensions}},
                IsOptional = {{Tokens.IsOptional}},
                Description = {{Tokens.Description}}
            },
            """);

        /// <summary>
        /// Enum definition
        /// </summary>
        public static readonly TemplateString EnumDefinition = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The enum definition for the {{Tokens.BrowseName}} DataType.
            /// </summary>
            public static global::Opc.Ua.EnumDefinition Create{{Tokens.ClassName}}(
                global::Opc.Ua.NamespaceTable namespaceUris)
            {
                return new global::Opc.Ua.EnumDefinition
                {
                    IsOptionSet = {{Tokens.IsOptionSet}},
                    Fields = new global::Opc.Ua.EnumField[]
                    {
                        {{Tokens.ListOfFields}}
                    }
                };
            }
            """);

        /// <summary>
        /// Enum field
        /// </summary>
        public static readonly TemplateString EnumField = TemplateString.Parse(
            $$"""
            new global::Opc.Ua.EnumField
            {
                Name = {{Tokens.FieldName}},
                DisplayName = {{Tokens.DisplayName}},
                Value = {{Tokens.ValueCode}},
                Description = {{Tokens.Description}}
            },
            """);

        /// <summary>
        /// Encodeable activator
        /// </summary>
        public static readonly TemplateString StructureActivatorClass = TemplateString.Parse(
            $$"""
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            public sealed class {{Tokens.ClassName}}Activator : global::Opc.Ua.EncodeableType<{{Tokens.ClassName}}>
            {
                /// <summary>
                /// The singleton instance of the activator.
                /// </summary>
                public static readonly {{Tokens.ClassName}}Activator Instance
                    = new {{Tokens.ClassName}}Activator();

                /// <inheritdoc/>
                public override global::Opc.Ua.IEncodeable CreateInstance()
                {
                    return new {{Tokens.ClassName}}();
                }
            }
            """);

        /// <summary>
        /// Encodeable activator builder registration
        /// </summary>
        public static readonly TemplateString StructureActivatorRegistration = TemplateString.Parse(
            $$"""
            // Add encodeable activator for {{Tokens.BrowseName}}
            builder = builder
                .AddEncodeableType(DataTypeIds.{{Tokens.BrowseName}}, {{Tokens.ClassName}}Activator.Instance)
                .AddEncodeableType({{Tokens.BinaryEncodingId}}, {{Tokens.ClassName}}Activator.Instance)
                .AddEncodeableType({{Tokens.XmlEncodingId}}, {{Tokens.ClassName}}Activator.Instance)
                .AddEncodeableType({{Tokens.JsonEncodingId}}, {{Tokens.ClassName}}Activator.Instance);
            """);

        /// <summary>
        /// A union data type
        /// </summary>
        public static readonly TemplateString UnionClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The field bitmask for the {{Tokens.ClassName}} class.
            /// </summary>
            public enum {{Tokens.ClassName}}Fields : uint
            {
                None = 0,
                {{Tokens.ListOfSwitchFields}}
            }

            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})]
            public partial class {{Tokens.ClassName}} : global::Opc.Ua.IEncodeable, global::Opc.Ua.IJsonEncodeable
            {
                /// <summary>
                /// The default constructor.
                /// </summary>
                public {{Tokens.ClassName}}()
                {
                    Initialize();
                }

                /// <inheritdoc/>
                [global::System.Runtime.Serialization.OnDeserializing]
                private void Initialize(global::System.Runtime.Serialization.StreamingContext context)
                {
                    Initialize();
                }

                /// <summary>
                /// Initialize the object.
                /// </summary>
                private void Initialize()
                {
                    SwitchField = {{Tokens.ClassName}}Fields.None;
                    {{Tokens.ListOfFieldInitializers}}
                }

                /// <summary>
                /// The switch field for the union.
                /// </summary>
                [global::System.Runtime.Serialization.DataMember(Name = "SwitchField", IsRequired = true, Order = 0)]
                public {{Tokens.ClassName}}Fields SwitchField { get; set; }

                {{Tokens.ListOfProperties}}

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId TypeId => DataTypeIds.{{Tokens.BrowseName}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId BinaryEncodingId => {{Tokens.BinaryEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId XmlEncodingId => {{Tokens.XmlEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId JsonEncodingId => {{Tokens.JsonEncodingId}};

                /// <inheritdoc/>
                public virtual void Encode(global::Opc.Ua.IEncoder encoder)
                {
                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    encoder.WriteSwitchField((uint)SwitchField, out var fieldName);

                    switch (SwitchField)
                    {
                        {{Tokens.ListOfEncodedFields}}
                        default:
                        {
                            break;
                        }
                    }

                    encoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual void Decode(global::Opc.Ua.IDecoder decoder)
                {
                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    SwitchField = ({{Tokens.ClassName}}Fields)decoder.ReadSwitchField(
                        m_FieldNames,
                        out var fieldName);

                    switch (SwitchField)
                    {
                        {{Tokens.ListOfDecodedFields}}
                        default:
                        {
                            break;
                        }
                    }

                    decoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual bool IsEqual(global::Opc.Ua.IEncodeable encodeable)
                {
                    if (object.ReferenceEquals(this, encodeable))
                    {
                        return true;
                    }

                    {{Tokens.BrowseName}}? value = encodeable as {{Tokens.BrowseName}};

                    if (value == null)
                    {
                        return false;
                    }

                    if (value.SwitchField != this.SwitchField) return false;

                    switch (SwitchField)
                    {
                        {{Tokens.ListOfComparedFields}}
                        default:
                        {
                            break;
                        }
                    }

                    return true;
                }

                /// <inheritdoc/>
                public virtual object Clone()
                {
                    return ({{Tokens.ClassName}})this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}} clone = ({{Tokens.ClassName}})base.MemberwiseClone();

                    clone.SwitchField = this.SwitchField;

                    switch (SwitchField)
                    {
                        {{Tokens.ListOfClonedFields}}
                        default:
                        {
                            break;
                        }
                    }

                    return clone;
                }

                {{Tokens.ListOfFields}}

                private static readonly string[] m_FieldNames = new string[]
                {
                    {{Tokens.ListOfSwitchFieldNames}}
                };
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Derived data type with optional fields
        /// </summary>
        public static readonly TemplateString DerivedClassWithOptionalFields = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The field bitmask for the {{Tokens.ClassName}} class.
            /// </summary>
            [global::System.Flags]
            public enum {{Tokens.ClassName}}Fields : uint
            {
                None = 0,
                {{Tokens.ListOfEncodingMaskFields}}
            }

            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})]
            public partial class {{Tokens.ClassName}} : {{Tokens.BaseType}}
            {
                /// <summary>
                /// The default constructor.
                /// </summary>
                public {{Tokens.ClassName}}()
                {
                    Initialize();
                }

                /// <inheritdoc/>
                [global::System.Runtime.Serialization.OnDeserializing]
                private void Initialize(global::System.Runtime.Serialization.StreamingContext context)
                {
                    Initialize();
                }

                /// <summary>
                /// Initialize the object.
                /// </summary>
                private void Initialize()
                {
                    {{Tokens.ListOfFieldInitializers}}
                }

                {{Tokens.ListOfProperties}}

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId TypeId => DataTypeIds.{{Tokens.BrowseName}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId BinaryEncodingId => {{Tokens.BinaryEncodingId}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId XmlEncodingId  => {{Tokens.XmlEncodingId}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId JsonEncodingId  => {{Tokens.JsonEncodingId}};

                /// <inheritdoc/>
                public override void Encode(global::Opc.Ua.IEncoder encoder)
                {
                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    encoder.WriteEncodingMask((uint)EncodingMask);
                    encoder.PopNamespace();

                    base.Encode(encoder);

                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfEncodedFields}}
                    encoder.PopNamespace();
                }

                /// <inheritdoc/>
                public override void Decode(global::Opc.Ua.IDecoder decoder)
                {
                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    EncodingMask = decoder.ReadEncodingMask(m_FieldNames);
                    decoder.PopNamespace();

                    base.Decode(decoder);

                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfDecodedFields}}
                    decoder.PopNamespace();
                }

                /// <inheritdoc/>
                public override bool IsEqual(global::Opc.Ua.IEncodeable encodeable)
                {
                    if (object.ReferenceEquals(this, encodeable))
                    {
                        return true;
                    }

                    {{Tokens.BrowseName}}? value = encodeable as {{Tokens.BrowseName}};

                    if (value == null)
                    {
                        return false;
                    }

                    {{Tokens.ListOfComparedFields}}

                    return base.IsEqual(encodeable);
                }

                /// <inheritdoc/>
                public override object Clone()
                {
                    return ({{Tokens.ClassName}})this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}} clone = ({{Tokens.ClassName}})base.MemberwiseClone();
                    {{Tokens.ListOfClonedFields}}
                    return clone;
                }

                {{Tokens.ListOfFields}}

                private static readonly string[] m_FieldNames = new string[]
                {
                    {{Tokens.ListOfEncodingMaskFieldNames}}
                };
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Class with optional fields
        /// </summary>
        public static readonly TemplateString ClassWithOptionalFields = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The field bitmask for the {{Tokens.ClassName}} class.
            /// </summary>
            [global::System.Flags]
            public enum {{Tokens.ClassName}}Fields : uint
            {
                None = 0,
                {{Tokens.ListOfEncodingMaskFields}}
            }

            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})]
            public partial class {{Tokens.ClassName}} : global::Opc.Ua.IEncodeable, global::Opc.Ua.IJsonEncodeable
            {
                /// <summary>
                /// The default constructor.
                /// </summary>
                public {{Tokens.ClassName}}()
                {
                    Initialize();
                }

                /// <inheritdoc/>
                [global::System.Runtime.Serialization.OnDeserializing]
                private void Initialize(global::System.Runtime.Serialization.StreamingContext context)
                {
                    Initialize();
                }

                /// <summary>
                /// Initialize the object.
                /// </summary>
                private void Initialize()
                {
                    EncodingMask = (uint){{Tokens.ClassName}}Fields.None;
                    {{Tokens.ListOfFieldInitializers}}
                }

                /// <summary>
                /// The encoding mask for the optional fields.
                /// </summary>
                [global::System.Runtime.Serialization.DataMember(Name = "EncodingMask", IsRequired = true, Order = 0)]
                public virtual uint EncodingMask { get; set; }

                {{Tokens.ListOfProperties}}

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId TypeId => DataTypeIds.{{Tokens.BrowseName}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId BinaryEncodingId => {{Tokens.BinaryEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId XmlEncodingId  => {{Tokens.XmlEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId JsonEncodingId => {{Tokens.JsonEncodingId}};

                /// <inheritdoc/>
                public virtual void Encode(global::Opc.Ua.IEncoder encoder)
                {
                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    encoder.WriteEncodingMask((uint)EncodingMask);
                    {{Tokens.ListOfEncodedFields}}
                    encoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual void Decode(global::Opc.Ua.IDecoder decoder)
                {
                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    EncodingMask = decoder.ReadEncodingMask(m_FieldNames);
                    {{Tokens.ListOfDecodedFields}}
                    decoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual bool IsEqual(global::Opc.Ua.IEncodeable encodeable)
                {
                    if (object.ReferenceEquals(this, encodeable))
                    {
                        return true;
                    }

                    {{Tokens.BrowseName}}? value = encodeable as {{Tokens.BrowseName}};

                    if (value == null)
                    {
                        return false;
                    }

                    if (value.EncodingMask != this.EncodingMask) return false;

                    {{Tokens.ListOfComparedFields}}

                    return true;
                }

                /// <inheritdoc/>
                public virtual object Clone()
                {
                    return ({{Tokens.ClassName}})this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}} clone = ({{Tokens.ClassName}})base.MemberwiseClone();
                    clone.EncodingMask = this.EncodingMask;
                    {{Tokens.ListOfClonedFields}}
                    return clone;
                }

                {{Tokens.ListOfFields}}

                private static readonly string[] m_FieldNames = new string[]
                {
                    {{Tokens.ListOfEncodingMaskFieldNames}}
                };
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Data type class
        /// </summary>
        public static readonly TemplateString Class = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})]
            public {{Tokens.IsAbstract}}partial class {{Tokens.ClassName}} : global::Opc.Ua.IEncodeable, global::Opc.Ua.IJsonEncodeable
            {
                /// <summary>
                /// The default constructor.
                /// </summary>
                public {{Tokens.ClassName}}()
                {
                    Initialize();
                }

                /// <inheritdoc/>
                [global::System.Runtime.Serialization.OnDeserializing]
                private void Initialize(global::System.Runtime.Serialization.StreamingContext context)
                {
                    Initialize();
                }

                /// <summary>
                /// Initialize the object.
                /// </summary>
                private void Initialize()
                {
                    {{Tokens.ListOfFieldInitializers}}
                }

                {{Tokens.ListOfProperties}}

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId TypeId => DataTypeIds.{{Tokens.BrowseName}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId BinaryEncodingId => {{Tokens.BinaryEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId XmlEncodingId => {{Tokens.XmlEncodingId}};

                /// <inheritdoc/>
                public virtual global::Opc.Ua.ExpandedNodeId JsonEncodingId => {{Tokens.JsonEncodingId}};

                /// <inheritdoc/>
                public virtual void Encode(global::Opc.Ua.IEncoder encoder)
                {
                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfEncodedFields}}
                    encoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual void Decode(global::Opc.Ua.IDecoder decoder)
                {
                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfDecodedFields}}
                    decoder.PopNamespace();
                }

                /// <inheritdoc/>
                public virtual bool IsEqual(global::Opc.Ua.IEncodeable encodeable)
                {
                    if (object.ReferenceEquals(this, encodeable))
                    {
                        return true;
                    }

                    {{Tokens.BrowseName}}? value = encodeable as {{Tokens.BrowseName}};

                    if (value == null)
                    {
                        return false;
                    }

                    {{Tokens.ListOfComparedFields}}

                    return true;
                }

                /// <inheritdoc/>
                public virtual object Clone()
                {
                    return ({{Tokens.ClassName}})this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}} clone = ({{Tokens.ClassName}})base.MemberwiseClone();
                    {{Tokens.ListOfClonedFields}}
                    return clone;
                }

                {{Tokens.ListOfFields}}
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Derived data type class
        /// </summary>
        public static readonly TemplateString DerivedClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})]
            public partial class {{Tokens.ClassName}} : {{Tokens.BaseType}}
            {
                /// <summary>
                /// The default constructor.
                /// </summary>
                public {{Tokens.ClassName}}()
                {
                    Initialize();
                }

                /// <inheritdoc/>
                [global::System.Runtime.Serialization.OnDeserializing]
                private void Initialize(global::System.Runtime.Serialization.StreamingContext context)
                {
                    Initialize();
                }

                /// <summary>
                /// Initialize the object.
                /// </summary>
                private void Initialize()
                {
                    {{Tokens.ListOfFieldInitializers}}
                }

                {{Tokens.ListOfProperties}}

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId TypeId => DataTypeIds.{{Tokens.BrowseName}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId BinaryEncodingId => {{Tokens.BinaryEncodingId}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId XmlEncodingId => {{Tokens.XmlEncodingId}};

                /// <inheritdoc/>
                public override global::Opc.Ua.ExpandedNodeId JsonEncodingId => {{Tokens.JsonEncodingId}};

                /// <inheritdoc/>
                public override void Encode(global::Opc.Ua.IEncoder encoder)
                {
                    base.Encode(encoder);
                    encoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfEncodedFields}}
                    encoder.PopNamespace();
                }

                /// <inheritdoc/>
                public override void Decode(global::Opc.Ua.IDecoder decoder)
                {
                    base.Decode(decoder);
                    decoder.PushNamespace({{Tokens.XmlNamespaceUri}});
                    {{Tokens.ListOfDecodedFields}}
                    decoder.PopNamespace();
                }

                /// <inheritdoc/>
                public override bool IsEqual(global::Opc.Ua.IEncodeable encodeable)
                {
                    if (object.ReferenceEquals(this, encodeable))
                    {
                        return true;
                    }

                    {{Tokens.BrowseName}}? value = encodeable as {{Tokens.BrowseName}};

                    if (value == null)
                    {
                        return false;
                    }

                    {{Tokens.ListOfComparedFields}}

                    return base.IsEqual(encodeable);
                }

                /// <inheritdoc/>
                public override object Clone()
                {
                    return ({{Tokens.ClassName}})this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}} clone = ({{Tokens.ClassName}})base.MemberwiseClone();
                    {{Tokens.ListOfClonedFields}}
                    return clone;
                }

                {{Tokens.ListOfFields}}
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Enum data type
        /// </summary>
        public static readonly TemplateString Enumeration = TemplateString.Parse(
            $$"""
            /// <summary>
            /// The {{Tokens.BrowseName}} DataType.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Runtime.Serialization.DataContract(Namespace = {{Tokens.XmlNamespaceUri}})] {{Tokens.Flags}}
            public enum {{Tokens.ClassName}}{{Tokens.BasicType}}
            {
                {{Tokens.ListOfProperties}}
            }

            {{Tokens.CollectionClass}}

            """);

        /// <summary>
        /// Collection class for data types
        /// </summary>
        public static readonly TemplateString CollectionClass = TemplateString.Parse(
            $$"""
            /// <summary>
            /// A collection of {{Tokens.ClassName}} objects.
            /// </summary>
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("{{Tokens.Tool}}", "{{Tokens.Version}}")]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
            [global::System.Runtime.Serialization.CollectionDataContract(
                Name = "ListOf{{Tokens.BrowseName}}",
                Namespace = {{Tokens.XmlNamespaceUri}},
                ItemName = "{{Tokens.BrowseName}}")]
            public partial class {{Tokens.ClassName}}Collection :
                global::System.Collections.Generic.List<{{Tokens.ClassName}}>,
                global::System.ICloneable
            {
                /// <inheritdoc/>
                public {{Tokens.ClassName}}Collection()
                {
                }

                /// <inheritdoc/>
                public {{Tokens.ClassName}}Collection(int capacity)
                    : base(capacity)
                {
                }

                /// <inheritdoc/>
                public {{Tokens.ClassName}}Collection(
                    global::System.Collections.Generic.IEnumerable<{{Tokens.ClassName}}> collection)
                    : base(collection)
                {
                }

                /// <inheritdoc/>
                public static implicit operator {{Tokens.ClassName}}Collection({{Tokens.ClassName}}[]? values)
                {
                    return To{{Tokens.ClassName}}Collection(values);
                }

                /// <inheritdoc/>
                public static {{Tokens.ClassName}}Collection To{{Tokens.ClassName}}Collection({{Tokens.ClassName}}[]? values)
                {
                    if (values != null)
                    {
                        return new {{Tokens.ClassName}}Collection(values);
                    }
                    return new {{Tokens.ClassName}}Collection();
                }

                /// <inheritdoc/>
                public static explicit operator {{Tokens.ClassName}}[]?({{Tokens.ClassName}}Collection? values)
                {
                    return From{{Tokens.ClassName}}Collection(values);
                }

                /// <inheritdoc/>
                public static {{Tokens.ClassName}}[]? From{{Tokens.ClassName}}Collection({{Tokens.ClassName}}Collection? values)
                {
                    if (values != null)
                    {
                        return values.ToArray();
                    }
                    return null;
                }

                /// <inheritdoc/>
                public object Clone()
                {
                    return ({{Tokens.ClassName}}Collection)this.MemberwiseClone();
                }

                /// <inheritdoc/>
                public new object MemberwiseClone()
                {
                    {{Tokens.ClassName}}Collection clone =
                        new {{Tokens.ClassName}}Collection(this.Count);

                    for (int ii = 0; ii < this.Count; ii++)
                    {
                        clone.Add(({{Tokens.ClassName}})global::Opc.Ua.CoreUtils.Clone(this[ii]));
                    }

                    return clone;
                }
            }
            """);

        /// <summary>
        /// Properties of data types
        /// </summary>
        public static readonly TemplateString ScalarProperty = TemplateString.Parse(
            $$"""
            /// <summary>
            /// {{Tokens.BrowseName}} property
            /// </summary>
            [global::System.Runtime.Serialization.DataMember(
                Name = "{{Tokens.BrowseName}}",
                IsRequired = {{Tokens.IsRequired}},
                EmitDefaultValue = {{Tokens.EmitDefaultValue}},
                Order = {{Tokens.FieldIndex}})]
            {{Tokens.AccessorSymbol}} {{Tokens.TypeName}} {{Tokens.BrowseName}}
            {
                get => {{Tokens.FieldName}};
                set => {{Tokens.FieldName}} = value;
            }

            """);

        /// <summary>
        /// Array Properties of data types
        /// </summary>
        public static readonly TemplateString ArrayProperty = TemplateString.Parse(
            $$"""
            /// <summary>
            /// {{Tokens.BrowseName}} property
            /// </summary>
            [global::System.Runtime.Serialization.DataMember(
                Name = "{{Tokens.BrowseName}}",
                IsRequired = {{Tokens.IsRequired}},
                EmitDefaultValue = {{Tokens.EmitDefaultValue}},
                Order = {{Tokens.FieldIndex}})]
            {{Tokens.AccessorSymbol}} {{Tokens.TypeName}} {{Tokens.BrowseName}}
            {
                get => {{Tokens.FieldName}};
                set => {{Tokens.FieldName}} = value == null ?
                    ({{Tokens.TypeName}}){{Tokens.DefaultValue}} :
                    value;
            }

            """);

        /// <summary>
        /// Enumeration value of enum data types
        /// </summary>
        public static readonly TemplateString EnumerationValue = TemplateString.Parse(
            $$"""
            /// <summary>
            /// {{Tokens.EnumerationName}} enumeration value ({{Tokens.Identifier}})
            /// </summary>
            [global::System.Runtime.Serialization.EnumMember(Value = "{{Tokens.XmlIdentifier}}")]
            {{Tokens.EnumerationName}} = {{Tokens.Identifier}},

            """);
    }
}
