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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua.Export
{
    /// <summary>
    /// A set of nodes in an address space.
    /// </summary>
    public partial class UANodeSet
    {
        /// <summary>
        /// Creates an empty nodeset.
        /// </summary>
        public UANodeSet()
        {
        }

        /// <summary>
        /// Gets a cached <see cref="XmlSerializer"/> instance for
        /// <see cref="UANodeSet"/>. The serializer is lazily created and
        /// reused for all subsequent calls.
        /// </summary>
        /// <remarks>
        /// The suppression is safe because <see cref="UANodeSet"/> and all
        /// reachable types in the object graph are annotated with
        /// <see cref="XmlRootAttribute"/>, <see cref="XmlTypeAttribute"/>,
        /// <see cref="XmlIncludeAttribute"/>, and element/attribute
        /// mapping attributes. The NativeAOT linker preserves these types
        /// through the static attribute references.
        /// </remarks>
        internal static XmlSerializer Serializer => s_serializer.Value;

        [UnconditionalSuppressMessage("AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
        [UnconditionalSuppressMessage("Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
#if NET5_0_OR_GREATER
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UANodeSet))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ModelTableEntry))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NodeIdAlias))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UANode))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAInstance))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAObject))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAVariable))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAMethod))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAView))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAObjectType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAVariableType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAReferenceType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UADataType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DataTypeDefinition))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DataTypeField))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Reference))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LocalizedText))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RolePermission))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UAMethodArgument))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TranslationType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(StructureTranslationType))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NodeSetStatus))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NodeToDelete))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReferenceChange))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UANodeSetChanges))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UANodeSetChangesStatus))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ReleaseStatus))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DataTypePurpose))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(System.Xml.XmlElement))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(XmlDocument))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(XmlNode))]
#endif
        private static XmlSerializer CreateSerializer()
        {
            return new XmlSerializer(typeof(UANodeSet));
        }

        private static readonly Lazy<XmlSerializer> s_serializer = new(CreateSerializer);

#if NET5_0_OR_GREATER
        /// <summary>
        /// Serializes a <see cref="UANodeSet"/> to an <see cref="XmlWriter"/>
        /// using the pre-generated serializer code. This avoids the
        /// reflection-based fallback which fails under NativeAOT.
        /// </summary>
        private static void SerializePreGen(XmlWriter writer, UANodeSet nodeSet)
        {
            var serWriter = new UANodeSetXmlSerializerWriter(writer);
            serWriter.Write26_UANodeSet(nodeSet);
        }

        /// <summary>
        /// Exposes the protected <see cref="XmlSerializationWriter.Writer"/>
        /// property for direct use outside <see cref="XmlSerializer"/>.
        /// </summary>
        private sealed class UANodeSetXmlSerializerWriter
            : Microsoft.Xml.Serialization.GeneratedAssembly.XmlSerializationWriterUANodeSet
        {
            public UANodeSetXmlSerializerWriter(XmlWriter w)
            {
                Writer = w;
            }
        }
