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

                if (field.IsMatrix)
                {
                    // A matrix is carried in a Variant, which only exists for
                    // the element types the Variant has matrix accessors for.
                    if (resolvedType != null && s_matrixGetterMap.ContainsKey(resolvedType))
                    {
                        valid.Add(field);
                        continue;
                    }
                }
                else if (resolvedType != null && s_scalarTypeMap.ContainsKey(resolvedType))
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
                        "IEncodeable, enums, ArrayOf<T>, and MatrixOf<T> are supported."
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
            template.AddReplacement(Tokens.DataTypeDefinitionsClass,
                DataTypeDefinitionsClassName(model.NamespaceSymbol));

            if (model.IsEnum)
            {
                template.AddReplacement(
                    Tokens.ListOfTypeActivators,
                    TypeSourceTemplates.EnumerationActivatorClassWithSourceDefinition,
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
                    TypeSourceTemplates.StructureActivatorClassWithSourceDefinition,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
                template.AddReplacement(
                    Tokens.ListOfActivatorRegistrations,
                    TypeSourceTemplates.SourceActivatorRegistration,
                    [model],
                    WriteTemplate_ListOfTypeActivators);
            }

            template.AddReplacement(
                Tokens.ListOfDataTypeDefinitions,
                [model.IsEnum ? model : model with { Fields = validFields }],
                LoadTemplate_ListOfDataTypeDefinitions,
                WriteTemplate_ListOfDataTypeDefinitions);

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
            template.AddReplacement(Tokens.DataTypeDefinitionsClass,
                DataTypeDefinitionsClassName(nsSymbol));

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
            template.AddReplacement(
                Tokens.ListOfDataTypeDefinitions,
                allActivators,
                LoadTemplate_ListOfDataTypeDefinitions,
                WriteTemplate_ListOfDataTypeDefinitions);

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

            // A nested type is emitted inside partial declarations of its
            // containing types, one wrapper per level, outermost first.
            if (ctx.ContainingTypeDeclarations is { Count: > 0 })
            {
                return TypeSourceTemplates.ContainingType;
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

            if (model.ContainingTypeDeclarations is { Count: > 0 })
            {
                context.Template.AddReplacement(
                    Tokens.TypeName,
                    model.ContainingTypeDeclarations[0]);
                context.Template.AddReplacement(
                    Tokens.ListOfTypes,
                    [model with
                    {
                        ContainingTypeDeclarations = [.. model.ContainingTypeDeclarations.Skip(1)]
                    }],
                    LoadTemplate_ListOfPartialClasses,
                    WriteTemplate_ListOfPartialClasses);
                return context.Template.Render();
            }

            string typeIdExpr = FormatExpandedNodeIdExpression(
                model.DataTypeId, GetDataTypeName(model), model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId, model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId, model.NamespaceUri);

            context.Template.AddReplacement(Tokens.ClassName, model.ClassName);
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                GetDataTypeName(model));
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"\"{model.NamespaceUri.Escape()}\"");
            context.Template.AddReplacement(Tokens.AccessModifier,
                model.AccessModifier ?? (model.IsInternal ? "internal" : "public"));

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
            if (field.IsMatrix)
            {
                // A matrix field is an inline matrix (OPC 10000-6 5.2.5):
                // matrices of structures use the typed inline matrix call,
                // every other element type the raw Variant value, normalized
                // to an inline matrix by WriteInlineMatrixValue - the calls
                // the model driven generator and the DataTypeDefinition
                // driven codec (Structure.EncodeProperty) make.
                // A matrix of a structure that allows subtypes is a matrix of
                // extension objects, like the scalar and array forms: the
                // inline form decodes every element as the declared type.
                // FromStructure turns a null matrix into a null Variant, which
                // carries no shape; keep a null matrix.
                encodeLine = field.IsEncodeable && ShouldUseExtensionObject(field)
                    ? CoreUtils.Format(
                        "global::Opc.Ua.EncoderExtensions.WriteInlineMatrixValue(encoder, \"{0}\", {1}.IsNull ? " +
                        "global::Opc.Ua.Variant.From(default(global::Opc.Ua.MatrixOf<global::Opc.Ua.ExtensionObject>)) : " +
                        "global::Opc.Ua.Variant.FromStructure({1}));",
                        field.FieldName.Escape(),
                        field.PropertyName)
                    : field.IsEncodeable
                    ? CoreUtils.Format(
                        "encoder.WriteEncodeableMatrix(\"{0}\", {1});",
                        field.FieldName.Escape(),
                        field.PropertyName)
                    : CoreUtils.Format(
                        "global::Opc.Ua.EncoderExtensions.WriteInlineMatrixValue(encoder, \"{0}\", global::Opc.Ua.Variant.From({1}));",
                        field.FieldName.Escape(),
                        field.PropertyName);
            }
            else if (field.IsEncodeable)
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

            // OPC 10000-6 5.4.1/5.4.2.1 (and 5.3.5 for XML): a Compact encoder
            // may only omit a field whose value is the default of its type
            // (Table 1: 0, false, null, ...), because a conformant decoder
            // reads a missing field as that type default. The field is
            // therefore only omitted when its value is the type default and
            // a missing field also decodes to the type default here: in JSON
            // and for a SetIfMissing field it always does; an Exclude field
            // missing from XML keeps its declared default, so only when that
            // is the type default too (no initializer, or a default literal).
            // A field with any other or an unknown declared default is always
            // written (the Compact JsonEncoder still drops a type default by
            // itself, which JSON decodes back to the type default), and
            // nothing is omitted when the encoder cannot omit fields (binary,
            // Verbose).
            if ((field.DefaultValueHandling & 1) == 0 &&
                (IsSetIfMissing(field) ||
                    (!field.HasNonConstantInitializer && field.DefaultValueLiteral == null)))
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
            if (field.IsMatrix)
            {
                if (field.IsEncodeable && ShouldUseExtensionObject(field))
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadVariantValue(\"{1}\", global::Opc.Ua.TypeInfo.Create(" +
                        "global::Opc.Ua.BuiltInType.ExtensionObject, global::Opc.Ua.ValueRanks.TwoDimensions))" +
                        ".GetStructureMatrix<{2}>();",
                        target,
                        field.FieldName.Escape(),
                        field.ElementTypeName);
                }
                else if (field.IsEncodeable)
                {
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadEncodeableMatrix<{1}>(\"{2}\");",
                        target,
                        field.ElementTypeName,
                        field.FieldName.Escape());
                }
                else
                {
                    // The inline matrix is read with the field's type info;
                    // the decoders take the actual rank from the encoded
                    // dimensions.
                    string builtInType = field.IsEnum
                        ? "Enumeration"
                        : s_matrixGetterMap[field.ElementShortTypeName];
                    decodeLine = CoreUtils.Format(
                        "{0} = decoder.ReadVariantValue(\"{1}\", global::Opc.Ua.TypeInfo.Create(" +
                        "global::Opc.Ua.BuiltInType.{2}, global::Opc.Ua.ValueRanks.TwoDimensions)).{3};",
                        target,
                        field.FieldName.Escape(),
                        builtInType,
                        field.IsEnum
                            ? $"GetEnumerationMatrix<{field.ElementTypeName}>()"
                            : $"Get{builtInType}Matrix()");
                }
            }
            else if (field.IsEncodeable)
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

            // OPC 10000-6 5.4.1/5.4.2.1/5.4.7: a field missing from JSON is
            // the type default (Table 1), so JSON (and binary) always read
            // the field; the JSON decoder returns the type default for a
            // missing field. Only XML keeps the declared default of a
            // missing field (unless SetIfMissing): a deliberate leniency so
            // configuration files that predate a field still load. The
            // encoder never omits a field in XML whose declared default
            // differs from the type default, so values this SDK writes
            // still round trip with conformant peers.
            if ((field.DefaultValueHandling & 2) == 0)
            {
                decodeLine = CoreUtils.Format(
                    "if (decoder.EncodingType != global::Opc.Ua.EncodingType.Xml || " +
                    "decoder.HasField(\"{0}\")) {1}",
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

            // The != operator is not reflexive for scalar floating point values:
            // NaN != NaN is true, which would make a decoded value compare unequal
            // to itself. Route those through CoreUtils.IsEqual (which uses the
            // NaN-aware IEquatable comparer) so equality stays reflexive. Array /
            // matrix forms already compare NaN-safely via ArrayOf/MatrixOf.
            static bool IsDotNetEqualityComparable(TypeFieldModel field) =>
                !field.IsEncodeable && !IsFloatingPointScalar(field);

            static bool IsFloatingPointScalar(TypeFieldModel field) =>
                !field.IsArray &&
                !field.IsMatrix &&
                field.ShortTypeName != null &&
                (field.ShortTypeName.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
                    field.ShortTypeName.Equals("Single", StringComparison.OrdinalIgnoreCase) ||
                    field.ShortTypeName.Equals("Float", StringComparison.OrdinalIgnoreCase));
        }

        private static TemplateString LoadTemplate_ListOfClonedFields(ILoadContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return null;
            }

            // The clone starts as a shallow copy of the whole object
            // (base.MemberwiseClone() for classes, 'with { }' for records),
            // which already carries every field including inherited and
            // init-only ones. Only fields with reference semantics need an
            // explicit deep copy on top of that.
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
            return null;

            // An array or matrix is cloned element-wise when its elements
            // have reference semantics, like the model driven generator does.
            static bool NeedsCloning(TypeFieldModel field)
            {
                string typeName = field.IsArray || field.IsMatrix
                    ? field.ElementShortTypeName
                    : field.ShortTypeName;
                switch (typeName)
                {
                    case "DataValue":
                    case "Variant":
                    case "ExtensionObject":
                        return true;
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
                return TypeSourceTemplates.EnumerationActivatorClassWithSourceDefinition;
            }
            return TypeSourceTemplates.StructureActivatorClassWithSourceDefinition;
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
                GetDataTypeName(model),
                model.NamespaceUri);
            string binaryIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.BinaryEncodingId,
                model.NamespaceUri);
            string xmlIdExpr = FormatOptionalExpandedNodeIdExpression(
                model.XmlEncodingId,
                model.NamespaceUri);

            // The activator lives at namespace level: it is named after the
            // symbol name and refers to a nested type by its qualified name.
            context.Template.AddReplacement(Tokens.ClassName, model.SymbolName ?? model.ClassName);
            context.Template.AddReplacement(Tokens.TypeName, model.TypeReference ?? model.ClassName);
            // EncodeableType<T> / EnumeratedType<T> is only as accessible as
            // T, so a public activator of a non-public type is CS0060.
            context.Template.AddReplacement(Tokens.AccessModifier,
                model.IsEffectivelyPublic ? "public" : "internal");
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                GetDataTypeName(model));
            context.Template.AddReplacement(Tokens.DataTypeIdConstant, typeIdExpr);
            context.Template.AddReplacement(Tokens.BinaryEncodingId, binaryIdExpr);
            context.Template.AddReplacement(Tokens.XmlEncodingId, xmlIdExpr);
            context.Template.AddReplacement(Tokens.DataTypeDefinitionsClass,
                DataTypeDefinitionsClassName(model.NamespaceSymbol));
            context.Template.AddReplacement(Tokens.XmlNamespaceUri,
                $"""
                "{model.NamespaceUri.Escape()}"
                """);

            return context.Template.Render();
        }

        private static TemplateString LoadTemplate_ListOfDataTypeDefinitions(ILoadContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return null;
            }
            if (model.IsEnum)
            {
                return DataTypeTemplates.EnumDefinition;
            }
            return model.IsDerived && model.BaseDefinitionActivator != null
                ? TypeSourceTemplates.DerivedStructureDefinition
                : DataTypeTemplates.StructureDefinition;
        }

        private static bool WriteTemplate_ListOfDataTypeDefinitions(IWriteContext context)
        {
            if (context.Target is not TypeSourceModel model)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ClassName, model.SymbolName ?? model.ClassName);
            context.Template.AddBrowseNameReplacement(
                Tokens.BrowseName,
                Tokens.BrowseNameLiteral,
                GetDataTypeName(model));

            if (model.IsEnum)
            {
                // Every enum, [Flags] included, is registered as an
                // EnumeratedType and encoded as an Enumeration (Int32). An
                // OptionSet of up to 64 bits is a UInteger subtype encoded as
                // that integer (OPC 10000-3 5.8.2, 8.40), so publishing a
                // [Flags] enum as an OptionSet would describe a different wire
                // format (the width for byte/short/long backing, the text form
                // in XML and JSON). It is published as the enumeration it is
                // encoded as: all members with their values.
                context.Template.AddReplacement(Tokens.IsOptionSet, false);
                context.Template.AddReplacement(
                    Tokens.ListOfFields,
                    DataTypeTemplates.EnumField,
                    model.EnumMembers,
                    WriteTemplate_ListOfEnumDefinitionFields);
                return context.Template.Render();
            }

            // The attribute model only carries this type's own (explicit)
            // fields. A derived type whose base exposes a definition through
            // its activator prepends the base fields at runtime (see
            // DerivedStructureDefinition); otherwise the base data type is
            // emitted as Structure and the explicit field index is zero.
            // Encode() never writes an encoding mask - a nullable property is
            // always encoded - so the definition never declares optional
            // fields.
            context.Template.AddReplacement(
                Tokens.BaseType,
                model.IsDerived && model.BaseDefinitionActivator != null
                    ? model.BaseDefinitionActivator
                    : "new global::Opc.Ua.NodeId(22u)");
            context.Template.AddReplacement(Tokens.FirstExplicitFieldIndex, 0);
            context.Template.AddReplacement(Tokens.StructureType, "Structure");
            context.Template.AddReplacement(
                Tokens.ListOfFields,
                DataTypeTemplates.StructureField,
                model.Fields,
                WriteTemplate_ListOfStructureDefinitionFields);
            return context.Template.Render();
        }

        private static bool WriteTemplate_ListOfEnumDefinitionFields(IWriteContext context)
        {
            if (context.Target is not TypeEnumMember member)
            {
                return false;
            }
            context.Template.AddReplacement(
                Tokens.FieldName,
                $"\"{member.Name.Escape()}\"");
            context.Template.AddReplacement(
                Tokens.DisplayName,
                $"new global::Opc.Ua.LocalizedText(string.Empty, string.Empty, \"{member.Name.Escape()}\")");
            context.Template.AddReplacement(
                Tokens.ValueCode,
                FormatEnumMemberValue(member.Value));
            context.Template.AddReplacement(
                Tokens.Description,
                "global::Opc.Ua.LocalizedText.Null");
            return context.Template.Render();
        }

        private static bool WriteTemplate_ListOfStructureDefinitionFields(IWriteContext context)
        {
            if (context.Target is not TypeFieldModel field)
            {
                return false;
            }

            // A StructureField ValueRank is -1 or >= 1, never 0 (OPC 10000-3
            // 8.51), and a matrix field has at least two dimensions (OPC
            // 10000-6 5.2.5). MatrixOf<T> does not tell the rank: publish a
            // two dimensional matrix of unknown lengths. A value of another
            // rank is still encoded with all its dimensions, but a consumer
            // checking the published rank may reject it; a field of a fixed
            // rank above two is declared in a model design instead.
            string valueRank = field.IsMatrix
                ? "global::Opc.Ua.ValueRanks.TwoDimensions"
                : field.IsArray
                    ? "global::Opc.Ua.ValueRanks.OneDimension"
                    : "global::Opc.Ua.ValueRanks.Scalar";
            string arrayDimensions = field.IsMatrix
                ? "new uint[] { 0, 0 }"
                : field.IsArray
                    ? "new uint[] { 0 }"
                    : "default";

            context.Template.AddReplacement(
                Tokens.FieldName,
                $"\"{field.FieldName.Escape()}\"");
            string dataType = field.DataTypeNodeId ?? "global::Opc.Ua.DataTypeIds.BaseDataType";
            if (field.IsEncodeable && !ShouldUseExtensionObject(field))
            {
                // Written inline with WriteEncodeable(Matrix): the field's DataType
                // must be the concrete structure. The abstract Structure
                // (i=22) would tell a definition driven decoder to expect an
                // ExtensionObject (OPC 10000-6 5.2.6).
                dataType = CoreUtils.Format(
                    "global::Opc.Ua.ExpandedNodeId.ToNodeId(new {0}().TypeId, namespaceUris)",
                    (field.IsArray || field.IsMatrix ? field.ElementTypeName : field.TypeName)
                        .TrimEnd('?'));
            }
            context.Template.AddReplacement(Tokens.DataType, dataType);
            context.Template.AddReplacement(Tokens.ValueRank, valueRank);
            context.Template.AddReplacement(Tokens.ArrayDimensions, arrayDimensions);
            // Encode() always writes the field, there is no encoding mask.
            context.Template.AddReplacement(Tokens.IsOptional, false);
            context.Template.AddReplacement(
                Tokens.Description,
                "global::Opc.Ua.LocalizedText.Null");
            return context.Template.Render();
        }

        /// <summary>
        /// Computes the namespace-unique data type definitions class name for a
        /// source-annotated namespace. The model-driven generator emits a
        /// <c>DataTypeDefinitions</c> class; using a namespace-unique name here
        /// avoids a cross-assembly CS0436 collision when both generators target
        /// the same OPC UA namespace (e.g. <c>Opc.Ua</c>).
        /// </summary>
        private static string DataTypeDefinitionsClassName(string namespaceSymbol)
        {
            return $"{namespaceSymbol}DataTypeDefinitions";
        }

        /// <summary>
        /// Formats an enum member's numeric value (captured as a string from
        /// the Roslyn constant) as a long literal for the EnumField.Value.
        /// </summary>
        private static string FormatEnumMemberValue(string value)
        {
            if (!string.IsNullOrEmpty(value) &&
                long.TryParse(
                    value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long parsed))
            {
                return parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (!string.IsNullOrEmpty(value) &&
                ulong.TryParse(
                    value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out ulong unsignedValue))
            {
                // A ulong backed enum member above long.MaxValue keeps its bits.
                return CoreUtils.Format("unchecked((long){0}UL)", unsignedValue);
            }
            return "0";
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
                if (field.IsMatrix)
                {
                    return ("WriteEncodeableMatrix", "ReadEncodeableMatrix");
                }
                return ("WriteEncodeable", "ReadEncodeable");
            }

            if (field.IsMatrix)
            {
                // IEncoder/IDecoder have no typed matrix calls for built-in
                // or enumerated elements: the inline matrix is the raw value
                // of a Variant.
                return field.IsEnum ||
                    (field.ElementShortTypeName != null &&
                        s_matrixGetterMap.ContainsKey(field.ElementShortTypeName))
                    ? ("WriteVariantValue", "ReadVariantValue")
                    : (null, null);
            }

            if (field.IsEnum)
            {
                if (field.IsArray)
                {
                    return ("WriteEnumeratedArray", "ReadEnumeratedArray");
                }
                return ("WriteEnumerated", "ReadEnumerated");
            }

            string lookupType = field.IsArray
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
                return methods;
            }

            return (null, null);
        }

        /// <summary>
        /// Maps the element type short name of a <c>MatrixOf&lt;T&gt;</c>
        /// property to the name of the Variant matrix getter
        /// (<c>Get{Name}Matrix()</c>) that returns exactly
        /// <c>MatrixOf&lt;T&gt;</c>.
        /// </summary>
        internal static readonly Dictionary<string, string> s_matrixGetterMap =
            new(StringComparer.Ordinal)
            {
                ["Boolean"] = "Boolean",
                ["SByte"] = "SByte",
                ["Byte"] = "Byte",
                ["Int16"] = "Int16",
                ["UInt16"] = "UInt16",
                ["Int32"] = "Int32",
                ["UInt32"] = "UInt32",
                ["Int64"] = "Int64",
                ["UInt64"] = "UInt64",
                ["Single"] = "Float",
                ["Double"] = "Double",
                ["String"] = "String",
                ["DateTimeUtc"] = "DateTime",
                ["Uuid"] = "Guid",
                ["ByteString"] = "ByteString",
                ["XmlElement"] = "XmlElement",
                ["NodeId"] = "NodeId",
                ["ExpandedNodeId"] = "ExpandedNodeId",
                ["StatusCode"] = "StatusCode",
                ["QualifiedName"] = "QualifiedName",
                ["LocalizedText"] = "LocalizedText",
                ["ExtensionObject"] = "ExtensionObject",
                ["DataValue"] = "DataValue",
                ["Variant"] = "Variant"
            };

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
                ["XmlElement"] = ("WriteXmlElement", "ReadXmlElement")
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
                // The String default is null (OPC 10000-6 Table 1); an empty
                // string is a value and is written.
                ["String"] = "{0} != null",
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
                ["DataValue"] = "!{0}.IsNull",
                ["Variant"] = "!{0}.IsNull",
                ["DiagnosticInfo"] = "!({0} is null)",
                ["XmlElement"] = "!{0}.IsNull"
            };

        /// <summary>
        /// The name the default DataTypeId, the XML name and the browse name
        /// of the data type are derived from: the nesting-qualified name of a
        /// nested type (so same-named nested types do not share an identity),
        /// the class name otherwise.
        /// </summary>
        private static string GetDataTypeName(TypeSourceModel model)
        {
            return model.QualifiedName ?? model.ClassName;
        }

        /// <summary>
        /// True for DefaultValueHandling.SetIfMissing (2): the field is always
        /// decoded, so a missing field yields the CLR default.
        /// </summary>
        private static bool IsSetIfMissing(TypeFieldModel field)
        {
            return (field.DefaultValueHandling & 2) != 0;
        }

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
