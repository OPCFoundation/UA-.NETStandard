/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Displays the results from a history read operation.
    /// </summary>
    public partial class HistoryEventCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public HistoryEventCtrl()
        {
            InitializeComponent();
            LeftPN.Enabled = false;

            ReadTypeCB.Items.Add(HistoryOperation.Read);
            ReadTypeCB.Items.Add(HistoryOperation.Update);
            ReadTypeCB.Items.Add(HistoryOperation.Delete);
            ReadTypeCB.SelectedIndex = 0;
        }
        #endregion

        #region HistoryOperation Enumeration
        /// <summary>
        /// The available history operations.
        /// </summary>
        public enum HistoryOperation
        {
            /// <summary>
            /// Read raw data.
            /// </summary>
            Read,

            /// <summary>
            /// Read modified data.
            /// </summary>
            Update,

            /// <summary>
            /// Read data at the specified times.
            /// </summary>
            Delete,
        }
        #endregion

        #region Private Fields
        private Session m_session;
        private NodeId m_nodeId;
        #endregion

        #region Public Members
        /// <summary>
        /// The node id to use.
        /// </summary>        
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public NodeId NodeId
        {
            get 
            { 
                return m_nodeId; 
            }
            
            set
            {
                m_nodeId = value;

                if (m_session != null)
                {
                    NodeIdTB.Text = m_session.NodeCache.GetDisplayText(m_nodeId);
                }
                else
                {
                    if (NodeId.IsNull(m_nodeId))
                    {
                        NodeIdTB.Text = String.Empty;
                    }
                    else
                    {
                        NodeIdTB.Text = m_nodeId.ToString();
                    }
                }
            }
        }

        /// <summary>
        /// The type of read operation.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public HistoryOperation Operation
        {
            get { return (HistoryOperation)ReadTypeCB.SelectedItem; }
            set { ReadTypeCB.SelectedItem = value; }
        }

        /// <summary>
        /// The start time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public DateTime StartTime
        {
            get
            {
                if (StartTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return StartTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    StartTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                StartTimeCK.Checked = true;
                StartTimeDP.Value = value;
            }
        }

        /// <summary>
        /// The end time for the query.
        /// </summary>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public DateTime EndTime
        {
            get
            {
                if (EndTimeCK.Checked)
                {
                    return DateTime.MinValue;
                }

                return EndTimeDP.Value;
            }

            set
            {
                if (value < Utils.TimeBase)
                {
                    EndTimeCK.Checked = false;
                    return;
                }

                if (value.Kind == DateTimeKind.Local)
                {
                    value = value.ToUniversalTime();
                }

                EndTimeCK.Checked = true;
                EndTimeDP.Value = value;
            }
        }

        /// <summary>
        /// Changes the session.
        /// </summary>
        public void ChangeSession(Session session)
        {
            m_session = session;
            LeftPN.Enabled = m_session != null;
        }
        
        /// <summary>
        /// Updates the control after the session has reconnected.
        /// </summary>
        public void SessionReconnected(Session session)
        {
            m_session = session;
        }

        /// <summary>
        /// Changes the node monitored by the control.
        /// </summary>
        public void ChangeNode(NodeId nodeId)
        {
            m_nodeId = nodeId;
            NodeIdTB.Text = m_session.NodeCache.GetDisplayText(m_nodeId);
        }

        /// <summary>
        /// A kludge to get around the stupid designer that keeps setting property values to bogus defaults.
        /// </summary>
        public void Reset()
        {
            NodeId = null;
            Operation = HistoryOperation.Read;
            StartTime = DateTime.MinValue;
            EndTime = DateTime.MinValue;
            StartTimeCK.Checked = true;
            EndTimeCK.Checked = false;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Recursively collects the variables in a NodeState and returns a collection of BrowsePaths.
        /// </summary>
        public void GetBrowsePathFromNodeState(
            ISystemContext context,
            NodeId rootId,
            NodeState parent,
            RelativePath parentPath,
            BrowsePathCollection browsePaths)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState child = children[ii];

                BrowsePath browsePath = new BrowsePath();
                browsePath.StartingNode = rootId;
                browsePath.Handle = child;

                if (parentPath != null)
                {
                    browsePath.RelativePath.Elements.AddRange(parentPath.Elements);
                }

                RelativePathElement element = new RelativePathElement();
                element.ReferenceTypeId = child.ReferenceTypeId;
                element.IsInverse = false;
                element.IncludeSubtypes = false;
                element.TargetName = child.BrowseName;

                browsePath.RelativePath.Elements.Add(element);

                if (child.NodeClass == NodeClass.Variable)
                {
                    browsePaths.Add(browsePath);
                }

                GetBrowsePathFromNodeState(context, rootId, child, browsePath.RelativePath, browsePaths); 
            }
        }

        private void NodeIdBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_session == null)
                {
                    return;
                }

                ReferenceDescription reference = new SelectNodeDlg().ShowDialog(
                    m_session,
                    Opc.Ua.ObjectIds.Server,
                    null,
                    "Select Notifier",
                    Opc.Ua.ReferenceTypeIds.HasNotifier);

                if (reference == null)
                {
                    return;
                }

                if (reference.NodeId != m_nodeId)
                {
                    ChangeNode((NodeId)reference.NodeId);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void GoBTN_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void NextBTN_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                HistoryOperation operation = (HistoryOperation)ReadTypeCB.SelectedItem;

                switch (operation)
                {
                    case HistoryOperation.Read:
                    {
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryOperation.Update:
                    {
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = true;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }

                    case HistoryOperation.Delete:
                    {
                        StartTimeLB.Visible = true;
                        StartTimeDP.Visible = true;
                        StartTimeCK.Visible = true;
                        StartTimeCK.Enabled = false;
                        StartTimeCK.Checked = true;
                        EndTimeLB.Visible = true;
                        EndTimeDP.Visible = true;
                        EndTimeCK.Visible = true;
                        EndTimeCK.Enabled = false;
                        EndTimeCK.Checked = true;
                        TimeStepLB.Visible = false;
                        TimeStepNP.Visible = false;
                        TimeStepUnitsLB.Visible = false;
                        TimeShiftBTN.Visible = false;
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion

        #region Event Handlers
        private void StartTimeDP_ValueChanged(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                StartTimeDP.Enabled = StartTimeCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                EndTimeDP.Enabled = EndTimeCK.Checked;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void TimeShiftBTN_Click(object sender, EventArgs e)
        {
            try
            {
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
