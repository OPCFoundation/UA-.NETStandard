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
using System.Xml;
using Opc.Ua.Schema.Model;
using Opc.Ua.Types;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generates strongly-typed event-record classes for every OPC UA
    /// <c>ObjectType</c> whose base-type chain ends at
    /// <c>BaseEventType</c>. Each emitted record derives from the
    /// record of its parent ObjectType (forming an inheritance chain
    /// that mirrors the OPC UA event-type hierarchy) and exposes one
    /// init-only property per directly-declared field on the type.
    /// </summary>
    /// <remarks>
    /// This generator runs by default for every model. The output
    /// namespace defaults to the model's target namespace prefix and
    /// can be overridden via
    /// <see cref="GeneratorOptions.EventRecordNamespace"/>. When a
    /// record must derive from a parent record emitted in a different
    /// assembly the
    /// <see cref="GeneratorOptions.EventRecordExternalNamespaces"/>
    /// dictionary is consulted; the standard UA namespace
    /// <c>http://opcfoundation.org/UA/</c> always maps to
    /// <c>Opc.Ua.Client.Alarms</c>.
    /// </remarks>
    internal sealed class EventRecordGenerator : IGenerator
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="EventRecordGenerator"/> class.
        /// </summary>
        public EventRecordGenerator(IGeneratorContext context)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IEnumerable<Resource> Emit()
        {
            List<ObjectTypeDesign> types = GetEmittedEventTypes();
            if (types.Count == 0)
            {
                return [];
            }
            string outputNamespace = GetOutputNamespace();
            // Use the namespace Name (identifier-safe) rather than
            // Prefix (which may contain dots like "Opc.Ua") for the
            // generated C# class + method names.
            string modelPrefix = m_context.ModelDesign.TargetNamespace.Prefix;
            string identifierPrefix = m_context.ModelDesign.TargetNamespace.Name
                ?? modelPrefix.Replace(".", string.Empty, StringComparison.Ordinal);
            string registrationClassName = CoreUtils.Format(
                "{0}EventRecordDecoders", identifierPrefix);
            string registrationMethodName = CoreUtils.Format(
                "Register{0}Decoders", identifierPrefix);

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format(
                    "{0}.EventRecords.g.cs",
                    modelPrefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, EventRecordTemplates.File);

            template.AddReplacement(Tokens.Namespace, outputNamespace);
            template.AddReplacement(
                Tokens.ListOfTypes,
                EventRecordTemplates.RecordClass,
                types,
                WriteTemplate_RecordClass);

            template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                EventRecordTemplates.RegistrationExtension,
                [
                    new RegistrationContext
                    {
                        Types = types,
                        ClassName = registrationClassName,
                        MethodName = registrationMethodName
                    }
                ],
                WriteTemplate_RegistrationExtension);

            template.Render();
            return [fileName.AsTextFileResource()];
        }

        /// <summary>
        /// Selects every non-excluded <see cref="ObjectTypeDesign"/>
        /// whose <see cref="TypeDesign.BaseTypeNode"/> chain ends at
        /// <c>BaseEventType</c>. Returns the union of the standard event
        /// types and any vendor extensions in the supplied model.
        /// </summary>
        private List<ObjectTypeDesign> GetEmittedEventTypes()
        {
            var result = new List<ObjectTypeDesign>();
            foreach (NodeDesign node in m_context.ModelDesign.GetNodeDesigns())
            {
                if (node is not ObjectTypeDesign objectType)
                {
                    continue;
                }
                if (m_context.ModelDesign.IsExcluded(objectType))
                {
                    continue;
                }
                if (!IsEventType(objectType))
                {
                    continue;
                }
                result.Add(objectType);
            }
            // Make output order deterministic across runs.
            result.Sort(static (a, b) => string.CompareOrdinal(
                a.SymbolicName?.Name,
                b.SymbolicName?.Name));
            return result;
        }

        /// <summary>
        /// Returns <c>true</c> when the type is <c>BaseEventType</c>
        /// itself or one of its subtypes.
        /// </summary>
        private static bool IsEventType(TypeDesign type)
        {
            for (TypeDesign current = type;
                current != null;
                current = current.BaseTypeNode)
            {
                if (current.SymbolicId == kBaseEventTypeId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Renders one record class for a given <see cref="ObjectTypeDesign"/>.
        /// </summary>
        private bool WriteTemplate_RecordClass(IWriteContext context)
        {
            if (context.Target is not ObjectTypeDesign objectType)
            {
                return false;
            }
            string typeName = objectType.SymbolicName.Name;
            string className = CoreUtils.Format("{0}Record", typeName);
            string baseClassName = ResolveBaseRecordName(objectType);
            // The root record (BaseEventTypeRecord) derives from
            // EventRecord, which has no nested Decoder — no 'new'
            // needed. Every subtype hides its parent's nested
            // Decoder, so 'new' is required to suppress CS0108.
            string newModifier = baseClassName == kRootBaseRecord
                ? string.Empty
                : "new ";

            context.Template.AddReplacement(Tokens.SymbolicName, typeName);
            context.Template.AddReplacement(Tokens.ClassName, className);
            context.Template.AddReplacement(Tokens.BaseClassName, baseClassName);
            context.Template.AddReplacement(Tokens.AccessModifier, newModifier);

            List<FieldEntry> ownFields = CollectDeclaredFields(objectType);
            context.Template.AddReplacement(
                Tokens.ListOfProperties,
                EventRecordTemplates.FieldProperty,
                ownFields,
                WriteTemplate_FieldProperty);

            // Decoder reads the full inherited + own field list at
            // stable positions. Inherited fields come first, in
            // root-to-leaf order; own fields trail.
            List<FieldEntry> allFields = CollectAllFieldsInOrder(objectType);
            // Assign stable positional indices for the decoder.
            for (int i = 0; i < allFields.Count; i++)
            {
                allFields[i].FieldIndex = i;
            }

            context.Template.AddReplacement(
                Tokens.ListOfFields,
                EventRecordTemplates.StandardFieldEntry,
                allFields,
                WriteTemplate_StandardFieldEntry);

            // Skip fields without a known reader — Variant fallback
            // types have no helper, so the property defaults to its
            // record-default value.
            List<FieldEntry> decodedFields = [];
            foreach (FieldEntry field in allFields)
            {
                if (!string.IsNullOrEmpty(field.ReaderMethod))
                {
                    decodedFields.Add(field);
                }
            }
            context.Template.AddReplacement(
                Tokens.ListOfDecodedFields,
                EventRecordTemplates.DecodedField,
                decodedFields,
                WriteTemplate_DecodedField);

            return context.Template.Render();
        }

        private static bool WriteTemplate_StandardFieldEntry(IWriteContext context)
        {
            if (context.Target is not FieldEntry field)
            {
                return false;
            }
            string path = field.IsTwoStateVariableId
                ? CoreUtils.Format(
                    "[global::Opc.Ua.QualifiedName.From(global::Opc.Ua.BrowseNames.{0}), global::Opc.Ua.QualifiedName.From(global::Opc.Ua.BrowseNames.Id)]",
                    field.BrowseName)
                : CoreUtils.Format(
                    "[global::Opc.Ua.QualifiedName.From(global::Opc.Ua.BrowseNames.{0})]",
                    field.BrowseName);
            context.Template.AddReplacement(Tokens.ChildPath, path);
            return context.Template.Render();
        }

        private static bool WriteTemplate_DecodedField(IWriteContext context)
        {
            if (context.Target is not FieldEntry field)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.PropertyName, field.PropertyName);
            context.Template.AddReplacement(Tokens.ClientMethod, field.ReaderMethod);
            context.Template.AddReplacement(Tokens.FieldIndex, field.FieldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return context.Template.Render();
        }

        private static bool WriteTemplate_RegistrationExtension(IWriteContext context)
        {
            if (context.Target is not RegistrationContext reg)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.ClassName, reg.ClassName);
            context.Template.AddReplacement(Tokens.ClientMethod, reg.MethodName);
            context.Template.AddReplacement(
                Tokens.ListOfActivatorRegistrations,
                EventRecordTemplates.DecoderRegistration,
                reg.Types,
                WriteTemplate_DecoderRegistration);
            return context.Template.Render();
        }

        private static bool WriteTemplate_DecoderRegistration(IWriteContext context)
        {
            if (context.Target is not ObjectTypeDesign type)
            {
                return false;
            }
            string typeName = type.SymbolicName.Name;
            context.Template.AddReplacement(Tokens.SymbolicName, typeName);
            context.Template.AddReplacement(
                Tokens.ClassName,
                CoreUtils.Format("{0}Record", typeName));
            return context.Template.Render();
        }

        /// <summary>
        /// Renders one init-only property for a declared field.
        /// </summary>
        private static bool WriteTemplate_FieldProperty(IWriteContext context)
        {
            if (context.Target is not FieldEntry field)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.PropertyName, field.PropertyName);
            context.Template.AddReplacement(Tokens.DataType, field.DotNetType);
            context.Template.AddReplacement(
                Tokens.Description,
                string.IsNullOrEmpty(field.Description) ? field.PropertyName : field.Description);

            return context.Template.Render();
        }

        /// <summary>
        /// Collects only the fields declared directly on this type
        /// (not inherited). Recognizes simple properties and
        /// TwoStateVariableType.Id sub-properties. Skips children
        /// whose browse name already appears on a supertype — these
        /// are re-declarations / promotions in OPC UA semantics that
        /// do not introduce a new C# property.
        /// </summary>
        private List<FieldEntry> CollectDeclaredFields(ObjectTypeDesign type)
        {
            HashSet<string> inheritedNames = CollectInheritedFieldNames(type);
            return CollectFieldsAtLevel(type, inheritedNames);
        }

        /// <summary>
        /// Collects the full field list (inherited + own) in root-to-
        /// leaf inheritance order. Used by the Decoder template to
        /// emit positional <c>StandardFields</c> entries and matching
        /// positional reads. Indices are stable: index <c>0</c> is the
        /// root ancestor's first field, the last index is the type's
        /// own last field.
        /// </summary>
        private List<FieldEntry> CollectAllFieldsInOrder(ObjectTypeDesign type)
        {
            // Walk parent chain to the BaseEventType, collecting the
            // chain root-to-leaf.
            var chain = new List<ObjectTypeDesign>();
            for (TypeDesign current = type;
                current is ObjectTypeDesign cot;
                current = current.BaseTypeNode)
            {
                chain.Insert(0, cot);
                if (cot.SymbolicId == kBaseEventTypeId)
                {
                    break;
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var fields = new List<FieldEntry>();
            foreach (ObjectTypeDesign level in chain)
            {
                foreach (FieldEntry field in CollectFieldsAtLevel(level, seen))
                {
                    fields.Add(field);
                    seen.Add(field.BrowseName);
                }
            }
            return fields;
        }

        /// <summary>
        /// Shared implementation used by both <see cref="CollectDeclaredFields"/>
        /// (with a parent-suppression set) and
        /// <see cref="CollectAllFieldsInOrder"/> (which threads its own
        /// seen-set across levels).
        /// </summary>
        private List<FieldEntry> CollectFieldsAtLevel(
            ObjectTypeDesign type,
            HashSet<string> suppressed)
        {
            var fields = new List<FieldEntry>();
            InstanceDesign[] children = type.Children?.Items;
            if (children == null)
            {
                return fields;
            }
            foreach (InstanceDesign child in children)
            {
                if (m_context.ModelDesign.IsExcluded(child))
                {
                    continue;
                }

                if (child is MethodDesign)

                {
                    continue;
                }
                string browseName = child.SymbolicName?.Name;
                if (string.IsNullOrEmpty(browseName))
                {
                    continue;
                }

                // Promoted / re-declared on a subtype: emit nothing —
                // the parent record already declares the property.
                if (suppressed.Contains(browseName))
                {
                    continue;
                }

                if (child is PropertyDesign property)
                {
                    string dotnet = MapDataType(property.DataTypeNode, property.ValueRank);
                    fields.Add(new FieldEntry
                    {
                        PropertyName = browseName,
                        DotNetType = dotnet,
                        Description = SanitizeDescription(
                            property.Description?.Value),
                        BrowseName = browseName,
                        ReaderMethod = MapReaderMethod(dotnet),
                        IsTwoStateVariableId = false
                    });
                    continue;
                }

                if (child is VariableDesign variable)
                {
                    string typeId = variable.TypeDefinitionNode?.SymbolicId?.Name;
                    if (typeId == "TwoStateVariableType")
                    {
                        fields.Add(new FieldEntry
                        {
                            PropertyName = CoreUtils.Format("{0}Id", browseName),
                            DotNetType = "bool?",
                            Description = SanitizeDescription(
                                $"Id of the {browseName} TwoStateVariable."),
                            BrowseName = browseName,
                            ReaderMethod = "GetNullableBool",
                            IsTwoStateVariableId = true
                        });
                        continue;
                    }

                    string dotnetVar = MapDataType(variable.DataTypeNode, variable.ValueRank);
                    fields.Add(new FieldEntry
                    {
                        PropertyName = browseName,
                        DotNetType = dotnetVar,
                        Description = SanitizeDescription(
                            variable.Description?.Value),
                        BrowseName = browseName,
                        ReaderMethod = MapReaderMethod(dotnetVar),
                        IsTwoStateVariableId = false
                    });
                    continue;
                }

                // Objects (state machines, sub-objects) are not emitted as
                // event-record fields.
            }
            return fields;
        }

        /// <summary>
        /// Walks the supertype chain (excluding the type itself) and
        /// collects the browse names of every child that the
        /// generator would have emitted as a record property. Used to
        /// suppress re-declarations on subtypes.
        /// </summary>
        private HashSet<string> CollectInheritedFieldNames(ObjectTypeDesign type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (TypeDesign current = type.BaseTypeNode;
                current is ObjectTypeDesign parent;
                current = parent.BaseTypeNode)
            {
                if (parent.Children?.Items == null)
                {
                    continue;
                }
                foreach (InstanceDesign child in parent.Children.Items)
                {
                    if (child is MethodDesign)
                    {
                        continue;
                    }
                    string name = child.SymbolicName?.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
                if (parent.SymbolicId == kBaseEventTypeId)
                {
                    break;
                }
            }
            return names;
        }

        /// <summary>
        /// Maps an OPC UA data type to a C# short type. Falls back to
        /// <c>global::Opc.Ua.Variant</c> for unsupported types so
        /// generation never fails. <c>Variant</c> itself implements
        /// <c>INullable</c>; check <c>.IsNull</c> rather than wrapping
        /// in <c>Nullable&lt;Variant&gt;</c>.
        /// </summary>
        private static string MapDataType(DataTypeDesign dataType, ValueRank rank)
        {
            if (dataType == null)
            {
                return "global::Opc.Ua.Variant";
            }
            string baseType = MapScalarDataType(dataType);
            if (rank == ValueRank.Array)
            {
                return CoreUtils.Format("{0}[]?", StripNullable(baseType));
            }
            return baseType;
        }

        private static string StripNullable(string typeName)
        {
            if (typeName != null && typeName.EndsWith('?'))
            {
                return typeName[..^1];
            }
            return typeName;
        }

        private static string MapScalarDataType(DataTypeDesign dataType)
        {
            switch (dataType.SymbolicId?.Name)
            {
                case "Boolean":
                    return "bool?";
                case "SByte":
                    return "sbyte?";
                case "Byte":
                    return "byte?";
                case "Int16":
                    return "short?";
                case "UInt16":
                    return "ushort?";
                case "Int32":
                    return "int?";
                case "UInt32":
                    return "uint?";
                case "Int64":
                    return "long?";
                case "UInt64":
                    return "ulong?";
                case "Float":
                    return "float?";
                case "Double":
                    return "double?";
                case "String":
                    return "string?";
                case "DateTime":
                    return "global::System.DateTime?";
                case "UtcTime":
                    return "global::System.DateTime?";
                case "Guid":
                    return "global::System.Guid?";
                case "Duration":
                    return "double?";
                // BuiltIn types that implement INullable — use the
                // type's own .IsNull instead of wrapping in Nullable<T>.
                case "ByteString":
                    return "global::Opc.Ua.ByteString";
                case "XmlElement":
                    return "global::System.Xml.XmlElement";
                case "NodeId":
                    return "global::Opc.Ua.NodeId";
                case "ExpandedNodeId":
                    return "global::Opc.Ua.ExpandedNodeId";
                case "QualifiedName":
                    return "global::Opc.Ua.QualifiedName";
                case "LocalizedText":
                    return "global::Opc.Ua.LocalizedText";
                case "StatusCode":
                    return "global::Opc.Ua.StatusCode";
                default:
                    return "global::Opc.Ua.Variant";
            }
        }

        /// <summary>
        /// Maps the emitted .NET type to the corresponding
        /// <c>EventRecordFieldReaders</c> helper method name. Used
        /// by the decoder template to emit positional reads.
        /// Returns <c>null</c> for types without a matching reader —
        /// the generated decoder falls back to a no-op default for
        /// those fields (and notably the <c>Variant</c> fallback for
        /// unmapped data types is not populated).
        /// </summary>
        private static string MapReaderMethod(string dotnetType)
        {
            switch (dotnetType)
            {
                case "bool?":
                    return "GetNullableBool";
                case "double?":
                    return "GetNullableDouble";
                case "global::System.DateTime?":
                    return "GetNullableDateTime";
                case "string?":
                    return "GetString";
                case "ushort?":
                    return "GetUInt16";
                case "global::Opc.Ua.ByteString":
                    return "GetByteString";
                case "global::Opc.Ua.NodeId":
                    return "GetNodeId";
                case "global::Opc.Ua.LocalizedText":
                    return "GetLocalizedText";
                case "global::Opc.Ua.StatusCode":
                    return "GetStatusCode";
                case "global::Opc.Ua.LocalizedText[]?":
                    return "GetLocalizedTextArray";
                default:
                    return null;
            }
        }

        private static string SanitizeDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return string.Empty;
            }
            return description
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the C# namespace for the generated record classes.
        /// </summary>
        private string GetOutputNamespace()
        {
            string @override = m_context.Options?.EventRecordNamespace;
            return string.IsNullOrWhiteSpace(@override)
                ? m_context.ModelDesign.TargetNamespace.Prefix
                : @override;
        }

        /// <summary>
        /// Resolves the fully-qualified base record name for the
        /// emitted record class. For types that derive directly from
        /// <c>BaseEventType</c>, returns the shared
        /// <c>global::Opc.Ua.Client.Alarms.EventRecord</c>; otherwise
        /// returns <c>{ParentNamespace}.{Parent}Record</c>. The chain
        /// stops at <c>BaseEventType</c> so types above it (like
        /// <c>BaseObjectType</c>) never enter the record hierarchy.
        /// </summary>
        private string ResolveBaseRecordName(ObjectTypeDesign objectType)
        {
            // BaseEventType itself derives from BaseObjectType — anchor at
            // EventRecord rather than trying to continue the chain.
            if (objectType.SymbolicId == kBaseEventTypeId)
            {
                return kRootBaseRecord;
            }
            if (objectType.BaseTypeNode is not ObjectTypeDesign parent)

            {
                return kRootBaseRecord;
            }
            string parentName = parent.SymbolicName?.Name;
            if (string.IsNullOrEmpty(parentName))
            {
                return kRootBaseRecord;
            }

            string parentNamespace = ResolveRecordNamespaceForType(parent);
            return CoreUtils.Format(
                "global::{0}.{1}Record",
                parentNamespace,
                parentName);
        }

        /// <summary>
        /// Returns the C# namespace into which the record for
        /// <paramref name="type"/> is (or would be) emitted. Internal
        /// types use the configured output namespace; external types
        /// are looked up via the standard UA mapping and the
        /// user-supplied
        /// <see cref="GeneratorOptions.EventRecordExternalNamespaces"/>
        /// override.
        /// </summary>
        private string ResolveRecordNamespaceForType(TypeDesign type)
        {
            string typeUri = type.SymbolicName?.Namespace;
            string targetUri = m_context.ModelDesign.TargetNamespace?.Value;

            if (!string.IsNullOrEmpty(typeUri) &&
                string.Equals(typeUri, targetUri, StringComparison.Ordinal))
            {
                return GetOutputNamespace();
            }

            if (!string.IsNullOrEmpty(typeUri))
            {
                IDictionary<string, string> overrides =
                    m_context.Options?.EventRecordExternalNamespaces;
                if (overrides != null &&
                    overrides.TryGetValue(typeUri, out string mapped) &&
                    !string.IsNullOrWhiteSpace(mapped))
                {
                    return mapped;
                }

                if (string.Equals(typeUri, kStandardUaNamespaceUri, StringComparison.Ordinal))
                {
                    return kStandardUaRecordNamespace;
                }

                Namespace[] namespaces = m_context.ModelDesign.Namespaces;
                if (namespaces != null)
                {
                    foreach (Namespace ns in namespaces)
                    {
                        if (string.Equals(ns?.Value, typeUri, StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(ns.Prefix))
                        {
                            return ns.Prefix;
                        }
                    }
                }
            }

            return GetOutputNamespace();
        }

        /// <summary>
        /// One row in the generated property list. Surfaces only the
        /// data needed by the FieldProperty + Decoder templates.
        /// </summary>
        private sealed class FieldEntry
        {
            public string PropertyName { get; set; }
            public string DotNetType { get; set; }
            public string Description { get; set; }
            public string BrowseName { get; set; }
            public string ReaderMethod { get; set; }
            public bool IsTwoStateVariableId { get; set; }
            public int FieldIndex { get; set; }
        }

        /// <summary>
        /// Aggregates the per-file registration extension template
        /// state. Used as the iteration target for
        /// <see cref="EventRecordTemplates.RegistrationExtension"/>.
        /// </summary>
        private sealed class RegistrationContext
        {
            public List<ObjectTypeDesign> Types { get; set; }
            public string ClassName { get; set; }
            public string MethodName { get; set; }
        }

        private static readonly XmlQualifiedName kBaseEventTypeId =
            new("BaseEventType", Namespaces.OpcUa);

        private const string kStandardUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string kStandardUaRecordNamespace = "Opc.Ua";
        private const string kRootBaseRecord = "global::Opc.Ua.EventRecord";

        private readonly IGeneratorContext m_context;
    }
}
