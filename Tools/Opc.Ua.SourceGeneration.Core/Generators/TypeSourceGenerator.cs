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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates source code for [DataType]-annotated classes and enums
    /// using the template system shared with the model-based generators.
    /// </summary>
    internal static class TypeSourceGenerator
    {
        /// <summary>
        /// Validates the fields of a model and returns diagnostics for
        /// properties with unsupported types. Valid fields are returned
        /// in the out parameter.
        /// </summary>
        public static IReadOnlyList<TypeSourceGeneratorDiagnostic> ValidateAndFilter(
            TypeSourceModel model,
            out IReadOnlyList<TypeFieldModel> validFields)
        {
            var diagnostics = new List<TypeSourceGeneratorDiagnostic>();
            var valid = new List<TypeFieldModel>();

            foreach (TypeFieldModel field in model.Fields)
            {
                string resolvedType = field.IsArray || field.IsMatrix
                    ? field.ElementShortTypeName
                    : field.ShortTypeName;

                if (field.IsEncodeable || field.IsEnum)
                {
                    valid.Add(field);
                    continue;
                }

                if (resolvedType != null && s_scalarTypeMap.ContainsKey(resolvedType))
                {
                    valid.Add(field);
                    continue;
                }

                // Unsupported type
                bool isError = field.HasDataTypeFieldAttribute;
                diagnostics.Add(new TypeSourceGeneratorDiagnostic
                {
                    PropertyName = field.PropertyName,
                    TypeName = field.TypeName,
                    IsError = isError,
                    Message = $"Property '{field.PropertyName}' has unsupported type " +
                        $"'{field.ShortTypeName}'. Only OPC UA built-in types, " +
                        $"IEncodeable, enums, ArrayOf<T>, and MatrixOf<T> are supported."
                });
            }

            validFields = valid;
            return diagnostics;
        }

        /// <summary>
        /// Enable tests to collect the output as string
        /// </summary>
        public static string Generate(TypeSourceModel model)
        {
            ValidateAndFilter(model, out IReadOnlyList<TypeFieldModel> validFields);
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            var template = new Template(templateWriter, TypeSourceTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, model.Namespace);
            template.AddReplacement(Tokens.Namespace, model.NamespaceSymbol);
            template.AddReplacement(Tokens.NamespaceUri, model.NamespaceUri);
            template.AddReplacement(Tokens.AccessModifier,
                model.PublicExtensions ? "public" : "internal");

            if (model.IsEnum)
            {
                template.AddReplacement(
                    Tokens.ListOfTypeActivators,
                    DataTypeTemplates.EnumerationActivatorClass,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
                template.AddReplacement(
                    Tokens.ListOfActivatorRegistrations,
                    TypeSourceTemplates.SourceEnumActivatorRegistration,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
            }
            else
            {
                template.AddReplacement(
                    Tokens.ListOfTypes,
                    [model with { Fields = validFields }],
                    LoadTemplate_ListOfPartialClasses,
                    WriteTemplate_ListOfPartialClasses);

                template.AddReplacement(
                    Tokens.ListOfTypeActivators,
                    DataTypeTemplates.StructureActivatorClass,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
                template.AddReplacement(
                    Tokens.ListOfActivatorRegistrations,
                    TypeSourceTemplates.SourceActivatorRegistration,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
            }

            template.Render();
            return stringWriter.ToString();
        }

        /// <summary>
        /// Generate a single file containing all types from the same namespace.
        /// Produces one extension method with all registrations combined.
        /// </summary>
        public static string GenerateBatch(
            string ns,
            string nsSymbol,
            string nsUri,
            bool publicExtensions,
            IReadOnlyList<TypeSourceModel> allTypes,
            IReadOnlyList<TypeSourceModel> allActivators)
        {
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            var template = new Template(
                templateWriter, TypeSourceTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, ns);
            template.AddReplacement(Tokens.Namespace, nsSymbol);
            template.AddReplacement(Tokens.NamespaceUri, nsUri);
            template.AddReplacement(Tokens.AccessModifier,
                publicExtensions ? "public" : "internal");

            template.AddReplacement(
                Tokens.ListOfTypes,
                allTypes,
                LoadTemplate_ListOfPartialClasses,
                WriteTemplate_ListOfPartialClasses);
            template.AddReplacement(
                Tokens.ListOfTypeActivators,
                allActivators,
                LoadTemplate_ListOfTypeActivators,
                WriteTemplate_ListOfTypeActivators);
            template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                allActivators,
                LoadTemplate_ListOfActivatorRegistrations,
                WriteTemplate_ListOfTypeActivators);

            template.Render();
            return stringWriter.ToString();
        }

        private static TemplateString LoadTemplate_ListOfPartialClasses(ILoadContext context)
        {
            if (context.Target is not TypeSourceModel ctx ||
                ctx.IsEnum)
            {
                return null;
            }

            if (ctx.IsRecord)
            {
                if (ctx.IsDerived)
                {
                    return TypeSourceTemplates.DerivedRecordPartialClassBody;
                }
                return ctx.IsSealed
                    ? TypeSourceTemplates.SealedRecordPartialClassBody
                    : TypeSourceTemplates.RecordPartialClassBody;
            }

            if (ctx.IsDerived)
            {
                return TypeSourceTemplates.DerivedPartialClassBody;
            }

            return ctx.IsSealed
                ? TypeSourceTemplates.SealedPartialClassBody
                : TypeSourceTemplates.PartialClassBody;
        }

        private static bool WriteTemplate_ListOfPartialClasses(IWriteContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return false;
            }

            string typeIdExpr = FormatExpandedNodeIdExpression(
                model.DataTypeId, model.ClassName, model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId, model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId, model.NamespaceUri);

            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddReplacement(Tokens.BrowseName, model.ClassName);
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"\"{model.NamespaceUri.Escape()}\"");
            context.Template.AddReplacement(Tokens.AccessModifier,
                model.IsInternal ? "internal" : "public");

            context.Template.AddReplacement(
                Tokens.ListOfEncodedFields,
                model.Fields,
                LoadTemplate_ListOfEncodedFields);
            context.Template.AddReplacement(
                Tokens.ListOfDecodedFields,
                model.Fields,
                LoadTemplate_ListOfDecodedFields);
            context.Template.AddReplacement(
                Tokens.ListOfComparedFields,
                model.Fields,
                LoadTemplate_ListOfComparedFields);

            context.Template.AddReplacement(
                Tokens.ListOfInitOnlyBackingFields,
                model.Fields.Where(f => f.IsInitOnly).ToList(),
                LoadTemplate_ListOfInitOnlyBackingFields);

            context.Template.AddReplacement(
                Tokens.ListOfChildCopies,
                [model],
                LoadTemplate_ListOfChildCopies,
                WriteTemplate_ListOfChildCopies);

            return context.Template.Render();
        }

        private static TemplateString LoadTemplate_ListOfChildCopies(ILoadContext context)
        {
            if (context.Target is not TypeSourceModel model ||
                model.HasManualClone)
            {
                return null;
            }
            // Generate Clone/MemberwiseClone unless the user already has them
            if (model.IsRecord)
            {
                return TypeSourceTemplates.RecordCloneMethod;
            }
            return TypeSourceTemplates.CloneMethod;
        }

        private static bool WriteTemplate_ListOfChildCopies(IWriteContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddReplacement(Tokens.AccessModifier,
                model.IsDerived ? "override" :
                    model.IsSealed ? string.Empty : "virtual");
            context.Template.AddReplacement(
                Tokens.ListOfClonedFields,
                model.Fields,
                LoadTemplate_ListOfClonedFields);

            return context.Template.Render();
        }

        private static TemplateString LoadTemplate_ListOfEncodedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }

            (string writeMethod, string _) = ResolveEncoderDecoder(field);
            if (writeMethod == null)
            {
                return null;
            }

            string encodeLine;
            if (field.IsEncodeable)
            {
                bool useExtObj = ShouldUseExtensionObject(field);
                if (field.IsArray)
                {
                    string method = useExtObj
                        ? "WriteEncodeableArrayAsExtensionObjects"
                        : "WriteEncodeableArray";
                    encodeLine = CoreUtils.Format(
                        "encoder.{0}(\"{1}\", {2});",
                        method,
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
                else
                {
                    string scalarMethod = useExtObj
                        ? "WriteEncodeableAsExtensionObject"
                        : "WriteEncodeable";
                    encodeLine = CoreUtils.Format(
                        "encoder.{0}(\"{1}\", {2});",
                        scalarMethod,
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
            }
            else if (field.IsEnum)
            {
                if (field.IsArray)
                {
                    encodeLine = CoreUtils.Format(
                        """encoder.WriteEnumeratedArray("{0}", {1});""",
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
                else
                {
                    encodeLine = CoreUtils.Format(
                        """encoder.WriteEnumerated("{0}", {1});""",
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
            }
            else
            {
                encodeLine = CoreUtils.Format(
                    """encoder.{0}("{1}", {2});""",
                    writeMethod,
                    field.FieldName.Escape(),
                    field.PropertyName);
            }

            if ((field.DefaultValueHandling & 1) == 0)
            {
                encodeLine = CoreUtils.Format(
                    "if (!encoder.CanOmitFields || {0}) {1}",
                    GetNotDefaultCheck(field),
                    encodeLine);
            }

            context.Out.WriteLine(encodeLine);
            return null;
        }

        private static TemplateString LoadTemplate_ListOfDecodedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }

            (string _, string readMethod) = ResolveEncoderDecoder(field);
            if (readMethod == null)
            {
                return null;
            }

            // For init-only partial properties, assign to the backing
            // field directly since the init setter is not available
            // inside the Decode method body.
            string target = field.BackingFieldName ?? field.PropertyName;

            string decodeLine;
            if (field.IsEncodeable)
            {
                bool useExtObj = ShouldUseExtensionObject(field);
                if (field.IsArray)
                {
                    string method = useExtObj
                        ? "ReadEncodeableArrayAsExtensionObjects"
                        : "ReadEncodeableArray";
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.{1}<{2}>(\"{3}\");",
                        target,
                        method,
                        field.ElementTypeName,
                        field.FieldName.Escape());
                }
                else if (useExtObj)
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadEncodeableAsExtensionObject<{1}>(\"{2}\");",
                        target,
                        field.TypeName,
                        field.FieldName.Escape());
                }
                else
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadEncodeable<{1}>(\"{2}\");",
                        target,
                        field.TypeName,
                        field.FieldName.Escape());
                }
            }
            else if (field.IsEnum)
            {
                string typeName = field.IsArray ? field.ElementTypeName : field.TypeName;
                if (field.IsArray)
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadEnumeratedArray<{1}>(\"{2}\");",
                        target,
                        typeName,
                        field.FieldName.Escape());
                }
                else
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadEnumerated<{1}>(\"{2}\");",
                        target,
                        typeName,
                        field.FieldName.Escape());
                }
            }
            else
            {
                decodeLine = CoreUtils.Format(
                    "{0} = decoder.{1}(\"{2}\");",
                    target,
                    readMethod,
                    field.FieldName.Escape());
            }

            if ((field.DefaultValueHandling & 2) == 0)
            {
                decodeLine = CoreUtils.Format(
                    "if (decoder.HasField(\"{0}\")) {1}",
                    field.FieldName.Escape(),
                    decodeLine);
            }

            context.Out.WriteLine(decodeLine);
            return null;
        }

        private static TemplateString LoadTemplate_ListOfComparedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }

            if (!IsDotNetEqualityComparable(field))
            {
                context.Out.WriteLine(
                    "if (!global::Opc.Ua.CoreUtils.IsEqual({0}, value.{0})) return false;",
                    field.PropertyName);
            }
            else
            {
                context.Out.WriteLine(
                    "if ({0} != value.{0}) return false;",
                    field.PropertyName);
            }
            return null;

            static bool IsDotNetEqualityComparable(TypeFieldModel field) =>
                !field.IsEncodeable; // Add any other type not comparable using ==
        }

        private static TemplateString LoadTemplate_ListOfClonedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }

            if (NeedsCloning(field))
            {
                // For init-only properties use the backing field for assignment
                string target = field.BackingFieldName != null
                    ? $"clone.{field.BackingFieldName}"
                    : $"clone.{field.PropertyName}";
                context.Out.WriteLine(
                    "{0} = ({1})global::Opc.Ua.CoreUtils.Clone({2});",
                    target, field.TypeName, field.PropertyName);
            }
            else if (field.IsInitOnly)
            {
                // Record's 'with' already copies simple init-only fields
                return null;
            }
            else
            {
                context.Out.WriteLine(
                    "clone.{0} = {0};",
                    field.PropertyName);
            }
            return null;

            static bool NeedsCloning(TypeFieldModel field)
            {
                switch (field.ShortTypeName)
                {
                    case "DataValue":
                    case "Variant":
                    case "ExtensionObject":
                        return true;
                }
                if (field.IsArray || field.IsMatrix)
                {
                    return false;
                }
                return field.IsEncodeable;
            }
        }

        /// <summary>
        /// Emits backing fields and partial property implementations for
        /// init-only partial properties so that Decode() can assign
        /// to the backing field directly.
        /// </summary>
        private static TemplateString LoadTemplate_ListOfInitOnlyBackingFields(
            ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field ||
                !field.IsInitOnly ||
                field.BackingFieldName == null)
            {
                return null;
            }

            // For field declarations the global:: prefix on C# keyword
            // aliases (global::string, global::int etc.) is invalid.
            // Strip it for built-in aliases, keep it for everything else.
            string typeName = StripGlobalPrefixForAliases(field.TypeName);

            // Include the property initializer on the backing field if present,
            // so that default values from the defining declaration are preserved.
            string initializer = field.DefaultInitializer != null
                ? $" = {field.DefaultInitializer}"
                : string.Empty;

            context.Out.WriteLine(
                "private {0} {1}{2};",
                typeName,
                field.BackingFieldName,
                initializer);
            context.Out.WriteLine(
                "public partial {0} {1} {{ get => {2}; init => {2} = value; }}",
                typeName,
                field.PropertyName,
                field.BackingFieldName);

            return null;
        }

        /// <summary>
        /// Strips the global:: prefix from C# keyword type aliases
        /// that Roslyn FullyQualifiedFormat emits (e.g. global::string).
        /// Returns the input unchanged for non-alias types.
        /// </summary>
        private static string StripGlobalPrefixForAliases(string typeName)
        {
            // C# type keyword aliases that Roslyn emits with global:: prefix
            return typeName switch
            {
                "global::string" => "string",
                "global::int" => "int",
                "global::uint" => "uint",
                "global::long" => "long",
                "global::ulong" => "ulong",
                "global::short" => "short",
                "global::ushort" => "ushort",
                "global::byte" => "byte",
                "global::sbyte" => "sbyte",
                "global::float" => "float",
                "global::double" => "double",
                "global::bool" => "bool",
                "global::decimal" => "decimal",
                "global::object" => "object",
                "global::char" => "char",
                _ => typeName
            };
        }

        private static TemplateString LoadTemplate_ListOfTypeActivators(ILoadContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return null;
            }
            if (model.IsEnum)
            {
                return DataTypeTemplates.EnumerationActivatorClass;
            }
            return DataTypeTemplates.StructureActivatorClass;
        }

        private static TemplateString LoadTemplate_ListOfActivatorRegistrations(ILoadContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return null;
            }
            if (model.IsEnum)
            {
                return TypeSourceTemplates.SourceEnumActivatorRegistration;
            }
            return TypeSourceTemplates.SourceActivatorRegistration;
        }

        private static bool WriteTemplate_ListOfTypeActivators(IWriteContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return false;
            }

            string typeIdExpr = FormatExpandedNodeIdExpression(
                model.DataTypeId,
                model.ClassName,
                model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId,
                model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId,
                model.NamespaceUri);

            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddReplacement(Tokens.BrowseName, model.ClassName);
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"""
                "{model.NamespaceUri.Escape()}"
                """);

            return context.Template.Render();
        }

        /// <summary>
        /// Determines whether an IEncodeable field should be encoded as
        /// an ExtensionObject (allowing subtyping) or directly.
        /// </summary>
        internal static bool ShouldUseExtensionObject(TypeFieldModel field)
        {
            // Explicit override from [DataTypeField(StructureHandling = ...)]
            if (field.StructureHandling == 1) // Per data encoding
            {
                return false;
            }

            if (field.StructureHandling == 2) // As ExtensionObject
            {
                return true;
            }

            // Auto-detect: use WriteEncodeable if the type is sealed
            // and does not derive from another IEncodeable base type
            if (field.FieldTypeIsSealed && !field.FieldTypeHasEncodeableBase)
            {
                return false;
            }

            // Default: use ExtensionObject to allow subtyping
            return true;
        }

        /// <summary>
        /// Resolves the IEncoder/IDecoder method names for a field.
        /// Returns (writeMethod, readMethod) or (null, null) if unsupported.
        /// </summary>
        internal static (string writeMethod, string readMethod) ResolveEncoderDecoder(
            TypeFieldModel field)
        {
            if (field.IsEncodeable)
            {
                if (field.IsArray)
                {
                    return ("WriteEncodeableArray", "ReadEncodeableArray");
                }
                return ("WriteEncodeable", "ReadEncodeable");
            }

            if (field.IsEnum)
            {
                if (field.IsArray)
                {
                    return ("WriteEnumeratedArray", "ReadEnumeratedArray");
                }
                return ("WriteEnumerated", "ReadEnumerated");
            }

            string lookupType = field.IsArray || field.IsMatrix
                ? field.ElementShortTypeName
                : field.ShortTypeName;

            if (lookupType != null &&
                s_scalarTypeMap.TryGetValue(
                    lookupType, out (string write, string read) methods))
            {
                if (field.IsArray)
                {
                    return (methods.write + "Array", methods.read + "Array");
                }
                if (field.IsMatrix)
                {
                    return (methods.write + "Matrix", methods.read + "Matrix");
                }
                return methods;
            }

            return (null, null);
        }

        /// <summary>
        /// Maps scalar C# type short names to IEncoder/IDecoder method name pairs.
        /// Only these types (plus IEncodeable and enums) are allowed.
        /// </summary>
        internal static readonly Dictionary<string, (string write, string read)> s_scalarTypeMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Boolean"] = ("WriteBoolean", "ReadBoolean"),
                ["bool"] = ("WriteBoolean", "ReadBoolean"),
                ["SByte"] = ("WriteSByte", "ReadSByte"),
                ["Byte"] = ("WriteByte", "ReadByte"),
                ["Int16"] = ("WriteInt16", "ReadInt16"),
                ["short"] = ("WriteInt16", "ReadInt16"),
                ["UInt16"] = ("WriteUInt16", "ReadUInt16"),
                ["ushort"] = ("WriteUInt16", "ReadUInt16"),
                ["Int32"] = ("WriteInt32", "ReadInt32"),
                ["int"] = ("WriteInt32", "ReadInt32"),
                ["UInt32"] = ("WriteUInt32", "ReadUInt32"),
                ["uint"] = ("WriteUInt32", "ReadUInt32"),
                ["Int64"] = ("WriteInt64", "ReadInt64"),
                ["long"] = ("WriteInt64", "ReadInt64"),
                ["UInt64"] = ("WriteUInt64", "ReadUInt64"),
                ["ulong"] = ("WriteUInt64", "ReadUInt64"),
                ["Single"] = ("WriteFloat", "ReadFloat"),
                ["float"] = ("WriteFloat", "ReadFloat"),
                ["Double"] = ("WriteDouble", "ReadDouble"),
                ["String"] = ("WriteString", "ReadString"),
                ["DateTime"] = ("WriteDateTime", "ReadDateTime"),
                ["DateTimeUtc"] = ("WriteDateTime", "ReadDateTime"),
                ["Guid"] = ("WriteGuid", "ReadGuid"),
                ["Uuid"] = ("WriteGuid", "ReadGuid"),
                ["ByteString"] = ("WriteByteString", "ReadByteString"),
                ["NodeId"] = ("WriteNodeId", "ReadNodeId"),
                ["ExpandedNodeId"] = ("WriteExpandedNodeId", "ReadExpandedNodeId"),
                ["StatusCode"] = ("WriteStatusCode", "ReadStatusCode"),
                ["QualifiedName"] = ("WriteQualifiedName", "ReadQualifiedName"),
                ["LocalizedText"] = ("WriteLocalizedText", "ReadLocalizedText"),
                ["ExtensionObject"] = ("WriteExtensionObject", "ReadExtensionObject"),
                ["DataValue"] = ("WriteDataValue", "ReadDataValue"),
                ["Variant"] = ("WriteVariant", "ReadVariant"),
                ["DiagnosticInfo"] = ("WriteDiagnosticInfo", "ReadDiagnosticInfo"),
                ["XmlElement"] = ("WriteXmlElement", "ReadXmlElement"),
            };

        internal static readonly Dictionary<string, string> NotDefaultCheckExpression =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Boolean"] = "{0}",
                ["bool"] = "{0}",
                ["SByte"] = "{0} != (sbyte)0",
                ["Byte"] = "{0} != (byte)0",
                ["Int16"] = "{0} != (short)0",
                ["short"] = "{0} != (short)0",
                ["UInt16"] = "{0} != (ushort)0",
                ["ushort"] = "{0} != (ushort)0",
                ["Int32"] = "{0} != 0",
                ["int"] = "{0} != 0",
                ["UInt32"] = "{0} != 0u",
                ["uint"] = "{0} != 0u",
                ["Int64"] = "{0} != 0L",
                ["long"] = "{0} != 0L",
                ["UInt64"] = "{0} != 0UL",
                ["ulong"] = "{0} != 0UL",
                ["Single"] = "{0} != 0f",
                ["float"] = "{0} != 0f",
                ["Double"] = "{0} != 0.0",
                ["String"] = "!string.IsNullOrEmpty({0})",
                ["DateTime"] = "{0} != global::System.DateTime.MinValue",
                ["DateTimeUtc"] = "!{0}.IsNull",
                ["Guid"] = "{0} != global::System.Guid.Empty",
                ["Uuid"] = "{0} != global::Opc.Ua.Uuid.Empty",
                ["ByteString"] = "!{0}.IsNull",
                ["NodeId"] = "!{0}.IsNull",
                ["ExpandedNodeId"] = "!{0}.IsNull",
                ["StatusCode"] = "{0} != global::Opc.Ua.StatusCodes.Good",
                ["QualifiedName"] = "!{0}.IsNull",
                ["LocalizedText"] = "!{0}.IsNull",
                ["ExtensionObject"] = "!{0}.IsNull",
                ["DataValue"] = "!({0} is null)",
                ["Variant"] = "!{0}.IsNull",
                ["DiagnosticInfo"] = "!({0} is null)",
                ["XmlElement"] = "!{0}.IsNull"
            };

        private static string GetNotDefaultCheck(TypeFieldModel field)
        {
            if (field.IsArray || field.IsMatrix)
            {
                return $"!{field.PropertyName}.IsNull";
            }
            if (field.IsEnum ||
                !NotDefaultCheckExpression.TryGetValue(field.ShortTypeName, out string expr))
            {
                return $"{field.PropertyName} != default";
            }
            return CoreUtils.Format(expr, field.PropertyName);
        }

        private static string FormatExpandedNodeIdExpression(
            string idString,
            string className,
            string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return $"""new global::Opc.Ua.ExpandedNodeId("{className.Escape()}", "{nsUri.Escape()}")""";
            }
            return $"""new global::Opc.Ua.ExpandedNodeId(global::Opc.Ua.NodeId.Parse("{idString.Escape()}"), "{nsUri.Escape()}")""";
        }

        private static string FormatOptionalExpandedNodeIdExpression(
            string idString,
            string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return "global::Opc.Ua.ExpandedNodeId.Null";
            }
            return $"""new global::Opc.Ua.ExpandedNodeId(global::Opc.Ua.NodeId.Parse("{idString.Escape()}"), "{nsUri.Escape()}")""";
        }
    }

    /// <summary>
    /// A diagnostic message produced during source generation.
    /// </summary>
    internal sealed class TypeSourceGeneratorDiagnostic
    {
        /// <summary>
        /// Name of the property in the type
        /// </summary>
        public string PropertyName { get; set; }

        /// <summary>
        /// Type name to generate code for
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Error code
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Message
        /// </summary>
        public string Message { get; set; }
    }
}