#endif

        /// <summary>
        /// Validate the nodeset against the schema.
        /// </summary>
        /// <param name="istrm"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        public static bool Validate(Stream istrm, out IReadOnlyList<string> errors)
        {
            var validationErrors = new List<string>();
            errors = validationErrors;
            bool success = true;
            try
            {
                using Stream schemaContent = Assembly
                    .GetExecutingAssembly()
                    .GetManifestResourceStream("Opc.Ua.Schema.UANodeSet.xsd")!;
                using var schema = XmlReader.Create(schemaContent);
                XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
                settings.Schemas.Add("http://opcfoundation.org/UA/2011/03/UANodeSet.xsd", schema);
                settings.ValidationType = ValidationType.Schema;

                using var reader = XmlReader.Create(istrm, settings);
                var document = new XmlDocument();
                document.Load(reader);

                var eventHandler = new ValidationEventHandler(ValidationEventHandler);
                document.Validate(eventHandler);
                void ValidationEventHandler(object? sender, ValidationEventArgs e)
                {
                    switch (e.Severity)
                    {
                        case XmlSeverityType.Error:
                            validationErrors.Add(CoreUtils.Format("Error: {0}", e.Message));
                            success = false;
                            break;
                        case XmlSeverityType.Warning:
                            validationErrors.Add(CoreUtils.Format("Warning: {0}", e.Message));
                            break;
                    }
                }
                return success;
            }
            catch (XmlSchemaValidationException xve)
            {
                validationErrors.Add(CoreUtils.Format(
                    "XmlSchemaValidationException: {0} at line {1} char: {2}",
                    xve.Message,
                    xve.LineNumber,
                    xve.LinePosition));
                return false;
            }
            catch (Exception e)
            {
                validationErrors.Add(CoreUtils.Format(
                    "{0}: {1}",
                    e.GetType().Name,
                    e.Message));
                return false;
            }
        }

        /// <summary>
        /// Loads a nodeset from a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        /// <returns>The set of nodes</returns>
        [UnconditionalSuppressMessage("AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
        [UnconditionalSuppressMessage("Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
        public static UANodeSet? Read(Stream istrm)
        {
            using var reader = new StreamReader(istrm);
            using var xmlReader = XmlReader.Create(reader, CoreUtils.DefaultXmlReaderSettings());
            return Serializer.Deserialize(xmlReader) as UANodeSet;
        }

        /// <summary>
        /// Write a nodeset to a stream.
        /// </summary>
        /// <param name="istrm">The input stream.</param>
        [UnconditionalSuppressMessage("AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
        [UnconditionalSuppressMessage("Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "UANodeSet and all reachable types are fully " +
                "annotated with XML serialization attributes.")]
        public void Write(Stream istrm)
        {
            XmlWriterSettings setting = CoreUtils.DefaultXmlWriterSettings();
            // Strip duplicate namespace declarations that inner value fragments emit.
            // Combined with the DeclareRootNamespacesWriter below, the xmlns:xsi
            // declaration only appears once (on UANodeSet) instead of once per Value.
            setting.NamespaceHandling = NamespaceHandling.OmitDuplicates;
            var writer = XmlWriter.Create(istrm, setting);
            // Pre-declare xmlns:xsi at the document root so inner value fragments
            // (which contain xsi:nil="true" on unset fields) inherit the binding
            // instead of each fragment declaring its own.
            var rootWriter = new DeclareRootNamespacesWriter(
                writer,
                ("xsi", Namespaces.XmlSchemaInstance));

            try
            {
#if NET5_0_OR_GREATER
                SerializePreGen(rootWriter, this);
#else
                Serializer.Serialize(rootWriter, this, null);
#endif
            }
            finally
            {
                rootWriter.Flush();
                rootWriter.Dispose();
            }
        }

        /// <summary>
        /// A delegating <see cref="XmlWriter"/> that emits a fixed set of
        /// <c>xmlns:prefix</c> declarations on the first (root) element written.
        /// </summary>
        private sealed class DeclareRootNamespacesWriter : XmlWriter
        {
            private readonly XmlWriter m_inner;
            private readonly (string Prefix, string Uri)[] m_declarations;
            private bool m_declared;

            public DeclareRootNamespacesWriter(
                XmlWriter inner,
                params (string Prefix, string Uri)[] declarations)
            {
                m_inner = inner;
                m_declarations = declarations;
            }

            private void DeclareIfNeeded()
            {
                if (m_declared)
                {
                    return;
                }
                m_declared = true;
                foreach ((string prefix, string uri) in m_declarations)
                {
                    m_inner.WriteAttributeString("xmlns", prefix, null, uri);
                }
            }

            public override WriteState WriteState => m_inner.WriteState;

            public override string? LookupPrefix(string ns)
            {
                foreach ((string prefix, string uri) in m_declarations)
                {
                    if (uri == ns)
                    {
                        return prefix;
                    }
                }
                return m_inner.LookupPrefix(ns);
            }

            public override void Flush()
            {
                m_inner.Flush();
            }

            public override void WriteBase64(byte[] buffer, int index, int count)
            {
                m_inner.WriteBase64(buffer, index, count);
            }

            public override void WriteCData(string? text)
            {
                m_inner.WriteCData(text);
            }

            public override void WriteCharEntity(char ch)
            {
                m_inner.WriteCharEntity(ch);
            }

            public override void WriteChars(char[] buffer, int index, int count)
            {
                m_inner.WriteChars(buffer, index, count);
            }

            public override void WriteComment(string? text)
            {
                m_inner.WriteComment(text);
            }

            public override void WriteDocType(string name, string? pubid, string? sysid, string? subset)
            {
                m_inner.WriteDocType(name, pubid, sysid, subset);
            }

            public override void WriteEndAttribute()
            {
                m_inner.WriteEndAttribute();
            }

            public override void WriteEndDocument()
            {
                m_inner.WriteEndDocument();
            }

            public override void WriteEndElement()
            {
                m_inner.WriteEndElement();
            }

            public override void WriteEntityRef(string name)
            {
                m_inner.WriteEntityRef(name);
            }

            public override void WriteFullEndElement()
            {
                m_inner.WriteFullEndElement();
            }

            public override void WriteProcessingInstruction(string name, string? text)
            {
                m_inner.WriteProcessingInstruction(name, text);
            }

            public override void WriteRaw(char[] buffer, int index, int count)
            {
                m_inner.WriteRaw(buffer, index, count);
            }

            public override void WriteRaw(string data)
            {
                m_inner.WriteRaw(data);
            }

            public override void WriteStartAttribute(string? prefix, string localName, string? ns)
            {
                m_inner.WriteStartAttribute(prefix, localName, ns);
            }

            public override void WriteStartDocument()
            {
                m_inner.WriteStartDocument();
            }

            public override void WriteStartDocument(bool standalone)
            {
                m_inner.WriteStartDocument(standalone);
            }

            public override void WriteStartElement(string? prefix, string localName, string? ns)
            {
                m_inner.WriteStartElement(prefix, localName, ns);
                DeclareIfNeeded();
            }

            public override void WriteString(string? text)
            {
                m_inner.WriteString(text);
            }

            public override void WriteSurrogateCharEntity(char lowChar, char highChar)
            {
                m_inner.WriteSurrogateCharEntity(lowChar, highChar);
            }

            public override void WriteWhitespace(string? ws)
            {
                m_inner.WriteWhitespace(ws);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_inner.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Adds an alias to the node set.
        /// </summary>
        public void AddAlias(ISystemContext context, string alias, NodeId nodeId)
        {
            int count = 1;

            if (Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == alias)
                    {
                        Aliases[ii].Value = Export(nodeId, context.NamespaceUris);
                        return;
                    }
                }

                count += Aliases.Length;
            }

            var aliases = new NodeIdAlias[count];

            if (Aliases != null)
            {
                Array.Copy(Aliases, aliases, Aliases.Length);
            }

            aliases[count - 1] = new NodeIdAlias
            {
                Alias = alias,
                Value = Export(nodeId, context.NamespaceUris)
            };
            Aliases = aliases;
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodes">The collection to add imported nodes to.</param>
        /// <param name="linkParentChild">If true, establishes parent-child relationships based on ParentNodeId attributes. Default is false for backward compatibility.</param>
        public void Import(ISystemContext context, NodeStateCollection nodes, bool linkParentChild = false)
        {
            Import(context, nodes, stateFactory: null, linkParentChild);
        }

        /// <summary>
        /// Imports nodes while allowing a server-side caller to replace the
        /// generic state allocated for a resolved discriminator.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodes">The collection to add imported nodes to.</param>
        /// <param name="stateFactory">
        /// Creates an empty state for the node class and resolved discriminator,
        /// or returns <c>null</c> to use the existing generic state.
        /// </param>
        /// <param name="linkParentChild">
        /// If true, establishes parent-child relationships after importing.
        /// </param>
        internal void Import(
            ISystemContext context,
            NodeStateCollection nodes,
            Func<NodeClass, NodeId, NodeId, NodeState?>? stateFactory,
            bool linkParentChild = false)
        {
            if (Items is null)
            {
                return;
            }

            for (int ii = 0; ii < Items.Length; ii++)
            {
                UANode node = Items[ii];
                NodeState importedNode = Import(context, node, stateFactory);
                nodes.Add(importedNode);
            }

            // Link parent-child relationships after all nodes are imported if requested
            if (linkParentChild)
            {
                LinkParentChildRelationships(nodes);
            }
        }

        /// <summary>
        /// Links parent-child relationships for imported nodes.
        /// </summary>
        /// <remarks>
        /// A node may declare a parent that is not part of this batch, because
        /// the parent lives in another NodeSet or is owned by another
        /// NodeManager. That parent cannot be linked in memory here, but it
        /// must not be discarded either: the caller needs it to wire the node
        /// as an external reference. Such a parent is recorded through
        /// <see cref="TryGetUnresolvedParentNodeId"/> instead of being dropped.
        /// </remarks>
        /// <param name="nodes">The collection of imported nodes.</param>
        private static void LinkParentChildRelationships(NodeStateCollection nodes)
        {
            LinkParentChildRelationshipsCore(context: null, nodes);
        }

        /// <summary>
        /// Links parent-child relationships and selects which imported children
        /// should flow through a typed parent's replacement hook.
        /// </summary>
        /// <param name="context">The import context.</param>
        /// <param name="nodes">The collection of imported nodes.</param>
        /// <param name="parentNodeIds">
        /// The authored parent NodeIds captured by the batched importer.
        /// </param>
        /// <param name="useTypedReplacement">
        /// Returns true when the typed replacement hook should be used.
        /// </param>
        internal static void LinkParentChildRelationships(
            ISystemContext context,
            NodeStateCollection nodes,
            IReadOnlyDictionary<BaseInstanceState, NodeId> parentNodeIds,
            Func<NodeState, BaseInstanceState, bool> useTypedReplacement)
        {
            LinkParentChildRelationshipsCore(
                context,
                nodes,
                parentNodeIds,
                useTypedReplacement);
        }

        private static void LinkParentChildRelationshipsCore(
            ISystemContext? context,
            NodeStateCollection nodes,
            IReadOnlyDictionary<BaseInstanceState, NodeId>? parentNodeIds = null,
            Func<NodeState, BaseInstanceState, bool>? useTypedReplacement = null)
        {
            // Create a dictionary for fast lookup of nodes by NodeId
            var nodeTable = new Dictionary<NodeId, NodeState>();
            foreach (NodeState node in nodes)
            {
                if (!node.NodeId.IsNull)
                {
                    nodeTable[node.NodeId] = node;
                }
            }

            // Process each node to establish parent-child relationships
            foreach (NodeState node in nodes)
            {
                if (node is BaseInstanceState instance &&
                    TryGetImportedParent(
                        instance,
                        parentNodeIds,
                        out NodeId parentNodeId,
                        out bool clearHandle))
                {
                    // Legacy import carries the authored parent in Handle and
                    // clears it here. Batched import supplies a side table so
                    // application-owned Handle values remain untouched.
                    if (clearHandle)
                    {
                        instance.Handle = null;
                    }

                    if (nodeTable.TryGetValue(parentNodeId, out NodeState? parent))
                    {
                        if (instance.ReferenceTypeId.IsNull)
                        {
                            instance.ReferenceTypeId = FindParentReferenceType(
                                context,
                                instance,
                                parentNodeId);
                        }

                        // Set the Parent property to establish the relationship
                        instance.Parent = parent;

                        // Add the child to the parent's children collection
                        if (context is null ||
                            useTypedReplacement is null ||
                            !useTypedReplacement(parent, instance) ||
                            parent.FindChild(context, instance.BrowseName) is not null)
                        {
                            parent.AddChild(instance);
                        }
                        else
                        {
                            // A generated replacement hook must adopt the imported
                            // instance directly; copying it would apply import data
                            // twice and leave the batch indexing the wrong object.
                            parent.ReplaceChild(context, instance);
                            if (!ReferenceEquals(
                                parent.FindChild(context, instance.BrowseName),
                                instance))
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadTypeMismatch,
                                    "Typed parent '{0}' did not retain imported child '{1}'. " +
                                    "Register the concrete child import factory.",
                                    parent.NodeId,
                                    instance.NodeId);
                            }
                        }
                        continue;
                    }

                    // Add throws on a duplicate key and AddOrUpdate is not
                    // available on netstandard2.0. Handle is a general-purpose
                    // slot, so a caller can legitimately present the same
                    // instance again carrying an authored parent; the most
                    // recent import wins rather than throwing.
                    s_unresolvedParents.Remove(instance);
                    s_unresolvedParents.Add(instance, new UnresolvedParent(parentNodeId));
                }
            }
        }

        private static bool TryGetImportedParent(
            BaseInstanceState instance,
            IReadOnlyDictionary<BaseInstanceState, NodeId>? parentNodeIds,
            out NodeId parentNodeId,
            out bool clearHandle)
        {
            if (parentNodeIds is not null &&
                parentNodeIds.TryGetValue(instance, out parentNodeId))
            {
                clearHandle = false;
                return true;
            }
            if (instance.Handle is NodeId handleParent)
            {
                parentNodeId = handleParent;
                clearHandle = true;
                return true;
            }

            parentNodeId = NodeId.Null;
            clearHandle = false;
            return false;
        }

        private static NodeId FindParentReferenceType(
            ISystemContext? context,
            BaseInstanceState instance,
            NodeId parentNodeId)
        {
            var references = new List<IReference>();
            instance.GetReferences(context!, references);
            for (int i = 0; i < references.Count; i++)
            {
                IReference reference = references[i];
                if (reference.IsInverse &&
                    !reference.TargetId.IsAbsolute &&
                    (NodeId)reference.TargetId == parentNodeId &&
                    (context is null ||
                        context.TypeTable.IsTypeOf(
                            reference.ReferenceTypeId,
                            ReferenceTypeIds.HierarchicalReferences)))
                {
                    return reference.ReferenceTypeId;
                }
            }

            return instance is PropertyState
                ? ReferenceTypeIds.HasProperty
                : ReferenceTypeIds.HasComponent;
        }

        /// <summary>
        /// Gets the parent a node declared at import when that parent was not
        /// part of the same import batch.
        /// </summary>
        /// <remarks>
        /// The parent of a node owned by another NodeManager cannot be linked
        /// in memory, so a caller that wants the hierarchical reference has to
        /// add it as an external reference. This reports the parent for exactly
        /// those nodes; a node whose parent was linked normally, or that
        /// declared no parent, yields <c>false</c>.
        /// <para>
        /// The record is held in a table keyed by weak reference, so it does
        /// not keep an imported node alive and does not widen
        /// <see cref="NodeState"/>'s public surface, whose
        /// <see cref="NodeState.Handle"/> is a general-purpose slot a caller
        /// may use for its own purposes.
        /// </para>
        /// </remarks>
        /// <param name="node">The imported node.</param>
        /// <param name="parentNodeId">
        /// The unresolved parent NodeId, or <see cref="NodeId.Null"/> when there
        /// is none.
        /// </param>
        /// <returns>
        /// <c>true</c> if the node declared a parent that was not part of the
        /// import batch; otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="node"/> is <c>null</c>.
        /// </exception>
        public static bool TryGetUnresolvedParentNodeId(NodeState node, out NodeId parentNodeId)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (s_unresolvedParents.TryGetValue(node, out UnresolvedParent? parent))
            {
                parentNodeId = parent.NodeId;
                return true;
            }

            parentNodeId = NodeId.Null;
            return false;
        }

        /// <summary>
        /// Boxes the unresolved parent so it can live in a weak-keyed table;
        /// <see cref="NodeId"/> is a value type.
        /// </summary>
        private sealed class UnresolvedParent(NodeId nodeId)
        {
            public NodeId NodeId { get; } = nodeId;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NodeState, UnresolvedParent>
            s_unresolvedParents = new();

        /// <summary>
        /// Adds a node to the set.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void Export(ISystemContext context, NodeState node, bool outputRedundantNames = true)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.NodeId.IsNull)
            {
                throw new ArgumentException("A non-null NodeId must be specified.");
            }

            UANode? exportedNode = null;

            switch (node.NodeClass)
            {
                case NodeClass.Object:
                {
                    var o = (BaseObjectState)node;
                    var value = new UAObject
                    {
                        EventNotifier = o.EventNotifier,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.Variable:
                {
                    var o = (BaseVariableState)node;
                    var value = new UAVariable
                    {
                        DataType = ExportAlias(o.DataType, context.NamespaceUris),
                        ValueRank = o.ValueRank,
                        ArrayDimensions = Export(o.ArrayDimensions),
                        AccessLevel = o.AccessLevelEx,
                        MinimumSamplingInterval = o.MinimumSamplingInterval,
                        Historizing = o.Historizing,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    if (!o.Value.IsNull)
                    {
                        using XmlEncoder encoder = CreateEncoder(context);
                        encoder.WriteVariantValue(null, o.Value);

                        var document = new XmlDocument();
                        document.LoadInnerXml(encoder.CloseAndReturnText()!);
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.Method:
                {
                    var o = (MethodState)node;
                    var value = new UAMethod
                    {
                        Executable = o.Executable,
                        DesignToolOnly = node.DesignToolOnly
                    };

                    if (!o.MethodDeclarationId.IsNull &&
                        o.MethodDeclarationId != o.NodeId)
                    {
                        value.MethodDeclarationId = Export(
                            o.MethodDeclarationId,
                            context.NamespaceUris);
                    }

                    if (o.Parent != null)
                    {
                        value.ParentNodeId = ExportAlias(o.Parent.NodeId, context.NamespaceUris);
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.View:
                {
                    var o = (ViewState)node;
                    exportedNode = new UAView
                    {
                        ContainsNoLoops = o.ContainsNoLoops,
                        DesignToolOnly = node.DesignToolOnly
                    };
                    break;
                }
                case NodeClass.ObjectType:
                {
                    var o = (BaseObjectTypeState)node;
                    exportedNode = new UAObjectType { IsAbstract = o.IsAbstract };
                    break;
                }
                case NodeClass.VariableType:
                {
                    var o = (BaseVariableTypeState)node;
                    var value = new UAVariableType
                    {
                        IsAbstract = o.IsAbstract,
                        DataType = ExportAlias(o.DataType, context.NamespaceUris),
                        ValueRank = o.ValueRank,
                        ArrayDimensions = Export(o.ArrayDimensions)
                    };

                    if (!o.Value.IsNull)
                    {
                        using XmlEncoder encoder = CreateEncoder(context);
                        encoder.WriteVariantValue(null, o.Value);

                        var document = new XmlDocument();
                        document.LoadInnerXml(encoder.CloseAndReturnText()!);
                        value.Value = document.DocumentElement;
                    }

                    exportedNode = value;
                    break;
                }
                case NodeClass.DataType:
                {
                    var o = (DataTypeState)node;
                    exportedNode = new UADataType
                    {
                        IsAbstract = o.IsAbstract,
                        Definition = Export(
                            o,
                            o.DataTypeDefinition,
                            context.NamespaceUris,
                            outputRedundantNames),
                        Purpose = o.Purpose
                    };
                    break;
                }
                case NodeClass.ReferenceType:
                {
                    var o = (ReferenceTypeState)node;
                    var value = new UAReferenceType { IsAbstract = o.IsAbstract };

                    if (!o.InverseName.IsNullOrEmpty)
                    {
                        value.InverseName = Export([o.InverseName]);
                    }

                    value.Symmetric = o.Symmetric;
                    exportedNode = value;
                    break;
                }
                case NodeClass.Unspecified:
                    // Unexpected?
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {node.NodeClass}");
            }

            exportedNode!.NodeId = Export(node.NodeId, context.NamespaceUris);
            exportedNode.BrowseName = Export(node.BrowseName, context.NamespaceUris);

            if (outputRedundantNames || node.DisplayName.Text != node.BrowseName.Name)
            {
                exportedNode.DisplayName = Export([node.DisplayName]);
            }
            else
            {
                exportedNode.DisplayName = null;
            }

            if (!string.IsNullOrEmpty(node.Description.Text))
            {
                exportedNode.Description = Export([node.Description]);
            }
            else
            {
                exportedNode.Description = [];
            }

            exportedNode.Documentation = node.NodeSetDocumentation;
            exportedNode.Category =
                node.Categories != null && node.Categories.Count > 0 ? [.. node.Categories] : null;
            exportedNode.ReleaseStatus = node.ReleaseStatus;
            exportedNode.WriteMask = (uint)node.WriteMask;
            exportedNode.UserWriteMask = (uint)node.UserWriteMask;
            exportedNode.Extensions = node.Extensions?.Select(x => x.AsXmlElement()!).ToArray();
            exportedNode.RolePermissions = null;
            exportedNode.AccessRestrictions = 0;
            exportedNode.AccessRestrictionsSpecified = false;

            if (!node.RolePermissions.IsNull)
            {
                var permissions = new List<RolePermission>();

                foreach (RolePermissionType ii in node.RolePermissions)
                {
                    var permission = new RolePermission
                    {
                        Permissions = ii.Permissions,
                        Value = ExportAlias(ii.RoleId, context.NamespaceUris)
                    };

                    permissions.Add(permission);
                }

                exportedNode.RolePermissions = [.. permissions];
            }

            if (node.AccessRestrictions != null)
            {
                exportedNode.AccessRestrictions = (ushort)node.AccessRestrictions;
                exportedNode.AccessRestrictionsSpecified = true;
            }

            if (!string.IsNullOrEmpty(node.SymbolicName) &&
                node.SymbolicName != node.BrowseName.Name)
            {
                exportedNode.SymbolicName = node.SymbolicName;
            }

            // export references.
            INodeBrowser browser = node.CreateBrowser(
                context,
                null,
                default,
                true,
                BrowseDirection.Both,
                default,
                default,
                true);
            var exportedReferences = new List<Reference>();
            IReference? reference = browser.Next();

            while (reference != null)
            {
                if (node.NodeClass == NodeClass.Method &&
                    !reference.IsInverse &&
                    reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                {
                    reference = browser.Next();
                    continue;
                }

                var exportedReference = new Reference
                {
                    ReferenceType = ExportAlias(reference.ReferenceTypeId, context.NamespaceUris),
                    IsForward = !reference.IsInverse,
                    Value = Export(reference.TargetId, context.NamespaceUris, context.ServerUris)
                };
                exportedReferences.Add(exportedReference);

                reference = browser.Next();
            }

            exportedNode.References = [.. exportedReferences];

            int count = 1;

            // add node to list.
            UANode[] nodes;
            if (Items == null)
            {
                nodes = new UANode[count];
            }
            else
            {
                count += Items.Length;
                nodes = new UANode[count];
                Array.Copy(Items, nodes, Items.Length);
            }

            nodes[count - 1] = exportedNode;

            Items = nodes;

            // recursively process children.
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                Export(context, children[ii], outputRedundantNames);
            }
        }

        /// <summary>
        /// Creates an encoder to save Variant values.
        /// </summary>
        private XmlEncoder CreateEncoder(ISystemContext context)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            var encoder = new XmlEncoder(messageContext);

            var namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            var serverUris = new StringTable();

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    serverUris.GetIndexOrAppend(ServerUris[ii]);
                }
            }

            encoder.SetMappingTables(namespaceUris, serverUris);

            return encoder;
        }

        /// <summary>
        /// Creates an decoder to restore Variant values.
        /// </summary>
        /// <remarks>
        /// A NodeSet writes a Variable's value as the typed element alone —
        /// <c>&lt;uax:String&gt;</c>, <c>&lt;uax:ExtensionObject&gt;</c> and so
        /// on — but <see cref="XmlDecoder.ReadVariant"/> reads the Variant
        /// encoding, which nests that element inside a <c>Value</c> element of
        /// the OPC UA XSD namespace. Decoding the bare element therefore found
        /// no <c>Value</c> to begin and returned a null Variant for every value
        /// in the document. The element is wrapped here so the decoder is given
        /// the shape it reads.
        /// </remarks>
        private XmlDecoder CreateDecoder(ISystemContext context, System.Xml.XmlElement source)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            var decoder = new XmlDecoder(WrapAsVariant(source), messageContext);

            var namespaceUris = new NamespaceTable();

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    namespaceUris.GetIndexOrAppend(NamespaceUris[ii]);
                }
            }

            var serverUris = new StringTable();

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    serverUris.GetIndexOrAppend(ServerUris[ii]);
                }
            }

            decoder.SetMappingTables(namespaceUris, serverUris);

            return decoder;
        }

        /// <summary>
        /// Nests a NodeSet value element inside the <c>Value</c> element the
        /// Variant XML encoding expects, leaving an element that is already a
        /// <c>Value</c> alone.
        /// </summary>
        private static System.Xml.XmlElement WrapAsVariant(System.Xml.XmlElement source)
        {
            if (string.Equals(source.LocalName, "Value", StringComparison.Ordinal) &&
                string.Equals(source.NamespaceURI, Namespaces.OpcUaXsd, StringComparison.Ordinal))
            {
                return source;
            }
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement wrapper = document.CreateElement(
                "uax", "Value", Namespaces.OpcUaXsd);
            document.AppendChild(wrapper);
            wrapper.AppendChild(document.ImportNode(source, deep: true));
            return wrapper;
        }

        /// <summary>
        /// Imports a node from the set.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private NodeState Import(
            ISystemContext context,
            UANode node,
            Func<NodeClass, NodeId, NodeId, NodeState?>? stateFactory)
        {
            NodeState? importedNode = null;

            NodeClass nodeClass = NodeClass.Unspecified;

            if (node is UAObject)
            {
                nodeClass = NodeClass.Object;
            }
            else if (node is UAVariable)
            {
                nodeClass = NodeClass.Variable;
            }
            else if (node is UAMethod)
            {
                nodeClass = NodeClass.Method;
            }
            else if (node is UAObjectType)
            {
                nodeClass = NodeClass.ObjectType;
            }
            else if (node is UAVariableType)
            {
                nodeClass = NodeClass.VariableType;
            }
            else if (node is UADataType)
            {
                nodeClass = NodeClass.DataType;
            }
            else if (node is UAReferenceType)
            {
                nodeClass = NodeClass.ReferenceType;
            }
            else if (node is UAView)
            {
                nodeClass = NodeClass.View;
            }

            NodeId discriminatorId = nodeClass switch
            {
                NodeClass.Object or NodeClass.Variable =>
                    ImportTypeDefinitionId(context, node),
                NodeClass.Method => ImportNodeId(
                    ((UAMethod)node).MethodDeclarationId,
                    context.NamespaceUris,
                    true),
                NodeClass.ObjectType or
                NodeClass.VariableType or
                NodeClass.DataType or
                NodeClass.ReferenceType or
                NodeClass.View => ImportNodeId(
                    node.NodeId,
                    context.NamespaceUris,
                    false),
                _ => NodeId.Null
            };

            NodeId importedNodeId = ImportNodeId(
                node.NodeId,
                context.NamespaceUris,
                false);
            importedNode = stateFactory?.Invoke(
                nodeClass,
                importedNodeId,
                discriminatorId);

            switch (nodeClass)
            {
                case NodeClass.Object:
                {
                    var o = (UAObject)node;
                    BaseObjectState value;
                    if (importedNode is null)
                    {
                        value = new BaseObjectState(null);
                    }
                    else if (importedNode is BaseObjectState objectState)
                    {
                        value = objectState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(BaseObjectState));
                    }

                    value.EventNotifier = o.EventNotifier;
                    value.DesignToolOnly = o.DesignToolOnly;
                    importedNode = value;
                    break;
                }
                case NodeClass.Variable:
                {
                    var o = (UAVariable)node;

                    BaseVariableState value;
                    if (importedNode is null)
                    {
                        if (discriminatorId == VariableTypeIds.PropertyType)
                        {
                            value = new PropertyState(null);
                        }
                        else
                        {
                            value = new BaseDataVariableState(null);
                        }
                    }
                    else if (importedNode is BaseVariableState variableState)
                    {
                        value = variableState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(BaseVariableState));
                    }

                    value.DataType = ImportNodeId(o.DataType, context.NamespaceUris, true);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = ImportArrayDimensions(o.ArrayDimensions) ?? [];
                    value.AccessLevelEx = o.AccessLevel;
                    value.UserAccessLevel = o.UserAccessLevelSpecified
                        ? (byte)(o.UserAccessLevel & 0xFF)
                        : (byte)(o.AccessLevel & 0xFF);
                    value.MinimumSamplingInterval = o.MinimumSamplingInterval;
                    value.Historizing = o.Historizing;
                    value.DesignToolOnly = o.DesignToolOnly;

                    if (o.Value != null)
                    {
                        using XmlDecoder decoder = CreateDecoder(context, o.Value);
                        value.Value = decoder.ReadVariant(null);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }
                case NodeClass.Method:
                {
                    var o = (UAMethod)node;
                    MethodState value;
                    if (importedNode is null)
                    {
                        value = new MethodState(null);
                    }
                    else if (importedNode is MethodState methodState)
                    {
                        value = methodState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(MethodState));
                    }

                    value.Executable = o.Executable;
                    value.UserExecutable = o.Executable;
                    value.MethodDeclarationId = discriminatorId;
                    value.DesignToolOnly = o.DesignToolOnly;
                    importedNode = value;
                    break;
                }
                case NodeClass.View:
                {
                    var o = (UAView)node;
                    ViewState value;
                    if (importedNode is null)
                    {
                        value = new ViewState();
                    }
                    else if (importedNode is ViewState viewState)
                    {
                        value = viewState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(ViewState));
                    }

                    value.ContainsNoLoops = o.ContainsNoLoops;
                    value.DesignToolOnly = o.DesignToolOnly;
                    importedNode = value;
                    break;
                }
                case NodeClass.ObjectType:
                {
                    var o = (UAObjectType)node;
                    BaseObjectTypeState value;
                    if (importedNode is null)
                    {
                        value = new BaseObjectTypeState();
                    }
                    else if (importedNode is BaseObjectTypeState objectTypeState)
                    {
                        value = objectTypeState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(BaseObjectTypeState));
                    }

                    value.IsAbstract = o.IsAbstract;
                    importedNode = value;
                    break;
                }
                case NodeClass.VariableType:
                {
                    var o = (UAVariableType)node;
                    BaseVariableTypeState value;
                    if (importedNode is null)
                    {
                        value = new BaseDataVariableTypeState();
                    }
                    else if (importedNode is BaseVariableTypeState variableTypeState)
                    {
                        value = variableTypeState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(BaseVariableTypeState));
                    }

                    value.IsAbstract = o.IsAbstract;
                    value.DataType = ImportNodeId(o.DataType, context.NamespaceUris, true);
                    value.ValueRank = o.ValueRank;
                    value.ArrayDimensions = ImportArrayDimensions(o.ArrayDimensions) ?? [];

                    if (o.Value != null)
                    {
                        using XmlDecoder decoder = CreateDecoder(context, o.Value);
                        value.Value = decoder.ReadVariant(null);
                        decoder.Close();
                    }

                    importedNode = value;
                    break;
                }
                case NodeClass.DataType:
                {
                    var o = (UADataType)node;
                    DataTypeState value;
                    if (importedNode is null)
                    {
                        value = new DataTypeState();
                    }
                    else if (importedNode is DataTypeState dataTypeState)
                    {
                        value = dataTypeState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(DataTypeState));
                    }

                    value.IsAbstract = o.IsAbstract;
                    Ua.DataTypeDefinition? dataTypeDefinition = Import(
                        o.Definition!,
                        context.NamespaceUris);
                    value.DataTypeDefinition = new ExtensionObject(dataTypeDefinition!);
                    value.Purpose = o.Purpose;
                    importedNode = value;
                    break;
                }
                case NodeClass.ReferenceType:
                {
                    var o = (UAReferenceType)node;
                    ReferenceTypeState value;
                    if (importedNode is null)
                    {
                        value = new ReferenceTypeState();
                    }
                    else if (importedNode is ReferenceTypeState referenceTypeState)
                    {
                        value = referenceTypeState;
                    }
                    else
                    {
                        throw CreateImportStateTypeMismatch(
                            nodeClass,
                            importedNode,
                            typeof(ReferenceTypeState));
                    }

                    value.IsAbstract = o.IsAbstract;
                    value.InverseName = Import(o.InverseName);
                    value.Symmetric = o.Symmetric;
                    importedNode = value;
                    break;
                }
                case NodeClass.Unspecified:
                    break;
                default:
                    throw ServiceResultException.Unexpected($"Unexpected NodeClass {nodeClass}");
            }

            importedNode!.NodeId = ImportNodeId(node.NodeId, context.NamespaceUris, false);
            importedNode.BrowseName = ImportQualifiedName(node.BrowseName, context.NamespaceUris);
            importedNode.DisplayName = Import(node.DisplayName);
            if (importedNode.DisplayName.IsNullOrEmpty)
            {
                importedNode.DisplayName = new Ua.LocalizedText(importedNode.BrowseName.Name);
            }

            importedNode.Description = Import(node.Description);
            importedNode.NodeSetDocumentation = node.Documentation;
            importedNode.Categories = node.Category != null && node.Category.Length > 0
                ? node.Category
                : null;
            importedNode.ReleaseStatus = node.ReleaseStatus;
            importedNode.WriteMask = (AttributeWriteMask)node.WriteMask;
            importedNode.UserWriteMask = (AttributeWriteMask)node.UserWriteMask;
            importedNode.Extensions = node.Extensions?.Select(x => XmlElement.From(x)).ToArray();

            if (node.RolePermissions != null)
            {
                var permissions = new List<RolePermissionType>();

                foreach (RolePermission ii in node.RolePermissions)
                {
                    var permission = new RolePermissionType
                    {
                        Permissions = ii.Permissions,
                        RoleId = ImportNodeId(ii.Value, context.NamespaceUris, true)
                    };

                    permissions.Add(permission);
                }

                importedNode.RolePermissions = permissions;
            }

            if (node.AccessRestrictionsSpecified)
            {
                importedNode.AccessRestrictions = (AccessRestrictionType?)node.AccessRestrictions;
            }

            if (!string.IsNullOrEmpty(node.SymbolicName))
            {
                importedNode.SymbolicName = node.SymbolicName!;
            }

            if (node.References != null)
            {
                for (int ii = 0; ii < node.References.Length; ii++)
                {
                    NodeId referenceTypeId = ImportNodeId(
                        node.References[ii].ReferenceType,
                        context.NamespaceUris,
                        true);
                    bool isInverse = !node.References[ii].IsForward;
                    ExpandedNodeId targetId = ImportExpandedNodeId(
                        node.References[ii].Value,
                        context.NamespaceUris,
                        context.ServerUris);

                    if (importedNode is BaseInstanceState instance)
                    {
                        if (referenceTypeId == ReferenceTypeIds.HasModellingRule && !isInverse)
                        {
                            instance.ModellingRuleId = ExpandedNodeId.ToNodeId(
                                targetId,
                                context.NamespaceUris);
                            continue;
                        }

                        if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                        {
                            instance.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                                targetId,
                                context.NamespaceUris);
                            continue;
                        }
                    }

                    if (importedNode is BaseTypeState type &&
                        referenceTypeId == ReferenceTypeIds.HasSubtype &&
                        isInverse)
                    {
                        type.SuperTypeId = ExpandedNodeId.ToNodeId(targetId, context.NamespaceUris);
                        continue;
                    }

                    importedNode.AddReference(referenceTypeId, isInverse, targetId);
                }
            }

            string? parentNodeId = (node as UAInstance)?.ParentNodeId;

            if (!string.IsNullOrEmpty(parentNodeId))
            {
                // set parent NodeId in Handle property.
                importedNode.Handle = ImportNodeId(parentNodeId, context.NamespaceUris, true);
            }

            return importedNode;
        }

        private NodeId ImportTypeDefinitionId(
            ISystemContext context,
            UANode node)
        {
            if (node.References is null)
            {
                return NodeId.Null;
            }

            for (int ii = 0; ii < node.References.Length; ii++)
            {
                NodeId referenceTypeId = ImportNodeId(
                    node.References[ii].ReferenceType,
                    context.NamespaceUris,
                    true);
                bool isInverse = !node.References[ii].IsForward;
                ExpandedNodeId targetId = ImportExpandedNodeId(
                    node.References[ii].Value,
                    context.NamespaceUris,
                    context.ServerUris);

                if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition && !isInverse)
                {
                    return ExpandedNodeId.ToNodeId(
                        targetId,
                        context.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        private static ServiceResultException CreateImportStateTypeMismatch(
            NodeClass nodeClass,
            NodeState state,
            Type expectedType)
        {
            return ServiceResultException.Create(
                StatusCodes.BadTypeMismatch,
                "The import state factory returned '{0}' for {1}; expected a state assignable to '{2}'.",
                state.GetType().FullName ?? state.GetType().Name,
                nodeClass,
                expectedType.FullName ?? expectedType.Name);
        }

        /// <summary>
        /// Exports a NodeId as an alias.
        /// </summary>
        private string? ExportAlias(NodeId source, NamespaceTable namespaceUris)
        {
            string nodeId = Export(source, namespaceUris);

            if (!string.IsNullOrEmpty(nodeId) && Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Value == nodeId)
                    {
                        return Aliases[ii].Alias;
                    }
                }
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a NodeId
        /// </summary>
        private string Export(NodeId source, NamespaceTable namespaceUris)
        {
            if (source.IsNull)
            {
                return string.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = source.WithNamespaceIndex(namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        ///  Imports a NodeId
        /// </summary>
        private NodeId ImportNodeId(string? source, NamespaceTable namespaceUris, bool lookupAlias)
        {
            if (string.IsNullOrEmpty(source))
            {
                return NodeId.Null;
            }

            // lookup alias.
            bool aliasRequested = lookupAlias;
            if (lookupAlias && Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == source)
                    {
                        source = Aliases[ii].Value;
                        aliasRequested = false;
                        break;
                    }
                }
            }

            // parse the string.
            NodeId nodeId;
            try
            {
                nodeId = NodeId.Parse(source!);
            }
            catch (Exception exception) when (aliasRequested &&
                exception is ArgumentException or ServiceResultException)
            {
                // A name that is not a NodeId and not in <Aliases> is almost always
                // an alias the document forgot to declare. Parsing it reports only
                // that an identifier is missing, which names neither the value nor
                // the fact that an alias was expected, so say so here. Parse throws
                // ArgumentException for that case and ServiceResultException for
                // others, and both mean the same thing to the document's author.
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    exception,
                    "'{0}' is neither a NodeId nor a declared alias. A NodeSet that " +
                        "uses an alias shall declare it in <Aliases>.",
                    source!);
            }

            if (nodeId.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);
                nodeId = nodeId.WithNamespaceIndex(namespaceIndex);
            }

            return nodeId;
        }

        /// <summary>
        /// Exports a ExpandedNodeId
        /// </summary>
        private string Export(
            ExpandedNodeId source,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (source.IsNull)
            {
                return string.Empty;
            }

            if (source.ServerIndex <= 0 &&
                source.NamespaceIndex <= 0 &&
                string.IsNullOrEmpty(source.NamespaceUri))
            {
                return source.ToString();
            }

            ushort namespaceIndex;
            if (string.IsNullOrEmpty(source.NamespaceUri))
            {
                namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
            }
            else
            {
                namespaceIndex = ExportNamespaceUri(source.NamespaceUri!, namespaceUris);
            }

            uint serverIndex = ExportServerIndex(source.ServerIndex, serverUris);
            source = source.WithNamespaceIndex(namespaceIndex).WithServerIndex(serverIndex);
            return source.ToString();
        }

        /// <summary>
        /// Imports a ExpandedNodeId
        /// </summary>
        private ExpandedNodeId ImportExpandedNodeId(
            string? source,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            if (string.IsNullOrEmpty(source))
            {
                return ExpandedNodeId.Null;
            }
            // lookup aliases
            if (Aliases != null)
            {
                for (int ii = 0; ii < Aliases.Length; ii++)
                {
                    if (Aliases[ii].Alias == source)
                    {
                        source = Aliases[ii].Value;
                        break;
                    }
                }
            }

            // parse the node.
            var nodeId = ExpandedNodeId.Parse(source!);

            if (nodeId.ServerIndex <= 0 &&
                nodeId.NamespaceIndex <= 0 &&
                string.IsNullOrEmpty(nodeId.NamespaceUri))
            {
                return nodeId;
            }

            uint serverIndex = ImportServerIndex(nodeId.ServerIndex, serverUris);
            ushort namespaceIndex = ImportNamespaceIndex(nodeId.NamespaceIndex, namespaceUris);

            if (serverIndex > 0)
            {
                string? namespaceUri = nodeId.NamespaceUri;

                if (string.IsNullOrEmpty(nodeId.NamespaceUri))
                {
                    namespaceUri = namespaceUris.GetString(namespaceIndex);
                }

                return nodeId.WithNamespaceUri(namespaceUri).WithServerIndex(serverIndex);
            }

            return nodeId.WithNamespaceIndex(namespaceIndex).WithServerIndex(0);
        }

        /// <summary>
        /// Exports a QualifiedName
        /// </summary>
        private string Export(QualifiedName source, NamespaceTable namespaceUris)
        {
            if (source.IsNull)
            {
                return string.Empty;
            }

            if (source.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ExportNamespaceIndex(source.NamespaceIndex, namespaceUris);
                source = new QualifiedName(source.Name, namespaceIndex);
            }

            return source.ToString();
        }

        /// <summary>
        /// Exports a DataTypeDefinition
        /// </summary>
        private DataTypeDefinition? Export(
            DataTypeState dataType,
            ExtensionObject source,
            NamespaceTable namespaceUris,
            bool outputRedundantNames)
        {
            if (source.IsNull)
            {
                return null;
            }

            var definition = new DataTypeDefinition();

            if (outputRedundantNames || !dataType.BrowseName.IsNull)
            {
                definition.Name = Export(dataType.BrowseName, namespaceUris);
            }

            if (dataType.BrowseName.Name != dataType.SymbolicName)
            {
                definition.SymbolicName = dataType.SymbolicName;
            }

            if (source.TryGetValue(out StructureDefinition? sd) && sd != null)
            {
                if (sd
                    .StructureType is StructureType.Union or StructureType.UnionWithSubtypedValues)
                {
                    definition.IsUnion = true;
                }

                if (!sd.Fields.IsNull)
                {
                    var fields = new List<DataTypeField>();

                    for (int ii = sd.FirstExplicitFieldIndex; ii < sd.Fields.Count; ii++)
                    {
                        StructureField field = sd.Fields[ii];

                        var output = new DataTypeField
                        {
                            Name = field.Name,
                            Description = Export([field.Description])
                        };

                        if (sd.StructureType == StructureType.StructureWithOptionalFields)
                        {
                            output.IsOptional = field.IsOptional;
                            output.AllowSubTypes = false;
                        }
                        else if (sd.StructureType
                            is StructureType.StructureWithSubtypedValues
                                or StructureType.UnionWithSubtypedValues)
                        {
                            output.IsOptional = false;
                            output.AllowSubTypes = field.IsOptional;
                        }
                        else
                        {
                            output.IsOptional = false;
                            output.AllowSubTypes = false;
                        }

                        if (field.DataType.IsNull)
                        {
                            output.DataType = Export(DataTypeIds.BaseDataType, namespaceUris);
                        }
                        else
                        {
                            output.DataType = Export(field.DataType, namespaceUris);
                        }

                        output.ValueRank = field.ValueRank;

                        if (!field.ArrayDimensions.IsEmpty)
                        {
                            if (output.ValueRank > 1 || field.ArrayDimensions[0] > 0)
                            {
                                output.ArrayDimensions = BaseVariableState.ArrayDimensionsToXml(
                                    field.ArrayDimensions);
                            }
                        }

                        output.MaxStringLength = field.MaxStringLength;

                        fields.Add(output);
                    }

                    definition.Field = [.. fields];
                }
            }

            if (source.TryGetValue(out EnumDefinition? ed) && ed != null)
            {
                definition.IsOptionSet = ed.IsOptionSet;

                if (!ed.Fields.IsNull)
                {
                    var fields = new List<DataTypeField>();

                    foreach (EnumField field in ed.Fields)
                    {
                        var output = new DataTypeField { Name = field.Name };

                        if (!field.DisplayName.IsNullOrEmpty && output.Name != field.DisplayName.Text)
                        {
                            output.DisplayName = Export([field.DisplayName]);
                        }
                        else
                        {
                            output.DisplayName = [];
                        }

                        output.Description = Export([field.Description]);
                        output.ValueRank = ValueRanks.Scalar;
                        output.Value = (int)field.Value;

                        fields.Add(output);
                    }

                    definition.Field = [.. fields];
                }
            }

            return definition;
        }

        /// <summary>
        /// Imports a DataTypeDefinition
        /// </summary>
        private Ua.DataTypeDefinition? Import(
            DataTypeDefinition source,
            NamespaceTable namespaceUris)
        {
            if (source == null)
            {
                return null;
            }

            Ua.DataTypeDefinition? definition = null;

            if (source.Field != null)
            {
                // check if definition is for enumeration or structure.
                bool isEnumeration = Array.Exists(
                    source.Field,
                    fieldLookup => fieldLookup.Value != -1);

                if (!isEnumeration)
                {
                    var sd = new StructureDefinition
                    {
                        BaseDataType = ImportNodeId(source.BaseType, namespaceUris, true)
                    };

                    if (source.IsUnion)
                    {
                        sd.StructureType = StructureType.Union;
                    }

                    if (source.Field != null)
                    {
                        var fields = new List<StructureField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            if (sd.StructureType is StructureType.Structure or StructureType.Union)
                            {
                                if (field.IsOptional)
                                {
                                    sd.StructureType = StructureType.StructureWithOptionalFields;
                                }
                                else if (field.AllowSubTypes)
                                {
                                    if (source.IsUnion)
                                    {
                                        sd.StructureType = StructureType.UnionWithSubtypedValues;
                                    }
                                    else
                                    {
                                        sd.StructureType
                                            = StructureType.StructureWithSubtypedValues;
                                    }
                                }
                            }

                            var output = new StructureField
                            {
                                Name = field.Name!,
                                Description = Import(field.Description),
                                DataType = ImportNodeId(field.DataType, namespaceUris, true),
                                ValueRank = field.ValueRank
                            };
                            if (!string.IsNullOrWhiteSpace(field.ArrayDimensions))
                            {
                                if (output.ValueRank > 1 || field.ArrayDimensions![0] > 0)
                                {
                                    output.ArrayDimensions =
                                    [
                                        .. BaseVariableState.ArrayDimensionsFromXml(
                                            field.ArrayDimensions)
                                    ];
                                }
                            }

                            output.MaxStringLength = field.MaxStringLength;

                            if (sd.StructureType is StructureType.Structure or StructureType.Union)
                            {
                                output.IsOptional = false;
                            }
                            else if (sd.StructureType
                                is StructureType.StructureWithSubtypedValues
                                    or StructureType.UnionWithSubtypedValues)
                            {
                                output.IsOptional = field.AllowSubTypes;
                            }
                            else
                            {
                                output.IsOptional = field.IsOptional;
                            }

                            fields.Add(output);
                        }

                        sd.Fields = fields;
                    }

                    definition = sd;
                }
                else
                {
                    var ed = new EnumDefinition
                    {
                        IsOptionSet = source.IsOptionSet
                    };

                    if (source.Field != null)
                    {
                        var fields = new List<EnumField>();

                        foreach (DataTypeField field in source.Field)
                        {
                            var output = new EnumField
                            {
                                Name = field.Name!,
                                DisplayName = Import(field.DisplayName),
                                Description = Import(field.Description),
                                Value = field.Value
                            };

                            fields.Add(output);
                        }

                        ed.Fields = fields;
                    }

                    definition = ed;
                }
            }

            return definition;
        }

        /// <summary>
        /// Imports a QualifiedName
        /// </summary>
        private QualifiedName ImportQualifiedName(string? source, NamespaceTable namespaceUris)
        {
            if (string.IsNullOrEmpty(source))
            {
                return QualifiedName.Null;
            }

            var qname = QualifiedName.Parse(source!);

            if (qname.NamespaceIndex > 0)
            {
                ushort namespaceIndex = ImportNamespaceIndex(qname.NamespaceIndex, namespaceUris);
                qname = new QualifiedName(qname.Name, namespaceIndex);
            }

            return qname;
        }

        /// <summary>
        /// Exports the array dimensions.
        /// </summary>
        private static string Export(ArrayOf<uint> arrayDimensions)
        {
            if (arrayDimensions.IsEmpty)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder();

            for (int ii = 0; ii < arrayDimensions.Count; ii++)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(',');
                }

                buffer.Append(arrayDimensions[ii]);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Imports the array dimensions.
        /// </summary>
        private static uint[]? ImportArrayDimensions(string? arrayDimensions)
        {
            if (string.IsNullOrEmpty(arrayDimensions))
            {
                return null;
            }

            string[] fields = arrayDimensions!.Split(',');
            uint[] dimensions = new uint[fields.Length];

            for (int ii = 0; ii < fields.Length; ii++)
            {
                try
                {
                    dimensions[ii] = Convert.ToUInt32(fields[ii], CultureInfo.InvariantCulture);
                }
                catch
                {
                    dimensions[ii] = 0;
                }
            }

            return dimensions;
        }

        /// <summary>
        /// Exports localized text.
        /// </summary>
        private static LocalizedText[]? Export(Ua.LocalizedText[] input)
        {
            if (input == null)
            {
                return null;
            }

            var output = new List<LocalizedText>();

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (!input[ii].IsNullOrEmpty)
                {
                    var text = new LocalizedText
                    {
                        Locale = input[ii].Locale,
                        Value = input[ii].Text
                    };
                    output.Add(text);
                }
            }

            return [.. output];
        }

