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
using System.Globalization;
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

            return ctx.IsRecord
                ? TypeSourceTemplates.RecordPartialClassBody
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
            string jsonIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.JsonEncodingId, model.NamespaceUri);

            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddReplacement(Tokens.BrowseName, model.ClassName);
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.JsonEncodingId, jsonIdExpr);
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"\"{model.NamespaceUri.Escape()}\"");

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

            if (field.IsEncodeable)
            {
                if (field.IsArray)
                {
                    return (TemplateString)CoreUtils.Format(
                        """encoder.WriteEncodeableArray("{0}", {1});""",
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
                return (TemplateString)CoreUtils.Format(
                    """encoder.WriteEncodeable("{0}", {1});""",
                    field.FieldName.Escape(),
                    field.PropertyName);
            }

            if (field.IsEnum)
            {
                if (field.IsArray)
                {
                    return (TemplateString)CoreUtils.Format(
                    """encoder.WriteEnumeratedArray("{0}", {1});""",
                        field.FieldName.Escape(),
                        field.PropertyName);
                }
                return (TemplateString)CoreUtils.Format(
                    """encoder.WriteEnumerated("{0}", {1});""",
                    field.FieldName.Escape(),
                    field.PropertyName);
            }

            return (TemplateString)CoreUtils.Format(
                """encoder.{0}("{1}", {2});""",
                writeMethod,
                field.FieldName.Escape(),
                field.PropertyName);
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

            if (field.IsEncodeable)
            {
                if (field.IsArray)
                {
                    return (TemplateString)CoreUtils.Format(
                        """{0} = decoder.ReadEncodeableArray<{1}>("{2}");""",
                        field.PropertyName,
                        field.ElementTypeName,
                        field.FieldName.Escape());
                }
                return (TemplateString)CoreUtils.Format(
                    """{0} = ({1})decoder.ReadEncodeable("{2}", typeof({1}));""",
                    field.PropertyName,
                    field.TypeName,
                    field.FieldName.Escape());
            }

            if (field.IsEnum)
            {
                string typeName = field.IsArray ? field.ElementTypeName : field.TypeName;
                if (field.IsArray)
                {
                    return (TemplateString)CoreUtils.Format(
                    """{0} = decoder.ReadEnumeratedArray<{1}>("{2}");""",
                        field.PropertyName,
                        typeName,
                        field.FieldName.Escape());
                }
                return (TemplateString)CoreUtils.Format(
                    """{0} = ({1})decoder.ReadEnumerated("{2}", typeof({1}));""",
                    field.PropertyName,
                    typeName,
                    field.FieldName.Escape());
            }

            return (TemplateString)CoreUtils.Format(
                    """{0} = decoder.{1}("{2}");""",
                field.PropertyName,
                readMethod,
                field.FieldName.Escape());
        }

        private static TemplateString LoadTemplate_ListOfComparedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }
            return (TemplateString)CoreUtils.Format(
                "if (!global::Opc.Ua.CoreUtils.IsEqual(this.{0}, value.{0})) return false;",
                field.PropertyName);
        }

        private static TemplateString LoadTemplate_ListOfClonedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }
            // ArrayOf<T> and MatrixOf<T> are value types — copied by MemberwiseClone
            if (field.IsArray || field.IsMatrix)
            {
                return null;
            }
            if (field.IsEncodeable)
            {
                return (TemplateString)CoreUtils.Format(
                    "clone.{0} = ({1})((global::Opc.Ua.IEncodeable)this.{0})?.Clone();",
                    field.PropertyName, field.TypeName);
            }
            return null;
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
            string jsonIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.JsonEncodingId,
                model.NamespaceUri);

            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddReplacement(Tokens.BrowseName, model.ClassName);
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.JsonEncodingId, jsonIdExpr);
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"""
                "{model.NamespaceUri.Escape()}"
                """);

            return context.Template.Render();
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
                ["sbyte"] = ("WriteSByte", "ReadSByte"),
                ["Byte"] = ("WriteByte", "ReadByte"),
                ["byte"] = ("WriteByte", "ReadByte"),
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
                ["double"] = ("WriteDouble", "ReadDouble"),
                ["String"] = ("WriteString", "ReadString"),
                ["string"] = ("WriteString", "ReadString"),
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

        private static string FormatExpandedNodeIdExpression(
            string idString,
            string className,
            string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return $"""new global::Opc.Ua.ExpandedNodeId("{className.Escape()}", "{nsUri.Escape()}")""";
            }
            return $"""global::Opc.Ua.ExpandedNodeId.Parse("{idString.Escape()}", "{nsUri.Escape()}")""";
        }

        private static string FormatOptionalExpandedNodeIdExpression(
            string idString,
            string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return "global::Opc.Ua.ExpandedNodeId.Null";
            }
            return $"""global::Opc.Ua.ExpandedNodeId.Parse("{idString.Escape()}", "{nsUri.Escape()}")""";
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
