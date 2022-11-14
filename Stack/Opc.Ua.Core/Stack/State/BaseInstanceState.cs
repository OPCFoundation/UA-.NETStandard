/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using System.Text;

namespace Opc.Ua
{
    /// <summary> 
    /// The base class for all instance nodes.
    /// </summary>
    public class BaseInstanceState : NodeState, IFilterTarget
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        protected BaseInstanceState(NodeClass nodeClass, NodeState parent) : base(nodeClass)
        {
            m_parent = parent;
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            BaseInstanceState instance = source as BaseInstanceState;

            if (instance != null)
            {
                m_referenceTypeId = instance.m_referenceTypeId;
                m_typeDefinitionId = instance.m_typeDefinitionId;
                m_modellingRuleId = instance.m_modellingRuleId;
                m_numericId = instance.m_numericId;
            }

            base.Initialize(context, source);
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <returns></returns>
        protected virtual NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return null;
        }
        #endregion

        #region ICloneable Members
        /// <inheritdoc/>
        public override object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            BaseInstanceState clone = new BaseInstanceState(this.NodeClass, this.Parent);
            return CloneChildren(clone);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The parent node.
        /// </summary>
        public NodeState Parent
        {
            get { return m_parent; }
            internal set { m_parent = value; }
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        /// <returns>The type definition id.</returns>
        public virtual NodeId GetDefaultTypeDefinitionId(ISystemContext context)
        {
            return GetDefaultTypeDefinitionId(context.NamespaceUris);
        }

        /// <summary>
        /// Gets a display path for the node.
        /// </summary>
        public string GetDisplayPath()
        {
            return GetDisplayPath(0, '.');
        }

        /// <summary>
        /// Gets a display text for the node.
        /// </summary>
        public string GetDisplayText()
        {
            return GetNonNullText(this);
        }

        /// <summary>
        /// Gets a display path for the node.
        /// </summary>
        public string GetDisplayPath(int maxLength, char seperator)
        {
            string name = GetNonNullText(this);

            if (m_parent == null)
            {
                return name;
            }

            StringBuilder buffer = new StringBuilder();

            if (maxLength > 2)
            {
                NodeState parent = m_parent;
                List<string> names = new List<string>();

                while (parent != null)
                {
                    BaseInstanceState instance = parent as BaseInstanceState;

                    if (instance == null)
                    {
                        break;
                    }

                    parent = instance.Parent;

                    string parentName = GetNonNullText(parent);
                    names.Add(parentName);

                    if (names.Count == maxLength - 2)
                    {
                        break;
                    }
                }

                for (int ii = names.Count - 1; ii >= 0; ii--)
                {
                    buffer.Append(names[ii]);
                    buffer.Append(seperator);
                }
            }

            buffer.Append(GetNonNullText(m_parent));
            buffer.Append(seperator);
            buffer.Append(name);

            return buffer.ToString();
        }

        /// <summary>
        /// Returns non-null text for the node.
        /// </summary>
        private string GetNonNullText(NodeState node)
        {
            if (node == null)
            {
                return "(null)";
            }

            if (node.DisplayName == null)
            {
                if (node.BrowseName != null)
                {
                    return node.BrowseName.Name;
                }
                else
                {
                    return node.NodeClass.ToString();
                }
            }

            return node.DisplayName.Text;
        }

        /// <summary>
        /// A numeric identifier for the instance that is unique within the parent.
        /// </summary>
        public uint NumericId
        {
            get { return m_numericId; }
            set { m_numericId = value; }
        }

        /// <summary>
        /// The type of reference from the parent node to the instance.
        /// </summary>
        public NodeId ReferenceTypeId
        {
            get
            {
                return m_referenceTypeId;
            }

            set
            {
                if (!Object.ReferenceEquals(m_referenceTypeId, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.References;
                }

                m_referenceTypeId = value;
            }
        }

        /// <summary>
        /// The identifier for the type definition node.
        /// </summary>
        public NodeId TypeDefinitionId
        {
            get
            {
                return m_typeDefinitionId;
            }

            set
            {
                if (!Object.ReferenceEquals(m_typeDefinitionId, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.References;
                }

                m_typeDefinitionId = value;
            }
        }

        /// <summary>
        /// The modelling rule assigned to the instance.
        /// </summary>
        public NodeId ModellingRuleId
        {
            get
            {
                return m_modellingRuleId;
            }

            set
            {
                if (!Object.ReferenceEquals(m_modellingRuleId, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.References;
                }

                m_modellingRuleId = value;
            }
        }

        /// <summary>
        /// Sets the flag which indicates whether event are being monitored for the instance and its children.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="e">The event to report.</param>
        public override void ReportEvent(ISystemContext context, IFilterTarget e)
        {
            base.ReportEvent(context, e);

            // recusively notify the parent.
            if (m_parent != null)
            {
                m_parent.ReportEvent(context, e);
            }
        }

        /// <summary>
        /// Initializes the instance from an event notification.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="fields">The fields selected for the event notification.</param>
        /// <param name="e">The event notification.</param>
        /// <remarks>
        /// This method creates components based on the browse paths in the event field and sets
        /// the NodeId or Value based on values in the event notification.
        /// </remarks>  
        public void Update(
            ISystemContext context,
            SimpleAttributeOperandCollection fields,
            EventFieldList e)
        {
            for (int ii = 0; ii < fields.Count; ii++)
            {
                SimpleAttributeOperand field = fields[ii];
                object value = e.EventFields[ii].Value;

                // check if value provided.
                if (value == null)
                {
                    continue;
                }

                // extract the NodeId for the event.
                if (field.BrowsePath.Count == 0)
                {
                    if (field.AttributeId == Attributes.NodeId)
                    {
                        this.NodeId = value as NodeId;
                        continue;
                    }
                }

                // extract the type definition for the event.
                if (field.BrowsePath.Count == 1)
                {
                    if (field.AttributeId == Attributes.Value)
                    {
                        if (field.BrowsePath[0] == BrowseNames.EventType)
                        {
                            m_typeDefinitionId = value as NodeId;
                            continue;
                        }
                    }
                }

                // save value for child node.
                NodeState parent = this;

                for (int jj = 0; jj < field.BrowsePath.Count; jj++)
                {
                    // find a predefined child identified by the browse name.
                    BaseInstanceState child = parent.CreateChild(context, field.BrowsePath[jj]);

                    // create a placeholder for unknown children.
                    if (child == null)
                    {
                        if (field.AttributeId == Attributes.Value)
                        {
                            child = new BaseDataVariableState(parent);
                        }
                        else
                        {
                            child = new BaseObjectState(parent);
                        }

                        parent.AddChild(child);
                    }

                    // ensure the browse name is set.
                    if (QualifiedName.IsNull(child.BrowseName))
                    {
                        child.BrowseName = field.BrowsePath[jj];
                    }

                    // ensure the display name is set.
                    if (LocalizedText.IsNullOrEmpty(child.DisplayName))
                    {
                        child.DisplayName = child.BrowseName.Name;
                    }

                    // process next element in path.
                    if (jj < field.BrowsePath.Count - 1)
                    {
                        parent = child;
                        continue;
                    }

                    // save the variable value.
                    if (field.AttributeId == Attributes.Value)
                    {
                        BaseVariableState variable = child as BaseVariableState;

                        if (variable != null && field.AttributeId == Attributes.Value)
                        {
                            try
                            {
                                variable.WrappedValue = e.EventFields[ii];
                            }
                            catch (Exception)
                            {
                                variable.Value = null;
                            }
                        }

                        break;
                    }

                    // save the node id.
                    child.NodeId = value as NodeId;
                }
            }
        }

        /// <summary>
        /// Sets the minimum sampling interval for the node an all of its child variables..
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="minimumSamplingInterval">The minimum sampling interval.</param>
        public void SetMinimumSamplingInterval(ISystemContext context, double minimumSamplingInterval)
        {
            BaseVariableState variable = this as BaseVariableState;

            if (variable != null)
            {
                variable.MinimumSamplingInterval = minimumSamplingInterval;
            }

            List<BaseInstanceState> children = new List<BaseInstanceState>();
            GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                variable = children[ii] as BaseVariableState;

                if (variable != null)
                {
                    variable.MinimumSamplingInterval = minimumSamplingInterval;
                }

                children[ii].SetMinimumSamplingInterval(context, minimumSamplingInterval);
            }
        }
        #endregion

        #region IFilterTarget Members
        /// <summary cref="IFilterTarget.IsTypeOf" />
        public virtual bool IsTypeOf(FilterContext context, NodeId typeDefinitionId)
        {
            if (!NodeId.IsNull(typeDefinitionId))
            {
                if (!context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary cref="IFilterTarget.GetAttributeValue" />
        public virtual object GetAttributeValue(
            FilterContext context,
            NodeId typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            // check the type definition.
            if (!NodeId.IsNull(typeDefinitionId) && typeDefinitionId != ObjectTypes.BaseEventType)
            {
                if (!context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId))
                {
                    return null;
                }
            }

            // read the child attribute.
            DataValue dataValue = new DataValue();

            ServiceResult result = ReadChildAttribute(
                null,
                relativePath,
                0,
                attributeId,
                dataValue);

            if (ServiceResult.IsBad(result))
            {
                return null;
            }

            // apply any index range.
            object value = dataValue.Value;

            if (value != null)
            {
                result = indexRange.ApplyRange(ref value);

                if (ServiceResult.IsBad(result))
                {
                    return null;
                }
            }

            // return the result.
            return value;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node to update with the values from the instance.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (this.Parent != null)
            {
                NodeId referenceTypeId = this.ReferenceTypeId;

                if (NodeId.IsNull(referenceTypeId))
                {
                    referenceTypeId = ReferenceTypeIds.HasComponent;
                }

                node.ReferenceTable.Add(referenceTypeId, true, this.Parent.NodeId);
            }

            if (!NodeId.IsNull(m_typeDefinitionId) && IsObjectOrVariable)
            {
                node.ReferenceTable.Add(ReferenceTypeIds.HasTypeDefinition, false, this.TypeDefinitionId);
            }

            if (!NodeId.IsNull(this.ModellingRuleId))
            {
                node.ReferenceTable.Add(ReferenceTypeIds.HasModellingRule, false, this.ModellingRuleId);
            }
        }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (!NodeId.IsNull(m_referenceTypeId))
            {
                encoder.WriteNodeId("ReferenceTypeId", m_referenceTypeId);
            }

            if (!NodeId.IsNull(m_typeDefinitionId))
            {
                encoder.WriteNodeId("TypeDefinitionId", m_typeDefinitionId);
            }

            if (!NodeId.IsNull(m_modellingRuleId))
            {
                encoder.WriteNodeId("ModellingRuleId", m_modellingRuleId);
            }

            if (m_numericId != 0)
            {
                encoder.WriteUInt32("NumericId", m_numericId);
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public override AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = base.GetAttributesToSave(context);

            if (!NodeId.IsNull(m_referenceTypeId))
            {
                attributesToSave |= AttributesToSave.ReferenceTypeId;
            }

            if (!NodeId.IsNull(m_typeDefinitionId))
            {
                attributesToSave |= AttributesToSave.TypeDefinitionId;
            }

            if (!NodeId.IsNull(m_modellingRuleId))
            {
                attributesToSave |= AttributesToSave.ModellingRuleId;
            }

            if (m_numericId != 0)
            {
                attributesToSave |= AttributesToSave.NumericId;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context user.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.ReferenceTypeId) != 0)
            {
                encoder.WriteNodeId(null, m_referenceTypeId);
            }

            if ((attributesToSave & AttributesToSave.TypeDefinitionId) != 0)
            {
                encoder.WriteNodeId(null, m_typeDefinitionId);
            }

            if ((attributesToSave & AttributesToSave.ModellingRuleId) != 0)
            {
                encoder.WriteNodeId(null, m_modellingRuleId);
            }

            if ((attributesToSave & AttributesToSave.NumericId) != 0)
            {
                encoder.WriteUInt32(null, m_numericId);
            }
        }

        /// <summary>
        /// Updates the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.ReferenceTypeId) != 0)
            {
                m_referenceTypeId = decoder.ReadNodeId(null);
            }

            if ((attibutesToLoad & AttributesToSave.TypeDefinitionId) != 0)
            {
                m_typeDefinitionId = decoder.ReadNodeId(null);
            }

            if ((attibutesToLoad & AttributesToSave.ModellingRuleId) != 0)
            {
                m_modellingRuleId = decoder.ReadNodeId(null);
            }

            if ((attibutesToLoad & AttributesToSave.NumericId) != 0)
            {
                m_numericId = decoder.ReadUInt32(null);
            }
        }

        /// <summary>
        /// Updates the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        public override void Update(ISystemContext context, XmlDecoder decoder)
        {
            base.Update(context, decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("ReferenceTypeId"))
            {
                ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            }

            if (decoder.Peek("TypeDefinitionId"))
            {
                TypeDefinitionId = decoder.ReadNodeId("TypeDefinitionId");
            }

            if (decoder.Peek("ModellingRuleId"))
            {
                ModellingRuleId = decoder.ReadNodeId("ModellingRuleId");
            }

            if (decoder.Peek("NumericId"))
            {
                NumericId = decoder.ReadUInt32("NumericId");
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="browser">The browser to populate.</param>
        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            if (!NodeId.IsNull(m_typeDefinitionId) && IsObjectOrVariable)
            {
                if (browser.IsRequired(ReferenceTypeIds.HasTypeDefinition, false))
                {
                    browser.Add(ReferenceTypeIds.HasTypeDefinition, false, m_typeDefinitionId);
                }
            }

            if (!NodeId.IsNull(m_modellingRuleId))
            {
                if (browser.IsRequired(ReferenceTypeIds.HasModellingRule, false))
                {
                    browser.Add(ReferenceTypeIds.HasModellingRule, false, m_modellingRuleId);
                }
            }

            if (m_parent != null)
            {
                if (!NodeId.IsNull(m_referenceTypeId))
                {
                    if (browser.IsRequired(m_referenceTypeId, true))
                    {
                        browser.Add(m_referenceTypeId, true, m_parent);
                    }
                }
            }
        }
        #endregion

        private bool IsObjectOrVariable => ((this.NodeClass & (NodeClass.Variable | NodeClass.Object)) != 0);

        #region Private Fields
        private NodeState m_parent;
        private NodeId m_referenceTypeId;
        private NodeId m_typeDefinitionId;
        private NodeId m_modellingRuleId;
        private uint m_numericId;
        #endregion
    }
}
