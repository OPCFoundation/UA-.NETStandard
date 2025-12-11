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
