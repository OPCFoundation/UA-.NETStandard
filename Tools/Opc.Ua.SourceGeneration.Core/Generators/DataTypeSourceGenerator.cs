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
    internal static class DataTypeSourceGenerator
    {
        /// <summary>
        /// Validates the fields of a model and returns diagnostics for
        /// properties with unsupported types. Valid fields are returned
        /// in the out parameter.
        /// </summary>
        public static IReadOnlyList<DataTypeSourceDiagnostic> ValidateAndFilter(
            DataTypeSourceModel model,
            out IReadOnlyList<DataTypeSourceField> validFields)
        {
            var diagnostics = new List<DataTypeSourceDiagnostic>();
            var valid = new List<DataTypeSourceField>();

            foreach (DataTypeSourceField field in model.Fields)
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
                diagnostics.Add(new DataTypeSourceDiagnostic
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
        /// Generate the source text for a [DataType]-annotated type.
        /// Only valid fields (those that passed validation) are included.
        /// </summary>
        public static string Generate(DataTypeSourceModel model,
            IReadOnlyList<DataTypeSourceField> validFields)
        {
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            var template = new Template(templateWriter, DataTypeSourceTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, model.Namespace);
            template.AddReplacement(Tokens.Namespace, model.NamespaceSymbol);
            template.AddReplacement(Tokens.NamespaceUri, model.NamespaceUri);

            if (model.IsEnum)
            {
                template.AddReplacement(Tokens.ListOfTypes, (string)null);
                template.AddReplacement(
                    Tokens.ListOfTypeActivators,
                    [model],
                    LoadEnumActivator,
                    WriteActivator);
                template.AddReplacement(
                    Tokens.ListOfActivatorRegistrations,
                    [model],
                    LoadEnumRegistration,
                    WriteActivator);
            }
            else
            {
                template.AddReplacement(
                    Tokens.ListOfTypes,
                    [new ClassBodyContext(model, validFields)],
                    LoadPartialClassBody,
                    WritePartialClassBody);
                template.AddReplacement(
                    Tokens.ListOfTypeActivators,
                    [model],
                    LoadStructureActivator,
                    WriteActivator);
                template.AddReplacement(
                    Tokens.ListOfActivatorRegistrations,
                    [model],
                    LoadStructureRegistration,
                    WriteActivator);
            }

            template.Render();
            return stringWriter.ToString();
        }

        /// <summary>
        /// Overload for backward compatibility with tests.
        /// Validates, filters, and generates in one call.
        /// </summary>
        public static string Generate(DataTypeSourceModel model)
        {
            ValidateAndFilter(model, out IReadOnlyList<DataTypeSourceField> validFields);
            return Generate(model, validFields);
        }

        /// <summary>
        /// Generate a single file containing all types from the same namespace.
        /// Produces one extension method with all registrations combined.
        /// </summary>
        public static string GenerateBatch(
            string ns,
            string nsSymbol,
            string nsUri,
            IReadOnlyList<object> allTypes,
            IReadOnlyList<object> allActivators,
            IReadOnlyList<object> allRegistrations)
        {
            using var stringWriter = new StringWriter();
            using var templateWriter = new TemplateWriter(stringWriter);

            var template = new Template(
                templateWriter, DataTypeSourceTemplates.File);
            template.AddReplacement(Tokens.NamespacePrefix, ns);
            template.AddReplacement(Tokens.Namespace, nsSymbol);
            template.AddReplacement(Tokens.NamespaceUri, nsUri);

            template.AddReplacement(
                Tokens.ListOfTypes,
                allTypes,
                LoadPartialClassBody,
                WritePartialClassBody);
            template.AddReplacement(
                Tokens.ListOfTypeActivators,
                allActivators,
                LoadActivator,
                WriteActivator);
            template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                allRegistrations,
                LoadRegistration,
                WriteActivator);

            template.Render();
            return stringWriter.ToString();
        }

        private static TemplateString LoadPartialClassBody(ILoadContext context)
        {
            if (!(context.Target is ClassBodyContext ctx))
            {
                return null;
            }

            return ctx.Model.IsRecord
                ? DataTypeSourceTemplates.RecordPartialClassBody
                : DataTypeSourceTemplates.PartialClassBody;
        }

        private static bool WritePartialClassBody(IWriteContext context)
        {
            if (!(context.Target is ClassBodyContext ctx))
            {
                return false;
            }

            DataTypeSourceModel model = ctx.Model;
            IReadOnlyList<DataTypeSourceField> fields = ctx.Fields;

            Template t = context.Template;
            string typeIdExpr = FormatExpandedNodeIdExpression(
                model.DataTypeId, model.ClassName, model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId, model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId, model.NamespaceUri);
            string jsonIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.JsonEncodingId, model.NamespaceUri);

            t.AddReplacement(Tokens.ClassName, model.ClassName);
            t.AddReplacement(Tokens.BrowseName, model.ClassName);
            t.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            t.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            t.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            t.AddReplacement(Tokens.JsonEncodingId, jsonIdExpr);
            t.AddReplacement(Tokens.XmlNamespaceUri,
                $"\"{EscapeString(model.NamespaceUri)}\"");

            var fieldList = fields.Cast<object>().ToList();
            t.AddReplacement(Tokens.ListOfEncodedFields, fieldList,
                LoadEncodedField, null);
            t.AddReplacement(Tokens.ListOfDecodedFields, fieldList,
                LoadDecodedField, null);
            t.AddReplacement(Tokens.ListOfComparedFields, fieldList,
                LoadComparedField, null);
            t.AddReplacement(Tokens.ListOfClonedFields, fieldList,
                LoadClonedField, null);

            return t.Render();
        }

        private static TemplateString LoadEncodedField(ILoadContext context)
        {
            if (!(context.Target is DataTypeSourceField field))
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
                return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "encoder.WriteEncodeable(\"{0}\", {1});",
                    EscapeString(field.FieldName), field.PropertyName);
            }

            if (field.IsEnum)
            {
                if (field.IsArray)
                {
                    return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "encoder.WriteEnumeratedArray(\"{0}\", {1});",
                        EscapeString(field.FieldName), field.PropertyName);
                }
                return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "encoder.WriteEnumerated(\"{0}\", {1});",
                    EscapeString(field.FieldName), field.PropertyName);
            }

            return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "encoder.{0}(\"{1}\", {2});",
                writeMethod, EscapeString(field.FieldName), field.PropertyName);
        }

        private static TemplateString LoadDecodedField(ILoadContext context)
        {
            if (!(context.Target is DataTypeSourceField field))
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
                return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "{0} = ({1})decoder.ReadEncodeable(\"{2}\", typeof({1}));",
                    field.PropertyName, field.TypeName, EscapeString(field.FieldName));
            }

            if (field.IsEnum)
            {
                string typeName = field.IsArray ? field.ElementTypeName : field.TypeName;
                if (field.IsArray)
                {
                    return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "{0} = decoder.ReadEnumeratedArray<{1}>(\"{2}\");",
                        field.PropertyName, typeName, EscapeString(field.FieldName));
                }
                return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "{0} = ({1})decoder.ReadEnumerated(\"{2}\", typeof({1}));",
                    field.PropertyName, typeName, EscapeString(field.FieldName));
            }

            return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "{0} = decoder.{1}(\"{2}\");",
                field.PropertyName, readMethod, EscapeString(field.FieldName));
        }

        private static TemplateString LoadComparedField(ILoadContext context)
        {
            if (!(context.Target is DataTypeSourceField field))
            {
                return null;
            }

            return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "if (!global::Opc.Ua.Utils.IsEqual(this.{0}, value.{0})) return false;",
                field.PropertyName);
        }

        private static TemplateString LoadClonedField(ILoadContext context)
        {
            if (!(context.Target is DataTypeSourceField field))
            {
                return null;
            }

            if (field.IsArray || field.IsMatrix || field.IsEncodeable)
            {
                return (TemplateString)string.Format(CultureInfo.InvariantCulture,
                    "clone.{0} = ({1})global::Opc.Ua.Utils.Clone(this.{0});",
                    field.PropertyName, field.TypeName);
            }

            return null;
        }

        private static TemplateString LoadStructureActivator(ILoadContext context)
        {
            return DataTypeTemplates.StructureActivatorClass;
        }

        private static TemplateString LoadEnumActivator(ILoadContext context)
        {
            return DataTypeTemplates.EnumerationActivatorClass;
        }

        private static TemplateString LoadActivator(ILoadContext context)
        {
            if (context.Target is DataTypeSourceModel model && model.IsEnum)
            {
                return DataTypeTemplates.EnumerationActivatorClass;
            }
            return DataTypeTemplates.StructureActivatorClass;
        }

        private static TemplateString LoadStructureRegistration(ILoadContext context)
        {
            return DataTypeSourceTemplates.SourceActivatorRegistration;
        }

        private static TemplateString LoadEnumRegistration(ILoadContext context)
        {
            return DataTypeSourceTemplates.SourceEnumActivatorRegistration;
        }

        private static TemplateString LoadRegistration(ILoadContext context)
        {
            if (context.Target is DataTypeSourceModel model && model.IsEnum)
            {
                return DataTypeSourceTemplates.SourceEnumActivatorRegistration;
            }
            return DataTypeSourceTemplates.SourceActivatorRegistration;
        }

        private static bool WriteActivator(IWriteContext context)
        {
            if (context.Target is not DataTypeSourceModel model)
            {
                return false;
            }

            Template t = context.Template;
            string typeIdExpr = FormatExpandedNodeIdExpression(
                model.DataTypeId, model.ClassName, model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId, model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId, model.NamespaceUri);
            string jsonIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.JsonEncodingId, model.NamespaceUri);

            t.AddReplacement(Tokens.ClassName, model.ClassName);
            t.AddReplacement(Tokens.BrowseName, model.ClassName);
            t.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            t.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            t.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            t.AddReplacement(Tokens.JsonEncodingId, jsonIdExpr);
            t.AddReplacement(Tokens.XmlNamespaceUri,
                $"\"{EscapeString(model.NamespaceUri)}\"");

            return t.Render();
        }

        /// <summary>
        /// Resolves the IEncoder/IDecoder method names for a field.
        /// Returns (writeMethod, readMethod) or (null, null) if unsupported.
        /// </summary>
        internal static (string writeMethod, string readMethod) ResolveEncoderDecoder(
            DataTypeSourceField field)
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

            if (lookupType != null && s_scalarTypeMap.TryGetValue(
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
            new Dictionary<string, (string, string)>(StringComparer.Ordinal)
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
                ["Float"] = ("WriteFloat", "ReadFloat"),
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
            string idString, string className, string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return "new global::Opc.Ua.ExpandedNodeId(\"" +
                    EscapeString(className) + "\", \"" +
                    EscapeString(nsUri) + "\")";
            }
            return "global::Opc.Ua.ExpandedNodeId.Parse(\"" +
                EscapeString(idString) + "\", \"" +
                EscapeString(nsUri) + "\")";
        }

        private static string FormatOptionalExpandedNodeIdExpression(
            string idString, string nsUri)
        {
            if (string.IsNullOrEmpty(idString))
            {
                return "global::Opc.Ua.ExpandedNodeId.Null";
            }
            return "global::Opc.Ua.ExpandedNodeId.Parse(\"" +
                EscapeString(idString) + "\", \"" +
                EscapeString(nsUri) + "\")";
        }

        internal static string EscapeString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }
#if NETSTANDARD2_0
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
#else
            return value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
#endif
        }
    }

    /// <summary>
    /// A diagnostic message produced during source generation.
    /// </summary>
    internal sealed class DataTypeSourceDiagnostic
    {
        public string PropertyName { get; set; }
        public string TypeName { get; set; }
        public bool IsError { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Wrapper to pass model + validated fields as a single target object
    /// to the template system (avoids ValueTuple deconstruct issues on netstandard2.0).
    /// </summary>
    internal sealed class ClassBodyContext
    {
        public DataTypeSourceModel Model { get; }
        public IReadOnlyList<DataTypeSourceField> Fields { get; }

        public ClassBodyContext(
            DataTypeSourceModel model,
            IReadOnlyList<DataTypeSourceField> fields)
        {
            Model = model;
            Fields = fields;
        }
    }
}
