/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.DataAccessServer
{    
    /// <summary>
    /// A object which maps a block to a UA object.
    /// </summary>
    public partial class BlockState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="BlockState"/> class.
        /// </summary>
        /// <param name="nodeManager">The context.</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="block">The block.</param>
        public BlockState(
            DataAccessServerNodeManager nodeManager, 
            NodeId nodeId, 
            UnderlyingSystemBlock block) : base(null)
        {
            m_blockId = block.Id;
            m_nodeManager = nodeManager;

            this.SymbolicName = block.Name;
            this.NodeId = nodeId;
            this.BrowseName = new QualifiedName(block.Name, nodeId.NamespaceIndex);
            this.DisplayName = new LocalizedText(block.Name);
            this.Description = null;
            this.WriteMask = 0;
            this.UserWriteMask = 0;
            this.EventNotifier = EventNotifiers.None;

            UnderlyingSystem system = nodeManager.SystemContext.SystemHandle as UnderlyingSystem;

            if (system != null)
            {
                IList<UnderlyingSystemTag> tags = block.GetTags();

                for (int ii = 0; ii < tags.Count; ii++)
                {
                    BaseVariableState variable = CreateVariable(nodeManager.SystemContext, tags[ii]);
                    AddChild(variable);
                    variable.OnSimpleWriteValue = OnWriteTagValue;
                }
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Starts the monitoring the block.
        /// </summary>
        /// <param name="context">The context.</param>
        public void StartMonitoring(ServerSystemContext context)
        {
            if (m_monitoringCount == 0)
            {
                UnderlyingSystem system = context.SystemHandle as UnderlyingSystem;

                if (system != null)
                {
                    UnderlyingSystemBlock block = system.FindBlock(m_blockId);

                    if (block != null)
                    {
                        block.StartMonitoring(OnTagsChanged);
                    }
                }
            }

            m_monitoringCount++;
        }

        /// <summary>
        /// Stop the monitoring the block.
        /// </summary>
        /// <param name="context">The context.</param>
        public bool StopMonitoring(ServerSystemContext context)
        {
            m_monitoringCount--;

            if (m_monitoringCount == 0)
            {
                UnderlyingSystem system = context.SystemHandle as UnderlyingSystem;

                if (system != null)
                {
                    UnderlyingSystemBlock block = system.FindBlock(m_blockId);

                    if (block != null)
                    {
                        block.StopMonitoring();
                    }
                }
            }

            return m_monitoringCount != 0;
        }

        /// <summary>
        /// Used to receive notifications when the value attribute is read or written.
        /// </summary>
        public ServiceResult OnWriteTagValue(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            UnderlyingSystem system = context.SystemHandle as UnderlyingSystem;

            if (system == null)
            {
                return StatusCodes.BadCommunicationError;
            }

            UnderlyingSystemBlock block = system.FindBlock(m_blockId);

            if (block == null)
            {                
                return StatusCodes.BadNodeIdUnknown;
            }
                
            uint error = block.WriteTagValue(node.SymbolicName, value);

            if (error != 0)
            {
                // the simulator uses UA status codes so there is no need for a mapping table.
                return error;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when one or more tags changes.
        /// </summary>
        /// <param name="tags">The tags.</param>
        private void OnTagsChanged(IList<UnderlyingSystemTag> tags)
        {
            lock (m_nodeManager.Lock)
            {
                for (int ii = 0; ii < tags.Count; ii++)
                {
                    BaseVariableState variable = FindChildBySymbolicName(m_nodeManager.SystemContext, tags[ii].Name) as BaseVariableState;

                    if (variable != null)
                    {
                        UpdateVariable(m_nodeManager.SystemContext, tags[ii], variable);
                    }
                }

                this.ClearChangeMasks(m_nodeManager.SystemContext, true);
            }
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="browser">The browser to populate.</param>
        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            // check if the parent segments need to be returned.
            if (browser.IsRequired(ReferenceTypeIds.Organizes, true))
            {
                UnderlyingSystem system = context.SystemHandle as UnderlyingSystem;

                if (system == null)
                {
                    return;
                }

                // add reference for each segment.
                IList<UnderlyingSystemSegment> segments = system.FindSegmentsForBlock(m_blockId);

                for (int ii = 0; ii < segments.Count; ii++)
                {
                    browser.Add(ReferenceTypeIds.Organizes, true, ModelUtils.ConstructIdForSegment(segments[ii].Id, this.NodeId.NamespaceIndex));
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a variable from a tag.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The variable that represents the tag.</returns>
        private BaseVariableState CreateVariable(ISystemContext context, UnderlyingSystemTag tag)
        {
            // create the variable type based on the tag type.
            BaseDataVariableState variable = null;

            switch (tag.TagType)
            {
                case UnderlyingSystemTagType.Analog:
                {
                    AnalogItemState node = new AnalogItemState(this);

                    if (tag.EngineeringUnits != null)
                    {
                        node.EngineeringUnits = new PropertyState<EUInformation>(node);
                    }

                    if (tag.EuRange.Length >= 4)
                    {
                        node.InstrumentRange = new PropertyState<Range>(node);
                    }

                    variable = node;
                    break;
                }

                case UnderlyingSystemTagType.Digital:
                {
                    TwoStateDiscreteState node = new TwoStateDiscreteState(this);
                    variable = node;
                    break;
                }

                case UnderlyingSystemTagType.Enumerated:
                {
                    MultiStateDiscreteState node = new MultiStateDiscreteState(this);

                    if (tag.Labels != null)
                    {
                        node.EnumStrings = new PropertyState<LocalizedText[]>(node);
                    }

                    variable = node;
                    break;
                }

                default:
                {
                    DataItemState node = new DataItemState(this);
                    variable = node;
                    break;
                }
            }

            // set the symbolic name and reference types.
            variable.SymbolicName = tag.Name;
            variable.ReferenceTypeId = ReferenceTypeIds.HasComponent;

            // initialize the variable from the type model.
            variable.Create(
                context,
                null,
                new QualifiedName(tag.Name, this.BrowseName.NamespaceIndex),
                null,
                true);

            // update the variable values.
            UpdateVariable(context, tag, variable);
            
            return variable;
        }

        /// <summary>
        /// Updates a variable from a tag.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="variable">The variable to update.</param>
        private void UpdateVariable(ISystemContext context, UnderlyingSystemTag tag, BaseVariableState variable)
        {
            variable.Description = tag.Description;
            variable.Value = tag.Value;
            variable.Timestamp = tag.Timestamp;

            switch (tag.DataType)
            {
                case UnderlyingSystemDataType.Integer1: { variable.DataType = DataTypes.SByte;  break; }
                case UnderlyingSystemDataType.Integer2: { variable.DataType = DataTypes.Int16;  break; }
                case UnderlyingSystemDataType.Integer4: { variable.DataType = DataTypes.Int32;  break; }
                case UnderlyingSystemDataType.Real4:    { variable.DataType = DataTypes.Float;  break; }
                case UnderlyingSystemDataType.String:   { variable.DataType = DataTypes.String; break; }
            }

            variable.ValueRank = ValueRanks.Scalar;
            variable.ArrayDimensions = null;

            if (tag.IsWriteable)
            {
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }
            else
            {
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
            }

            variable.MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            variable.Historizing = false;
            
            switch (tag.TagType)
            {
                case UnderlyingSystemTagType.Analog:
                {
                    AnalogItemState node = variable as AnalogItemState;

                    if (tag.EuRange != null)
                    {
                        if (tag.EuRange.Length >= 2 && node.EURange != null)
                        {
                            Range range = new Range(tag.EuRange[0], tag.EuRange[1]);
                            node.EURange.Value = range;
                            node.EURange.Timestamp = tag.Block.Timestamp;
                        }

                        if (tag.EuRange.Length >= 4 && node.InstrumentRange != null)
                        {
                            Range range = new Range(tag.EuRange[2], tag.EuRange[3]);
                            node.InstrumentRange.Value = range;
                            node.InstrumentRange.Timestamp = tag.Block.Timestamp;
                        }
                    }

                    if (!String.IsNullOrEmpty(tag.EngineeringUnits) && node.EngineeringUnits != null)
                    {
                        EUInformation info = new EUInformation();
                        info.DisplayName = tag.EngineeringUnits;
                        info.NamespaceUri = Namespaces.DataAccess;
                        node.EngineeringUnits.Value = info;
                        node.EngineeringUnits.Timestamp = tag.Block.Timestamp;
                    }

                    break;
                }

                case UnderlyingSystemTagType.Digital:
                {
                    TwoStateDiscreteState node = variable as TwoStateDiscreteState;

                    if (tag.Labels != null && node.TrueState != null && node.FalseState != null)
                    {
                        if (tag.Labels.Length >= 2)
                        {
                            node.TrueState.Value = new LocalizedText(tag.Labels[0]);
                            node.TrueState.Timestamp = tag.Block.Timestamp;
                            node.FalseState.Value = new LocalizedText(tag.Labels[1]);
                            node.FalseState.Timestamp = tag.Block.Timestamp;
                        }
                    }

                    break;
                }

                case UnderlyingSystemTagType.Enumerated:
                {
                    MultiStateDiscreteState node = variable as MultiStateDiscreteState;

                    if (tag.Labels != null)
                    {
                        LocalizedText[] strings = new LocalizedText[tag.Labels.Length];

                        for (int ii = 0; ii < tag.Labels.Length; ii++)
                        {
                            strings[ii] = new LocalizedText(tag.Labels[ii]);
                        }

                        node.EnumStrings.Value = strings;
                        node.EnumStrings.Timestamp = tag.Block.Timestamp;
                    }

                    break;
                }
            }
        }
        #endregion

        #region Private Fields
        private string m_blockId;
        private QuickstartNodeManager m_nodeManager;
        private int m_monitoringCount;
        #endregion
    }
}
