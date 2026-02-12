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
using System.Threading;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for custom nodes.
    /// </summary>
    public abstract partial class NodeState : IDisposable, IFormattable, ICloneable
    {
        /// <summary>
        /// Creates an empty object.
        /// </summary>G
        /// <param name="nodeClass">The node class.</param>
        protected NodeState(NodeClass nodeClass)
        {
            NodeClass = nodeClass;
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // does nothing.
        }

        /// <inheritdoc/>
        public abstract object Clone();

        /// <inheritdoc/>
        public virtual bool DeepEquals(NodeState node)
        {
            // Compare references and children
            if (ReferenceEquals(this, node))
            {
                return true;
            }

            if (node is not null &&
                EqualityComparer<object>.Default.Equals(Handle, node.Handle) &&
                ChangeMasks == node.ChangeMasks &&
                SymbolicName == node.SymbolicName &&
                NodeId == node.NodeId &&
                NodeClass == node.NodeClass &&
                BrowseName == node.BrowseName &&
                DisplayName == node.DisplayName &&
                Description == node.Description &&
                WriteMask == node.WriteMask &&
                UserWriteMask == node.UserWriteMask &&
                EqualityComparer<RolePermissionTypeCollection>.Default.Equals(
                    RolePermissions, node.RolePermissions) &&
                EqualityComparer<RolePermissionTypeCollection>.Default.Equals(
                    UserRolePermissions, node.UserRolePermissions) &&
                AccessRestrictions == node.AccessRestrictions &&
                AreEventsMonitored == node.AreEventsMonitored &&
                Initialized == node.Initialized &&
                ValidationRequired == node.ValidationRequired &&

                // TODO: Remove below as not needed during runtime
                EqualityComparer<XmlElement[]>.Default.Equals(
                    Extensions, node.Extensions) &&
                EqualityComparer<IList<string>>.Default.Equals(
                    Categories, node.Categories) &&
                ReleaseStatus == node.ReleaseStatus &&
                Specification == node.Specification &&
                NodeSetDocumentation == node.NodeSetDocumentation &&
                DesignToolOnly == node.DesignToolOnly)
            {
                lock (m_referencesLock)
                {
                    if (!ArrayEqualityComparer<IReference>.Default.Equals(
                        m_references?.Keys.ToArray(), node.m_references?.Keys.ToArray()))
                    {
                        return false;
                    }
                }

                lock (m_childrenLock)
                {
                    if (!ArrayEqualityComparer<BaseInstanceState>.Default.Equals(
                        m_children?.ToArray(), node.m_children?.ToArray()))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public virtual int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ChangeMasks);
            hash.Add(SymbolicName);
            hash.Add(NodeId);
            hash.Add(NodeClass);
            hash.Add(BrowseName);
            hash.Add(DisplayName);
            hash.Add(Description);
            hash.Add(WriteMask);
            hash.Add(UserWriteMask);
            hash.Add(RolePermissions);
            hash.Add(UserRolePermissions);
            hash.Add(AccessRestrictions);
            hash.Add(Extensions);
            hash.Add(Categories);
            hash.Add(ReleaseStatus);
            hash.Add(Specification);
            hash.Add(NodeSetDocumentation);
            hash.Add(DesignToolOnly);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Copy all state to the target node state. This performs
        /// the actual deep copy of the node state.
        /// </summary>
        /// <param name="target"></param>
        protected virtual void CopyTo(NodeState target)
        {
            target.Handle = Handle;
            target.SymbolicName = SymbolicName;
            target.NodeClass = NodeClass;
            target.m_nodeId = m_nodeId;
            target.m_browseName = m_browseName;
            target.m_displayName = m_displayName;
            target.m_description = m_description;
            target.m_writeMask = m_writeMask;
            target.m_changeMasks = m_changeMasks;

            target.RolePermissions = RolePermissions;
            target.UserRolePermissions = UserRolePermissions;

            lock (m_referencesLock)
            {
                if (m_references != null)
                {
                    target.AddReferences([.. m_references.Keys]);
                }
            }

            List<BaseInstanceState> children;
            lock (m_childrenLock)
            {
                children = m_children != null ? [.. m_children] : null;
            }
            if (children != null)
            {
                target.m_children = new List<BaseInstanceState>(children.Count);
                for (int ii = 0; ii < children.Count; ii++)
                {
                    var child = (BaseInstanceState)children[ii].Clone();
                    target.m_children.Add(child);
                }
            }

            // TODO: Remove below as not needed during runtime
            target.Categories = Categories;
            target.Specification = Specification;
            target.DesignToolOnly = DesignToolOnly;
            target.ReleaseStatus = ReleaseStatus;
            target.NodeSetDocumentation = NodeSetDocumentation;
            target.Extensions = Extensions;
        }

        /// <summary>
        /// Makes a copy of all children.
        /// Children must implement MemberwiseClone or ICloneable.
        /// </summary>
        protected object CloneChildren(NodeState clone)
        {
            List<BaseInstanceState> children;

            lock (m_childrenLock)
            {
                children = m_children != null ? [.. m_children] : null;
            }

            if (children != null)
            {
                clone.m_children = new List<BaseInstanceState>(children.Count);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    var child = (BaseInstanceState)children[ii].Clone();
                    clone.m_children.Add(child);
                }
            }

            clone.m_changeMasks = NodeStateChangeMasks.None;

            return clone;
        }

        /// <summary>
        /// Allows a subclass to consume the telemetry context to create loggers or any other observability
        /// instruments. This method is called by all Initialize overloads. However, it is possible that it
        /// is not called if any of them does not call their base implementation.  Therefore it is advised
        /// to always initialize a logger using the Utils.NullLogger.Instance to avoid Null reference exceptions.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        protected virtual void Initialize(ITelemetryContext telemetry)
        {
            // defined by subclass.
        }

        /// <summary>
        /// When overridden in a derived class, initializes the instance with the default values.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        protected virtual void Initialize(ISystemContext context)
        {
            Initialize(context.Telemetry);

            // defined by subclass.
        }

        /// <summary>
        /// When overridden in a derived class, initializes the any option children defined for the instance.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        protected virtual void InitializeOptionalChildren(ISystemContext context)
        {
            // defined by subclass.
        }

        /// <summary>
        /// Initializes the instance with the XML or binary (array of bytes) representation contained in the string.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="initializationString">The initialization string that is used to initializes the node.</param>
        public virtual void Initialize(ISystemContext context, string initializationString)
        {
            if (initializationString.StartsWith('<'))
            {
                using var reader = new StringReader(initializationString);
                LoadFromXml(context, reader);
            }
            else
            {
                byte[] bytes = Convert.FromBase64String(initializationString);

                using var istrm = new MemoryStream(bytes);
                LoadAsBinary(context, istrm);
            }
        }

        /// <summary>
        /// Initializes the instance with the XML or binary (array of bytes) representation contained in the string.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="initializationString">The initialization string that is used to initializes the node.</param>
        /// <param name="encoding">The initialization string is a utf8 xml string or binary</param>
        /// <exception cref="NotSupportedException"></exception>
        public virtual void Initialize(ISystemContext context, ReadOnlySpan<byte> initializationString, EncodingType encoding)
        {
            using var istrm = new MemoryStream(initializationString.ToArray());
            switch (encoding)
            {
                case EncodingType.Xml:
                    LoadFromXml(context, istrm);
                    break;
                case EncodingType.Binary:
                    LoadAsBinary(context, istrm);
                    break;
                default:
                    throw new NotSupportedException("Json encoding is not yet supported");
            }
        }

        /// <summary>
        /// Initializes the instance with attributes children and references from the source node state.
        /// This is called when deserializing a raw nodestate and initializing a generated class with its
        /// values, including properties etc. Generated classes override this to also perform initialization
        /// of any optional children added to the node state BEFORE calling this method.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="source">The source node.</param>
        protected virtual void Initialize(ISystemContext context, NodeState source)
        {
            Initialize(context.Telemetry);

            Handle = source.Handle;
            SymbolicName = source.SymbolicName;
            m_nodeId = source.m_nodeId;
            NodeClass = source.NodeClass;
            m_browseName = source.m_browseName;
            m_displayName = source.m_displayName;
            m_description = source.m_description;
            m_writeMask = source.m_writeMask;
            m_children = null;
            m_references = null;
            m_changeMasks = NodeStateChangeMasks.None;

            var children = new List<BaseInstanceState>();
            source.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState sourceChild = children[ii];
                BaseInstanceState child = CreateChild(context, sourceChild.BrowseName);

                if (child == null)
                {
                    child = (BaseInstanceState)sourceChild.Clone();
                    AddChild(child);
                }

                child.Initialize(context, sourceChild);
            }

            var references = new List<IReference>();
            source.GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];
                AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
            }
        }

        /// <summary>
        /// Returns a string representation of the node.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents the current <see cref="object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the node.
        /// </summary>
        /// <param name="format">The <see cref="string"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
            }

            if (!m_browseName.IsNull)
            {
                return string.Format(formatProvider, "[{0}]{1}", NodeClass, m_displayName);
            }

            return string.Format(formatProvider, "[{0}]{1}", NodeClass, m_nodeId);
        }

        /// <summary>
        /// An arbitrary handle associated with the node.
        /// </summary>
        public object Handle { get; set; }

        /// <summary>
        /// What has changed in the node since <see cref="ClearChangeMasks"/> was last called.
        /// </summary>
        /// <value>The change masks that indicates what has changed in a node.</value>
        public NodeStateChangeMasks ChangeMasks
        {
            get => m_changeMasks;
            protected set => m_changeMasks = value;
        }

        /// <summary>
        /// A symbolic name for the node that is not expected to be globally unique.
        /// </summary>
        /// <value>The name of the symbolic.</value>
        /// <remarks>
        /// This string can only contain characters that are valid for an XML element name.
        /// </remarks>
        public string SymbolicName { get; set; }

        /// <summary>
        /// The identifier for the node.
        /// </summary>
        /// <value>An instance that stores an identifier for a node in a server's address space.</value>
        public NodeId NodeId
        {
            get => m_nodeId;
            set
            {
                if (m_nodeId != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_nodeId = value;
            }
        }

        /// <summary>
        /// The class for the node.
        /// </summary>
        /// <value>The node class that is a description of the node.</value>
        public NodeClass NodeClass { get; private set; }

        /// <summary>
        /// The browse name of the node.
        /// </summary>
        /// <value>The name qualified with a namespace.</value>
        public QualifiedName BrowseName
        {
            get => m_browseName;
            set
            {
                if (m_browseName != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_browseName = value;
            }
        }

        /// <summary>
        /// The display name for the node.
        /// </summary>
        /// <value>Human readable qualified with a locale.</value>
        public LocalizedText DisplayName
        {
            get => m_displayName;
            set
            {
                if (m_displayName != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_displayName = value;
            }
        }

        /// <summary>
        /// The localized description for the node.
        /// </summary>
        /// <value>Human readable qualified with a locale.</value>
        public LocalizedText Description
        {
            get => m_description;
            set
            {
                if (m_description != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_description = value;
            }
        }

        /// <summary>
        /// Specifies which attributes are writeable.
        /// </summary>
        /// <value>A description for the AttributeWriteMask of the node fields.</value>
        public AttributeWriteMask WriteMask
        {
            get => m_writeMask;
            set
            {
                if (m_writeMask != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_writeMask = value;
            }
        }

        /// <summary>
        /// Specifies which attributes are writeable for the current user.
        /// </summary>
        /// <value>A description for the AttributeWriteMask of the node fields.</value>
        public AttributeWriteMask UserWriteMask
        {
            get => m_userWriteMask;
            set
            {
                if (m_userWriteMask != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userWriteMask = value;
            }
        }

        /// <summary>
        /// Specifies  a list of permissions for the node assigned to roles.
        /// </summary>
        /// <value>The Permissions that apply to the node.</value>
        public RolePermissionTypeCollection RolePermissions
        {
            get => m_rolePermissions;
            set
            {
                if (m_rolePermissions != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_rolePermissions = value;
            }
        }

        /// <summary>
        /// Specifies a list of permissions for the node assigned to roles for the current user.
        /// </summary>
        /// <value>The Permissions that apply to the node for the current user.</value>
        public RolePermissionTypeCollection UserRolePermissions
        {
            get => m_userRolePermissions;
            set
            {
                if (m_userRolePermissions != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userRolePermissions = value;
            }
        }

        /// <summary>
        /// Specifies  a mask indicating any access restrictions that apply to the node.
        /// </summary>
        /// <value>The server specific access restrictions of the node.</value>
        public AccessRestrictionType? AccessRestrictions
        {
            get => m_accessRestrictions;
            set
            {
                if (m_accessRestrictions != value)
                {
                    m_changeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_accessRestrictions = value;
            }
        }

        /// <summary>
        /// Gets or sets the extensions of the node set. Property used when importing NodeSet2.xml files.
        /// </summary>
        /// <value>
        /// The extensions.
        /// </value>
        public XmlElement[] Extensions { get; set; }

        /// <summary>
        /// The categories assigned to the node.
        /// </summary>
        public IList<string> Categories { get; set; }

        /// <summary>
        /// The release status for the node.
        /// </summary>
        public Export.ReleaseStatus ReleaseStatus { get; set; }

        /// <summary>
        /// The specification that defines the node.
        /// </summary>
        public string Specification { get; set; }

        /// <summary>
        /// The documentation for the node that is saved in the NodeSet.
        /// </summary>
        public string NodeSetDocumentation { get; set; }

        /// <summary>
        /// The documentation for the node that is saved in the NodeSet.
        /// </summary>
        public bool DesignToolOnly { get; set; }

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="table">A table of nodes.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void Export(ISystemContext context, NodeTable table)
        {
            Node node;
            switch (NodeClass)
            {
                case NodeClass.Object:
                    node = new ObjectNode();
                    break;
                case NodeClass.ObjectType:
                    node = new ObjectTypeNode();
                    break;
                case NodeClass.Variable:
                    node = new VariableNode();
                    break;
                case NodeClass.VariableType:
                    node = new VariableTypeNode();
                    break;
                case NodeClass.Method:
                    node = new MethodNode();
                    break;
                case NodeClass.ReferenceType:
                    node = new ReferenceTypeNode();
                    break;
                case NodeClass.DataType:
                    node = new DataTypeNode();
                    break;
                case NodeClass.View:
                    node = new ViewNode();
                    break;
                case NodeClass.Unspecified:
                    node = new Node();
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass value: {NodeClass}");
            }

            Export(context, node);

            var references = new List<IReference>();
            GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                node.ReferenceTable.Add(references[ii]);
            }

            table.Attach(node);

            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].Export(context, table);
            }
        }

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="node">The node to update with the values from the instance.</param>
        protected virtual void Export(ISystemContext context, Node node)
        {
            node.NodeId = NodeId;
            node.NodeClass = NodeClass;
            node.BrowseName = BrowseName;
            node.DisplayName = DisplayName;
            node.Description = Description;
            node.WriteMask = (uint)WriteMask;
            node.UserWriteMask = (uint)UserWriteMask;
        }

        /// <summary>
        /// Saves the node as XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="ostrm">The stream to write.</param>
        public void SaveAsXml(ISystemContext context, Stream ostrm)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            XmlWriterSettings settings = CoreUtils.DefaultXmlWriterSettings();
            settings.CloseOutput = true;
            using var writer = XmlWriter.Create(ostrm, settings);
            var root = new XmlQualifiedName(
                SymbolicName,
                context.NamespaceUris.GetString(BrowseName.NamespaceIndex));

            using var encoder = new XmlEncoder(root, writer, messageContext);
            encoder.SaveStringTable("NamespaceUris", "NamespaceUri", context.NamespaceUris);
            encoder.SaveStringTable("ServerUris", "ServerUri", context.ServerUris);

            Save(context, encoder);
            SaveReferences(context, encoder);
            SaveChildren(context, encoder);

            encoder.Close();
        }

        /// <summary>
        /// Saves the node in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="ostrm">The stream to write.</param>
        public void SaveAsBinary(ISystemContext context, Stream ostrm)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            using var encoder = new BinaryEncoder(ostrm, messageContext, true);
            encoder.SaveStringTable(context.NamespaceUris);
            encoder.SaveStringTable(context.ServerUris);

            AttributesToSave attributesToSave = GetAttributesToSave(context);
            encoder.WriteUInt32(null, (uint)attributesToSave);

            Save(context, encoder, attributesToSave);
            SaveReferences(context, encoder);
            SaveChildren(context, encoder);

            encoder.Close();
        }

        /// <summary>
        /// Saves the object in the binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The stream to write.</param>
        public void SaveAsBinary(ISystemContext context, BinaryEncoder encoder)
        {
            AttributesToSave attributesToSave = GetAttributesToSave(context);
            encoder.WriteUInt32(null, (uint)attributesToSave);

            Save(context, encoder, attributesToSave);
            SaveReferences(context, encoder);
            SaveChildren(context, encoder);
        }

        /// <summary>
        /// Loads the node from a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="istrm">The stream to read.</param>
        public void LoadAsBinary(ISystemContext context, Stream istrm)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            using var decoder = new BinaryDecoder(istrm, messageContext, true);
            // check if a namespace table was provided.
            var namespaceUris = new NamespaceTable();

            if (!decoder.LoadStringTable(namespaceUris))
            {
                namespaceUris = null;
            }

            // check if a server uri table was provided.
            var serverUris = new StringTable();

            if (namespaceUris != null && namespaceUris.Count > 1)
            {
                serverUris.Append(namespaceUris.GetString(1));
            }

            if (!decoder.LoadStringTable(serverUris))
            {
                serverUris = null;
            }

            // setup the mappings to use during decoding.
            decoder.SetMappingTables(namespaceUris, serverUris);

            // update the node and children.
            var attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);
            Update(context, decoder, attributesToLoad);
            UpdateReferences(context, decoder);
            UpdateChildren(context, decoder);
        }

        /// <summary>
        /// Flags which control the serialization of a NodeState in a stream.
        /// </summary>
        [Flags]
        public enum AttributesToSave : uint
        {
            /// <summary>
            /// The default value.
            /// </summary>
            None = 0x00000000,

            /// <summary>
            /// The AccessLevel attribute.
            /// </summary>
            AccessLevel = 0x00000001,

            /// <summary>
            /// The ArrayDimensions attribute.
            /// </summary>
            ArrayDimensions = 0x00000002,

            /// <summary>
            /// The BrowseName attribute.
            /// </summary>
            BrowseName = 0x00000004,

            /// <summary>
            /// The ContainsNoLoops attribute.
            /// </summary>
            ContainsNoLoops = 0x00000008,

            /// <summary>
            /// The DataType attribute.
            /// </summary>
            DataType = 0x00000010,

            /// <summary>
            /// The Description attribute.
            /// </summary>
            Description = 0x00000020,

            /// <summary>
            /// The DisplayName attribute.
            /// </summary>
            DisplayName = 0x00000040,

            /// <summary>
            /// The EventNotifier attribute.
            /// </summary>
            EventNotifier = 0x00000080,

            /// <summary>
            /// The Executable attribute.
            /// </summary>
            Executable = 0x00000100,

            /// <summary>
            /// The Historizing attribute.
            /// </summary>
            Historizing = 0x00000200,

            /// <summary>
            /// The InverseName attribute.
            /// </summary>
            InverseName = 0x00000400,

            /// <summary>
            /// The IsAbstract attribute.
            /// </summary>
            IsAbstract = 0x00000800,

            /// <summary>
            /// The MinimumSamplingInterval attribute.
            /// </summary>
            MinimumSamplingInterval = 0x00001000,

            /// <summary>
            /// The NodeClass attribute.
            /// </summary>
            NodeClass = 0x00002000,

            /// <summary>
            /// The NodeId attribute.
            /// </summary>
            NodeId = 0x00004000,

            /// <summary>
            /// The Symmetric attribute.
            /// </summary>
            Symmetric = 0x00008000,

            /// <summary>
            /// The UserAccessLevel attribute.
            /// </summary>
            UserAccessLevel = 0x00010000,

            /// <summary>
            /// The UserExecutable attribute.
            /// </summary>
            UserExecutable = 0x00020000,

            /// <summary>
            /// The UserWriteMask attribute.
            /// </summary>
            UserWriteMask = 0x00040000,

            /// <summary>
            /// The ValueRank attribute.
            /// </summary>
            ValueRank = 0x00080000,

            /// <summary>
            /// The WriteMask attribute.
            /// </summary>
            WriteMask = 0x00100000,

            /// <summary>
            /// The Value attribute.
            /// </summary>
            Value = 0x00200000,

            /// <summary>
            /// The SymbolicName for the node.
            /// </summary>
            SymbolicName = 0x00400000,

            /// <summary>
            /// The target of the TypeDefinitionId reference.
            /// </summary>
            TypeDefinitionId = 0x00800000,

            /// <summary>
            /// The target of the HasModellingRule reference.
            /// </summary>
            ModellingRuleId = 0x01000000,

            /// <summary>
            /// The NumericId for the node.
            /// </summary>
            NumericId = 0x02000000,

            /// <summary>
            /// The type of reference between a child and a parent.
            /// </summary>
            ReferenceTypeId = 0x08000000,

            /// <summary>
            /// The source of the HasSubType reference.
            /// </summary>
            SuperTypeId = 0x10000000,

            /// <summary>
            /// The StatusCode associated with the Value attribute.
            /// </summary>
            StatusCode = 0x20000000,

            /// <summary>
            /// The DataTypeDefinition attribute of a DataType Node.
            /// </summary>
            DataTypeDefinition = 0x40000000
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public virtual AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = AttributesToSave.None;

            if (!string.IsNullOrEmpty(SymbolicName) &&
                SymbolicName != m_browseName.Name)
            {
                attributesToSave |= AttributesToSave.SymbolicName;
            }

            attributesToSave |= AttributesToSave.NodeClass;

            if (!m_nodeId.IsNull)
            {
                attributesToSave |= AttributesToSave.NodeId;
            }

            if (!m_browseName.IsNull)
            {
                attributesToSave |= AttributesToSave.BrowseName;
            }

            if (!m_displayName.IsNullOrEmpty)
            {
                if (m_browseName.IsNull ||
                    !string.IsNullOrEmpty(m_displayName.Locale) ||
                    m_displayName.Text != m_browseName.Name)
                {
                    attributesToSave |= AttributesToSave.DisplayName;
                }
            }

            if (!m_description.IsNullOrEmpty)
            {
                attributesToSave |= AttributesToSave.Description;
            }

            if (m_writeMask != AttributeWriteMask.None)
            {
                attributesToSave |= AttributesToSave.WriteMask;
            }

            if (m_userWriteMask != AttributeWriteMask.None)
            {
                attributesToSave |= AttributesToSave.UserWriteMask;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public virtual void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
        {
            encoder.WriteEnumerated(null, NodeClass);

            if ((attributesToSave & AttributesToSave.SymbolicName) != 0)
            {
                encoder.WriteString(null, SymbolicName);
            }

            if ((attributesToSave & AttributesToSave.BrowseName) != 0)
            {
                encoder.WriteQualifiedName(null, m_browseName);
            }

            if ((attributesToSave & AttributesToSave.NodeId) != 0)
            {
                encoder.WriteNodeId(null, m_nodeId);
            }

            if ((attributesToSave & AttributesToSave.DisplayName) != 0)
            {
                encoder.WriteLocalizedText(null, m_displayName);
            }

            if ((attributesToSave & AttributesToSave.Description) != 0)
            {
                encoder.WriteLocalizedText(null, m_description);
            }

            if ((attributesToSave & AttributesToSave.WriteMask) != 0)
            {
                encoder.WriteEnumerated(null, m_writeMask);
            }

            if ((attributesToSave & AttributesToSave.UserWriteMask) != 0)
            {
                encoder.WriteEnumerated(null, m_userWriteMask);
            }
        }

        /// <summary>
        /// Updates the object from a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder to read from.</param>
        /// <param name="attributesToLoad">The masks indicating what attributes to read.</param>
        public virtual void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
        {
            if ((attributesToLoad & AttributesToSave.NodeClass) != 0)
            {
                NodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
            }

            if ((attributesToLoad & AttributesToSave.SymbolicName) != 0)
            {
                SymbolicName = decoder.ReadString(null);
            }

            if ((attributesToLoad & AttributesToSave.BrowseName) != 0)
            {
                m_browseName = decoder.ReadQualifiedName(null);
            }

            if (string.IsNullOrEmpty(SymbolicName) && !m_browseName.IsNull)
            {
                SymbolicName = m_browseName.Name;
            }

            if ((attributesToLoad & AttributesToSave.NodeId) != 0)
            {
                m_nodeId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.DisplayName) != 0)
            {
                m_displayName = decoder.ReadLocalizedText(null);
            }

            if (m_displayName.IsNullOrEmpty && !m_browseName.IsNull)
            {
                m_displayName = new LocalizedText(m_browseName.Name);
            }

            if ((attributesToLoad & AttributesToSave.Description) != 0)
            {
                m_description = decoder.ReadLocalizedText(null);
            }

            if ((attributesToLoad & AttributesToSave.WriteMask) != 0)
            {
                m_writeMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    null,
                    typeof(AttributeWriteMask));
            }

            if ((attributesToLoad & AttributesToSave.UserWriteMask) != 0)
            {
                m_userWriteMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    null,
                    typeof(AttributeWriteMask));
            }
        }

        /// <summary>
        /// Saves the children in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public virtual void SaveChildren(ISystemContext context, BinaryEncoder encoder)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            encoder.WriteInt32(null, children.Count);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                AttributesToSave attributesToSave = child.GetAttributesToSave(context);
                encoder.WriteUInt32(null, (uint)attributesToSave);

                child.Save(context, encoder, attributesToSave);
                child.SaveReferences(context, encoder);
                child.SaveChildren(context, encoder);
            }
        }

        /// <summary>
        /// Loads the children from a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder to read from.</param>
        public virtual void UpdateChildren(ISystemContext context, BinaryDecoder decoder)
        {
            int count = decoder.ReadInt32(null);

            for (int ii = 0; ii < count; ii++)
            {
                try
                {
                    BaseInstanceState child = UpdateChild(context, decoder);
                }
                catch (Exception)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Reads attributes for the next child found in the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        /// <returns>The updated child.</returns>
        /// <remarks>
        /// The child is created if it does not already exist.
        /// Recursively updates any children of the child.
        /// </remarks>
        protected BaseInstanceState UpdateChild(ISystemContext context, BinaryDecoder decoder)
        {
            var attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);
            string symbolicName = null;
            QualifiedName browseName = default;

            var nodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
            attributesToLoad &= ~AttributesToSave.NodeClass;

            if ((attributesToLoad & AttributesToSave.SymbolicName) != 0)
            {
                symbolicName = decoder.ReadString(null);
                attributesToLoad &= ~AttributesToSave.SymbolicName;
            }

            if ((attributesToLoad & AttributesToSave.BrowseName) != 0)
            {
                browseName = decoder.ReadQualifiedName(null);
                attributesToLoad &= ~AttributesToSave.BrowseName;
            }

            if (string.IsNullOrEmpty(symbolicName) && !browseName.IsNull)
            {
                symbolicName = browseName.Name;
            }

            // check for children defined by the type.
            BaseInstanceState child = CreateChild(context, browseName);

            if (child != null)
            {
                child.SymbolicName = symbolicName;
                child.BrowseName = browseName;

                // update attributes.
                child.Update(context, decoder, attributesToLoad);

                // update any references.
                child.UpdateReferences(context, decoder);

                // update any children.
                child.UpdateChildren(context, decoder);

                // all done.
                return child;
            }

            // handle unknown child.
            child = UpdateUnknownChild(
                context,
                decoder,
                this,
                attributesToLoad,
                nodeClass,
                symbolicName,
                browseName);

            // add the child.
            if (child != null)
            {
                child.BrowseName = browseName;
                AddChild(child);
            }

            return child;
        }

        /// <summary>
        /// Creates a node and initializes it from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder.</param>
        /// <returns>The new node.</returns>
        public static NodeState LoadNode(ISystemContext context, BinaryDecoder decoder)
        {
            var attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);
            string symbolicName = null;
            QualifiedName browseName = default;

            var nodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
            attributesToLoad &= ~AttributesToSave.NodeClass;

            if ((attributesToLoad & AttributesToSave.SymbolicName) != 0)
            {
                symbolicName = decoder.ReadString(null);
                attributesToLoad &= ~AttributesToSave.SymbolicName;
            }

            if ((attributesToLoad & AttributesToSave.BrowseName) != 0)
            {
                browseName = decoder.ReadQualifiedName(null);
                attributesToLoad &= ~AttributesToSave.BrowseName;
            }

            if (string.IsNullOrEmpty(symbolicName) && !browseName.IsNull)
            {
                symbolicName = browseName.Name;
            }

            // read the node from the stream.
            return LoadUnknownNode(
                context,
                decoder,
                attributesToLoad,
                nodeClass,
                symbolicName,
                browseName);
        }

        /// <summary>
        /// Saves the reference table in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveReferences(ISystemContext context, BinaryEncoder encoder)
        {
            if (m_references == null || m_references.Count == 0)
            {
                encoder.WriteInt32(null, -1);
                return;
            }

            encoder.WriteInt32(null, m_references.Count);

            foreach (IReference reference in m_references.Keys)
            {
                encoder.WriteNodeId(null, reference.ReferenceTypeId);
                encoder.WriteBoolean(null, reference.IsInverse);
                encoder.WriteExpandedNodeId(null, reference.TargetId);
            }
        }

        /// <summary>
        /// Loads the reference table from a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder to read from.</param>
        public void UpdateReferences(ISystemContext context, BinaryDecoder decoder)
        {
            int count = decoder.ReadInt32(null);

            // Collect references to temporary list first to avoid unnecessary locking during the deserialization.
            var references = new List<NodeStateReference>();

            for (int ii = 0; ii < count; ii++)
            {
                NodeId referenceTypeId = decoder.ReadNodeId(null);
                bool isInverse = decoder.ReadBoolean(null);
                ExpandedNodeId targetId = decoder.ReadExpandedNodeId(null);

                references.Add(new NodeStateReference(referenceTypeId, isInverse, targetId));
            }

            lock (m_referencesLock)
            {
                m_references ??= [];

                foreach (NodeStateReference reference in references)
                {
                    m_references[reference] = null;
                }
            }
        }

        /// <summary>
        /// Saves the node as XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveAsXml(ISystemContext context, XmlEncoder encoder)
        {
            encoder.Push(SymbolicName, context.NamespaceUris.GetString(BrowseName.NamespaceIndex));

            Save(context, encoder);
            SaveReferences(context, encoder);
            SaveChildren(context, encoder);

            encoder.Pop();
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="input">The stream to read.</param>
        public void LoadFromXml(ISystemContext context, TextReader input)
        {
            using var reader = XmlReader.Create(input, CoreUtils.DefaultXmlReaderSettings());
            LoadFromXml(context, reader);
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="input">The stream to read.</param>
        public void LoadFromXml(ISystemContext context, Stream input)
        {
            using var reader = XmlReader.Create(input, CoreUtils.DefaultXmlReaderSettings());
            LoadFromXml(context, reader);
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="reader">The stream to read.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void LoadFromXml(ISystemContext context, XmlReader reader)
        {
            IServiceMessageContext messageContext = context.AsMessageContext();

            reader.MoveToContent();

            // get the root of the child element.
            var symbolicName = new XmlQualifiedName(reader.LocalName, reader.NamespaceURI);

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(symbolicName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not resolve namespace uri: {0}",
                    symbolicName.Namespace);
            }

            // initialize browse name.
            SymbolicName = symbolicName.Name;
            BrowseName = new QualifiedName(symbolicName.Name, (ushort)namespaceIndex);

            using var decoder = new XmlDecoder(null, reader, messageContext);
            // check if a namespace table was provided.
            var namespaceUris = new NamespaceTable();

            if (!decoder.LoadStringTable("NamespaceUris", "NamespaceUri", namespaceUris))
            {
                namespaceUris = null;
            }

            // check if a server uri table was provided.
            var serverUris = new StringTable();

            if (!decoder.LoadStringTable("ServerUris", "ServerUri", serverUris))
            {
                serverUris = null;
            }

            // setup the mappings to use during decoding.
            decoder.SetMappingTables(namespaceUris, serverUris);

            // update the node and children.
            Update(context, decoder);
            UpdateReferences(context, decoder);
            UpdateChildren(context, decoder);
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void LoadFromXml(ISystemContext context, XmlDecoder decoder)
        {
            // get the name of the child element.
            XmlQualifiedName symbolicName =
                decoder.Peek(XmlNodeType.Element)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Expecting an XML start element in stream.");

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(symbolicName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not resolve namespace uri: {0}",
                    symbolicName.Namespace);
            }

            // initialize browse name.
            SymbolicName = symbolicName.Name;
            BrowseName = new QualifiedName(symbolicName.Name, (ushort)namespaceIndex);

            // initialize the node.
            decoder.ReadStartElement();

            Update(context, decoder);
            UpdateReferences(context, decoder);
            UpdateChildren(context, decoder);

            decoder.Skip(symbolicName);
        }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public virtual void Save(ISystemContext context, XmlEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEnumerated("NodeClass", NodeClass);

            if (!m_nodeId.IsNull)
            {
                encoder.WriteNodeId("NodeId", m_nodeId);
            }

            if (!m_browseName.IsNull)
            {
                encoder.WriteQualifiedName("BrowseName", m_browseName);
            }

            if (!m_displayName.IsNullOrEmpty)
            {
                if (m_browseName.IsNull ||
                    !string.IsNullOrEmpty(m_displayName.Locale) ||
                    m_browseName.Name != m_displayName.Text)
                {
                    encoder.WriteLocalizedText("DisplayName", m_displayName);
                }
            }

            if (!m_description.IsNullOrEmpty)
            {
                encoder.WriteLocalizedText("Description", m_description);
            }

            if (m_writeMask != AttributeWriteMask.None)
            {
                encoder.WriteEnumerated("WriteMask", m_writeMask);
            }

            if (m_userWriteMask != AttributeWriteMask.None)
            {
                encoder.WriteEnumerated("UserWriteMask", m_userWriteMask);
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Updates the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void Update(ISystemContext context, XmlDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("NodeClass"))
            {
                var nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));

                if (NodeClass != NodeClass.Unspecified && nodeClass != NodeClass)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "Unexpected NodeClass in input stream. {0} != {1}",
                        NodeClass,
                        nodeClass);
                }
            }

            if (decoder.Peek("NodeId"))
            {
                NodeId nodeId = decoder.ReadNodeId("NodeId");

                if (!nodeId.IsNull)
                {
                    NodeId = nodeId;
                }
            }

            if (decoder.Peek("BrowseName"))
            {
                QualifiedName browseName = decoder.ReadQualifiedName("BrowseName");

                if (!browseName.IsNull)
                {
                    BrowseName = browseName;
                }
            }

            if (decoder.Peek("DisplayName"))
            {
                DisplayName = decoder.ReadLocalizedText("DisplayName");
            }

            if (m_displayName.IsNullOrEmpty && !m_browseName.IsNull)
            {
                DisplayName = new LocalizedText(m_browseName.Name);
            }

            if (decoder.Peek("Description"))
            {
                Description = decoder.ReadLocalizedText("Description");
            }

            if (decoder.Peek("WriteMask"))
            {
                WriteMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    "WriteMask",
                    typeof(AttributeWriteMask));
            }

            if (decoder.Peek("UserWriteMask"))
            {
                UserWriteMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    "UserWriteMask",
                    typeof(AttributeWriteMask));
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Saves the children from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public virtual void SaveChildren(ISystemContext context, XmlEncoder encoder)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                encoder.Push(
                    child.SymbolicName,
                    context.NamespaceUris.GetString(child.BrowseName.NamespaceIndex));

                child.Save(context, encoder);
                child.SaveReferences(context, encoder);
                child.SaveChildren(context, encoder);

                encoder.Pop();
            }
        }

        /// <summary>
        /// Saves a reference table from an XML stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveReferences(ISystemContext context, XmlEncoder encoder)
        {
            if (m_references == null || m_references.Count == 0)
            {
                return;
            }

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            try
            {
                encoder.Push("References", Namespaces.OpcUaXsd);

                foreach (IReference reference in m_references.Keys)
                {
                    encoder.Push("Reference", Namespaces.OpcUaXsd);

                    if (!reference.ReferenceTypeId.IsNull)
                    {
                        encoder.WriteNodeId("ReferenceTypeId", reference.ReferenceTypeId);
                    }

                    if (reference.IsInverse)
                    {
                        encoder.WriteBoolean("IsInverse", reference.IsInverse);
                    }

                    if (!reference.TargetId.IsNull)
                    {
                        encoder.WriteExpandedNodeId("TargetId", reference.TargetId);
                    }

                    encoder.Pop();
                }

                encoder.Pop();
            }
            finally
            {
                encoder.PopNamespace();
            }
        }

        /// <summary>
        /// Reads attributes for the children from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        /// <remarks>
        /// Any children found in the stream that do not exist are created and initialized from the stream.
        /// </remarks>
        public virtual void UpdateChildren(ISystemContext context, XmlDecoder decoder)
        {
            // get the first child.
            BaseInstanceState child = UpdateChild(context, decoder);

            while (child != null)
            {
                // loop until all children are read.
                child = UpdateChild(context, decoder);
            }
        }

        /// <summary>
        /// Loads any additional references from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        public virtual void UpdateReferences(ISystemContext context, XmlDecoder decoder)
        {
            // remove existing references.
            if (m_references != null)
            {
                m_references.Clear();
                m_changeMasks |= NodeStateChangeMasks.References;
            }

            // check if the table exists.
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (!decoder.Peek("References"))
            {
                decoder.PopNamespace();
                return;
            }

            // read the references.
            decoder.ReadStartElement();

            while (decoder.Peek("Reference"))
            {
                decoder.ReadStartElement();

                NodeId referenceTypeId = default;
                bool isInverse = false;
                ExpandedNodeId targetId = default;

                if (decoder.Peek("ReferenceTypeId"))
                {
                    referenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
                }

                if (decoder.Peek("IsInverse"))
                {
                    isInverse = decoder.ReadBoolean("IsInverse");
                }

                if (decoder.Peek("TargetId"))
                {
                    targetId = decoder.ReadExpandedNodeId("TargetId");
                }

                // create table if it does not already exist.
                m_references ??= [];

                // create reference.
                m_references[new NodeStateReference(referenceTypeId, isInverse, targetId)] = null;
                m_changeMasks |= NodeStateChangeMasks.References;

                decoder.Skip(new XmlQualifiedName("Reference", Namespaces.OpcUaXsd));
            }

            decoder.Skip(new XmlQualifiedName("References", Namespaces.OpcUaXsd));

            decoder.PopNamespace();
        }

        /// <summary>
        /// Reads attributes for the next child found in the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        /// <returns>The updated child.</returns>
        /// <remarks>
        /// The child is created if it does not already exist.
        /// Recursively updates any children of the child.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        protected BaseInstanceState UpdateChild(ISystemContext context, XmlDecoder decoder)
        {
            // get the name of the child element.
            XmlQualifiedName childName = decoder.Peek(XmlNodeType.Element);

            if (childName == null)
            {
                return null;
            }

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(childName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not resolve namespace uri: {0}",
                    childName.Namespace);
            }

            // move to body.
            decoder.ReadStartElement();

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // pre-fetch enough information to know what type of node to create.
            var nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));
            NodeId nodeId = decoder.ReadNodeId("NodeId");
            QualifiedName browseName = decoder.ReadQualifiedName("BrowseName");

            decoder.PopNamespace();

            // check for children defined by the type.
            BaseInstanceState child = CreateChild(context, browseName);

            if (child != null)
            {
                child.SymbolicName = childName.Name;
                child.NodeId = nodeId;
                child.BrowseName = browseName;

                // update attributes.
                child.Update(context, decoder);

                // update any references.
                child.UpdateReferences(context, decoder);

                // update any children.
                child.UpdateChildren(context, decoder);

                // skip to end.
                decoder.Skip(childName);
                return child;
            }

            // handle unknown child.
            child = UpdateUnknownChild(context, decoder, this, childName, nodeClass, browseName);

            // add the child.
            if (child != null)
            {
                child.NodeId = nodeId;
                AddChild(child);
            }

            return child;
        }

        /// <summary>
        /// Creates a node and initializes it from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder.</param>
        /// <returns>The new node.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static NodeState LoadNode(ISystemContext context, XmlDecoder decoder)
        {
            // get the name of the child element.
            XmlQualifiedName childName = decoder.Peek(XmlNodeType.Element);

            if (childName == null)
            {
                return null;
            }

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(childName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not resolve namespace uri: {0}",
                    childName.Namespace);
            }

            // move to body.
            decoder.ReadStartElement();

            var browseName = new QualifiedName(childName.Name, (ushort)namespaceIndex);

            // read the node from the stream.
            return LoadUnknownNode(context, decoder, childName, browseName);
        }

        /// <summary>
        /// Updates a child which is not defined by the type definition.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="attributesToLoad">The attributes to load.</param>
        /// <param name="nodeClass">The node class.</param>
        /// <param name="symbolicName">Symbolic name of the node.</param>
        /// <param name="browseName">A name qualified with a namespace.</param>
        /// <returns>An instance of the <see cref="BaseInstanceState"/> type that is a base class for all instance nodes.</returns>
        /// <exception cref="ServiceResultException"></exception>
        private static BaseInstanceState UpdateUnknownChild(
            ISystemContext context,
            BinaryDecoder decoder,
            NodeState parent,
            AttributesToSave attributesToLoad,
            NodeClass nodeClass,
            string symbolicName,
            QualifiedName browseName)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            NodeId nodeId = default;
            LocalizedText displayName = default;
            LocalizedText description = default;
            AttributeWriteMask writeMask = AttributeWriteMask.None;
            const AttributeWriteMask userWriteMask = AttributeWriteMask.None;
            NodeId referenceTypeId = default;
            NodeId typeDefinitionId = default;

            if ((attributesToLoad & AttributesToSave.NodeId) != 0)
            {
                nodeId = decoder.ReadNodeId(null);
                attributesToLoad &= ~AttributesToSave.NodeId;
            }

            if ((attributesToLoad & AttributesToSave.DisplayName) != 0)
            {
                displayName = decoder.ReadLocalizedText(null);
                attributesToLoad &= ~AttributesToSave.DisplayName;
            }

            if (displayName.IsNullOrEmpty && !browseName.IsNull)
            {
                displayName = new LocalizedText(browseName.Name);
            }

            if ((attributesToLoad & AttributesToSave.Description) != 0)
            {
                description = decoder.ReadLocalizedText(null);
                attributesToLoad &= ~AttributesToSave.Description;
            }

            if ((attributesToLoad & AttributesToSave.WriteMask) != 0)
            {
                writeMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    null,
                    typeof(AttributeWriteMask));
                attributesToLoad &= ~AttributesToSave.WriteMask;
            }

            if ((attributesToLoad & AttributesToSave.UserWriteMask) != 0)
            {
                writeMask = (AttributeWriteMask)decoder.ReadEnumerated(
                    null,
                    typeof(AttributeWriteMask));
                attributesToLoad &= ~AttributesToSave.UserWriteMask;
            }

            if ((attributesToLoad & AttributesToSave.ReferenceTypeId) != 0)
            {
                referenceTypeId = decoder.ReadNodeId(null);
                attributesToLoad &= ~AttributesToSave.ReferenceTypeId;
            }

            if ((attributesToLoad & AttributesToSave.TypeDefinitionId) != 0)
            {
                typeDefinitionId = decoder.ReadNodeId(null);
                attributesToLoad &= ~AttributesToSave.TypeDefinitionId;
            }

            // get the node factory.
            NodeStateFactory factory = context.NodeStateFactory
                ?? new NodeStateFactory();

            // create the appropriate node.

            if (factory.CreateInstance(
                    context,
                    parent,
                    nodeClass,
                    browseName,
                    referenceTypeId,
                    typeDefinitionId) is not BaseInstanceState child)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load child '{0}', with NodeClass {1}",
                    browseName,
                    nodeClass);
            }

            // initialize the child from the stream.
            child.SymbolicName = symbolicName;
            child.NodeId = nodeId;
            child.BrowseName = browseName;
            child.DisplayName = displayName;
            child.Description = description;
            child.WriteMask = writeMask;
            child.UserWriteMask = userWriteMask;
            child.ReferenceTypeId = referenceTypeId;
            child.TypeDefinitionId = typeDefinitionId;

            // update attributes.
            child.Update(context, decoder, attributesToLoad);

            // update any references.
            child.UpdateReferences(context, decoder);

            // update any children.
            child.UpdateChildren(context, decoder);

            return child;
        }

        /// <summary>
        /// Reads an unknown node from a stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static NodeState LoadUnknownNode(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad,
            NodeClass nodeClass,
            string symbolicName,
            QualifiedName browseName)
        {
            // create the appropriate node.
            switch (nodeClass)
            {
                case NodeClass.Variable:
                case NodeClass.Object:
                case NodeClass.Method:
                    return UpdateUnknownChild(
                        context,
                        decoder,
                        null,
                        attributesToLoad,
                        nodeClass,
                        symbolicName,
                        browseName);
                case NodeClass.Unspecified:
                case NodeClass.ObjectType:
                case NodeClass.VariableType:
                case NodeClass.ReferenceType:
                case NodeClass.DataType:
                case NodeClass.View:
                    // get the node factory.
                    NodeStateFactory factory = context.NodeStateFactory
                        ?? new NodeStateFactory();

                    // create the appropriate node.
                    NodeState child =
                        factory.CreateInstance(context, null, nodeClass, browseName, default, default)
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Could not load node '{0}', with NodeClass {1}",
                            browseName,
                            nodeClass);

                    // update symbolic name.
                    child.SymbolicName = symbolicName;
                    child.BrowseName = browseName;

                    // update attributes.
                    child.Update(context, decoder, attributesToLoad);

                    // update any references.
                    child.UpdateReferences(context, decoder);

                    // update any children.
                    child.UpdateChildren(context, decoder);

                    return child;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {nodeClass}");
            }
        }

        /// <summary>
        /// Reads an unknown node from a stream.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static NodeState LoadUnknownNode(
            ISystemContext context,
            XmlDecoder decoder,
            XmlQualifiedName childName,
            QualifiedName browseName)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // pre-fetch enough information to know what type of node to create.
            var nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));

            decoder.PopNamespace();

            // create the appropriate node.
            switch (nodeClass)
            {
                case NodeClass.Variable:
                case NodeClass.Object:
                case NodeClass.Method:
                    return UpdateUnknownChild(
                        context,
                        decoder,
                        null,
                        childName,
                        nodeClass,
                        browseName);
                case NodeClass.Unspecified:
                case NodeClass.ObjectType:
                case NodeClass.VariableType:
                case NodeClass.ReferenceType:
                case NodeClass.DataType:
                case NodeClass.View:
                    // get the node factory.
                    NodeStateFactory factory = context.NodeStateFactory
                        ?? new NodeStateFactory();

                    // create the appropriate node.
                    NodeState child =
                        factory.CreateInstance(context, null, nodeClass, browseName, default, default)
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadDecodingError,
                            "Could not load node '{0}', with NodeClass {1}",
                            browseName,
                            nodeClass);

                    // update symbolic name.
                    child.SymbolicName = childName.Name;

                    // update attributes.
                    child.Update(context, decoder);

                    // update any references.
                    child.UpdateReferences(context, decoder);

                    // update any children.
                    child.UpdateChildren(context, decoder);

                    // skip to the end of the child.
                    decoder.Skip(childName);

                    return child;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {nodeClass}");
            }
        }

        /// <summary>
        /// Updates a child which is not defined by the type definition.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static BaseInstanceState UpdateUnknownChild(
            ISystemContext context,
            XmlDecoder decoder,
            NodeState parent,
            XmlQualifiedName childName,
            NodeClass nodeClass,
            QualifiedName browseName)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // pre-fetch enough information to know what type of node to create.
            NodeId nodeId = decoder.ReadNodeId("NodeId");

            if (decoder.Peek("BrowseName"))
            {
                browseName = decoder.ReadQualifiedName("BrowseName");
            }

            LocalizedText displayName = default;

            if (decoder.Peek("DisplayName"))
            {
                displayName = decoder.ReadLocalizedText("DisplayName");
            }

            if (displayName.IsNullOrEmpty && !browseName.IsNull)
            {
                displayName = new LocalizedText(browseName.Name);
            }

            LocalizedText description = default;

            if (decoder.Peek("Description"))
            {
                description = decoder.ReadLocalizedText("Description");
            }

            var writeMask = (AttributeWriteMask)decoder.ReadEnumerated(
                "WriteMask",
                typeof(AttributeWriteMask));
            var userWriteMask = (AttributeWriteMask)decoder.ReadEnumerated(
                "UserWriteMask",
                typeof(AttributeWriteMask));
            NodeId referenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            NodeId typeDefinitionId = decoder.ReadNodeId("TypeDefinitionId");

            decoder.PopNamespace();

            // get the node factory.
            NodeStateFactory factory = context.NodeStateFactory
                ?? new NodeStateFactory();

            // create the appropriate node.

            if (factory.CreateInstance(
                    context,
                    parent,
                    nodeClass,
                    browseName,
                    referenceTypeId,
                    typeDefinitionId)
                is not BaseInstanceState child)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load child '{0}', with NodeClass {1}",
                    browseName,
                    nodeClass);
            }

            // initialize the child from the stream.
            child.SymbolicName = childName.Name;
            child.NodeId = nodeId;
            child.BrowseName = browseName;
            child.DisplayName = displayName;
            child.Description = description;
            child.WriteMask = writeMask;
            child.UserWriteMask = userWriteMask;
            child.ReferenceTypeId = referenceTypeId;
            child.TypeDefinitionId = typeDefinitionId;

            // update attributes.
            child.Update(context, decoder);

            // update any references.
            child.UpdateReferences(context, decoder);

            // update any children.
            child.UpdateChildren(context, decoder);

            // skip to the end of the child.
            decoder.Skip(childName);

            return child;
        }

        /// <summary>
        /// An event which allows multiple sinks to be notified when the OnStateChanged callback is called.
        /// </summary>
        public event NodeStateChangedHandler StateChanged;

        /// <summary>
        /// Called when the Validate method is called
        /// </summary>
        public NodeStateValidateHandler OnValidate;

        /// <summary>
        /// Called when ClearChangeMasks is called and the ChangeMask is not None.
        /// </summary>
        public NodeStateChangedHandler OnStateChanged;

        /// <summary>
        /// Called when a reference gets added to the node
        /// </summary>
        public NodeStateReferenceAdded OnReferenceAdded;

        /// <summary>
        /// Called when a reference gets removed from the node
        /// </summary>
        public NodeStateReferenceRemoved OnReferenceRemoved;

        /// <summary>
        /// Called when a node produces an event that needs to be reported.
        /// </summary>
        public NodeStateReportEventHandler OnReportEvent;

        /// <summary>
        /// Called when ClearChangeMasks is called and the ChangeMask is not None.
        /// </summary>
        public NodeStateConditionRefreshEventHandler OnConditionRefresh;

        /// <summary>
        /// Called after the CreateBrowser method is called.
        /// </summary>
        public NodeStateCreateBrowserEventHandler OnCreateBrowser;

        /// <summary>
        /// Called after the PopulateBrowser method is called.
        /// </summary>
        public NodeStatePopulateBrowserEventHandler OnPopulateBrowser;

        /// <summary>
        /// Called when the NodeId attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<NodeId> OnReadNodeId;

        /// <summary>
        /// Called when the NodeId attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<NodeId> OnWriteNodeId;

        /// <summary>
        /// Called when the NodeClass attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<NodeClass> OnReadNodeClass;

        /// <summary>
        /// Called when the NodeClass attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<NodeClass> OnWriteNodeClass;

        /// <summary>
        /// Called when the BrowseName attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<QualifiedName> OnReadBrowseName;

        /// <summary>
        /// Called when the BrowseName attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<QualifiedName> OnWriteBrowseName;

        /// <summary>
        /// Called when the DisplayName attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnReadDisplayName;

        /// <summary>
        /// Called when the DisplayName attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnWriteDisplayName;

        /// <summary>
        /// Called when the Description attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnReadDescription;

        /// <summary>
        /// Called when the Description attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<LocalizedText> OnWriteDescription;

        /// <summary>
        /// Called when the WriteMask attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<AttributeWriteMask> OnReadWriteMask;

        /// <summary>
        /// Called when the WriteMask attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<AttributeWriteMask> OnWriteWriteMask;

        /// <summary>
        /// Called when the UserWriteMask attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<AttributeWriteMask> OnReadUserWriteMask;

        /// <summary>
        /// Called when the UserWriteMask attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<AttributeWriteMask> OnWriteUserWriteMask;

        /// <summary>
        /// Called when the RolePermissions attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<RolePermissionTypeCollection> OnReadRolePermissions;

        /// <summary>
        /// Called when the RolePermissions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<RolePermissionTypeCollection> OnWriteRolePermissions;

        /// <summary>
        /// Called when the UserRolePermissions attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<RolePermissionTypeCollection> OnReadUserRolePermissions;

        /// <summary>
        /// Called when the UserRolePermissions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<RolePermissionTypeCollection> OnWriteUserRolePermissions;

        /// <summary>
        /// Called when the AccessRestrictions attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<AccessRestrictionType?> OnReadAccessRestrictions;

        /// <summary>
        /// Called when the AccessRestrictions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<AccessRestrictionType?> OnWriteAccessRestrictions;

        /// <summary>
        /// Returns the root node if the node is part of an instance hierarchy.
        /// </summary>
        public NodeState GetHierarchyRoot()
        {
            // only instance nodes can be part of a hierarchy.

            if ((this is not BaseInstanceState instance) || instance.Parent == null)
            {
                return this;
            }

            // find the root.
            NodeState root = instance.Parent;

            while (true)
            {
                instance = root as BaseInstanceState;

                if (instance == null || instance.Parent == null)
                {
                    return root;
                }

                root = instance.Parent;
            }
        }

        /// <summary>
        /// True if events produced by the instance are being monitored.
        /// </summary>
        public bool AreEventsMonitored => m_areEventsMonitored > 0;

        /// <summary>
        /// True if the node and its children have been initialized.
        /// </summary>
        public bool Initialized { get; set; }

        /// <summary>
        /// True if the node must be validated with the underlying system before use.
        /// </summary>
        public bool ValidationRequired => OnValidate != null;

        /// <summary>
        /// Sets the flag which indicates whether event are being monitored for the instance and its children.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="areEventsMonitored">True if monitoring is active.</param>
        /// <param name="includeChildren">Whether to recursively set the flag on any children.</param>
        public void SetAreEventsMonitored(
            ISystemContext context,
            bool areEventsMonitored,
            bool includeChildren)
        {
            lock (m_areEventsMonitoredLock)
            {
                if (areEventsMonitored)
                {
                    m_areEventsMonitored++;
                }
                else if (m_areEventsMonitored > 0)
                {
                    m_areEventsMonitored--;
                }
            }

            // propagate monitoring flag to children.
            if (includeChildren)
            {
                var children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].SetAreEventsMonitored(context, areEventsMonitored, true);
                }

                List<Notifier> notifiers;

                lock (m_notifiersLock)
                {
                    notifiers = m_notifiers != null ? [.. m_notifiers] : null;
                }

                // propagate monitoring flag to target notifiers.
                if (notifiers != null)
                {
                    for (int ii = 0; ii < notifiers.Count; ii++)
                    {
                        if (!notifiers[ii].IsInverse)
                        {
                            notifiers[ii].Node.SetAreEventsMonitored(
                                context,
                                areEventsMonitored,
                                includeChildren);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reports an event produced by the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="e">The event to report.</param>
        public virtual void ReportEvent(ISystemContext context, IFilterTarget e)
        {
            OnReportEvent?.Invoke(context, this, e);

            List<Notifier> notifiers;

            lock (m_notifiersLock)
            {
                notifiers = m_notifiers != null ? [.. m_notifiers] : null;
            }

            // report event to notifier sources.
            if (notifiers != null)
            {
                for (int ii = 0; ii < notifiers.Count; ii++)
                {
                    if (notifiers[ii].IsInverse)
                    {
                        notifiers[ii].Node.ReportEvent(context, e);
                    }
                }
            }
        }

        /// <summary>
        /// Adds a notifier relationship to the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="referenceTypeId">The type of reference (HasEventSource is used if null specified).</param>
        /// <param name="isInverse">True for an inverse reference.</param>
        /// <param name="target">The target of the reference.</param>
        public virtual void AddNotifier(
            ISystemContext context,
            NodeId referenceTypeId,
            bool isInverse,
            NodeState target)
        {
            if (referenceTypeId.IsNull)
            {
                referenceTypeId = ReferenceTypeIds.HasEventSource;
            }

            // ensure duplicate references are not left over from the model design.
            if (!target.NodeId.IsNull)
            {
                RemoveReference(referenceTypeId, isInverse, target.NodeId);
            }

            lock (m_notifiersLock)
            {
                m_notifiers ??= [];

                // check for existing reference.
                Notifier entry = null;

                for (int ii = 0; ii < m_notifiers.Count; ii++)
                {
                    if (ReferenceEquals(m_notifiers[ii].Node, target))
                    {
                        entry = m_notifiers[ii];
                        break;
                    }
                }

                if (entry == null)
                {
                    entry = new Notifier();
                    m_notifiers.Add(entry);
                }

                // save the notifier.
                entry.ReferenceTypeId = referenceTypeId;
                entry.IsInverse = isInverse;
                entry.Node = target;
            }
        }

        /// <summary>
        /// Removes a notifier relationship from the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="target">The target of the notifier relationship.</param>
        /// <param name="bidirectional">Whether the inverse relationship should be removed from the target.</param>
        public virtual void RemoveNotifier(
            ISystemContext context,
            NodeState target,
            bool bidirectional)
        {
            NodeState nodeState = null;

            lock (m_notifiersLock)
            {
                if (m_notifiers != null)
                {
                    for (int ii = 0; ii < m_notifiers.Count; ii++)
                    {
                        Notifier entry = m_notifiers[ii];

                        if (ReferenceEquals(entry.Node, target))
                        {
                            nodeState = entry.Node;
                            m_notifiers.RemoveAt(ii);
                            break;
                        }
                    }

                    if (m_notifiers.Count == 0)
                    {
                        m_notifiers = null;
                    }
                }
            }

            if (nodeState != null && bidirectional)
            {
                nodeState.RemoveNotifier(context, this, false);
            }
        }

        /// <summary>
        /// Populates a list with the notifiers that belong to the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="notifiers">The list of notifiers to populate.</param>
        public virtual void GetNotifiers(ISystemContext context, IList<Notifier> notifiers)
        {
            lock (m_notifiersLock)
            {
                if (m_notifiers != null)
                {
                    foreach (Notifier notifier in m_notifiers)
                    {
                        notifiers.Add(notifier);
                    }
                }
            }
        }

        /// <summary>
        /// Returns any notifiers with the specified notifier type (NodeId) and direction.
        /// </summary>
        public virtual void GetNotifiers(
            ISystemContext context,
            IList<Notifier> notifiers,
            NodeId notifierTypeId,
            bool isInverse)
        {
            lock (m_notifiersLock)
            {
                if (m_notifiers != null)
                {
                    foreach (Notifier notifier in m_notifiers)
                    {
                        if (isInverse == notifier.IsInverse &&
                            notifier.ReferenceTypeId == notifierTypeId)
                        {
                            notifiers.Add(notifier);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the last event produced for any conditions belonging to the node or its children.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="events">The list of condition events to return.</param>
        /// <param name="includeChildren">Whether to recursively report events for the children.</param>
        public virtual void ConditionRefresh(
            ISystemContext context,
            List<IFilterTarget> events,
            bool includeChildren)
        {
            OnConditionRefresh?.Invoke(context, this, events);

            if (includeChildren)
            {
                // request events from children.
                var children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].ConditionRefresh(context, events, true);
                }

                List<Notifier> notifiers;

                lock (m_notifiersLock)
                {
                    notifiers = m_notifiers != null ? [.. m_notifiers] : null;
                }

                // request events from notifier targets.
                if (notifiers != null)
                {
                    for (int ii = 0; ii < notifiers.Count; ii++)
                    {
                        if (!notifiers[ii].IsInverse)
                        {
                            notifiers[ii].Node.ConditionRefresh(context, events, true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the method with the specified NodeId or MethodDeclarationId.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="methodId">The identifier for the method to find.</param>
        /// <returns>Returns the method. Null if no method found.</returns>
        public virtual MethodState FindMethod(ISystemContext context, NodeId methodId)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is MethodState method)
                {
                    if (method.NodeId == methodId || method.MethodDeclarationId == methodId)
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the specified bits in the change masks (ORs with the current bits).
        /// </summary>
        public void UpdateChangeMasks(NodeStateChangeMasks changeMasks)
        {
            m_changeMasks |= changeMasks;
        }

        /// <summary>
        /// Clears the change masks.
        /// </summary>
        /// <param name="context">The context that describes how access the system containing the data..</param>
        /// <param name="includeChildren">if set to <c>true</c> clear masks recursively for all children..</param>
        public void ClearChangeMasks(ISystemContext context, bool includeChildren)
        {
            if (includeChildren)
            {
                var children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].ClearChangeMasks(context, true);
                }
            }

            NodeStateChangeMasks changeMasks = m_changeMasks;

            if (changeMasks != NodeStateChangeMasks.None)
            {
                OnStateChanged?.Invoke(context, this, changeMasks);
                StateChanged?.Invoke(context, this, changeMasks);
                m_changeMasks = NodeStateChangeMasks.None;
            }
        }

        /// <summary>
        /// Recursively sets the status code and timestamp for the node and all child variables.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="timestamp">The timestamp. Not updated if set to DateTime.Min</param>
        public virtual void SetStatusCode(
            ISystemContext context,
            StatusCode statusCode,
            DateTime timestamp)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].SetStatusCode(context, statusCode, timestamp);
            }
        }

        /// <summary>
        /// Called before a node is created.
        /// </summary>
        protected virtual void OnBeforeCreate(ISystemContext context, NodeState node)
        {
            // defined by the sub-class.
        }

        /// <summary>
        /// Called before the ids are assigned to the node and its children.
        /// </summary>
        protected virtual void OnBeforeAssignNodeIds(ISystemContext context)
        {
            // defined by the sub-class.
        }

        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected virtual void OnAfterCreate(ISystemContext context, NodeState node)
        {
            // defined by the sub-class.
        }

        /// <summary>
        /// Called before the node is deleted.
        /// </summary>
        protected virtual void OnBeforeDelete(ISystemContext context)
        {
            // must be defined by the sub-class.
        }

        /// <summary>
        /// Called after the object is deleted.
        /// </summary>
        protected virtual void OnAfterDelete(ISystemContext context)
        {
            // must be defined by the sub-class.
        }

        /// <summary>
        /// Creates a node with default values and assigns new node ids to it and all children.
        /// </summary>
        public virtual void Create(
            ISystemContext context,
            NodeId nodeId,
            QualifiedName browseName,
            LocalizedText displayName,
            bool assignNodeIds)
        {
            Initialize(context);

            // Call OnBeforeCreate on all children.
            CallOnBeforeCreate(context);

            // override node id.
            if (!nodeId.IsNull)
            {
                NodeId = nodeId;
            }

            // set defaults for names.
            if (!browseName.IsNull)
            {
                SymbolicName = browseName.Name;
                BrowseName = browseName;
                DisplayName = new LocalizedText(browseName.Name);
            }

            // override display name.
            if (!displayName.IsNullOrEmpty)
            {
                DisplayName = displayName;
            }

            CreateInternal(context, assignNodeIds);
        }

        /// <summary>
        /// Internal create sequence without node assignments
        /// </summary>
        private void CreateInternal(ISystemContext context, bool assignNodeIds)
        {
            // get all children.
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            if (assignNodeIds)
            {
                // Call CallOnAssignNodeIds on all children.
                CallOnBeforeAssignNodeIds(context, children);

                // assign the node ids.
                var mappingTable = new Dictionary<NodeId, NodeId>();
                AssignNodeIds(context, children, mappingTable);

                // update the reference targets.
                UpdateReferenceTargets(context, children, mappingTable);
            }

            CallOnAfterCreate(context, null);

            ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Recusivesly calls OnBeforeCreate for the node and its children.
        /// </summary>
        private void CallOnBeforeCreate(ISystemContext context)
        {
            OnBeforeCreate(context, this);

            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].CallOnBeforeCreate(context);
            }
        }

        /// <summary>
        /// Recusivesly calls OnBeforeCreate for the node and its children.
        /// </summary>
        private void CallOnBeforeAssignNodeIds(
            ISystemContext context,
            List<BaseInstanceState> children)
        {
            OnBeforeAssignNodeIds(context);

            if (children == null)
            {
                children = [];
                GetChildren(context, children);
            }

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].CallOnBeforeAssignNodeIds(context, null);
            }
        }

        /// <summary>
        /// Recusivesly calls OnAfterCreate for the node and its children.
        /// </summary>
        private void CallOnAfterCreate(ISystemContext context, List<BaseInstanceState> children)
        {
            if (children == null)
            {
                children = [];
                GetChildren(context, children);
            }

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].CallOnAfterCreate(context, null);
            }

            OnAfterCreate(context, this);
        }

        /// <summary>
        /// Create an instance by copying another node.
        /// </summary>
        public virtual void Create(ISystemContext context, NodeState source)
        {
            Initialize(context, source);
            CallOnBeforeCreate(context);
            CreateInternal(context, false);
        }

        /// <summary>
        /// Deletes an instance and its children (calls OnStateChange callback for each node).
        /// </summary>
        public virtual void Delete(ISystemContext context)
        {
            OnBeforeDelete(context);

            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].Delete(context);
            }

            OnAfterDelete(context);

            ChangeMasks = NodeStateChangeMasks.Deleted;
            ClearChangeMasks(context, false);
        }

        /// <summary>
        /// Called when the predefined node was fully created. Called
        /// by generated code after all children have been created.
        /// </summary>
        public void CreateAsPredefinedNode(ISystemContext context)
        {
            CallOnBeforeCreate(context);
            CreateInternal(context, false);
        }

        /// <summary>
        /// Recursively assigns NodeIds to the node and its children.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="mappingTable">A table mapping the old node ids to the new node ids.</param>
        public virtual void AssignNodeIds(
            ISystemContext context,
            Dictionary<NodeId, NodeId> mappingTable)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);
            AssignNodeIds(context, children, mappingTable);
        }

        /// <summary>
        /// Recursively assigns NodeIds to the node and its children.
        /// </summary>
        private void AssignNodeIds(
            ISystemContext context,
            List<BaseInstanceState> children,
            Dictionary<NodeId, NodeId> mappingTable)
        {
            if (context.NodeIdFactory == null)
            {
                return;
            }

            // update id for instance.
            NodeId oldId = NodeId;
            NodeId newId = context.NodeIdFactory.New(context, this);

            if (!oldId.IsNull)
            {
                mappingTable[oldId] = newId;
            }

            NodeId = newId;

            // update id for children.
            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].AssignNodeIds(context, mappingTable);
            }
        }

        /// <summary>
        /// Verifies that the node represents a valid node.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <returns>True if the node is currently valid.</returns>
        public virtual bool Validate(ISystemContext context)
        {
            NodeStateValidateHandler onValidate = OnValidate;

            if (onValidate != null)
            {
                return onValidate(context, this);
            }

            return true;
        }

        /// <summary>
        /// Creates a browser for the entity references.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="view">The view to use.</param>
        /// <param name="referenceType">The reference type filter to use.</param>
        /// <param name="includeSubtypes">Whether to include sub-types.</param>
        /// <param name="browseDirection">The direction to browse.</param>
        /// <param name="browseName">The browse name of the targets to return.</param>
        /// <param name="additionalReferences">Any additional references that should be included in the list.</param>
        /// <param name="internalOnly">Only return references that are stored in memory.</param>
        /// <returns>A thread safe object which enumerates the references for an entity.</returns>
        public virtual INodeBrowser CreateBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser =
                (
                    OnCreateBrowser?.Invoke(
                        context,
                        this,
                        view,
                        referenceType,
                        includeSubtypes,
                        browseDirection,
                        browseName,
                        additionalReferences,
                        internalOnly))
                ?? new NodeBrowser(
                    context,
                    view,
                    referenceType,
                    includeSubtypes,
                    browseDirection,
                    browseName,
                    additionalReferences,
                    internalOnly);

            PopulateBrowser(context, browser);

            OnPopulateBrowser?.Invoke(context, this, browser);

            return browser;
        }

        /// <summary>
        /// Populates a table with all nodes in the hierarchy.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="browsePath">The path to the parent object.</param>
        /// <param name="hierarchy">A table of all nodes in the hierarchy.</param>
        /// <remarks>
        /// This method is use get a snapshot of the relative paths to all nodes in the hierarchy.
        /// The hierarchy may not be complete if portions of it are stored external systems.
        /// </remarks>
        public void GetInstanceHierarchy(
            ISystemContext context,
            string browsePath,
            Dictionary<NodeId, string> hierarchy)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];
                string childPath = CoreUtils.Format("{0}/{1}", browsePath, child.SymbolicName);
                hierarchy[child.NodeId] = childPath;
                child.GetInstanceHierarchy(context, childPath, hierarchy);
            }
        }

        /// <summary>
        /// Populates a table with all references in the hierarchy.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="browsePath">The path to the parent object.</param>
        /// <param name="hierarchy">A table of all nodes in the hierarchy.</param>
        /// <param name="references">The references in the hierarchy.</param>
        /// <remarks>
        /// The method is used to serialize any additional references between nodes in the hierarchy.
        /// The references are stored as relative paths from the root node.
        /// Any references to nodes outside the hierachy are stored as NodeIds instead of relative paths.
        /// </remarks>
        public void GetHierarchyReferences(
            ISystemContext context,
            string browsePath,
            Dictionary<NodeId, string> hierarchy,
            List<NodeStateHierarchyReference> references)
        {
            lock (m_referencesLock)
            {
                // index any references.
                if (m_references != null)
                {
                    foreach (IReference reference in m_references.Keys)
                    {
                        var targetId = ExpandedNodeId.ToNodeId(
                            reference.TargetId,
                            context.NamespaceUris);

                        if (targetId.IsNull)
                        {
                            references.Add(new NodeStateHierarchyReference(browsePath, reference));
                            continue;
                        }

                        if (!hierarchy.TryGetValue(targetId, out string targetPath))
                        {
                            references.Add(new NodeStateHierarchyReference(browsePath, reference));
                            continue;
                        }

                        references.Add(
                            new NodeStateHierarchyReference(browsePath, targetPath, reference));
                    }
                }
            }

            // recursive index the references for the children.
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                string childPath = CoreUtils.Format("{0}/{1}", browsePath, children[ii].SymbolicName);
                children[ii].GetHierarchyReferences(context, childPath, hierarchy, references);
            }
        }

        /// <summary>
        /// Recursively updates the targets of references.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="mappingTable">A table mapping the old node ids to the new node ids.</param>
        public virtual void UpdateReferenceTargets(
            ISystemContext context,
            Dictionary<NodeId, NodeId> mappingTable)
        {
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);
            UpdateReferenceTargets(context, children, mappingTable);
        }

        /// <summary>
        /// Recursively updates the targets of references.
        /// </summary>
        private void UpdateReferenceTargets(
            ISystemContext context,
            List<BaseInstanceState> children,
            Dictionary<NodeId, NodeId> mappingTable)
        {
            lock (m_referencesLock)
            {
                // check if there are references to update.
                if (m_references != null)
                {
                    var referencesToAdd = new List<IReference>();
                    var referencesToRemove = new List<IReference>();

                    foreach (IReference reference in m_references.Keys)
                    {
                        // check for absolute id.
                        var oldId = ExpandedNodeId.ToNodeId(
                            reference.TargetId,
                            context.NamespaceUris);

                        if (oldId.IsNull)
                        {
                            continue;
                        }

                        // look up new node id.
                        if (mappingTable.TryGetValue(oldId, out NodeId newId))
                        {
                            referencesToRemove.Add(reference);
                            referencesToAdd.Add(
                                new NodeStateReference(
                                    reference.ReferenceTypeId,
                                    reference.IsInverse,
                                    newId));
                        }
                    }

                    // remove old references.
                    for (int ii = 0; ii < referencesToRemove.Count; ii++)
                    {
                        if (m_references.Remove(referencesToRemove[ii]))
                        {
                            m_changeMasks |= NodeStateChangeMasks.References;
                        }
                    }

                    // add new references.
                    for (int ii = 0; ii < referencesToAdd.Count; ii++)
                    {
                        m_references[referencesToAdd[ii]] = null;
                        m_changeMasks |= NodeStateChangeMasks.References;
                    }
                }
            }

            // recursively update targets for children.
            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].UpdateReferenceTargets(context, mappingTable);
            }
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="browser">The browser to populate.</param>
        protected virtual void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            // get the reference type being browsed.
            NodeId referenceTypeId = browser.ReferenceType;

            if (referenceTypeId.IsNull ||
                browser.ReferenceType == ReferenceTypeIds.References)
            {
                referenceTypeId = default;
            }

            var children = new List<BaseInstanceState>();

            bool childrenRequired = referenceTypeId.IsNull;

            // check if any hierarchial reference is being requested.
            if (!childrenRequired &&
                context.TypeTable != null &&
                context.TypeTable
                    .IsTypeOf(browser.ReferenceType, ReferenceTypeIds.HierarchicalReferences) &&
                browser.BrowseDirection != BrowseDirection.Inverse)
            {
                childrenRequired = true;
            }

            // fetch the children but still need to filter by reference type.
            if (childrenRequired)
            {
                GetChildren(context, children);
            }

            // add children.
            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState instance = children[ii];

                if (!browser.IsRequired(instance))
                {
                    continue;
                }

                if (browser.IsRequired(instance.ReferenceTypeId, false))
                {
                    browser.Add(instance.ReferenceTypeId, false, instance);
                }
            }

            List<Notifier> notifiers;

            lock (m_notifiersLock)
            {
                notifiers = m_notifiers != null ? [.. m_notifiers] : null;
            }

            // add any notifiers.
            if (notifiers != null)
            {
                for (int ii = 0; ii < notifiers.Count; ii++)
                {
                    Notifier entry = notifiers[ii];

                    if (browser.IsRequired(entry.ReferenceTypeId, entry.IsInverse))
                    {
                        browser.Add(entry.ReferenceTypeId, entry.IsInverse, notifiers[ii].Node);
                    }
                }
            }

            var referencesToAdd = new List<IReference>();

            BrowseDirection browserBrowseDirection = browser.BrowseDirection;
            bool browserIncludeSubtypes = browser.IncludeSubtypes;
            NodeId browserReferenceType = browser.ReferenceType;

            lock (m_referencesLock)
            {
                // add any arbitrary references.
                if (m_references != null)
                {
                    if (referenceTypeId.IsNull)
                    {
                        foreach (IReference reference in m_references.Keys)
                        {
                            if (reference.IsInverse)
                            {
                                if (browserBrowseDirection == BrowseDirection.Forward)
                                {
                                    continue;
                                }
                            }
                            else if (browserBrowseDirection == BrowseDirection.Inverse)
                            {
                                continue;
                            }

                            referencesToAdd.Add(reference);
                        }
                    }
                    else
                    {
                        IList<IReference> references;
                        if (browserBrowseDirection != BrowseDirection.Inverse)
                        {
                            if (browserIncludeSubtypes)
                            {
                                references = m_references.Find(
                                    browserReferenceType,
                                    false,
                                    context.TypeTable);
                            }
                            else
                            {
                                references = m_references.Find(browserReferenceType, false);
                            }

                            for (int ii = 0; ii < references.Count; ii++)
                            {
                                referencesToAdd.Add(references[ii]);
                            }
                        }

                        if (browserBrowseDirection != BrowseDirection.Forward)
                        {
                            if (browserIncludeSubtypes)
                            {
                                references = m_references.Find(
                                    browserReferenceType,
                                    true,
                                    context.TypeTable);
                            }
                            else
                            {
                                references = m_references.Find(browserReferenceType, true);
                            }

                            for (int ii = 0; ii < references.Count; ii++)
                            {
                                referencesToAdd.Add(references[ii]);
                            }
                        }
                    }
                }
            }

            foreach (IReference reference in referencesToAdd)
            {
                browser.Add(reference);
            }
        }

        /// <summary>
        /// Reads the values for a set of attributes.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="attributeIds">The attributes to read.</param>
        /// <returns>
        /// A list of values.
        /// If any error occurs for an attribute the value will be null.
        /// </returns>
        public virtual List<object> ReadAttributes(
            ISystemContext context,
            params uint[] attributeIds)
        {
            var values = new List<object>();

            if (attributeIds != null)
            {
                for (int ii = 0; ii < attributeIds.Length; ii++)
                {
                    var value = new DataValue();

                    ServiceResult result = ReadAttribute(
                        context,
                        attributeIds[ii],
                        NumericRange.Empty,
                        default,
                        value);

                    if (ServiceResult.IsBad(result))
                    {
                        values.Add(null);
                        continue;
                    }

                    values.Add(value.Value);
                }
            }

            return values;
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        public virtual ServiceResult ReadAttribute(
            ISystemContext context,
            uint attributeId,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            DataValue value)
        {
            // check for bad parameter.
            if (value == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadStructureMissing,
                    "DataValue missing");
            }

            Variant valueToRead = value.WrappedValue;

            ServiceResult result;
            // read value attribute.
            if (attributeId == Attributes.Value)
            {
                DateTime sourceTimestamp = value.SourceTimestamp;

                try
                {
                    result = ReadValueAttribute(
                        context,
                        indexRange,
                        dataEncoding,
                        ref valueToRead,
                        ref sourceTimestamp);

                    value.SourceTimestamp = sourceTimestamp;
                    value.SourcePicoseconds = 0;
                }
                catch (Exception e)
                {
                    result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Failed to read value attribute from node.");
                }
            }
            // read any non-value attribute.
            else
            {
                try
                {
                    result = ReadNonValueAttribute(context, attributeId, ref valueToRead);
                }
                catch (Exception e)
                {
                    result = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Failed to read non value attribute from node.");
                }
            }

            // ensure status code matches result.
            if (result != null && result != ServiceResult.Good)
            {
                value.StatusCode = result.StatusCode;
            }
            else
            {
                value.StatusCode = StatusCodes.Good;
            }

            // update value.
            if (StatusCode.IsBad(value.StatusCode))
            {
                value.WrappedValue = Variant.Null;
            }
            else
            {
                value.WrappedValue = new Variant(valueToRead);
            }

            // return result.
            return result;
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute identifier <see cref="Attributes"/>.</param>
        /// <param name="value">The returned value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected virtual ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.NodeId:
                    NodeId nodeId = m_nodeId;

                    NodeAttributeEventHandler<NodeId> onReadNodeId = OnReadNodeId;

                    if (onReadNodeId != null)
                    {
                        result = onReadNodeId(context, this, ref nodeId);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = nodeId;
                    }

                    return result;
                case Attributes.NodeClass:
                    NodeClass nodeClass = NodeClass;

                    NodeAttributeEventHandler<NodeClass> onReadNodeClass = OnReadNodeClass;

                    if (onReadNodeClass != null)
                    {
                        result = onReadNodeClass(context, this, ref nodeClass);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = Variant.From(nodeClass);
                    }

                    return result;
                case Attributes.BrowseName:
                    QualifiedName browseName = m_browseName;

                    NodeAttributeEventHandler<QualifiedName> onReadBrowseName = OnReadBrowseName;

                    if (onReadBrowseName != null)
                    {
                        result = onReadBrowseName(context, this, ref browseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = browseName;
                    }

                    return result;
                case Attributes.DisplayName:
                    LocalizedText displayName = m_displayName;

                    NodeAttributeEventHandler<LocalizedText> onReadDisplayName = OnReadDisplayName;

                    if (onReadDisplayName != null)
                    {
                        result = onReadDisplayName(context, this, ref displayName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = displayName;
                    }

                    if (!value.IsNull || result != null)
                    {
                        return result;
                    }

                    break;
                case Attributes.Description:
                    LocalizedText description = m_description;

                    NodeAttributeEventHandler<LocalizedText> onReadDescription = OnReadDescription;

                    if (onReadDescription != null)
                    {
                        result = onReadDescription(context, this, ref description);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = description;
                    }

                    if (!value.IsNull || result != null)
                    {
                        return result;
                    }

                    break;
                case Attributes.WriteMask:
                    AttributeWriteMask writeMask = m_writeMask;

                    NodeAttributeEventHandler<AttributeWriteMask> onReadWriteMask = OnReadWriteMask;

                    if (onReadWriteMask != null)
                    {
                        result = onReadWriteMask(context, this, ref writeMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = (uint)writeMask;
                    }

                    return result;
                case Attributes.UserWriteMask:
                    AttributeWriteMask userWriteMask = m_userWriteMask;

                    NodeAttributeEventHandler<AttributeWriteMask> onReadUserWriteMask
                        = OnReadUserWriteMask;

                    if (onReadUserWriteMask != null)
                    {
                        result = onReadUserWriteMask(context, this, ref userWriteMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = (uint)userWriteMask;
                    }

                    return result;
                case Attributes.RolePermissions:
                    RolePermissionTypeCollection rolePermissions = m_rolePermissions;

                    NodeAttributeEventHandler<RolePermissionTypeCollection> onReadRolePermissions =
                        OnReadRolePermissions;

                    if (onReadRolePermissions != null)
                    {
                        result = onReadRolePermissions(context, this, ref rolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = Variant.FromStructure(rolePermissions);
                    }

                    if (!value.IsNull || result != null)
                    {
                        return result;
                    }

                    break;
                case Attributes.UserRolePermissions:
                    RolePermissionTypeCollection userRolePermissions = m_userRolePermissions;

                    NodeAttributeEventHandler<RolePermissionTypeCollection> onReadUserRolePermissions =
                        OnReadUserRolePermissions;

                    if (onReadUserRolePermissions != null)
                    {
                        result = onReadUserRolePermissions(context, this, ref userRolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = Variant.FromStructure(userRolePermissions);
                    }

                    if (!value.IsNull || result != null)
                    {
                        return result;
                    }

                    break;
                case Attributes.AccessRestrictions:
                    AccessRestrictionType? accessRestrictions = m_accessRestrictions;

                    NodeAttributeEventHandler<AccessRestrictionType?> onReadAccessRestrictions =
                        OnReadAccessRestrictions;

                    if (onReadAccessRestrictions != null)
                    {
                        result = onReadAccessRestrictions(context, this, ref accessRestrictions);
                    }

                    if (ServiceResult.IsGood(result) && accessRestrictions != null)
                    {
                        value = (ushort)accessRestrictions;
                    }

                    if (!value.IsNull || result != null)
                    {
                        return result;
                    }

                    break;
            }

            return StatusCodes.BadAttributeIdInvalid;
        }

        /// <summary>
        /// When overridden in a derived class, iReads the value for the value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value to be returned.</param>
        /// <param name="sourceTimestamp">The source timestamp.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected virtual ServiceResult ReadValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref DateTime sourceTimestamp)
        {
            value = Variant.Null;
            sourceTimestamp = DateTime.MinValue;
            return StatusCodes.BadAttributeIdInvalid;
        }

        /// <summary>
        /// Writes the specified attribute value.
        /// </summary>
        /// <param name="context">The context for the current operation.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        public ServiceResult WriteAttribute(
            ISystemContext context,
            uint attributeId,
            NumericRange indexRange,
            DataValue value)
        {
            // check for bad parameter.
            if (value == null)
            {
                return ServiceResult.Create(
                    StatusCodes.BadStructureMissing,
                    "DataValue missing");
            }

            Variant valueToWrite = value.WrappedValue;

            if (attributeId == Attributes.Value)
            {
                // writes to server timestamp never supported.
                if (value.ServerTimestamp != DateTime.MinValue)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadWriteNotSupported,
                        "Cannot write to server timestamp");
                }

                // call implementation.
                try
                {
                    return WriteValueAttribute(
                        context,
                        indexRange,
                        valueToWrite,
                        value.StatusCode,
                        value.SourceTimestamp);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Failed to write value attribute.");
                }
            }

            // writes to status code or timestamps never supported.
            if (value.StatusCode != StatusCodes.Good ||
                value.ServerTimestamp != DateTime.MinValue ||
                value.SourceTimestamp != DateTime.MinValue)
            {
                return ServiceResult.Create(
                    StatusCodes.BadWriteNotSupported,
                    "Cannot write timestamps or status codes to non value attributes.");
            }

            // cannot use index range for non-value attributes.
            if (indexRange != NumericRange.Empty)
            {
                return ServiceResult.Create(
                    StatusCodes.BadIndexRangeInvalid,
                    "Index range can only be specified for value attribute");
            }

            // call implementation.
            try
            {
                return WriteNonValueAttribute(context, attributeId, valueToWrite);
            }
            catch (Exception e)
            {
                return ServiceResult.Create(e,
                    StatusCodes.BadUnexpectedError,
                    "Failed to write non value attribute");
            }
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected virtual ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.NodeId:
                    if (!value.TryGet(out NodeId nodeId))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.NodeId) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<NodeId> onWriteNodeId = OnWriteNodeId;

                    if (onWriteNodeId != null)
                    {
                        result = onWriteNodeId(context, this, ref nodeId);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_nodeId = nodeId;
                    }

                    return result;
                case Attributes.NodeClass:
                    if (!value.TryGet(out NodeClass nodeClass))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.NodeClass) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<NodeClass> onWriteNodeClass = OnWriteNodeClass;

                    if (onWriteNodeClass != null)
                    {
                        result = onWriteNodeClass(context, this, ref nodeClass);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        NodeClass = nodeClass;
                    }

                    return result;
                case Attributes.BrowseName:
                    if (!value.TryGet(out QualifiedName browseName))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.BrowseName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<QualifiedName> onWriteBrowseName = OnWriteBrowseName;

                    if (onWriteBrowseName != null)
                    {
                        result = onWriteBrowseName(context, this, ref browseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_browseName = browseName;
                    }

                    return result;
                case Attributes.DisplayName:
                    if (!value.TryGet(out LocalizedText displayName))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.DisplayName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<LocalizedText> onWriteDisplayName
                        = OnWriteDisplayName;

                    if (onWriteDisplayName != null)
                    {
                        result = onWriteDisplayName(context, this, ref displayName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_displayName = displayName;
                    }

                    return result;
                case Attributes.Description:
                    if (!value.TryGet(out LocalizedText description))
                    {
                        if (!value.IsNull)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }
                        description = LocalizedText.Null;
                    }

                    if ((WriteMask & AttributeWriteMask.Description) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<LocalizedText> onWriteDescription
                        = OnWriteDescription;

                    if (onWriteDescription != null)
                    {
                        result = onWriteDescription(context, this, ref description);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_description = description;
                    }

                    return result;
                case Attributes.WriteMask:
                    if (!value.TryGet(out uint writeMask32))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.WriteMask) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    var writeMask = (AttributeWriteMask)writeMask32;

                    NodeAttributeEventHandler<AttributeWriteMask> onWriteWriteMask
                        = OnWriteWriteMask;

                    if (onWriteWriteMask != null)
                    {
                        result = onWriteWriteMask(context, this, ref writeMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        WriteMask = writeMask;
                    }

                    return result;
                case Attributes.UserWriteMask:
                    if (!value.TryGet(out uint userWriteMask32))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserWriteMask) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    var userWriteMask = (AttributeWriteMask)userWriteMask32;
                    NodeAttributeEventHandler<AttributeWriteMask> onWriteUserWriteMask
                        = OnWriteUserWriteMask;

                    if (onWriteUserWriteMask != null)
                    {
                        result = onWriteUserWriteMask(context, this, ref userWriteMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_userWriteMask = userWriteMask;
                    }

                    return result;
                case Attributes.RolePermissions:
                    if (!value.TryGet(out ExtensionObject[] rolePermissionsArray))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    var rolePermissions = new RolePermissionTypeCollection();

                    foreach (ExtensionObject arrayValue in rolePermissionsArray)
                    {
                        if (arrayValue.Body is not RolePermissionType rolePermission)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }

                        rolePermissions.Add(rolePermission);
                    }

                    if ((WriteMask & AttributeWriteMask.RolePermissions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<RolePermissionTypeCollection> onWriteRolePermissions =
                        OnWriteRolePermissions;

                    if (onWriteRolePermissions != null)
                    {
                        result = onWriteRolePermissions(context, this, ref rolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_rolePermissions = rolePermissions;
                    }

                    return result;
                case Attributes.AccessRestrictions:
                    AccessRestrictionType? accessRestrictions = null;
                    if (value.TryGet(out ushort accessRestrictions16))
                    {
                        accessRestrictions = (AccessRestrictionType)accessRestrictions16;
                    }
                    if (value.TryGet(out uint accessRestrictions32))
                    {
                        accessRestrictions = (AccessRestrictionType)accessRestrictions32;
                    }
                    else if (!value.IsNull)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.AccessRestrictions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<AccessRestrictionType?> onWriteAccessRestrictions =
                        OnWriteAccessRestrictions;

                    if (onWriteAccessRestrictions != null)
                    {
                        result = onWriteAccessRestrictions(context, this, ref accessRestrictions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_accessRestrictions = accessRestrictions;
                    }

                    return result;
                default:
                    return StatusCodes.BadAttributeIdInvalid;
            }
        }

        /// <summary>
        /// Write the value for the value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="value">The value.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="sourceTimestamp">The source timestamp.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected virtual ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            return StatusCodes.BadAttributeIdInvalid;
        }

        /// <summary>
        /// Finds the child by a path constructed from the symbolic names.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="symbolicPath">The symbolic path.</param>
        /// <returns>The matching child. Null if the no child was found.</returns>
        /// <remarks>
        /// This method assumes the symbolicPath consists of symbolic names separated by a slash ('/').
        /// Leading and trailing slashes are ignored.
        /// </remarks>
        public virtual BaseInstanceState FindChildBySymbolicName(
            ISystemContext context,
            string symbolicPath)
        {
            // check for null.
            if (string.IsNullOrEmpty(symbolicPath))
            {
                return null;
            }

            // strip out leading slashes.
            int start = 0;

            while (start < symbolicPath.Length)
            {
                if (symbolicPath[start] != '/')
                {
                    break;
                }

                start++;
            }

            // check if nothing left in path.
            if (start >= symbolicPath.Length)
            {
                return null;
            }

            // find next slash.
            int end = start + 1;

            while (end < symbolicPath.Length)
            {
                if (symbolicPath[end] == '/')
                {
                    break;
                }

                end++;
            }

            // extract the symbolic name for the top level.
            string symbolicName = symbolicPath;

            if (start > 0 || end < symbolicPath.Length)
            {
                symbolicName = symbolicPath[start..end];
            }

            // find the top level child.
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                if (child.SymbolicName == symbolicName)
                {
                    // check if additional path elements remain.
                    if (end < symbolicPath.Length - 1)
                    {
                        return child.FindChildBySymbolicName(context, symbolicPath[(end + 1)..]);
                    }

                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the child with the specified browse name
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="browseName">The browse name.</param>
        /// <returns>The target if found. Null otherwise.</returns>
        public virtual BaseInstanceState FindChild(ISystemContext context, QualifiedName browseName)
        {
            return FindChild(context, browseName, false, null);
        }

        /// <summary>
        /// Finds the child with the specified browse path.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="browsePath">The browse path.</param>
        /// <param name="index">The current position in the browse path.</param>
        /// <returns>The target if found. Null otherwise.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public virtual BaseInstanceState FindChild(
            ISystemContext context,
            IList<QualifiedName> browsePath,
            int index)
        {
            if (index is < 0 or >= int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            BaseInstanceState instance = FindChild(context, browsePath[index], false, null);

            if (instance != null)
            {
                if (browsePath.Count == index + 1)
                {
                    return instance;
                }

                return instance.FindChild(context, browsePath, index + 1);
            }

            return null;
        }

        /// <summary>
        /// Finds or creates the child with the specified browse name.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="browseName">The browse name.</param>
        /// <returns>The child if available. Null otherwise.</returns>
        public virtual BaseInstanceState CreateChild(
            ISystemContext context,
            QualifiedName browseName)
        {
            if (browseName.IsNull)
            {
                return null;
            }

            return FindChild(context, browseName, true, null);
        }

        /// <summary>
        /// Creates or replaces the child with the same browse name.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="child">The child to add or replace.</param>
        /// <exception cref="ArgumentException"></exception>
        public virtual void ReplaceChild(ISystemContext context, BaseInstanceState child)
        {
            if (child == null || child.BrowseName.IsNull)
            {
                throw new ArgumentException("Cannot replace child without a browse name.");
            }

            FindChild(context, child.BrowseName, true, child);
        }

        /// <summary>
        /// Adds a child to the node.
        /// </summary>
        public void AddChild(BaseInstanceState child)
        {
            if (!ReferenceEquals(child.Parent, this))
            {
                child.Parent = this;

                if (child.ReferenceTypeId.IsNull)
                {
                    child.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                }
            }

            lock (m_childrenLock)
            {
                (m_children ??= []).Add(child);
            }

            m_changeMasks |= NodeStateChangeMasks.Children;
        }

        /// <summary>
        /// Creates a property and adds it to the node.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public PropertyState AddProperty<T>(string propertyName, NodeId dataTypeId, int valueRank)
        {
            PropertyState property = new PropertyState<T>(this)
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                ModellingRuleId = default,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                SymbolicName = propertyName,
                NodeId = default,
                BrowseName = QualifiedName.From(propertyName),
                DisplayName = LocalizedText.From(propertyName),
                Description = default,
                WriteMask = 0,
                UserWriteMask = 0,
                Value = default,
                DataType = dataTypeId,
                ValueRank = valueRank,
                ArrayDimensions = null,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate,
                Historizing = false
            };

            AddChild(property);

            return property;
        }

        /// <summary>
        /// Removes an explicitly defined child from the node.
        /// </summary>
        protected virtual void RemoveExplicitlyDefinedChild(BaseInstanceState child)
        {
            // no explicitly defined children on base type.
        }

        /// <summary>
        /// Removes a child from the node.
        /// </summary>
        public virtual void RemoveChild(BaseInstanceState child)
        {
            lock (m_childrenLock)
            {
                if (m_children != null)
                {
                    for (int ii = 0; ii < m_children.Count; ii++)
                    {
                        if (ReferenceEquals(m_children[ii], child))
                        {
                            child.Parent = null;
                            m_children.RemoveAt(ii);
                            m_changeMasks |= NodeStateChangeMasks.Children;
                            return;
                        }
                    }
                }

                RemoveExplicitlyDefinedChild(child);
            }
        }

        /// <summary>
        /// Finds the child with the specified browse and assigns the values from any variables in the hierachy of the source.
        /// </summary>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue(
            ISystemContext context,
            string browseName,
            BaseInstanceState source,
            bool copy)
        {
            return SetChildValue(context, QualifiedName.From(browseName), source, copy);
        }

        /// <summary>
        /// Finds the child with the specified browse and assigns the values from any variables in the hierachy of the source.
        /// </summary>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue(
            ISystemContext context,
            QualifiedName browseName,
            BaseInstanceState source,
            bool copy)
        {
            if (source == null)
            {
                return false;
            }

            if (CreateChild(context, browseName) is not BaseInstanceState child)
            {
                return false;
            }

            if (child is BaseVariableState variable && source is BaseVariableState sourceVariable)
            {
                if (copy)
                {
                    variable.Value = CoreUtils.Clone(sourceVariable.Value);
                }
                else
                {
                    variable.Value = sourceVariable.Value;
                }
            }

            var children = new List<BaseInstanceState>();
            source.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                child.SetChildValue(context, children[ii].BrowseName, children[ii], copy);
            }

            return true;
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue(
            ISystemContext context,
            string browseName,
            Variant value,
            bool copy)
        {
            return SetChildValue(context, QualifiedName.From(browseName), value, copy);
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue(
            ISystemContext context,
            QualifiedName browseName,
            Variant value,
            bool copy)
        {
            if (CreateChild(context, browseName) is not BaseVariableState child)
            {
                return false;
            }

            child.Value = value;
            return true;
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            string browseName,
            T value,
            bool copy) where T : IEncodeable
        {
            return SetChildValue(context, QualifiedName.From(browseName), value, copy);
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            QualifiedName browseName,
            T value,
            bool copy) where T : IEncodeable
        {
            if (CreateChild(context, browseName) is not BaseVariableState child)
            {
                return false;
            }

            child.Value = Variant.FromStructure(value, copy);
            return true;
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            string browseName,
            T[] value,
            bool copy) where T : IEncodeable
        {
            return SetChildValue(context, QualifiedName.From(browseName), value, copy);
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            QualifiedName browseName,
            T[] value,
            bool copy) where T : IEncodeable
        {
            if (CreateChild(context, browseName) is not BaseVariableState child)
            {
                return false;
            }

            child.Value = Variant.FromStructure(value, copy);
            return true;
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            string browseName,
            T value) where T : Enum
        {
            return SetChildValue(context, QualifiedName.From(browseName), value);
        }

        /// <summary>
        /// Finds the child variable with the specified browse and assigns the value to it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>False if the child does not exist or is not a variable.</returns>
        /// <remarks>Creates the child if does not already exist.</remarks>
        public bool SetChildValue<T>(
            ISystemContext context,
            QualifiedName browseName,
            T value) where T : Enum
        {
            if (CreateChild(context, browseName) is not BaseVariableState child)
            {
                return false;
            }

            child.Value = Variant.From(value);
            return true;
        }

        /// <summary>
        /// Reads the attribute of the child node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="relativePath">The relative path.</param>
        /// <param name="index">The index.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="dataValue">The data value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        public virtual ServiceResult ReadChildAttribute(
            ISystemContext context,
            IList<QualifiedName> relativePath,
            int index,
            uint attributeId,
            DataValue dataValue)
        {
            // check if reading attributes of current node.
            if (index >= relativePath.Count)
            {
                return ReadAttribute(context, attributeId, NumericRange.Empty, default, dataValue);
            }

            // find the child at the current level.
            BaseInstanceState child = FindChild(context, relativePath[index], false, null);

            if (child == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // recursively search child nodes.
            ServiceResult result = child.ReadChildAttribute(
                context,
                relativePath,
                index + 1,
                attributeId,
                dataValue);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // success.
            return StatusCodes.Good;
        }

        /// <summary>
        /// Writes the value of the child attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="componentPath">The component path.</param>
        /// <param name="index">The index.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        public ServiceResult WriteChildAttribute(
            ISystemContext context,
            IList<QualifiedName> componentPath,
            int index,
            uint attributeId,
            DataValue value)
        {
            if (componentPath.Count >= index)
            {
                return WriteAttribute(context, attributeId, NumericRange.Empty, value);
            }

            List<BaseInstanceState> children = null;

            lock (m_childrenLock)
            {
                if (m_children != null)
                {
                    children = [.. m_children];
                }
            }

            // recursively update children.
            if (children != null)
            {
                for (int ii = 0; ii < children.Count; ii++)
                {
                    if (componentPath[index] != children[ii].BrowseName)
                    {
                        continue;
                    }

                    return children[ii].WriteChildAttribute(
                        context,
                        componentPath,
                        index + 1,
                        attributeId,
                        value);
                }
            }

            return StatusCodes.BadNodeIdUnknown;
        }

        /// <summary>
        /// Returns true if the reference exists.
        /// </summary>
        /// <param name="referenceTypeId">The type of reference.</param>
        /// <param name="isInverse">Whether the reference is an inverse reference.</param>
        /// <param name="targetId">The target of the reference.</param>
        /// <returns>True if the reference exists.</returns>
        public bool ReferenceExists(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            lock (m_referencesLock)
            {
                if (m_references == null || referenceTypeId.IsNull || targetId.IsNull)
                {
                    return false;
                }

                return m_references.ContainsKey(
                    new NodeStateReference(referenceTypeId, isInverse, targetId));
            }
        }

        /// <summary>
        /// Adds a reference.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the reference is an inverse reference.</param>
        /// <param name="targetId">The target of the reference.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddReference(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            if (targetId.IsNull)
            {
                throw new ArgumentNullException(nameof(targetId));
            }

            lock (m_referencesLock)
            {
                (m_references ??= []).Add(
                    new NodeStateReference(referenceTypeId, isInverse, targetId),
                    null);
            }

            m_changeMasks |= NodeStateChangeMasks.References;

            OnReferenceAdded?.Invoke(this, referenceTypeId, isInverse, targetId);
        }

        /// <summary>
        /// Removes a reference.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the reference is an inverse reference.</param>
        /// <param name="targetId">The target of the reference.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool RemoveReference(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            if (targetId.IsNull)
            {
                throw new ArgumentNullException(nameof(targetId));
            }

            lock (m_referencesLock)
            {
                if (m_references != null &&
                    m_references.Remove(
                        new NodeStateReference(referenceTypeId, isInverse, targetId)))
                {
                    m_changeMasks |= NodeStateChangeMasks.References;
                    OnReferenceRemoved?.Invoke(this, referenceTypeId, isInverse, targetId);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a list of references (ignores duplicates).
        /// </summary>
        /// <param name="references">The list of references to add.</param>
        /// <exception cref="ArgumentNullException"><paramref name="references"/> is <c>null</c>.</exception>
        public void AddReferences(IList<IReference> references)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            var addedReferences = new List<IReference>();

            lock (m_referencesLock)
            {
                m_references ??= [];

                for (int ii = 0; ii < references.Count; ii++)
                {
                    if (!m_references.ContainsKey(references[ii]))
                    {
                        m_references.Add(references[ii], null);
                        addedReferences.Add(references[ii]);
                    }
                }
            }

            foreach (IReference addedReference in addedReferences)
            {
                OnReferenceAdded?.Invoke(
                    this,
                    addedReference.ReferenceTypeId,
                    addedReference.IsInverse,
                    addedReference.TargetId);
            }

            m_changeMasks |= NodeStateChangeMasks.References;
        }

        /// <summary>
        /// Removes all references of the specified type.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the reference is an inverse reference.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public bool RemoveReferences(NodeId referenceTypeId, bool isInverse)
        {
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }

            List<IReference> refsToRemove = null;

            lock (m_referencesLock)
            {
                if (m_references == null)
                {
                    return false;
                }

                refsToRemove =
                [
                    .. m_references
                        .Select(r => r.Key)
                        .Where(
                            r => r.ReferenceTypeId == referenceTypeId && r.IsInverse == isInverse)
                ];
            }

            refsToRemove.ForEach(r => RemoveReference(r.ReferenceTypeId, r.IsInverse, r.TargetId));

            return refsToRemove.Count != 0;
        }

        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        /// <remarks>
        /// This method returns the children that are in memory and does not attempt to
        /// access an underlying system. The PopulateBrowser method is used to discover those references.
        /// </remarks>
        public virtual void GetChildren(ISystemContext context, IList<BaseInstanceState> children)
        {
            lock (m_childrenLock)
            {
                if (m_children != null)
                {
                    for (int ii = 0; ii < m_children.Count; ii++)
                    {
                        children.Add(m_children[ii]);
                    }
                }
            }
        }

        /// <summary>
        /// Populates a list with the non-child related references that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="references">The list of references to populate.</param>
        /// <remarks>
        /// <para>
        /// This method only returns references that are not implied by the parent-child
        /// relation or references which are intrinsic to the NodeState classes (e.g. HasTypeDefinition)
        /// </para>
        /// <para>
        /// This method also only returns the reference that are in memory and does not attempt to
        /// access an underlying system. The PopulateBrowser method is used to discover those references.
        /// </para>
        /// </remarks>
        public virtual void GetReferences(ISystemContext context, IList<IReference> references)
        {
            lock (m_referencesLock)
            {
                if (m_references != null)
                {
                    foreach (IReference reference in m_references.Keys)
                    {
                        references.Add(reference);
                    }
                }
            }
        }

        /// <summary>
        /// Returns any references with the specified reference type and direction.
        /// </summary>
        public virtual void GetReferences(
            ISystemContext context,
            IList<IReference> references,
            NodeId referenceTypeId,
            bool isInverse)
        {
            lock (m_referencesLock)
            {
                if (m_references != null)
                {
                    foreach (IReference reference in m_references.Keys)
                    {
                        if (isInverse == reference.IsInverse &&
                            reference.ReferenceTypeId == referenceTypeId)
                        {
                            references.Add(reference);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="browseName">The browse name of the children to add.</param>
        /// <param name="createOrReplace">if set to <c>true</c> and the child does
        /// not exist then the child is created or replaced with the provided
        /// replacement.</param>
        /// <param name="replacement">The replacement to use if createOrReplace is
        /// true. If not of same type, the node state is used to initialize a new
        /// instance of the required type (for narrowing conversation to the type
        /// definition</param>
        /// <returns>The child.</returns>
        protected virtual BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (browseName.IsNull)
            {
                return null;
            }

            // search for existing child to replace or add to the list of children
            // that are not assigned to a sub type's properties. Unlike the sub
            // type implementations we do not create a new instance here if
            // replacement is null. TODO: should this be reconsidered?
            lock (m_childrenLock)
            {
                if (m_children != null)
                {
                    for (int ii = 0; ii < m_children.Count; ii++)
                    {
                        BaseInstanceState child = m_children[ii];

                        if (browseName == child.BrowseName)
                        {
                            if (createOrReplace && replacement != null)
                            {
                                m_children[ii] = child = replacement;
                            }

                            return child;
                        }
                    }
                }
            }

            if (createOrReplace && replacement != null)
            {
                AddChild(replacement);
            }

            return null;
        }

        /// <summary>
        /// Stores the notifier relationship to another node.
        /// </summary>
        public class Notifier
        {
            /// <summary>
            /// The node state.
            /// </summary>
            public NodeState Node;

            /// <summary>
            /// The reference type id.
            /// </summary>
            public NodeId ReferenceTypeId;

            /// <summary>
            /// Whether the reference direction is inverse.
            /// </summary>
            public bool IsInverse;
        }

        /// <summary>
        /// A list of children of the node.
        /// </summary>
        protected List<BaseInstanceState> m_children;

        /// <summary>
        /// Indicates what has changed in the node.
        /// </summary>
        protected NodeStateChangeMasks m_changeMasks;

        private readonly Lock m_areEventsMonitoredLock = new();
        private readonly Lock m_notifiersLock = new();
        private readonly Lock m_referencesLock = new();
        private readonly Lock m_childrenLock = new();
        private NodeId m_nodeId;
        private QualifiedName m_browseName;
        private LocalizedText m_displayName;
        private LocalizedText m_description;
        private AttributeWriteMask m_writeMask;
        private AttributeWriteMask m_userWriteMask;
        private RolePermissionTypeCollection m_rolePermissions;
        private RolePermissionTypeCollection m_userRolePermissions;
        private AccessRestrictionType? m_accessRestrictions;
        private ReferenceDictionary<object> m_references;
        private int m_areEventsMonitored;
        private List<Notifier> m_notifiers;
    }

    /// <summary>
    /// Indicates what has changed in a node.
    /// </summary>
    [Flags]
    public enum NodeStateChangeMasks
    {
        /// <summary>
        /// None has changed
        /// </summary>
        None = 0x00,

        /// <summary>
        /// One or more children have been added, removed or replaced.
        /// </summary>
        Children = 0x01,

        /// <summary>
        /// One or more references have been added or removed.
        /// </summary>
        References = 0x02,

        /// <summary>
        /// The value attribute has changed.
        /// </summary>
        Value = 0x04,

        /// <summary>
        /// One or more non-value attribute has changed.
        /// </summary>
        NonValue = 0x08,

        /// <summary>
        /// The node has been deleted.
        /// </summary>
        Deleted = 0x10
    }

    /// <summary>
    /// Used to validate a node.
    /// </summary>
    public delegate bool NodeStateValidateHandler(ISystemContext context, NodeState node);

    /// <summary>
    /// Used to receive notifications when a non-value attribute is read or written.
    /// </summary>
    public delegate void NodeStateChangedHandler(
        ISystemContext context,
        NodeState node,
        NodeStateChangeMasks changes);

    /// <summary>
    /// Used to receive notifications when a reference get added to the node
    /// </summary>
    public delegate void NodeStateReferenceAdded(
        NodeState node,
        NodeId referenceTypeId,
        bool isInverse,
        ExpandedNodeId targetId);

    /// <summary>
    /// Used to receive notifications when a reference get removed to the node
    /// </summary>
    public delegate void NodeStateReferenceRemoved(
        NodeState node,
        NodeId referenceTypeId,
        bool isInverse,
        ExpandedNodeId targetId);

    /// <summary>
    /// Used to receive notifications when a node produces an event.
    /// </summary>
    public delegate void NodeStateReportEventHandler(
        ISystemContext context,
        NodeState node,
        IFilterTarget e);

    /// <summary>
    /// Used to receive notifications when a node needs to refresh its conditions.
    /// </summary>
    public delegate void NodeStateConditionRefreshEventHandler(
        ISystemContext context,
        NodeState node,
        List<IFilterTarget> events);

    /// <summary>
    /// Used to receive notifications when a node browser is created.
    /// </summary>
    public delegate NodeBrowser NodeStateCreateBrowserEventHandler(
        ISystemContext context,
        NodeState node,
        ViewDescription view,
        NodeId referenceType,
        bool includeSubtypes,
        BrowseDirection browseDirection,
        QualifiedName browseName,
        IEnumerable<IReference> additionalReferences,
        bool internalOnly);

    /// <summary>
    /// Used to receive notifications when a node is browsed.
    /// </summary>
    public delegate void NodeStatePopulateBrowserEventHandler(
        ISystemContext context,
        NodeState node,
        NodeBrowser browser);

    /// <summary>
    /// Used to receive notifications when a non-value attribute is read or written.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public delegate ServiceResult NodeAttributeEventHandler<T>(
        ISystemContext context,
        NodeState node,
        ref T value);

    /// <summary>
    /// Used to receive notifications when the value attribute is read or written.
    /// </summary>
    public delegate ServiceResult NodeValueSimpleEventHandler(
        ISystemContext context,
        NodeState node,
        ref Variant value);

    /// <summary>
    /// Used to receive notifications when the value attribute is read or written.
    /// </summary>
    public delegate ServiceResult NodeValueEventHandler(
        ISystemContext context,
        NodeState node,
        NumericRange indexRange,
        QualifiedName dataEncoding,
        ref Variant value,
        ref StatusCode statusCode,
        ref DateTime timestamp);

    /// <summary>
    /// Stores a reference from a node in the instance hierarchy.
    /// </summary>
    public class NodeStateHierarchyReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateHierarchyReference"/> class.
        /// </summary>
        /// <param name="sourcePath">The path to the source node.</param>
        /// <param name="reference">The reference.</param>
        public NodeStateHierarchyReference(string sourcePath, IReference reference)
        {
            SourcePath = sourcePath;
            ReferenceTypeId = reference.ReferenceTypeId;
            IsInverse = reference.IsInverse;
            TargetPath = null;
            TargetId = reference.TargetId;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateHierarchyReference"/> class.
        /// </summary>
        /// <param name="sourcePath">The path to the source node.</param>
        /// <param name="targetPath">The path to the target node.</param>
        /// <param name="reference">The reference.</param>
        public NodeStateHierarchyReference(
            string sourcePath,
            string targetPath,
            IReference reference)
        {
            SourcePath = sourcePath;
            ReferenceTypeId = reference.ReferenceTypeId;
            IsInverse = reference.IsInverse;
            TargetPath = targetPath;
        }

        /// <summary>
        /// Gets the path to the source node.
        /// </summary>
        /// <value>The source path.</value>
        public string SourcePath { get; }

        /// <summary>
        /// Gets the identifier for the reference type.
        /// </summary>
        /// <value>The reference type id.</value>
        public NodeId ReferenceTypeId { get; }

        /// <summary>
        /// Gets a value indicating whether the reference is an inverse reference.
        /// </summary>
        /// <value>
        /// <c>true</c> if this is an inverse reference; otherwise, <c>false</c>.
        /// </value>
        public bool IsInverse { get; }

        /// <summary>
        /// Gets the identifier for the target node.
        /// </summary>
        /// <value>The target id.</value>
        /// <remarks>Only one of TargetId or TargetPath is specified.</remarks>
        public ExpandedNodeId TargetId { get; }

        /// <summary>
        /// Gets the path to the target node.
        /// </summary>
        /// <value>The target path.</value>
        /// <remarks>Only one of TargetId or TargetPath is specified.</remarks>
        public string TargetPath { get; }
    }

    /// <summary>
    /// A delegate which creates a new node.
    /// </summary>
    /// <param name="parent">The parent of the node.</param>
    /// <returns>The new node.</returns>
    public delegate NodeState NodeStateConstructDelegate(NodeState parent);
}
