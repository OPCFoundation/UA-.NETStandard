/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
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
    /// This generator runs by default for every model; consumers can
    /// suppress it by setting
    /// <see cref="GeneratorOptions.OmitEventRecords"/> to <c>true</c>.
    /// The output namespace defaults to the model's target namespace
    /// prefix and can be overridden via
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

            string fileName = Path.Combine(
                m_context.OutputFolder,
                CoreUtils.Format(
                    "{0}.EventRecords.g.cs",
                    m_context.ModelDesign.TargetNamespace.Prefix));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(fileName);
            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, EventRecordTemplates.File);

            template.AddReplacement(Tokens.Namespace, outputNamespace);
            template.AddReplacement(
                Tokens.ListOfTypes,
                EventRecordTemplates.RecordClass,
                types,
                WriteTemplate_RecordClass);

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

            context.Template.AddReplacement(Tokens.SymbolicName, typeName);
            context.Template.AddReplacement(Tokens.ClassName, className);
            context.Template.AddReplacement(Tokens.BaseClassName, baseClassName);

            List<FieldEntry> fields = CollectDeclaredFields(objectType);
            context.Template.AddReplacement(
                Tokens.ListOfProperties,
                EventRecordTemplates.FieldProperty,
                fields,
                WriteTemplate_FieldProperty);

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
                if (inheritedNames.Contains(browseName))
                {
                    continue;
                }

                if (child is PropertyDesign property)
                {
                    fields.Add(new FieldEntry
                    {
                        PropertyName = browseName,
                        DotNetType = MapDataType(property.DataTypeNode, property.ValueRank),
                        Description = SanitizeDescription(
                            property.Description?.Value)
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
                                $"Id of the {browseName} TwoStateVariable.")
                        });
                        continue;
                    }

                    fields.Add(new FieldEntry
                    {
                        PropertyName = browseName,
                        DotNetType = MapDataType(variable.DataTypeNode, variable.ValueRank),
                        Description = SanitizeDescription(
                            variable.Description?.Value)
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
            if (typeName != null && typeName.EndsWith("?", StringComparison.Ordinal))
            {
                return typeName.Substring(0, typeName.Length - 1);
            }
            return typeName;
        }

        private static string MapScalarDataType(DataTypeDesign dataType)
        {
            string id = dataType.SymbolicId?.Name;
            switch (id)
            {
                case "Boolean": return "bool?";
                case "SByte": return "sbyte?";
                case "Byte": return "byte?";
                case "Int16": return "short?";
                case "UInt16": return "ushort?";
                case "Int32": return "int?";
                case "UInt32": return "uint?";
                case "Int64": return "long?";
                case "UInt64": return "ulong?";
                case "Float": return "float?";
                case "Double": return "double?";
                case "String": return "string?";
                case "DateTime": return "global::System.DateTime?";
                case "UtcTime": return "global::System.DateTime?";
                case "Guid": return "global::System.Guid?";
                case "Duration": return "double?";
                // BuiltIn types that implement INullable — use the
                // type's own .IsNull instead of wrapping in Nullable<T>.
                case "ByteString": return "global::Opc.Ua.ByteString";
                case "XmlElement": return "global::System.Xml.XmlElement";
                case "NodeId": return "global::Opc.Ua.NodeId";
                case "ExpandedNodeId": return "global::Opc.Ua.ExpandedNodeId";
                case "QualifiedName": return "global::Opc.Ua.QualifiedName";
                case "LocalizedText": return "global::Opc.Ua.LocalizedText";
                case "StatusCode": return "global::Opc.Ua.StatusCode";
                default: return "global::Opc.Ua.Variant";
            }
        }

        private static string SanitizeDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return string.Empty;
            }
            return description.Replace("\r", " ").Replace("\n", " ");
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
        /// data needed by the FieldProperty template.
        /// </summary>
        private sealed class FieldEntry
        {
            public string PropertyName { get; set; }
            public string DotNetType { get; set; }
            public string Description { get; set; }
        }

        private static readonly XmlQualifiedName kBaseEventTypeId =
            new XmlQualifiedName("BaseEventType", Namespaces.OpcUa);

        private const string kStandardUaNamespaceUri = "http://opcfoundation.org/UA/";
        private const string kStandardUaRecordNamespace = "Opc.Ua";
        private const string kRootBaseRecord = "global::Opc.Ua.EventRecord";

        private readonly IGeneratorContext m_context;
    }
}