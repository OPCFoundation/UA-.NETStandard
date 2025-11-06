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
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        protected BaseInstanceState(NodeClass nodeClass, NodeState parent)
            : base(nodeClass)
        {
            Parent = parent;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is BaseInstanceState instance)
            {
                m_referenceTypeId = instance.m_referenceTypeId;
                m_typeDefinitionId = instance.m_typeDefinitionId;
                m_modellingRuleId = instance.m_modellingRuleId;
                NumericId = instance.NumericId;
            }

            base.Initialize(context, source);
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        protected virtual NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return null;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            var clone = new BaseInstanceState(NodeClass, Parent);
            return CloneChildren(clone);
        }

        /// <summary>
        /// The parent node.
        /// </summary>
        public NodeState Parent { get; internal set; }

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

            NodeState stateParent = Parent;

            if (stateParent == null)
            {
                return name;
            }

            var buffer = new StringBuilder();

            if (maxLength > 2)
            {
                NodeState parent = stateParent;
                var names = new List<string>();

                while (parent != null)
                {
                    if (parent is not BaseInstanceState instance)
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
                    buffer.Append(names[ii])
                        .Append(seperator);
                }
            }

            buffer.Append(GetNonNullText(stateParent))
                .Append(seperator)
                .Append(name);

            return buffer.ToString();
        }

        /// <summary>
        /// Returns non-null text for the node.
        /// </summary>
        private static string GetNonNullText(NodeState node)
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

                return node.NodeClass.ToString();
            }

            return node.DisplayName.Text;
        }

        /// <summary>
        /// A numeric identifier for the instance that is unique within the parent.
        /// </summary>
        public uint NumericId { get; set; }

        /// <summary>
        /// The type of reference from the parent node to the instance.
        /// </summary>
        public NodeId ReferenceTypeId
        {
            get => m_referenceTypeId;
            set
            {
                if (!ReferenceEquals(m_referenceTypeId, value))
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
            get => m_typeDefinitionId;
            set
            {
                if (!ReferenceEquals(m_typeDefinitionId, value))
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
            get => m_modellingRuleId;
            set
            {
                if (!ReferenceEquals(m_modellingRuleId, value))
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

            // recursively notify the parent.
            Parent?.ReportEvent(context, e);
        }

        /// <summary>
        /// Sets the minimum sampling interval for the node an all of its child variables..
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="minimumSamplingInterval">The minimum sampling interval.</param>
        public void SetMinimumSamplingInterval(
            ISystemContext context,
            double minimumSamplingInterval)
        {
            if (this is BaseVariableState variable)
            {
                variable.MinimumSamplingInterval = minimumSamplingInterval;
            }

            var children = new List<BaseInstanceState>();
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

        /// <inheritdoc/>
        public virtual bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
        {
            return NodeId.IsNull(typeDefinitionId) ||
                context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId);
        }

        /// <inheritdoc/>
        public virtual object GetAttributeValue(
            IFilterContext context,
            NodeId typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint attributeId,
            NumericRange indexRange)
        {
            // check the type definition.
            if (!NodeId.IsNull(typeDefinitionId) &&
                typeDefinitionId != ObjectTypeIds.BaseEventType &&
                !context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId))
            {
                return null;
            }

            // read the child attribute.
            var dataValue = new DataValue();

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

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node to update with the values from the instance.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (Parent != null)
            {
                NodeId referenceTypeId = ReferenceTypeId;

                if (NodeId.IsNull(referenceTypeId))
                {
                    referenceTypeId = ReferenceTypeIds.HasComponent;
                }

                node.ReferenceTable.Add(referenceTypeId, true, Parent.NodeId);
            }

            if (!NodeId.IsNull(m_typeDefinitionId) && IsObjectOrVariable)
            {
                node.ReferenceTable
                    .Add(ReferenceTypeIds.HasTypeDefinition, false, TypeDefinitionId);
            }

            if (!NodeId.IsNull(ModellingRuleId))
            {
                node.ReferenceTable.Add(ReferenceTypeIds.HasModellingRule, false, ModellingRuleId);
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

            if (NumericId != 0)
            {
                encoder.WriteUInt32("NumericId", NumericId);
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

            if (NumericId != 0)
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
        public override void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
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
                encoder.WriteUInt32(null, NumericId);
            }
        }

        /// <summary>
        /// Updates the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="attributesToLoad">The attributes to load.</param>
        public override void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
        {
            base.Update(context, decoder, attributesToLoad);

            if ((attributesToLoad & AttributesToSave.ReferenceTypeId) != 0)
            {
                m_referenceTypeId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.TypeDefinitionId) != 0)
            {
                m_typeDefinitionId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.ModellingRuleId) != 0)
            {
                m_modellingRuleId = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.NumericId) != 0)
            {
                NumericId = decoder.ReadUInt32(null);
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

            NodeId typeDefinitionId = m_typeDefinitionId;

            if (!NodeId.IsNull(typeDefinitionId) &&
                IsObjectOrVariable &&
                browser.IsRequired(ReferenceTypeIds.HasTypeDefinition, false))
            {
                browser.Add(ReferenceTypeIds.HasTypeDefinition, false, typeDefinitionId);
            }

            NodeId modellingRuleId = m_modellingRuleId;

            if (!NodeId.IsNull(modellingRuleId) &&
                browser.IsRequired(ReferenceTypeIds.HasModellingRule, false))
            {
                browser.Add(ReferenceTypeIds.HasModellingRule, false, modellingRuleId);
            }

            NodeState parent = Parent;

            if (parent != null)
            {
                NodeId referenceTypeId = m_referenceTypeId;

                if (!NodeId.IsNull(referenceTypeId) && browser.IsRequired(referenceTypeId, true))
                {
                    browser.Add(referenceTypeId, true, parent);
                }
            }
        }

        private bool IsObjectOrVariable
            => ((int)NodeClass & ((int)NodeClass.Variable | (int)NodeClass.Object)) != 0;

        private NodeId m_referenceTypeId;
        private NodeId m_typeDefinitionId;
        private NodeId m_modellingRuleId;
    }
}