#if UNUSED
        /// <summary>
        /// Exports localized text.
        /// </summary>
        private static LocalizedText Export(Ua.LocalizedText input)
        {
            if (input == null)
            {
                return null;
            }

            return new LocalizedText { Locale = input.Locale, Value = input.Text };
        }
#endif

        /// <summary>
        /// Imports localized text.
        /// </summary>
        private static Ua.LocalizedText Import(params LocalizedText[]? input)
        {
            if (input == null)
            {
                return default;
            }

            for (int ii = 0; ii < input.Length; ii++)
            {
                if (input[ii] != null)
                {
                    return new Ua.LocalizedText(input[ii].Locale, input[ii].Value);
                }
            }

            return default;
        }

        /// <summary>
        /// Exports a namespace index.
        /// </summary>
        private ushort ExportNamespaceIndex(ushort namespaceIndex, NamespaceTable namespaceUris)
        {
            // nothing special required for indexes 0.
            if (namespaceIndex < 1)
            {
                return namespaceIndex;
            }

            // return a bad value if parameters are bad.
            if (namespaceUris == null || namespaceUris.Count <= namespaceIndex)
            {
                return ushort.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string? targetUri = namespaceUris.GetString(namespaceIndex);

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    if (NamespaceUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (NamespaceUris != null)
            {
                Array.Copy(NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = targetUri!;
            NamespaceUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a namespace index.
        /// </summary>
        private ushort ImportNamespaceIndex(ushort namespaceIndex, NamespaceTable namespaceUris)
        {
            // nothing special required for indexes 0 and 1.
            if (namespaceIndex < 1)
            {
                return namespaceIndex;
            }

            // return a bad value if parameters are bad.
            if (namespaceUris == null ||
                NamespaceUris == null ||
                NamespaceUris.Length <= namespaceIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return namespaceUris.GetIndexOrAppend(NamespaceUris[namespaceIndex - 1]);
        }

        /// <summary>
        /// Exports a namespace uri.
        /// </summary>
        private ushort ExportNamespaceUri(string namespaceUri, NamespaceTable namespaceUris)
        {
            // return a bad value if parameters are bad.
            if (namespaceUris == null)
            {
                return ushort.MaxValue;
            }

            int namespaceIndex = namespaceUris.GetIndex(namespaceUri);

            // nothing special required for the first two URIs.
            if (namespaceIndex == 0)
            {
                return (ushort)namespaceIndex;
            }

            // find an existing index.
            int count = 1;

            if (NamespaceUris != null)
            {
                for (int ii = 0; ii < NamespaceUris.Length; ii++)
                {
                    if (NamespaceUris[ii] == namespaceUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += NamespaceUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (NamespaceUris != null)
            {
                Array.Copy(NamespaceUris, uris, count - 1);
            }

            uris[count - 1] = namespaceUri;
            NamespaceUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a server index.
        /// </summary>
        private uint ExportServerIndex(uint serverIndex, StringTable serverUris)
        {
            // nothing special required for indexes 0.
            if (serverIndex <= 0)
            {
                return serverIndex;
            }

            // return a bad value if parameters are bad.
            if (serverUris == null || serverUris.Count < serverIndex)
            {
                return ushort.MaxValue;
            }

            // find an existing index.
            int count = 1;
            string? targetUri = serverUris.GetString(serverIndex);

            if (ServerUris != null)
            {
                for (int ii = 0; ii < ServerUris.Length; ii++)
                {
                    if (ServerUris[ii] == targetUri)
                    {
                        return (ushort)(ii + 1); // add 1 to adjust for the well-known URIs which are not stored.
                    }
                }

                count += ServerUris.Length;
            }

            // add a new entry.
            string[] uris = new string[count];

            if (ServerUris != null)
            {
                Array.Copy(ServerUris, uris, count - 1);
            }

            uris[count - 1] = targetUri!;
            ServerUris = uris;

            // return the new index.
            return (ushort)count;
        }

        /// <summary>
        /// Exports a server index.
        /// </summary>
        private uint ImportServerIndex(uint serverIndex, StringTable serverUris)
        {
            // nothing special required for indexes 0.
            if (serverIndex <= 0)
            {
                return serverIndex;
            }

            // return a bad value if parameters are bad.
            if (serverUris == null || ServerUris == null || ServerUris.Length <= serverIndex - 1)
            {
                return ushort.MaxValue;
            }

            // find or append uri.
            return serverUris.GetIndexOrAppend(ServerUris[serverIndex - 1]);
        }
    }
}
