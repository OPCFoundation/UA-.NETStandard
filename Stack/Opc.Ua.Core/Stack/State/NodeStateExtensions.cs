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

namespace Opc.Ua
{
    /// <summary>
    /// Node state extensions
    /// </summary>
    public static class NodeStateExtensions
    {
        /// <summary>
        /// Updates the node with the values from an event notification.
        /// </summary>
        public static void UpdateValues(
            this NodeState state,
            ISystemContext context,
            SimpleAttributeOperandCollection attributes,
            EventFieldList values)
        {
            for (int ii = 0; ii < attributes.Count; ii++)
            {
                NodeState child = state.FindChild(context, attributes[ii].BrowsePath, 0);

                if (child == null || values.EventFields.Count >= ii)
                {
                    continue;
                }

                if (child is BaseVariableState variableInstance)
                {
                    variableInstance.Value = values.EventFields[ii].Value;
                    continue;
                }

                if (child is BaseObjectState objectInstance)
                {
                    var nodeId = values.EventFields[ii].Value as NodeId;

                    if (nodeId != null)
                    {
                        objectInstance.NodeId = nodeId;
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the instance from an event notification.
        /// </summary>
        /// <param name="state">The instance state to update</param>
        /// <param name="context">The context.</param>
        /// <param name="fields">The fields selected for the event notification.</param>
        /// <param name="e">The event notification.</param>
        /// <remarks>
        /// This method creates components based on the browse paths in the event field and sets
        /// the NodeId or Value based on values in the event notification.
        /// </remarks>
        public static void Update(
            this BaseInstanceState state,
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
                if (field.BrowsePath.Count == 0 && field.AttributeId == Attributes.NodeId)
                {
                    state.NodeId = value as NodeId;
                    continue;
                }

                // extract the type definition for the event.
                if (field.BrowsePath.Count == 1 &&
                    field.AttributeId == Attributes.Value &&
                    field.BrowsePath[0] == BrowseNames.EventType)
                {
                    state.TypeDefinitionId = value as NodeId;
                    continue;
                }

                // save value for child node.
                BaseInstanceState parent = state;

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
                        if (child is BaseVariableState variable &&
                            field.AttributeId == Attributes.Value)
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
    }
}
