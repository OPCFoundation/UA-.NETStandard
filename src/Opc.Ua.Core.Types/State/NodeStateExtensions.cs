/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

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
            ArrayOf<SimpleAttributeOperand> attributes,
            EventFieldList values)
        {
            for (int ii = 0; ii < attributes.Count; ii++)
            {
                NodeState? child = state.FindChild(context, attributes[ii].BrowsePath, 0);

                if (child == null || values.EventFields.Count >= ii)
                {
                    continue;
                }

                if (child is BaseVariableState variableInstance)
                {
                    variableInstance.Value = values.EventFields[ii];
                    continue;
                }

                if (child is BaseObjectState objectInstance &&
                    values.EventFields[ii].TryGetValue(out NodeId nodeId) &&
                    !nodeId.IsNull)
                {
                    objectInstance.NodeId = nodeId;
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
            ArrayOf<SimpleAttributeOperand> fields,
            EventFieldList e)
        {
            for (int ii = 0; ii < fields.Count; ii++)
            {
                SimpleAttributeOperand field = fields[ii];

                // check if value provided.
                if (e.EventFields[ii].IsNull)
                {
                    continue;
                }

                // extract the NodeId for the event.
                if (field.BrowsePath.Count == 0 &&
                    field.AttributeId == Attributes.NodeId &&
                    e.EventFields[ii].TryGetValue(out NodeId nodeId))
                {
                    state.NodeId = nodeId;
                    continue;
                }

                // extract the type definition for the event.
                if (field.BrowsePath.Count == 1 &&
                    field.AttributeId == Attributes.Value &&
                    field.BrowsePath[0] == BrowseNames.EventType &&
                    e.EventFields[ii].TryGetValue(out NodeId typeDefinitionId))
                {
                    state.TypeDefinitionId = typeDefinitionId;
                    continue;
                }

                // save value for child node.
                BaseInstanceState parent = state;

                for (int jj = 0; jj < field.BrowsePath.Count; jj++)
                {
                    // find a predefined child identified by the browse name.
                    BaseInstanceState? child = parent.CreateChild(context, field.BrowsePath[jj]);

                    // create a placeholder for unknown children.
                    if (child == null)
                    {
                        BaseInstanceState? newChild = field.AttributeId == Attributes.Value
                            ? new BaseDataVariableState(parent)
                            : new BaseObjectState(parent);

                        parent.AddChild(newChild);
                        child = newChild;
                    }

                    // ensure the browse name is set.
                    if (child.BrowseName.IsNull)
                    {
                        child.BrowseName = field.BrowsePath[jj];
                    }

                    // ensure the display name is set.
                    if (child.DisplayName.IsNullOrEmpty)
                    {
                        child.DisplayName = new LocalizedText(child.BrowseName.Name);
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
                                variable.Value = e.EventFields[ii];
                            }
                            catch (Exception)
                            {
                                variable.Value = default;
                            }
                        }

                        break;
                    }

                    // save the node id.
                    child.NodeId = e.EventFields[ii].GetNodeId();
                }
            }
        }
    }
}
