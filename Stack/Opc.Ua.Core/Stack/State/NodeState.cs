/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for custom nodes.
    /// </summary>
    public abstract class NodeState : IDisposable, IFormattable
    {
        #region Constructors
        /// <summary>
        /// Creates an empty object.
        /// </summary>
        /// <param name="nodeClass">The node class.</param>
        protected NodeState(NodeClass nodeClass)
        {
            m_nodeClass = nodeClass;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        public virtual void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // does nothing.
        }
        #endregion

        #region Initialization
        /// <summary>
        /// When overridden in a derived class, iinitializes the instance with the default values.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        protected virtual void Initialize(ISystemContext context)
        {
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
        /// Initializes the instance with the XML or bnary (array of bytes) representation contained in the string.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="initializationString">The initialization string that is used to initializes the node.</param>
        public virtual void Initialize(ISystemContext context, string initializationString)
        {
            if (initializationString.StartsWith("<"))
            {
                using (System.IO.StringReader reader = new System.IO.StringReader(initializationString))
                {
                    LoadFromXml(context, reader);
                }
            }
            else
            {
                byte[] bytes = Convert.FromBase64String(initializationString);

                using (System.IO.MemoryStream istrm = new MemoryStream(bytes))
                {
                    LoadAsBinary(context, istrm);
                }
            }
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="source">The source node.</param>
        protected virtual void Initialize(ISystemContext context, NodeState source)
        {
            m_handle = source.m_handle;
            m_symbolicName = source.m_symbolicName;
            m_nodeId = source.m_nodeId;
            m_nodeClass = source.m_nodeClass;
            m_browseName = source.m_browseName;
            m_displayName = source.m_displayName;
            m_description = source.m_description;
            m_writeMask = source.m_writeMask;
            m_children = null;
            m_references = null;
            m_changeMasks = NodeStateChangeMasks.None;

            // set the initialization flags.
            m_initialized = true;

            List<BaseInstanceState> children = new List<BaseInstanceState>();
            source.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState sourceChild = children[ii];
                BaseInstanceState child = CreateChild(context, sourceChild.BrowseName);

                if (child == null)
                {
                    child = (BaseInstanceState)sourceChild.MemberwiseClone();
                    AddChild(child);
                }

                child.Initialize(context, sourceChild);
            }

            List<IReference> references = new List<IReference>();
            source.GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                IReference reference = references[ii];
                AddReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
            }
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns a string representation of the node.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the node.
        /// </summary>
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            if (!QualifiedName.IsNull(m_browseName))
            {
                return Utils.Format("[{0}]{1}", m_nodeClass, m_displayName);
            }

            return Utils.Format("[{0}]{1}", m_nodeClass, m_nodeId);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// An arbitrary handle associated with the node.
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
            set { m_handle = value; }
        }

        /// <summary>
        /// What has changed in the node since <see cref="ClearChangeMasks"/> was last called.
        /// </summary>
        /// <value>The change masks that indicates what has changed in a node.</value>
        public NodeStateChangeMasks ChangeMasks
        {
            get { return m_changeMasks; }
            protected set { m_changeMasks = value; }
        }

        /// <summary>
        /// A symbolic name for the node that is not expected to be globally unique.
        /// </summary>
        /// <value>The name of the symbolic.</value>
        /// <remarks>
        /// This string can only contain characters that are valid for an XML element name.
        /// </remarks>
        public string SymbolicName
        {
            get { return m_symbolicName; }
            set { m_symbolicName = value; }
        }

        /// <summary>
        /// The identifier for the node.
        /// </summary>
        /// <value>An instance that stores an identifier for a node in a server's address space.</value>
        public NodeId NodeId
        {
            get
            {
                return m_nodeId;
            }

            set
            {
                if (!Object.ReferenceEquals(m_nodeId, value))
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
        public NodeClass NodeClass
        {
            get { return m_nodeClass; }
        }

        /// <summary>
        /// The browse name of the node.
        /// </summary>
        /// <value>The name qualified with a namespace.</value>
        public QualifiedName BrowseName
        {
            get
            {
                return m_browseName;
            }

            set
            {
                if (!Object.ReferenceEquals(m_browseName, value))
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
            get
            {
                return m_displayName;
            }

            set
            {
                if (!Object.ReferenceEquals(m_displayName, value))
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
            get
            {
                return m_description;
            }

            set
            {
                if (!Object.ReferenceEquals(m_description, value))
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
            get
            {
                return m_writeMask;
            }

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
            get
            {
                return m_userWriteMask;
            }

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
            get
            {
                return m_rolePermissions;
            }

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
        /// Specifies  a list of permissions for the node assigned to roles for the current user.
        /// </summary>
        /// <value>The Permissions that apply to the node for the current user.</value>
        public RolePermissionTypeCollection UserRolePermissions
        {
            get
            {
                return m_userRolePermissions;
            }

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
        public AccessRestrictionType AccessRestrictions
        {
            get
            {
                return m_accessRestrictions;
            }

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
        /// Gets or sets the extensions of the node set. Property used when importing nodeset2.xml files.
        /// </summary>
        /// <value>
        /// The extensions.
        /// </value>
        public System.Xml.XmlElement[] Extensions
        {
            get
            {
                return m_extensions;
            }
            set
            {
                m_extensions = value;
            }
        }

        /// <summary>
        /// The categories assigned to the node.
        /// </summary>
        public IList<string> Categories { get; set; }

        /// <summary>
        /// The release status for the node.
        /// </summary>
        public Opc.Ua.Export.ReleaseStatus ReleaseStatus { get; set; }
        #endregion

        #region Serialization Methods
        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The object that describes how access the system containing the data.</param>
        /// <param name="table">A table of nodes.</param>
        public void Export(ISystemContext context, NodeTable table)
        {
            Node node = null;

            switch (NodeClass)
            {
                case NodeClass.Object: { node = new ObjectNode(); break; }
                case NodeClass.ObjectType: { node = new ObjectTypeNode(); break; }
                case NodeClass.Variable: { node = new VariableNode(); break; }
                case NodeClass.VariableType: { node = new VariableTypeNode(); break; }
                case NodeClass.Method: { node = new MethodNode(); break; }
                case NodeClass.ReferenceType: { node = new ReferenceTypeNode(); break; }
                case NodeClass.DataType: { node = new DataTypeNode(); break; }
                case NodeClass.View: { node = new ViewNode(); break; }

                default:
                    {
                        node = new Node();
                        break;
                    }
            }

            Export(context, node);

            List<IReference> references = new List<IReference>();
            GetReferences(context, references);

            for (int ii = 0; ii < references.Count; ii++)
            {
                node.ReferenceTable.Add(references[ii]);
            }

            table.Attach(node);

            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            node.NodeId = this.NodeId;
            node.NodeClass = this.NodeClass;
            node.BrowseName = this.BrowseName;
            node.DisplayName = this.DisplayName;
            node.Description = this.Description;
            node.WriteMask = (uint)this.WriteMask;
            node.UserWriteMask = (uint)this.UserWriteMask;
        }

        /// <summary>
        /// Saves the node as XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="ostrm">The stream to write.</param>
        public void SaveAsXml(ISystemContext context, Stream ostrm)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Encoding = Encoding.UTF8;
            settings.CloseOutput = true;
            settings.ConformanceLevel = ConformanceLevel.Document;
            settings.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(ostrm, settings))
            {
                XmlQualifiedName root = new XmlQualifiedName(this.SymbolicName, context.NamespaceUris.GetString(this.BrowseName.NamespaceIndex));

                XmlEncoder encoder = new XmlEncoder(root, writer, messageContext);

                encoder.SaveStringTable("NamespaceUris", "NamespaceUri", context.NamespaceUris);
                encoder.SaveStringTable("ServerUris", "ServerUri", context.ServerUris);

                Save(context, encoder);
                SaveReferences(context, encoder);
                SaveChildren(context, encoder);

                encoder.Close();
            }
        }

        /// <summary>
        /// Saves the node in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="ostrm">The stream to write.</param>
        public void SaveAsBinary(ISystemContext context, Stream ostrm)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            BinaryEncoder encoder = new BinaryEncoder(ostrm, messageContext);

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
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            BinaryDecoder decoder = new BinaryDecoder(istrm, messageContext);

            // check if a namespace table was provided.
            NamespaceTable namespaceUris = new NamespaceTable();

            if (!decoder.LoadStringTable(namespaceUris))
            {
                namespaceUris = null;
            }

            // check if a server uri table was provided.
            StringTable serverUris = new StringTable();

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
            AttributesToSave attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);
            Update(context, decoder, attributesToLoad);
            UpdateReferences(context, decoder);
            UpdateChildren(context, decoder);
        }

        #region AttributesToSave Enumeration
        /// <summary>
        /// Flags which control the serialization of a NodeState in a stream.
        /// </summary>
        [Flags]
        public enum AttributesToSave
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
        #endregion

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public virtual AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = AttributesToSave.None;

            if (!String.IsNullOrEmpty(m_symbolicName))
            {
                if (m_browseName == null || m_symbolicName != m_browseName.Name)
                {
                    attributesToSave |= AttributesToSave.SymbolicName;
                }
            }

            attributesToSave |= AttributesToSave.NodeClass;

            if (!NodeId.IsNull(m_nodeId))
            {
                attributesToSave |= AttributesToSave.NodeId;
            }

            if (!QualifiedName.IsNull(m_browseName))
            {
                attributesToSave |= AttributesToSave.BrowseName;
            }

            if (!LocalizedText.IsNullOrEmpty(m_displayName))
            {
                if (m_browseName == null || !String.IsNullOrEmpty(m_displayName.Locale) || m_displayName.Text != m_browseName.Name)
                {
                    attributesToSave |= AttributesToSave.DisplayName;
                }
            }

            if (!LocalizedText.IsNullOrEmpty(m_description))
            {
                attributesToSave |= AttributesToSave.Description;
            }

            if (m_writeMask != AttributeWriteMask.None)
            {
                attributesToSave |= AttributesToSave.WriteMask;
            }

            if (m_writeMask != AttributeWriteMask.None)
            {
                attributesToSave |= AttributesToSave.UserAccessLevel;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public virtual void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            encoder.WriteEnumerated(null, m_nodeClass);

            if ((attributesToSave & AttributesToSave.SymbolicName) != 0)
            {
                encoder.WriteString(null, m_symbolicName);
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
        public virtual void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attributesToLoad)
        {
            if ((attributesToLoad & AttributesToSave.NodeClass) != 0)
            {
                m_nodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
            }

            if ((attributesToLoad & AttributesToSave.SymbolicName) != 0)
            {
                m_symbolicName = decoder.ReadString(null);
            }

            if ((attributesToLoad & AttributesToSave.BrowseName) != 0)
            {
                m_browseName = decoder.ReadQualifiedName(null);
            }

            if (String.IsNullOrEmpty(m_symbolicName) && m_browseName != null)
            {
                m_symbolicName = m_browseName.Name;
            }

            if ((attributesToLoad & AttributesToSave.NodeId) != 0)
            {
                m_nodeId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.DisplayName) != 0)
            {
                m_displayName = decoder.ReadLocalizedText(null);
            }

            if (LocalizedText.IsNullOrEmpty(m_displayName) && m_browseName != null)
            {
                m_displayName = m_browseName.Name;
            }

            if ((attributesToLoad & AttributesToSave.Description) != 0)
            {
                m_description = decoder.ReadLocalizedText(null);
            }

            if ((attributesToLoad & AttributesToSave.WriteMask) != 0)
            {
                m_writeMask = (AttributeWriteMask)decoder.ReadEnumerated(null, typeof(AttributeWriteMask));
            }

            if ((attributesToLoad & AttributesToSave.UserWriteMask) != 0)
            {
                m_userWriteMask = (AttributeWriteMask)decoder.ReadEnumerated(null, typeof(AttributeWriteMask));
            }
        }

        /// <summary>
        /// Saves the children in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public virtual void SaveChildren(ISystemContext context, BinaryEncoder encoder)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            AttributesToSave attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);

            NodeClass nodeClass = NodeClass.Unspecified;
            string symbolicName = null;
            QualifiedName browseName = null;

            nodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
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

            if (String.IsNullOrEmpty(symbolicName) && browseName != null)
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
            child = UpdateUnknownChild(context, decoder, this, attributesToLoad, nodeClass, symbolicName, browseName);

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
        public static NodeState LoadNode(
            ISystemContext context,
            BinaryDecoder decoder)
        {
            AttributesToSave attributesToLoad = (AttributesToSave)decoder.ReadUInt32(null);

            NodeClass nodeClass = NodeClass.Unspecified;
            string symbolicName = null;
            QualifiedName browseName = null;

            nodeClass = (NodeClass)decoder.ReadEnumerated(null, typeof(NodeClass));
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

            if (String.IsNullOrEmpty(symbolicName) && browseName != null)
            {
                symbolicName = browseName.Name;
            }

            // read the node from the stream.
            return LoadUnknownNode(context, decoder, attributesToLoad, nodeClass, symbolicName, browseName);
        }

        /// <summary>
        /// Saves the reference table in a binary stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveReferences(ISystemContext context, BinaryEncoder encoder)
        {
            if (m_references == null || m_references.Count <= 0)
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

            for (int ii = 0; ii < count; ii++)
            {
                NodeId referenceTypeId = decoder.ReadNodeId(null);
                bool isInverse = decoder.ReadBoolean(null);
                ExpandedNodeId targetId = decoder.ReadExpandedNodeId(null);

                if (m_references == null)
                {
                    m_references = new IReferenceDictionary<object>();
                }

                m_references[new NodeStateReference(referenceTypeId, isInverse, targetId)] = null;
            }
        }

        /// <summary>
        /// Saves the node as XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveAsXml(ISystemContext context, XmlEncoder encoder)
        {
            encoder.Push(this.SymbolicName, context.NamespaceUris.GetString(this.BrowseName.NamespaceIndex));

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
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ConformanceLevel = ConformanceLevel.Document;

            using (XmlReader reader = XmlReader.Create(input, settings))
            {
                LoadFromXml(context, reader);
            }
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="input">The stream to read.</param>
        public void LoadFromXml(ISystemContext context, Stream input)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ConformanceLevel = ConformanceLevel.Document;

            using (XmlReader reader = XmlReader.Create(input, settings))
            {
                LoadFromXml(context, reader);
            }
        }

        /// <summary>
        /// Initializes the node from XML in a stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="reader">The stream to read.</param>
        public void LoadFromXml(ISystemContext context, XmlReader reader)
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();

            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            messageContext.Factory = context.EncodeableFactory;

            reader.MoveToContent();

            // get the root of the child element.
            XmlQualifiedName symbolicName = new XmlQualifiedName(reader.LocalName, reader.NamespaceURI);

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(symbolicName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not resolve namespace uri: {0}", symbolicName.Namespace);
            }

            // initialize browse name.
            this.SymbolicName = symbolicName.Name;
            this.BrowseName = new QualifiedName(symbolicName.Name, (ushort)namespaceIndex);

            XmlDecoder decoder = new XmlDecoder(null, reader, messageContext);

            // check if a namespace table was provided.
            NamespaceTable namespaceUris = new NamespaceTable();

            if (!decoder.LoadStringTable("NamespaceUris", "NamespaceUri", namespaceUris))
            {
                namespaceUris = null;
            }

            // check if a server uri table was provided.
            StringTable serverUris = new StringTable();

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
        public void LoadFromXml(ISystemContext context, XmlDecoder decoder)
        {
            // get the name of the child element.
            XmlQualifiedName symbolicName = decoder.Peek(XmlNodeType.Element);

            if (symbolicName == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Expecting an XML start element in stream.");
            }

            // map to a namespace index.
            int namespaceIndex = context.NamespaceUris.GetIndex(symbolicName.Namespace);

            if (namespaceIndex < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not resolve namespace uri: {0}", symbolicName.Namespace);
            }

            // initialize browse name.
            this.SymbolicName = symbolicName.Name;
            this.BrowseName = new QualifiedName(symbolicName.Name, (ushort)namespaceIndex);

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

            encoder.WriteEnumerated("NodeClass", m_nodeClass);

            if (!NodeId.IsNull(m_nodeId))
            {
                encoder.WriteNodeId("NodeId", m_nodeId);
            }

            if (!QualifiedName.IsNull(m_browseName))
            {
                encoder.WriteQualifiedName("BrowseName", m_browseName);
            }

            if (!LocalizedText.IsNullOrEmpty(m_displayName))
            {
                if (m_browseName == null || !String.IsNullOrEmpty(m_displayName.Locale) || m_browseName.Name != m_displayName.Text)
                {
                    encoder.WriteLocalizedText("DisplayName", m_displayName);
                }
            }

            if (!LocalizedText.IsNullOrEmpty(m_description))
            {
                encoder.WriteLocalizedText("Description", m_description);
            }

            if (m_writeMask != AttributeWriteMask.None)
            {
                encoder.WriteEnumerated("WriteMask", m_writeMask);
            }

            if (m_writeMask != AttributeWriteMask.None)
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
        public virtual void Update(ISystemContext context, XmlDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("NodeClass"))
            {
                NodeClass nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));

                if (NodeClass != NodeClass.Unspecified && nodeClass != NodeClass)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Unexpected NodeClass in input stream. {0} != {1}", NodeClass, nodeClass);
                }
            }

            if (decoder.Peek("NodeId"))
            {
                NodeId nodeId = decoder.ReadNodeId("NodeId");

                if (!NodeId.IsNull(nodeId))
                {
                    NodeId = nodeId;
                }
            }

            if (decoder.Peek("BrowseName"))
            {
                QualifiedName browseName = decoder.ReadQualifiedName("BrowseName");

                if (!QualifiedName.IsNull(browseName))
                {
                    BrowseName = browseName;
                }
            }

            if (decoder.Peek("DisplayName"))
            {
                DisplayName = decoder.ReadLocalizedText("DisplayName");
            }

            if (LocalizedText.IsNullOrEmpty(m_displayName) && m_browseName != null)
            {
                DisplayName = m_browseName.Name;
            }

            if (decoder.Peek("Description"))
            {
                Description = decoder.ReadLocalizedText("Description");
            }

            if (decoder.Peek("WriteMask"))
            {
                WriteMask = (AttributeWriteMask)decoder.ReadEnumerated("WriteMask", typeof(AttributeWriteMask));
            }

            if (decoder.Peek("UserWriteMask"))
            {
                UserWriteMask = (AttributeWriteMask)decoder.ReadEnumerated("UserWriteMask", typeof(AttributeWriteMask));
            }

            decoder.PopNamespace();

            // set the initialization flags.
            m_initialized = true;
        }

        /// <summary>
        /// Saves the children from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public virtual void SaveChildren(ISystemContext context, XmlEncoder encoder)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                encoder.Push(child.SymbolicName, context.NamespaceUris.GetString(child.BrowseName.NamespaceIndex));

                child.Save(context, encoder);
                child.SaveReferences(context, encoder);
                child.SaveChildren(context, encoder);

                encoder.Pop();
            }
        }

        /// <summary>
        /// Saves a refernce table from an XML stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public void SaveReferences(ISystemContext context, XmlEncoder encoder)
        {
            if (m_references == null || m_references.Count <= 0)
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

                    if (!NodeId.IsNull(reference.ReferenceTypeId))
                    {
                        encoder.WriteNodeId("ReferenceTypeId", reference.ReferenceTypeId);
                    }

                    if (reference.IsInverse)
                    {
                        encoder.WriteBoolean("IsInverse", reference.IsInverse);
                    }

                    if (!NodeId.IsNull(reference.TargetId))
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

                NodeId referenceTypeId = null;
                bool isInverse = false;
                ExpandedNodeId targetId = null;

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
                if (m_references == null)
                {
                    m_references = new IReferenceDictionary<object>();
                }

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
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not resolve namespace uri: {0}", childName.Namespace);
            }

            // move to body.
            decoder.ReadStartElement();

            QualifiedName symbolicName = new QualifiedName(childName.Name, (ushort)namespaceIndex);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // pre-fetch enough information to know what type of node to create.
            NodeClass nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));
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
        public static NodeState LoadNode(
            ISystemContext context,
            XmlDecoder decoder)
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
                throw ServiceResultException.Create(StatusCodes.BadDecodingError, "Could not resolve namespace uri: {0}", childName.Namespace);
            }

            // move to body.
            decoder.ReadStartElement();

            QualifiedName browseName = new QualifiedName(childName.Name, (ushort)namespaceIndex);

            // read the node from the stream.
            return LoadUnknownNode(context, decoder, childName, browseName);
        }

        #region private methods

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

            NodeId nodeId = null;
            LocalizedText displayName = null;
            LocalizedText description = null;
            AttributeWriteMask writeMask = AttributeWriteMask.None;
            AttributeWriteMask userWriteMask = AttributeWriteMask.None;
            NodeId referenceTypeId = null;
            NodeId typeDefinitionId = null;

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

            if (LocalizedText.IsNullOrEmpty(displayName) && browseName != null)
            {
                displayName = browseName.Name;
            }

            if ((attributesToLoad & AttributesToSave.Description) != 0)
            {
                description = decoder.ReadLocalizedText(null);
                attributesToLoad &= ~AttributesToSave.Description;
            }

            if ((attributesToLoad & AttributesToSave.WriteMask) != 0)
            {
                writeMask = (AttributeWriteMask)decoder.ReadEnumerated(null, typeof(AttributeWriteMask));
                attributesToLoad &= ~AttributesToSave.WriteMask;
            }

            if ((attributesToLoad & AttributesToSave.UserWriteMask) != 0)
            {
                writeMask = (AttributeWriteMask)decoder.ReadEnumerated(null, typeof(AttributeWriteMask));
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
            NodeStateFactory factory = context.NodeStateFactory;

            if (factory == null)
            {
                factory = new NodeStateFactory();
            }

            // create the appropriate node.
            BaseInstanceState child = factory.CreateInstance(
                context,
                parent,
                nodeClass,
                browseName,
                referenceTypeId,
                typeDefinitionId) as BaseInstanceState;

            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load child '{1}', with NodeClass {0}",
                    nodeClass,
                    browseName);
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
                {
                    return UpdateUnknownChild(context, decoder, null, attributesToLoad, nodeClass, symbolicName, browseName);
                }
            }

            // get the node factory.
            NodeStateFactory factory = context.NodeStateFactory;

            if (factory == null)
            {
                factory = new NodeStateFactory();
            }

            // create the appropriate node.
            NodeState child = factory.CreateInstance(
                context,
                null,
                nodeClass,
                browseName,
                null,
                null);

            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load node '{1}', with NodeClass {0}",
                    nodeClass,
                    browseName);
            }

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
        }

        /// <summary>
        /// Reads an unknown node from a stream.
        /// </summary>
        private static NodeState LoadUnknownNode(
            ISystemContext context,
            XmlDecoder decoder,
            XmlQualifiedName childName,
            QualifiedName browseName)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            // pre-fetch enough information to know what type of node to create.
            NodeClass nodeClass = (NodeClass)decoder.ReadEnumerated("NodeClass", typeof(NodeClass));

            decoder.PopNamespace();

            // create the appropriate node.
            switch (nodeClass)
            {
                case NodeClass.Variable:
                case NodeClass.Object:
                case NodeClass.Method:
                {
                    return UpdateUnknownChild(context, decoder, null, childName, nodeClass, browseName);
                }
            }

            // get the node factory.
            NodeStateFactory factory = context.NodeStateFactory;

            if (factory == null)
            {
                factory = new NodeStateFactory();
            }

            // create the appropriate node.
            NodeState child = factory.CreateInstance(
                context,
                null,
                nodeClass,
                browseName,
                null,
                null);

            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load node '{1}', with NodeClass {0}",
                    nodeClass,
                    browseName);
            }

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
        }

        /// <summary>
        /// Updates a child which is not defined by the type definition.
        /// </summary>
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

            LocalizedText displayName = null;

            if (decoder.Peek("DisplayName"))
            {
                displayName = decoder.ReadLocalizedText("DisplayName");
            }

            if (LocalizedText.IsNullOrEmpty(displayName) && browseName != null)
            {
                displayName = browseName.Name;
            }

            LocalizedText description = null;

            if (decoder.Peek("Description"))
            {
                description = decoder.ReadLocalizedText("Description");
            }

            AttributeWriteMask writeMask = (AttributeWriteMask)decoder.ReadEnumerated("WriteMask", typeof(AttributeWriteMask));
            AttributeWriteMask userWriteMask = (AttributeWriteMask)decoder.ReadEnumerated("UserWriteMask", typeof(AttributeWriteMask));
            NodeId referenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            NodeId typeDefinitionId = decoder.ReadNodeId("TypeDefinitionId");

            decoder.PopNamespace();

            // get the node factory.
            NodeStateFactory factory = context.NodeStateFactory;

            if (factory == null)
            {
                factory = new NodeStateFactory();
            }

            // create the appropriate node.
            BaseInstanceState child = factory.CreateInstance(
                context,
                parent,
                nodeClass,
                browseName,
                referenceTypeId,
                typeDefinitionId) as BaseInstanceState;

            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "Could not load child '{1}', with NodeClass {0}",
                    nodeClass,
                    browseName);
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
        #endregion

        #endregion

        #region Events
        /// <summary>
        /// An event which allows multiple sinks to be notified when the OnStateChanged callback is called.
        /// </summary>
        public event NodeStateChangedHandler StateChanged;
        #endregion 

        #region Callback Handlers
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
        public NodeAttributeEventHandler<AccessRestrictionType> OnReadAccessRestrictions;

        /// <summary>
        /// Called when the AccessRestrictions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<AccessRestrictionType> OnWriteAccessRestrictions;
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns the root node if the node is part of an instance hierarchy.
        /// </summary>
        /// <returns></returns>
        public NodeState GetHierarchyRoot()
        {
            // only instance nodes can be part of a hierarchy.
            BaseInstanceState instance = this as BaseInstanceState;

            if (instance == null || instance.Parent == null)
            {
                return this;
            }

            // find the root.
            NodeState root = instance.Parent;

            while (root != null)
            {
                instance = root as BaseInstanceState;

                if (instance == null || instance.Parent == null)
                {
                    return root;
                }

                root = instance.Parent;
            }

            return root;
        }

        /// <summary>
        /// True if events produced by the instance are being monitored.
        /// </summary>
        public bool AreEventsMonitored
        {
            get { return m_areEventsMonitored > 0; }
        }

        /// <summary>
        /// True if the node and its children have been initialized.
        /// </summary>
        public bool Initialized
        {
            get { return m_initialized; }
            set { m_initialized = value; }
        }

        /// <summary>
        /// True if the node must be validated with the underlying system before use.
        /// </summary>
        public bool ValidationRequired
        {
            get { return OnValidate != null; }
        }

        /// <summary>
        /// Sets the flag which indicates whether event are being monitored for the instance and its children.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="areEventsMonitored">True if monitoring is active.</param>
        /// <param name="includeChildren">Whether to recursively set the flag on any children.</param>
        public void SetAreEventsMonitored(ISystemContext context, bool areEventsMonitored, bool includeChildren)
        {
            if (areEventsMonitored)
            {
                m_areEventsMonitored++;
            }
            else if (m_areEventsMonitored > 0)
            {
                m_areEventsMonitored--;
            }

            // propagate monitoring flag to children.
            if (includeChildren)
            {
                List<BaseInstanceState> children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].SetAreEventsMonitored(context, areEventsMonitored, true);
                }

                // propagate monitoring flag to target notifiers.
                if (m_notifiers != null)
                {
                    for (int ii = 0; ii < m_notifiers.Count; ii++)
                    {
                        if (!m_notifiers[ii].IsInverse)
                        {
                            m_notifiers[ii].Node.SetAreEventsMonitored(context, areEventsMonitored, includeChildren);
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
            if (OnReportEvent != null)
            {
                OnReportEvent(context, this, e);
            }

            // report event to notifier sources.
            if (m_notifiers != null)
            {
                for (int ii = 0; ii < m_notifiers.Count; ii++)
                {
                    if (m_notifiers[ii].IsInverse)
                    {
                        m_notifiers[ii].Node.ReportEvent(context, e);
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
            if (m_notifiers == null)
            {
                m_notifiers = new List<Notifier>();
            }

            if (NodeId.IsNull(referenceTypeId))
            {
                referenceTypeId = ReferenceTypeIds.HasEventSource;
            }

            // check for existing reference.
            Notifier entry = null;

            for (int ii = 0; ii < m_notifiers.Count; ii++)
            {
                if (Object.ReferenceEquals(m_notifiers[ii].Node, target))
                {
                    entry = m_notifiers[ii];
                    break;
                }
            }

            // ensure duplicate references are not left over from the model design.
            if (!NodeId.IsNull(target.NodeId))
            {
                RemoveReference(referenceTypeId, isInverse, target.NodeId);
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

        /// <summary>
        /// Removes a notifier relationship from the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="target">The target of the notifier relationship.</param>
        /// <param name="bidirectional">Whether the inverse relationship should be removed from the target.</param>
        public virtual void RemoveNotifier(ISystemContext context, NodeState target, bool bidirectional)
        {
            if (m_notifiers != null)
            {
                for (int ii = 0; ii < m_notifiers.Count; ii++)
                {
                    Notifier entry = m_notifiers[ii];

                    if (Object.ReferenceEquals(entry.Node, target))
                    {
                        if (bidirectional)
                        {
                            entry.Node.RemoveNotifier(context, this, false);
                        }

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

        /// <summary>
        /// Populates a list with the notifiers that belong to the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="notifiers">The list of notifiers to populate.</param>
        public virtual void GetNotifiers(
            ISystemContext context,
            IList<Notifier> notifiers)
        {
            if (m_notifiers != null)
            {
                foreach (Notifier notifier in m_notifiers)
                {
                    notifiers.Add(notifier);
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
            if (m_notifiers != null)
            {
                foreach (Notifier notifier in m_notifiers)
                {
                    if (isInverse == notifier.IsInverse && notifier.ReferenceTypeId == notifierTypeId)
                    {
                        notifiers.Add(notifier);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the last event produced for any conditions belonging to the node or its chilren.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="events">The list of condition events to return.</param>
        /// <param name="includeChildren">Whether to recursively report events for the children.</param>
        public virtual void ConditionRefresh(ISystemContext context, List<IFilterTarget> events, bool includeChildren)
        {
            if (OnConditionRefresh != null)
            {
                OnConditionRefresh(context, this, events);
            }

            if (includeChildren)
            {
                // request events from children.
                List<BaseInstanceState> children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].ConditionRefresh(context, events, true);
                }

                // request events from notifier targets.
                if (m_notifiers != null)
                {
                    for (int ii = 0; ii < m_notifiers.Count; ii++)
                    {
                        if (!m_notifiers[ii].IsInverse)
                        {
                            m_notifiers[ii].Node.ConditionRefresh(context, events, true);
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
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                MethodState method = children[ii] as MethodState;

                if (method != null)
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
                List<BaseInstanceState> children = new List<BaseInstanceState>();
                GetChildren(context, children);

                for (int ii = 0; ii < children.Count; ii++)
                {
                    children[ii].ClearChangeMasks(context, true);
                }
            }

            if (m_changeMasks != NodeStateChangeMasks.None)
            {
                if (OnStateChanged != null)
                {
                    OnStateChanged(context, this, m_changeMasks);
                }

                if (StateChanged != null)
                {
                    StateChanged(context, this, m_changeMasks);
                }

                m_changeMasks = NodeStateChangeMasks.None;
            }
        }

        /// <summary>
        /// Recusively sets the status code and timestamp for the node and all child variables.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="timestamp">The timestamp. Not updated if set to DateTime.Min</param>
        public virtual void SetStatusCode(ISystemContext context, StatusCode statusCode, DateTime timestamp)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            if (nodeId != null)
            {
                NodeId = nodeId;
            }

            // set defaults for names.
            if (!QualifiedName.IsNull(browseName))
            {
                SymbolicName = browseName.Name;
                BrowseName = browseName;
                DisplayName = browseName.Name;
            }

            // override display name.
            if (displayName != null)
            {
                DisplayName = displayName;
            }

            // get all children.
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            if (assignNodeIds)
            {
                // Call CallOnAssignNodeIds on all children.
                CallOnBeforeAssignNodeIds(context, children);

                // assign the node ids.
                Dictionary<NodeId, NodeId> mappingTable = new Dictionary<NodeId, NodeId>();
                AssignNodeIds(context, children, mappingTable);

                // update the reference targets.
                UpdateReferenceTargets(context, children, mappingTable);
            }

            // Call OnAfterCreate on all children.
            CallOnAfterCreate(context, children);

            ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Recusivesly calls OnBeforeCreate for the node and its children.
        /// </summary>
        private void CallOnBeforeCreate(ISystemContext context)
        {
            OnBeforeCreate(context, this);

            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                children[ii].CallOnBeforeCreate(context);
            }
        }

        /// <summary>
        /// Recusivesly calls OnBeforeCreate for the node and its children.
        /// </summary>
        private void CallOnBeforeAssignNodeIds(ISystemContext context, List<BaseInstanceState> children)
        {
            OnBeforeAssignNodeIds(context);

            if (children == null)
            {
                children = new List<BaseInstanceState>();
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
                children = new List<BaseInstanceState>();
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

            CallOnAfterCreate(context, null);

            ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Deletes an instance and its children (calls OnStateChange callback for each node).
        /// </summary>
        public virtual void Delete(ISystemContext context)
        {
            OnBeforeDelete(context);

            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
        /// Recursively assigns NodeIds to the node and its children.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="mappingTable">A table mapping the old node ids to the new node ids.</param>
        public virtual void AssignNodeIds(ISystemContext context, Dictionary<NodeId, NodeId> mappingTable)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
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

            if (!NodeId.IsNull(oldId))
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
            if (OnValidate != null)
            {
                return OnValidate(context, this);
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
        /// <returns>A thread safe object which enumerates the refernces for an entity.</returns>
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
            NodeBrowser browser = null;

            // see if a callback has been provided.
            if (OnCreateBrowser != null)
            {
                browser = OnCreateBrowser(
                    context,
                    this,
                    view,
                    referenceType,
                    includeSubtypes,
                    browseDirection,
                    browseName,
                    additionalReferences,
                    internalOnly);
            }

            // use default browser.
            if (browser == null)
            {
                browser = new NodeBrowser(
                    context,
                    view,
                    referenceType,
                    includeSubtypes,
                    browseDirection,
                    browseName,
                    additionalReferences,
                    internalOnly);
            }

            PopulateBrowser(context, browser);

            if (OnPopulateBrowser != null)
            {
                OnPopulateBrowser(context, this, browser);
            }

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
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];
                string childPath = Utils.Format("{0}/{1}", browsePath, child.SymbolicName);
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
            // index any references.
            if (m_references != null)
            {
                foreach (IReference reference in m_references.Keys)
                {
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);

                    if (targetId == null)
                    {
                        references.Add(new NodeStateHierarchyReference(browsePath, reference));
                        continue;
                    }

                    string targetPath = null;

                    if (!hierarchy.TryGetValue(targetId, out targetPath))
                    {
                        references.Add(new NodeStateHierarchyReference(browsePath, reference));
                        continue;
                    }

                    references.Add(new NodeStateHierarchyReference(browsePath, targetPath, reference));
                }
            }

            // recursive index the references for the children.
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                string childPath = Utils.Format("{0}/{1}", browsePath, children[ii].SymbolicName);
                children[ii].GetHierarchyReferences(context, childPath, hierarchy, references);
            }
        }

        /// <summary>
        /// Recursively updates the targets of references.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="mappingTable">A table mapping the old node ids to the new node ids.</param>
        public virtual void UpdateReferenceTargets(ISystemContext context, Dictionary<NodeId, NodeId> mappingTable)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            // check if there are references to update.
            if (m_references != null)
            {
                List<IReference> referencesToAdd = new List<IReference>();
                List<IReference> referencesToRemove = new List<IReference>();

                foreach (IReference reference in m_references.Keys)
                {
                    // check for absolute id.
                    NodeId oldId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);

                    if (oldId == null)
                    {
                        continue;
                    }

                    // look up new node id.
                    NodeId newId = null;

                    if (mappingTable.TryGetValue(oldId, out newId))
                    {
                        referencesToRemove.Add(reference);
                        referencesToAdd.Add(new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse, newId));
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

            if (NodeId.IsNull(referenceTypeId) || browser.ReferenceType == ReferenceTypeIds.References)
            {
                referenceTypeId = null;
            }

            List<BaseInstanceState> children = new List<BaseInstanceState>();

            bool childrenRequired = referenceTypeId == null;

            // check if any hierarchial reference is being requested.
            if (!childrenRequired && context.TypeTable != null)
            {
                if (context.TypeTable.IsTypeOf(browser.ReferenceType, ReferenceTypeIds.HierarchicalReferences) && browser.BrowseDirection != BrowseDirection.Inverse)
                {
                    childrenRequired = true;
                }
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

            // add any notifiers.
            if (m_notifiers != null)
            {
                for (int ii = 0; ii < m_notifiers.Count; ii++)
                {
                    Notifier entry = m_notifiers[ii];

                    if (browser.IsRequired(entry.ReferenceTypeId, entry.IsInverse))
                    {
                        browser.Add(entry.ReferenceTypeId, entry.IsInverse, m_notifiers[ii].Node);
                    }
                }
            }

            // add any arbitrary references.
            if (m_references != null)
            {
                if (referenceTypeId == null)
                {
                    foreach (IReference reference in m_references.Keys)
                    {
                        if (reference.IsInverse)
                        {
                            if (browser.BrowseDirection == BrowseDirection.Forward)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (browser.BrowseDirection == BrowseDirection.Inverse)
                            {
                                continue;
                            }
                        }

                        browser.Add(reference);
                    }
                }
                else
                {
                    IList<IReference> references = null;

                    if (browser.BrowseDirection != BrowseDirection.Inverse)
                    {
                        if (browser.IncludeSubtypes)
                        {
                            references = m_references.Find(browser.ReferenceType, false, context.TypeTable);
                        }
                        else
                        {
                            references = m_references.Find(browser.ReferenceType, false);
                        }

                        for (int ii = 0; ii < references.Count; ii++)
                        {
                            browser.Add(references[ii]);
                        }
                    }

                    if (browser.BrowseDirection != BrowseDirection.Forward)
                    {
                        if (browser.IncludeSubtypes)
                        {
                            references = m_references.Find(browser.ReferenceType, true, context.TypeTable);
                        }
                        else
                        {
                            references = m_references.Find(browser.ReferenceType, true);
                        }

                        for (int ii = 0; ii < references.Count; ii++)
                        {
                            browser.Add(references[ii]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates the node with the values from an event notification.
        /// </summary>
        public virtual void UpdateValues(
            ISystemContext context,
            SimpleAttributeOperandCollection attributes,
            EventFieldList values)
        {
            for (int ii = 0; ii < attributes.Count; ii++)
            {
                NodeState child = FindChild(context, attributes[ii].BrowsePath, 0);

                if (child == null || values.EventFields.Count >= ii)
                {
                    continue;
                }

                BaseVariableState variableInstance = child as BaseVariableState;

                if (variableInstance != null)
                {
                    variableInstance.Value = values.EventFields[ii].Value;
                    continue;
                }

                BaseObjectState objectInstance = child as BaseObjectState;

                if (objectInstance != null)
                {
                    NodeId nodeId = values.EventFields[ii].Value as NodeId;

                    if (nodeId != null)
                    {
                        objectInstance.NodeId = nodeId;
                    }
                }
            }
        }
        #endregion

        #region Read Support Functions
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
            List<object> values = new List<object>();

            if (attributeIds != null)
            {
                for (int ii = 0; ii < attributeIds.Length; ii++)
                {
                    DataValue value = new DataValue();

                    ServiceResult result = ReadAttribute(
                        context,
                        attributeIds[ii],
                        NumericRange.Empty,
                        null,
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
                return StatusCodes.BadStructureMissing;
            }

            ServiceResult result = null;
            object valueToRead = value.Value;

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
                    result = new ServiceResult(e, StatusCodes.BadUnexpectedError);
                }
            }

            // read any non-value attribute.
            else
            {
                try
                {
                    result = ReadNonValueAttribute(
                        context,
                        attributeId,
                        ref valueToRead);
                }
                catch (Exception e)
                {
                    result = new ServiceResult(e, StatusCodes.BadUnexpectedError);
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
                value.Value = null;
            }
            else
            {
                value.Value = valueToRead;
            }

            // return result.
            return result;
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute idetifier <see cref="Attributes"/>.</param>
        /// <param name="value">The returned value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected virtual ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.NodeId:
                {
                    NodeId nodeId = m_nodeId;

                    if (OnReadNodeId != null)
                    {
                        result = OnReadNodeId(context, this, ref nodeId);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = nodeId;
                    }

                    return result;
                }

                case Attributes.NodeClass:
                {
                    NodeClass nodeClass = m_nodeClass;

                    if (OnReadNodeClass != null)
                    {
                        result = OnReadNodeClass(context, this, ref nodeClass);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = nodeClass;
                    }

                    return result;
                }

                case Attributes.BrowseName:
                {
                    QualifiedName browseName = m_browseName;

                    if (OnReadBrowseName != null)
                    {
                        result = OnReadBrowseName(context, this, ref browseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = browseName;
                    }

                    return result;
                }

                case Attributes.DisplayName:
                {
                    LocalizedText displayName = m_displayName;

                    if (OnReadDisplayName != null)
                    {
                        result = OnReadDisplayName(context, this, ref displayName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = displayName;
                    }

                    return result;
                }

                case Attributes.Description:
                {
                    LocalizedText description = m_description;

                    if (OnReadDescription != null)
                    {
                        result = OnReadDescription(context, this, ref description);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = description;
                    }

                    return result;
                }

                case Attributes.WriteMask:
                {
                    AttributeWriteMask writeMask = m_writeMask;

                    if (OnReadWriteMask != null)
                    {
                        result = OnReadWriteMask(context, this, ref writeMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = (uint)writeMask;
                    }

                    return result;
                }

                case Attributes.UserWriteMask:
                {
                    AttributeWriteMask userWriteMask = m_userWriteMask;

                    if (OnReadUserWriteMask != null)
                    {
                        result = OnReadUserWriteMask(context, this, ref userWriteMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = (uint)userWriteMask;
                    }

                    return result;
                }

                case Attributes.RolePermissions:
                {
                    RolePermissionTypeCollection rolePermissions = m_rolePermissions;

                    if (OnReadRolePermissions != null)
                    {
                        result = OnReadRolePermissions(context, this, ref rolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = rolePermissions;
                    }

                    return result;
                }

                case Attributes.UserRolePermissions:
                {
                    RolePermissionTypeCollection userRolePermissions = m_userRolePermissions;

                    if (OnReadUserRolePermissions != null)
                    {
                        result = OnReadUserRolePermissions(context, this, ref userRolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userRolePermissions;
                    }

                    return result;
                }

                case Attributes.AccessRestrictions:
                {
                    AccessRestrictionType accessRestrictions = m_accessRestrictions;

                    if (OnReadAccessRestrictions != null)
                    {
                        result = OnReadAccessRestrictions(context, this, ref accessRestrictions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = (ushort)m_accessRestrictions;
                    }

                    return result;
                }
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
            ref object value,
            ref DateTime sourceTimestamp)
        {
            value = null;
            sourceTimestamp = DateTime.MinValue;
            return StatusCodes.BadAttributeIdInvalid;
        }
        #endregion

        #region Write Support Functions
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
                return StatusCodes.BadStructureMissing;
            }

            object valueToWrite = value.Value;

            if (attributeId == Attributes.Value)
            {
                // writes to server timestamp never supported.
                if (value.ServerTimestamp != DateTime.MinValue)
                {
                    return StatusCodes.BadWriteNotSupported;
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
                    return new ServiceResult(e, StatusCodes.BadUnexpectedError);
                }
            }

            // writes to status code or timestamps never supported.
            if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            // cannot use index range for non-value attributes.
            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            // call implementation.
            try
            {
                return WriteNonValueAttribute(context, attributeId, value.Value);
            }
            catch (Exception e)
            {
                return new ServiceResult(e, StatusCodes.BadUnexpectedError);
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
            object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.NodeId:
                {
                    NodeId nodeId = value as NodeId;

                    if (nodeId == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.NodeId) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteNodeId != null)
                    {
                        result = OnWriteNodeId(context, this, ref nodeId);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_nodeId = nodeId;
                    }

                    return result;
                }

                case Attributes.NodeClass:
                {
                    int? nodeClassRef = value as int?;

                    if (nodeClassRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.NodeClass) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeClass nodeClass = (NodeClass)nodeClassRef.Value;

                    if (OnWriteNodeClass != null)
                    {
                        result = OnWriteNodeClass(context, this, ref nodeClass);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_nodeClass = nodeClass;
                    }

                    return result;
                }

                case Attributes.BrowseName:
                {
                    QualifiedName browseName = value as QualifiedName;

                    if (browseName == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.BrowseName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteBrowseName != null)
                    {
                        result = OnWriteBrowseName(context, this, ref browseName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_browseName = browseName;
                    }

                    return result;
                }

                case Attributes.DisplayName:
                {
                    LocalizedText displayName = value as LocalizedText;

                    if (displayName == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.DisplayName) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteDisplayName != null)
                    {
                        result = OnWriteDisplayName(context, this, ref displayName);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_displayName = displayName;
                    }

                    return result;
                }

                case Attributes.Description:
                {
                    LocalizedText description = value as LocalizedText;

                    if (description == null && value != null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Description) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteDescription != null)
                    {
                        result = OnWriteDescription(context, this, ref description);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_description = description;
                    }

                    return result;
                }

                case Attributes.WriteMask:
                {
                    uint? writeMaskRef = value as uint?;

                    if (writeMaskRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.WriteMask) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    AttributeWriteMask writeMask = (AttributeWriteMask)writeMaskRef.Value;

                    if (OnWriteWriteMask != null)
                    {
                        result = OnWriteWriteMask(context, this, ref writeMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        WriteMask = writeMask;
                    }

                    return result;
                }

                case Attributes.UserWriteMask:
                {
                    uint? userWriteMaskRef = value as uint?;

                    if (userWriteMaskRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserWriteMask) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    AttributeWriteMask userWriteMask = (AttributeWriteMask)userWriteMaskRef.Value;

                    if (OnWriteUserWriteMask != null)
                    {
                        result = OnWriteUserWriteMask(context, this, ref userWriteMask);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_userWriteMask = userWriteMask;
                    }

                    return result;
                }

                case Attributes.RolePermissions:
                {
                    ExtensionObject[] rolePermissionsArray = value as ExtensionObject[];

                    if(rolePermissionsArray == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    RolePermissionTypeCollection rolePermissions = new RolePermissionTypeCollection();

                    foreach (ExtensionObject arrayValue in rolePermissionsArray)
                    {
                        RolePermissionType rolePermission = arrayValue.Body as RolePermissionType;

                        if (rolePermission == null)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }
                        else
                        {
                            rolePermissions.Add(rolePermission);
                        }
                    }

                    if ((WriteMask & AttributeWriteMask.RolePermissions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteRolePermissions != null)
                    {
                        result = OnWriteRolePermissions(context, this, ref rolePermissions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_rolePermissions = rolePermissions;
                    }

                    return result;
                }

                case Attributes.AccessRestrictions:
                {
                    ushort? accessRestrictionsRef = value as ushort?;

                    if (accessRestrictionsRef == null && value != null)
                    {
                        if (value.GetType() == typeof(uint))
                        {
                            accessRestrictionsRef = Convert.ToUInt16(value);
                        }
                        else
                        {
                            return StatusCodes.BadTypeMismatch;
                        }
                    }

                    if ((WriteMask & AttributeWriteMask.AccessRestrictions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    AccessRestrictionType accessRestrictions = (AccessRestrictionType)accessRestrictionsRef.Value;

                    if (OnWriteAccessRestrictions != null)
                    {
                        result = OnWriteAccessRestrictions(context, this, ref accessRestrictions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        m_accessRestrictions = accessRestrictions;
                    }

                    return result;
                }
            }

            return StatusCodes.BadAttributeIdInvalid;
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
            object value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            return StatusCodes.BadAttributeIdInvalid;
        }
        #endregion

        #region Child Access Functions
        /// <summary>
        /// Finds the child by a path constructed from the symbolic names.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="symbolicPath">The symbolic path.</param>
        /// <returns>The matching child. Null if the no child was found.</returns>
        /// <remarks>
        /// This method assumes the symbolicPath consists of symbolic names seperated by a slash ('/').
        /// Leading and trailing slashes are ignored.
        /// </remarks>
        public virtual BaseInstanceState FindChildBySymbolicName(
            ISystemContext context,
            string symbolicPath)
        {
            // check for null.
            if (String.IsNullOrEmpty(symbolicPath))
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
                symbolicName = symbolicPath.Substring(start, end - start);
            }

            // find the top level child.
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                if (child.SymbolicName == symbolicName)
                {
                    // check if additional path elements remain.
                    if (end < symbolicPath.Length - 1)
                    {
                        return child.FindChildBySymbolicName(context, symbolicPath.Substring(end + 1));
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
        public virtual BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName)
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
        public virtual BaseInstanceState FindChild(
            ISystemContext context,
            IList<QualifiedName> browsePath,
            int index)
        {
            if (index < 0 || index >= Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(index));

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
        /// <returns>The child if avialble. Null otherwise.</returns>
        public virtual BaseInstanceState CreateChild(
            ISystemContext context,
            QualifiedName browseName)
        {
            if (QualifiedName.IsNull(browseName))
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
        public virtual void ReplaceChild(ISystemContext context, BaseInstanceState child)
        {
            if (child == null || QualifiedName.IsNull(child.BrowseName))
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
            if (!Object.ReferenceEquals(child.Parent, this))
            {
                child.Parent = this;

                if (NodeId.IsNull(child.ReferenceTypeId))
                {
                    child.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                }
            }

            if (m_children == null)
            {
                m_children = new List<BaseInstanceState>();
            }

            m_children.Add(child);
            m_changeMasks |= NodeStateChangeMasks.Children;
        }

        /// <summary>
        /// Creates a property and adds it to the node.
        /// </summary>
        public PropertyState AddProperty<T>(string propertyName, NodeId dataTypeId, int valueRank)
        {
            PropertyState property = new PropertyState<T>(this);

            property.ReferenceTypeId = ReferenceTypes.HasProperty;
            property.ModellingRuleId = null;
            property.TypeDefinitionId = VariableTypeIds.PropertyType;
            property.SymbolicName = propertyName;
            property.NodeId = null;
            property.BrowseName = propertyName;
            property.DisplayName = propertyName;
            property.Description = null;
            property.WriteMask = 0;
            property.UserWriteMask = 0;
            property.Value = default(T);
            property.DataType = dataTypeId;
            property.ValueRank = valueRank;
            property.ArrayDimensions = null;
            property.AccessLevel = AccessLevels.CurrentRead;
            property.UserAccessLevel = AccessLevels.CurrentRead;
            property.MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            property.Historizing = false;

            AddChild(property);

            return property;
        }

        /// <summary>
        /// Adds a child from the node. 
        /// </summary>
        public void RemoveChild(BaseInstanceState child)
        {
            if (m_children != null)
            {
                for (int ii = 0; ii < m_children.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_children[ii], child))
                    {
                        child.Parent = null;
                        m_children.RemoveAt(ii);
                        m_changeMasks |= NodeStateChangeMasks.Children;
                        return;
                    }
                }
            }
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

            BaseInstanceState child = CreateChild(context, browseName) as BaseInstanceState;

            if (child == null)
            {
                return false;
            }

            BaseVariableState variable = child as BaseVariableState;
            BaseVariableState sourceVariable = source as BaseVariableState;

            if (variable != null && sourceVariable != null)
            {
                if (copy)
                {
                    variable.Value = Utils.Clone(sourceVariable.Value);
                }
                else
                {
                    variable.Value = sourceVariable.Value;
                }
            }

            List<BaseInstanceState> children = new List<BaseInstanceState>();
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
            QualifiedName browseName,
            object value,
            bool copy)
        {
            BaseVariableState child = CreateChild(context, browseName) as BaseVariableState;

            if (child == null)
            {
                return false;
            }

            if (copy)
            {
                child.Value = Utils.Clone(value);
            }
            else
            {
                child.Value = value;
            }

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
                return ReadAttribute(context, attributeId, NumericRange.Empty, null, dataValue);
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

            // recursively update children.
            if (m_children != null)
            {
                for (int ii = 0; ii < m_children.Count; ii++)
                {
                    if (componentPath[index] != m_children[ii].BrowseName)
                    {
                        continue;
                    }

                    return m_children[ii].WriteChildAttribute(
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
        public bool ReferenceExists(
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId)
        {
            if (m_references == null || referenceTypeId == null || targetId == null)
            {
                return false;
            }

            return m_references.ContainsKey(new NodeStateReference(referenceTypeId, isInverse, targetId));
        }

        /// <summary>
        /// Adds a reference.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the refernce is an inverse reference.</param>
        /// <param name="targetId">The target of the reference.</param>
        public void AddReference(
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId)
        {
            if (NodeId.IsNull(referenceTypeId)) throw new ArgumentNullException(nameof(referenceTypeId));
            if (NodeId.IsNull(targetId)) throw new ArgumentNullException(nameof(targetId));

            if (m_references == null)
            {
                m_references = new IReferenceDictionary<object>();
            }

            m_references.Add(new NodeStateReference(referenceTypeId, isInverse, targetId), null);
            m_changeMasks |= NodeStateChangeMasks.References;

            OnReferenceAdded?.Invoke(this, referenceTypeId, isInverse, targetId);
        }

        /// <summary>
        /// Removes a reference.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the refernce is an inverse reference.</param>
        /// <param name="targetId">The target of the reference.</param>
        public bool RemoveReference(
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId)
        {
            if (NodeId.IsNull(referenceTypeId)) throw new ArgumentNullException(nameof(referenceTypeId));
            if (NodeId.IsNull(targetId)) throw new ArgumentNullException(nameof(targetId));

            if (m_references == null)
            {
                return false;
            }

            if (m_references.Remove(new NodeStateReference(referenceTypeId, isInverse, targetId)))
            {
                m_changeMasks |= NodeStateChangeMasks.References;
                OnReferenceRemoved?.Invoke(this, referenceTypeId, isInverse, targetId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds a list of references (ignores duplicates).
        /// </summary>
        /// <param name="references">The list of references to add.</param>
        public void AddReferences(IList<IReference> references)
        {
            if (references == null) throw new ArgumentNullException(nameof(references));

            if (m_references == null)
            {
                m_references = new IReferenceDictionary<object>();
            }

            for (int ii = 0; ii < references.Count; ii++)
            {
                if (!m_references.ContainsKey(references[ii]))
                {
                    m_references.Add(references[ii], null);
                    OnReferenceAdded?.Invoke(this, references[ii].ReferenceTypeId, references[ii].IsInverse, references[ii].TargetId);
                }
            }

            m_changeMasks |= NodeStateChangeMasks.References;
        }

        /// <summary>
        /// Removes all references of the specified type.
        /// </summary>
        /// <param name="referenceTypeId">Type of the reference.</param>
        /// <param name="isInverse">If set to <c>true</c> the reference is an inverse reference.</param>
        public bool RemoveReferences(
            NodeId referenceTypeId,
            bool isInverse)
        {
            if (NodeId.IsNull(referenceTypeId)) throw new ArgumentNullException(nameof(referenceTypeId));

            if (m_references == null)
            {
                return false;
            }

            var refsToRemove = m_references
                .Select(r => r.Key)
                .Where(r => r.ReferenceTypeId == referenceTypeId && r.IsInverse == isInverse)
                .ToList();
            
            refsToRemove.ForEach(r => RemoveReference(r.ReferenceTypeId, r.IsInverse, r.TargetId));

            return refsToRemove.Count != 0;
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        /// <remarks>
        /// This method returns the children that are in memory and does not attempt to
        /// access an underlying system. The PopulateBrowser method is used to discover those references. 
        /// </remarks>
        public virtual void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_children != null)
            {
                for (int ii = 0; ii < m_children.Count; ii++)
                {
                    children.Add(m_children[ii]);
                }
            }
        }

        /// <summary>
        /// Populates a list with the non-child related references that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="references">The list of references to populate.</param>
        /// <remarks>
        /// This method only returns references that are not implied by the parent-child 
        /// relation or references which are intrinsic to the NodeState classes (e.g. HasTypeDefinition)
        /// 
        /// This method also only returns the reference that are in memory and does not attempt to
        /// access an underlying system. The PopulateBrowser method is used to discover those references.        
        /// </remarks>
        public virtual void GetReferences(
            ISystemContext context,
            IList<IReference> references)
        {
            if (m_references != null)
            {
                foreach (IReference reference in m_references.Keys)
                {
                    references.Add(reference);
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
            if (m_references != null)
            {
                foreach (IReference reference in m_references.Keys)
                {
                    if (isInverse == reference.IsInverse && reference.ReferenceTypeId == referenceTypeId)
                    {
                        references.Add(reference);
                    }
                }
            }
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="browseName">The browse name of the children to add.</param>
        /// <param name="createOrReplace">if set to <c>true</c> and the child could exist then the child is created.</param>
        /// <param name="replacement">The replacement to use if createOrReplace is true.</param>
        /// <returns>The child.</returns>
        protected virtual BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

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

            if (createOrReplace)
            {
                if (replacement != null)
                {
                    AddChild(replacement);
                }
            }

            return null;
        }
        #endregion

        #region Notifier Class
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
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private object m_handle;
        private string m_symbolicName;
        private NodeId m_nodeId;
        private NodeClass m_nodeClass;
        private QualifiedName m_browseName;
        private LocalizedText m_displayName;
        private LocalizedText m_description;
        private AttributeWriteMask m_writeMask;
        private AttributeWriteMask m_userWriteMask;
        private RolePermissionTypeCollection m_rolePermissions;
        private RolePermissionTypeCollection m_userRolePermissions;
        private AccessRestrictionType m_accessRestrictions;
        protected List<BaseInstanceState> m_children;
        private IReferenceDictionary<object> m_references;
        protected NodeStateChangeMasks m_changeMasks;
        private int m_areEventsMonitored;
        private bool m_initialized;
        private List<Notifier> m_notifiers;
        private System.Xml.XmlElement[] m_extensions;
        #endregion
    }

    [Flags]
    /// <summary>
    /// Indicates what has changed in a node.
    /// </summary>
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
    public delegate bool NodeStateValidateHandler(
        ISystemContext context,
        NodeState node);

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
        ref object value);

    /// <summary>
    /// Used to receive notifications when the value attribute is read or written.
    /// </summary>
    public delegate ServiceResult NodeValueEventHandler(
        ISystemContext context,
        NodeState node,
        NumericRange indexRange,
        QualifiedName dataEncoding,
        ref object value,
        ref StatusCode statusCode,
        ref DateTime timestamp);

    /// <summary>
    /// Stores a reference from a node in the instance hierarchy.
    /// </summary>
    public class NodeStateHierarchyReference
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeStateHierarchyReference"/> class.
        /// </summary>
        /// <param name="sourcePath">The path to the source node.</param>
        /// <param name="reference">The reference.</param>
        public NodeStateHierarchyReference(string sourcePath, IReference reference)
        {
            m_sourcePath = sourcePath;
            m_referenceTypeId = reference.ReferenceTypeId;
            m_isInverse = reference.IsInverse;
            m_targetPath = null;
            m_targetId = reference.TargetId;
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
            m_sourcePath = sourcePath;
            m_referenceTypeId = reference.ReferenceTypeId;
            m_isInverse = reference.IsInverse;
            m_targetPath = targetPath;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the path to the source node.
        /// </summary>
        /// <value>The source path.</value>
        public string SourcePath
        {
            get { return m_sourcePath; }
        }

        /// <summary>
        /// Gets the identifier for the reference type.
        /// </summary>
        /// <value>The reference type id.</value>
        public NodeId ReferenceTypeId
        {
            get { return m_referenceTypeId; }
        }

        /// <summary>
        /// Gets a value indicating whether the reference is an inverse reference.
        /// </summary>
        /// <value>
        /// <c>true</c> if this is an inverse reference; otherwise, <c>false</c>.
        /// </value>
        public bool IsInverse
        {
            get { return m_isInverse; }
        }

        /// <summary>
        /// Gets the identifier for the target node.
        /// </summary>
        /// <value>The target id.</value>
        /// <remarks>Only one of TargetId or TargetPath is specified.</remarks>
        public ExpandedNodeId TargetId
        {
            get { return m_targetId; }
        }

        /// <summary>
        /// Gets the path to the target node.
        /// </summary>
        /// <value>The target path.</value>
        /// <remarks>Only one of TargetId or TargetPath is specified.</remarks>
        public string TargetPath
        {
            get { return m_targetPath; }
        }
        #endregion

        #region Private Fields
        private string m_sourcePath;
        private NodeId m_referenceTypeId;
        private bool m_isInverse;
        private ExpandedNodeId m_targetId;
        private string m_targetPath;
        #endregion
    }

    /// <summary>
    /// A delegate which creates a new node.
    /// </summary>
    /// <param name="parent">The parent of the node.</param>
    /// <returns>The new node.</returns>
    public delegate NodeState NodeStateConstructDelegate(NodeState parent);
}
