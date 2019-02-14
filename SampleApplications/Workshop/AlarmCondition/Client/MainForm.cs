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
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using System.IO;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using System.Threading.Tasks;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// A form which displays the condition events produced by the server.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        private MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        /// <summary>
        /// Creates a form which uses the specified client configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use.</param>
        public MainForm(ApplicationConfiguration configuration)
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            ConnectServerCTRL.Configuration = m_configuration = configuration;
            ConnectServerCTRL.ServerUrl = "opc.tcp://localhost:62544/Quickstarts/AlarmConditionServer";
            this.Text = m_configuration.ApplicationName;

            // a table used to track event types.
            m_eventTypeMappings = new Dictionary<NodeId, NodeId>();

            // the filter to use.
            m_filter = new FilterDefinition();

            m_filter.AreaId = ObjectIds.Server;
            m_filter.Severity = EventSeverity.Min;
            m_filter.IgnoreSuppressedOrShelved = true;
            m_filter.EventTypes = new NodeId[] { ObjectTypeIds.ConditionType };

            // declate callback.
            m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

            // initialize controls.
            Conditions_Severity_AllMI.Checked = true;
            Conditions_Severity_AllMI.Tag = EventSeverity.Min;
            Conditions_Severity_LowMI.Tag = EventSeverity.Low;
            Conditions_Severity_MediumMI.Tag = EventSeverity.Medium;
            Conditions_Severity_HighMI.Tag = EventSeverity.High;

            Condition_Type_AllMI.Checked = true;
            Condition_Type_DialogsMI.Checked = false;
            Condition_Type_AlarmsMI.Checked = false;
            Condition_Type_LimitAlarmsMI.Checked = false;
            Condition_Type_DiscreteAlarmsMI.Checked = false;
        }
        #endregion

        #region Private Fields
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
        private MonitoredItem m_monitoredItem;
        private FilterDefinition m_filter;
        private Dictionary<NodeId, NodeId> m_eventTypeMappings;
        private MonitoredItemNotificationEventHandler m_MonitoredItem_Notification;
        private AuditEventForm m_auditEventForm;
        private bool m_connectedOnce;
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Connects to a server.
        /// </summary>
        private async void Server_ConnectMI_ClickAsync(object sender, EventArgs e)
        {
            try
            {
                await ConnectServerCTRL.Connect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Disconnects from the current session.
        /// </summary>
        private void Server_DisconnectMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Disconnect();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Prompts the user to choose a server on another host.
        /// </summary>
        private void Server_DiscoverMI_Click(object sender, EventArgs e)
        {
            try
            {
                ConnectServerCTRL.Discover(null);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after connecting to or disconnecting from the server.
        /// </summary>
        private void Server_ConnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                // check for disconnect.
                if (m_session == null)
                {
                    if (m_auditEventForm != null)
                    {
                        m_auditEventForm.Close();
                        m_auditEventForm = null;
                    }

                    return;
                }

                // set a suitable initial state.
                if (m_session != null && !m_connectedOnce)
                {
                    m_connectedOnce = true;
                }

                // create the default subscription.
                m_subscription = new Subscription();

                m_subscription.DisplayName = null;
                m_subscription.PublishingInterval = 1000;
                m_subscription.KeepAliveCount = 10;
                m_subscription.LifetimeCount = 100;
                m_subscription.MaxNotificationsPerPublish = 1000;
                m_subscription.PublishingEnabled = true;
                m_subscription.TimestampsToReturn = TimestampsToReturn.Both;

                m_session.AddSubscription(m_subscription);
                m_subscription.Create();

                // must specify the fields that the form is interested in.
                m_filter.SelectClauses = m_filter.ConstructSelectClauses(
                    m_session,
                    NodeId.Parse("ns=2;s=4:2"),
                    NodeId.Parse("ns=2;s=4:1"),
                    ObjectTypeIds.DialogConditionType,
                    ObjectTypeIds.ExclusiveLimitAlarmType,
                    ObjectTypeIds.NonExclusiveLimitAlarmType);

                // create a monitored item based on the current filter settings.
                m_monitoredItem = m_filter.CreateMonitoredItem(m_session);

                // set up callback for notifications.
                m_monitoredItem.Notification += m_MonitoredItem_Notification;

                m_subscription.AddItem(m_monitoredItem);
                m_subscription.ApplyChanges();

                // send an initial refresh.
                Conditions_RefreshMI_Click(sender, e);

                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after a communicate error was detected.
        /// </summary>
        private void Server_ReconnectStarting(object sender, EventArgs e)
        {
            try
            {
                ConditionsMI.Enabled = false;
                ViewMI.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Updates the application after reconnecting to the server.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                m_session = ConnectServerCTRL.Session;

                // replace the subscription.
                foreach (Subscription subscription in m_session.Subscriptions)
                {
                    m_subscription = subscription;
                    break;
                }

                // replace the monitored item.
                foreach (MonitoredItem monitoredItem in m_subscription.MonitoredItems)
                {
                    if (Object.ReferenceEquals(monitoredItem.Handle, m_filter))
                    {
                        m_monitoredItem = monitoredItem;
                        break;
                    }
                }

                if (m_auditEventForm != null)
                {
                    m_auditEventForm.ReconnectComplete(m_session, m_subscription);
                }

                // send a refresh.
                m_subscription.ConditionRefresh();

                ConditionsMI.Enabled = true;
                ViewMI.Enabled = true;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Cleans up when the main form closes.
        /// </summary>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            ConnectServerCTRL.Disconnect();
        }
        #endregion

        #region Condition Methods
        /// <summary>
        /// Updates the filter.
        /// </summary>
        private void UpdateFilter()
        {
            if (m_subscription != null)
            {
                // changing the filter changes the fields requested. this makes it 
                // impossible to process notifications sent before the change.
                // to avoid this problem we create a new item and remove the old one.
                MonitoredItem monitoredItem = m_filter.CreateMonitoredItem(m_session);

                // set up callback for notifications.
                monitoredItem.Notification += m_MonitoredItem_Notification;

                m_subscription.AddItem(monitoredItem);
                m_subscription.RemoveItem(m_monitoredItem);
                m_subscription.ApplyChanges();

                // replace monitored item.
                m_monitoredItem.Notification -= m_MonitoredItem_Notification;
                m_monitoredItem = monitoredItem;

                // send a refresh since previously filtered conditions may be now available.
                Conditions_RefreshMI_Click(this, null);
            }
        }

        /// <summary>
        /// Enables or disables the selected conditions.
        /// </summary>
        /// <param name="enable">if set to <c>true</c> the conditions are enabled.</param>
        private void EnableDisableCondition(bool enable)
        {
            if (enable)
            {
                CallMethod(MethodIds.ConditionType_Enable, null);
            }
            else
            {
                CallMethod(MethodIds.ConditionType_Disable, null);
            }
        }

        /// <summary>
        /// Adds a comment to the selected conditions.
        /// </summary>
        private void AddComment()
        {
            string comment = new AddCommentDlg().ShowDialog(String.Empty);

            if (comment == null)
            {
                return;
            }

            CallMethod(MethodIds.ConditionType_AddComment, comment);
        }

        /// <summary>
        /// Acknowledges the selected conditions.
        /// </summary>
        private void Acknowledge()
        {
            string comment = new AddCommentDlg().ShowDialog(String.Empty);

            if (comment == null)
            {
                return;
            }

            CallMethod(MethodIds.AcknowledgeableConditionType_Acknowledge, comment);
        }

        /// <summary>
        /// Confirms the selected conditions.
        /// </summary>
        private void Confirm()
        {
            string comment = new AddCommentDlg().ShowDialog(String.Empty);

            if (comment == null)
            {
                return;
            }

            CallMethod(MethodIds.AcknowledgeableConditionType_Confirm, comment);
        }

        /// <summary>
        /// Confirms the selected conditions.
        /// </summary>
        private void Shelve(bool shelving, bool oneShot, double shelvingTime)
        {
            // build list of methods to call.
            CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();

            for (int ii = 0; ii < ConditionsLV.SelectedItems.Count; ii++)
            {
                ConditionState condition = (ConditionState)ConditionsLV.SelectedItems[ii].Tag;

                // check if the node supports shelving.
                BaseObjectState shelvingState = condition.FindChild(m_session.SystemContext, BrowseNames.ShelvingState) as BaseObjectState;

                if (shelvingState == null)
                {
                    continue;
                }

                CallMethodRequest request = new CallMethodRequest();

                request.ObjectId = shelvingState.NodeId;
                request.Handle = ConditionsLV.SelectedItems[ii];

                // select the method to call.
                if (!shelving)
                {
                    request.MethodId = MethodIds.ShelvedStateMachineType_Unshelve;
                }
                else
                {
                    if (oneShot)
                    {
                        request.MethodId = MethodIds.ShelvedStateMachineType_OneShotShelve;
                    }
                    else
                    {
                        request.MethodId = MethodIds.ShelvedStateMachineType_TimedShelve;
                        request.InputArguments.Add(new Variant(shelvingTime));
                    }
                }

                methodsToCall.Add(request);
            }

            if (methodsToCall.Count == 0)
            {
                return;
            }

            // call the methods.
            CallMethodResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Call(
                null,
                methodsToCall,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

            for (int ii = 0; ii < results.Count; ii++)
            {
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    ListViewItem item = (ListViewItem)methodsToCall[ii].Handle;
                    item.SubItems[8].Text = Utils.Format("{0}", results[ii].StatusCode);
                }
            }
        }
        
        /// <summary>
        /// Responds to the dialog.
        /// </summary>
        private void Respond(int selectedResponse)
        {
            // build list of dialogs to respond to (caller should always make sure that only one is selected).
            CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();

            for (int ii = 0; ii < ConditionsLV.SelectedItems.Count; ii++)
            {
                DialogConditionState dialog = ConditionsLV.SelectedItems[ii].Tag as DialogConditionState;

                if (dialog == null)
                {
                    continue;
                }

                CallMethodRequest request = new CallMethodRequest();

                request.ObjectId = dialog.NodeId;
                request.MethodId = MethodIds.DialogConditionType_Respond;
                request.InputArguments.Add(new Variant(selectedResponse));
                request.Handle = ConditionsLV.SelectedItems[ii];

                methodsToCall.Add(request);
            }

            if (methodsToCall.Count == 0)
            {
                return;
            }

            // call the methods.
            CallMethodResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Call(
                null,
                methodsToCall,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

            for (int ii = 0; ii < results.Count; ii++)
            {
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    ListViewItem item = (ListViewItem)methodsToCall[ii].Handle;
                    item.SubItems[8].Text = Utils.Format("{0}", results[ii].StatusCode);
                }
            }
        }

        /// <summary>
        /// Adds a comment to the selected conditions.
        /// </summary>
        /// <param name="methodId">The NodeId for the method to call.</param>
        /// <param name="comment">The comment to pass as an argument.</param>
        private void CallMethod(NodeId methodId, string comment)
        {
            // build list of methods to call.
            CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();

            for (int ii = 0; ii < ConditionsLV.SelectedItems.Count; ii++)
            {
                ConditionState condition = (ConditionState)ConditionsLV.SelectedItems[ii].Tag;

                CallMethodRequest request = new CallMethodRequest();

                request.ObjectId = condition.NodeId;
                request.MethodId = methodId;
                request.Handle = ConditionsLV.SelectedItems[ii];

                if (comment != null)
                {
                    request.InputArguments.Add(new Variant(condition.EventId.Value));
                    request.InputArguments.Add(new Variant((LocalizedText)comment));
                }

                methodsToCall.Add(request);
            }

            if (methodsToCall.Count == 0)
            {
                return;
            }

            // call the methods.
            CallMethodResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.Call(
                null,
                methodsToCall,
                out results,
                out diagnosticInfos);

            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

            for (int ii = 0; ii < results.Count; ii++)
            {
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    ListViewItem item = (ListViewItem)methodsToCall[ii].Handle;
                    item.SubItems[8].Text = Utils.Format("{0}", results[ii].StatusCode);
                }
            }
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the display with a new value for a monitored variable. 
        /// </summary>
        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification), monitoredItem, e);
                return;
            }

            try
            {
                EventFieldList notification = e.NotificationValue as EventFieldList;

                if (notification == null)
                {
                    return;
                }

                // check the type of event.
                NodeId eventTypeId = FormUtils.FindEventType(monitoredItem, notification);

                // ignore unknown events.
                if (NodeId.IsNull(eventTypeId))
                {
                    return;
                }
                
                // check for refresh start.
                if (eventTypeId == ObjectTypeIds.RefreshStartEventType)
                {
                    ConditionsLV.Items.Clear();
                    return;
                }

                // check for refresh end.
                if (eventTypeId == ObjectTypeIds.RefreshEndEventType)
                {
                    return;
                }
                
                // construct the condition object.
                ConditionState condition = FormUtils.ConstructEvent(
                    m_session, 
                    monitoredItem, 
                    notification,
                    m_eventTypeMappings) as ConditionState;

                if (condition == null)
                {
                    return;
                }

                // look for existing entry.
                ListViewItem item = null;

                for (int ii = 0; ii < ConditionsLV.Items.Count; ii++)
                {
                    ConditionState current = (ConditionState)ConditionsLV.Items[ii].Tag;

                    // the combination of a condition and branch id uniquely identify an item in the display. 
                    if (current.NodeId == condition.NodeId && BaseVariableState.GetValue(current.BranchId) == BaseVariableState.GetValue(condition.BranchId))
                    {
                        // match found but watch out for out of order events (async processing can cause this to happen).
                        if (BaseVariableState.GetValue(current.Time) > BaseVariableState.GetValue(condition.Time))
                        {
                            return;
                        }

                        item = ConditionsLV.Items[ii];
                        break;
                    }
                }
                
                // create a new entry.
                if (item == null)
                {
                    item = new ListViewItem(String.Empty);

                    item.SubItems.Add(String.Empty); // Condition
                    item.SubItems.Add(String.Empty); // Branch
                    item.SubItems.Add(String.Empty); // Type
                    item.SubItems.Add(String.Empty); // Severity
                    item.SubItems.Add(String.Empty); // Time
                    item.SubItems.Add(String.Empty); // State
                    item.SubItems.Add(String.Empty); // Message
                    item.SubItems.Add(String.Empty); // Comment

                    ConditionsLV.Items.Add(item);
                }

                // look up the condition type metadata in the local cache.
                INode type = m_session.NodeCache.Find(condition.TypeDefinitionId);

                // Source
                if (condition.SourceName != null)
                {
                    item.SubItems[0].Text = Utils.Format("{0}", condition.SourceName.Value);
                }
                else
                {
                    item.SubItems[0].Text = null;
                }

                // Condition
                if (condition.ConditionName != null)
                {
                    item.SubItems[1].Text = Utils.Format("{0}", condition.ConditionName.Value);
                }
                else
                {
                    item.SubItems[1].Text = null;
                }

                // Branch
                if (condition.BranchId != null && !NodeId.IsNull(condition.BranchId.Value))
                {
                    item.SubItems[2].Text = Utils.Format("{0}", condition.BranchId.Value);
                }
                else
                {
                    item.SubItems[2].Text = null;
                }

                // Type
                if (type != null)
                {
                    item.SubItems[3].Text = Utils.Format("{0}", type);
                }
                else
                {
                    item.SubItems[3].Text = null;
                }

                // Severity
                if (condition.Severity != null)
                {
                    item.SubItems[4].Text = Utils.Format("{0}", (EventSeverity)condition.Severity.Value);
                }
                else
                {
                    item.SubItems[4].Text = null;
                }

                // Time
                if (condition.Time != null)
                {
                    item.SubItems[5].Text = Utils.Format("{0:HH:mm:ss.fff}", condition.Time.Value.ToLocalTime());
                }
                else
                {
                    item.SubItems[5].Text = null;
                }

                // State
                if (condition.EnabledState != null && condition.EnabledState.EffectiveDisplayName != null)
                {
                    item.SubItems[6].Text = Utils.Format("{0}", condition.EnabledState.EffectiveDisplayName.Value);
                }
                else
                {
                    item.SubItems[6].Text = null;
                }

                // Message
                if (condition.Message != null)
                {
                    item.SubItems[7].Text = Utils.Format("{0}", condition.Message.Value);
                }
                else
                {
                    item.SubItems[7].Text = null;
                }

                // Comment
                if (condition.Comment != null)
                {
                    item.SubItems[8].Text = Utils.Format("{0}", condition.Comment.Value);
                }
                else
                {
                    item.SubItems[8].Text = null;
                }

                item.Tag = condition;

                // set the color based on the retain bit.
                if (!BaseVariableState.GetValue(condition.Retain))
                {
                    item.ForeColor = Color.DimGray;
                }
                else
                {
                    if (NodeId.IsNull(BaseVariableState.GetValue(condition.BranchId)))
                    {
                        item.ForeColor = Color.Empty;
                    }
                    else
                    {
                        item.ForeColor = Color.DarkGray;
                    }
                }

                // adjust the width of the columns.
                for (int ii = 0; ii < ConditionsLV.Columns.Count; ii++)
                {
                    ConditionsLV.Columns[ii].Width = -2;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_RefreshMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_RefreshMI_Click(object sender, EventArgs e)
        {
            try
            {
                CallMethodRequest request = new CallMethodRequest();

                request.ObjectId = ObjectTypeIds.ConditionType;
                request.MethodId = MethodIds.ConditionType_ConditionRefresh;
                request.InputArguments.Add(new Variant(m_subscription.Id));
                
                CallMethodRequestCollection methodsToCall = new CallMethodRequestCollection();
                methodsToCall.Add(request);

                CallMethodResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                m_session.Call(
                    null,
                    methodsToCall,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, methodsToCall);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create((uint)results[0].StatusCode, "Unexpected error calling RefreshConditions.");
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_EnableMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_EnableMI_Click(object sender, EventArgs e)
        {
            try
            {
                EnableDisableCondition(true);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_DisableMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_DisableMI_Click(object sender, EventArgs e)
        {
            try
            {
                EnableDisableCondition(false);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the DropDownOpening event of the ConditionsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void ConditionsMI_DropDownOpening(object sender, EventArgs e)
        {
            try
            {
                bool connected = m_session != null && m_session.Connected;

                Conditions_SetAreaFilterMI.Enabled = connected;
                Conditions_SetTypeMI.Enabled = connected;
                Conditions_SetSeverityMI.Enabled = connected;
                Conditions_EnableMI.Enabled = connected;
                Conditions_DisableMI.Enabled = connected;
                Conditions_AddCommentMI.Enabled = connected;
                Conditions_RefreshMI.Enabled = connected;
                Conditions_AcknowledgeMI.Enabled = connected;
                Conditions_ConfirmMI.Enabled = connected;
                Conditions_RespondMI.Enabled = connected;
                Conditions_ShelvingMI.Enabled = connected;
                Conditions_MonitorMI.Enabled = connected;

                if (ConditionsLV.SelectedItems.Count == 0)
                {
                    Conditions_EnableMI.Enabled = false;
                    Conditions_DisableMI.Enabled = false;
                    Conditions_AddCommentMI.Enabled = false;
                    Conditions_AcknowledgeMI.Enabled = false;
                    Conditions_ConfirmMI.Enabled = false;
                    Conditions_RespondMI.Enabled = false;
                    Conditions_ShelvingMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                }

                if (ConditionsLV.SelectedItems.Count > 1)
                {
                    Conditions_RespondMI.Enabled = false;
                    Conditions_MonitorMI.Enabled = false;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_AddCommentMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_AddCommentMI_Click(object sender, EventArgs e)
        {
            try
            {
                AddComment();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_AcknowledgeMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_AcknowledgeMI_Click(object sender, EventArgs e)
        {
            try
            {
                Acknowledge();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_ConfirmMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_ConfirmMI_Click(object sender, EventArgs e)
        {
            try
            {
                Confirm();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_UnshelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_UnshelveMI_Click(object sender, EventArgs e)
        {
            try
            {
                Shelve(false, false, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_ManualShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_ManualShelveMI_Click(object sender, EventArgs e)
        {
            try
            {
                Shelve(true, false, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_OneShotShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_OneShotShelveMI_Click(object sender, EventArgs e)
        {
            try
            {
                Shelve(true, true, 0);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_TimedShelveMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_TimedShelveMI_Click(object sender, EventArgs e)
        {
            try
            {
                Shelve(true, false, 30000);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_MonitorMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_MonitorMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ConditionsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                ConditionState condition = (ConditionState)ConditionsLV.SelectedItems[0].Tag;
                new ViewEventDetailsDlg().ShowDialog(m_monitoredItem, condition.Handle as EventFieldList);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_SeverityMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_SeverityMI_Click(object sender, EventArgs e)
        {
            try
            {
                Conditions_Severity_AllMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_AllMI);
                Conditions_Severity_LowMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_LowMI);
                Conditions_Severity_MediumMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_MediumMI);
                Conditions_Severity_HighMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_HighMI);

                m_filter.Severity = (EventSeverity)((ToolStripMenuItem)sender).Tag;

                UpdateFilter();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_TypeMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_TypeMI_Click(object sender, EventArgs e)
        {
            try
            {
                Condition_Type_AllMI.Checked = Object.ReferenceEquals(sender, Conditions_Severity_AllMI);
                Condition_Type_DialogsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DialogsMI);
                Condition_Type_AlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_AlarmsMI);
                Condition_Type_LimitAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_LimitAlarmsMI);
                Condition_Type_DiscreteAlarmsMI.Checked = Object.ReferenceEquals(sender, Condition_Type_DiscreteAlarmsMI);

                List<NodeId> selectedTypes = new List<NodeId>();

                if (Condition_Type_AllMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ConditionType);
                }

                if (Condition_Type_DialogsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DialogConditionType);
                }

                if (Condition_Type_AlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.AlarmConditionType);
                }

                if (Condition_Type_LimitAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.ExclusiveLimitAlarmType);
                    selectedTypes.Add(ObjectTypeIds.NonExclusiveLimitAlarmType);
                }

                if (Condition_Type_DiscreteAlarmsMI.Checked)
                {
                    selectedTypes.Add(ObjectTypeIds.DiscreteAlarmType);
                }

                m_filter.EventTypes = selectedTypes;

                UpdateFilter();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_SetAreaFilterMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_SetAreaFilterMI_Click(object sender, EventArgs e)
        {
            try
            {
                NodeId areaId = new SetAreaFilterDlg().ShowDialog(m_session);

                if (areaId == null)
                {
                    return;
                }

                m_filter.AreaId = areaId;

                UpdateFilter();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the View_AuditEventsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void View_AuditEventsMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_auditEventForm == null)
                {
                    m_auditEventForm = new AuditEventForm(m_session, m_subscription);
                    m_auditEventForm.FormClosing += new FormClosingEventHandler(AuditEventForm_FormClosing);
                }

                m_auditEventForm.Show();
                m_auditEventForm.BringToFront();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the FormClosing event of the AuditEventForm control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.FormClosingEventArgs"/> instance containing the event data.</param>
        void AuditEventForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Object.ReferenceEquals(m_auditEventForm, sender))
            {
                m_auditEventForm = null;
            }
        }

        /// <summary>
        /// Handles the Click event of the Conditions_RespondMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Conditions_RespondMI_Click(object sender, EventArgs e)
        {
            try
            {
                if (ConditionsLV.SelectedItems.Count != 1)
                {
                    return;
                }

                DialogConditionState dialog = ConditionsLV.SelectedItems[0].Tag as DialogConditionState;

                if (dialog == null)
                {
                    return;
                }

                int selectedResponse = new DialogResponseDlg().ShowDialog(dialog);

                if (selectedResponse != -1)
                {
                    Respond(selectedResponse);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the Click event of the Help_ContentsMI control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Help_ContentsMI_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start( Path.GetDirectoryName(Application.ExecutablePath) + "\\WebHelp\\acclientoverview.htm");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch help documentation. Error: " + ex.Message);
            }
        }
        #endregion

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exit the application?", "UA Sample Client", MessageBoxButtons.YesNoCancel) == DialogResult.Yes)
            {
                Application.Exit();
            }
        }
    }
}
