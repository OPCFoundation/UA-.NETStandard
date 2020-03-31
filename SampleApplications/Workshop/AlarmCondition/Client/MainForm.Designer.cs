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

namespace Quickstarts.AlarmConditionClient
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ConditionsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_MonitorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_RefreshMI = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.Conditions_SetAreaFilterMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_SetTypeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Condition_Type_AllMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Condition_Type_DialogsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Condition_Type_AlarmsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Condition_Type_LimitAlarmsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Condition_Type_DiscreteAlarmsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_SetSeverityMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_Severity_AllMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_Severity_LowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_Severity_MediumMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_Severity_HighMI = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Conditions_EnableMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_DisableMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_AddCommentMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_AcknowledgeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_ConfirmMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_RespondMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_ShelvingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_UnshelveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_ManualShelveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_OneShotShelveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Conditions_TimedShelveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.View_AuditEventsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ConditionsLV = new System.Windows.Forms.ListView();
            this.SourceCH = new System.Windows.Forms.ColumnHeader();
            this.ConditionNameCH = new System.Windows.Forms.ColumnHeader();
            this.BranchCH = new System.Windows.Forms.ColumnHeader();
            this.ConditionTypeCH = new System.Windows.Forms.ColumnHeader();
            this.SeverityCH = new System.Windows.Forms.ColumnHeader();
            this.TimeCH = new System.Windows.Forms.ColumnHeader();
            this.StateCH = new System.Windows.Forms.ColumnHeader();
            this.MessageCH = new System.Windows.Forms.ColumnHeader();
            this.CommentCH = new System.Windows.Forms.ColumnHeader();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.ServerMI,
            this.ConditionsMI,
            this.ViewMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(626, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.exitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(35, 20);
            this.fileToolStripMenuItem.Text = "&File";
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(92, 22);
            this.exitToolStripMenuItem.Text = "E&xit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.ExitToolStripMenuItem_Click);
            // 
            // ServerMI
            // 
            this.ServerMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Server_DiscoverMI,
            this.Server_ConnectMI,
            this.Server_DisconnectMI});
            this.ServerMI.Name = "ServerMI";
            this.ServerMI.Size = new System.Drawing.Size(51, 20);
            this.ServerMI.Text = "Server";
            // 
            // Server_DiscoverMI
            // 
            this.Server_DiscoverMI.Name = "Server_DiscoverMI";
            this.Server_DiscoverMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            // 
            // Server_ConnectMI
            // 
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            // 
            // Server_DisconnectMI
            // 
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            // 
            // ConditionsMI
            // 
            this.ConditionsMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Conditions_MonitorMI,
            this.Conditions_RefreshMI,
            this.toolStripSeparator2,
            this.Conditions_SetAreaFilterMI,
            this.Conditions_SetTypeMI,
            this.Conditions_SetSeverityMI,
            this.toolStripSeparator1,
            this.Conditions_EnableMI,
            this.Conditions_DisableMI,
            this.Conditions_AddCommentMI,
            this.Conditions_AcknowledgeMI,
            this.Conditions_ConfirmMI,
            this.Conditions_RespondMI,
            this.Conditions_ShelvingMI});
            this.ConditionsMI.Name = "ConditionsMI";
            this.ConditionsMI.Size = new System.Drawing.Size(69, 20);
            this.ConditionsMI.Text = "Conditions";
            this.ConditionsMI.DropDownOpening += new System.EventHandler(this.ConditionsMI_DropDownOpening);
            // 
            // Conditions_MonitorMI
            // 
            this.Conditions_MonitorMI.Name = "Conditions_MonitorMI";
            this.Conditions_MonitorMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_MonitorMI.Text = "View...";
            this.Conditions_MonitorMI.Click += new System.EventHandler(this.Conditions_MonitorMI_Click);
            // 
            // Conditions_RefreshMI
            // 
            this.Conditions_RefreshMI.Name = "Conditions_RefreshMI";
            this.Conditions_RefreshMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_RefreshMI.Text = "Refresh";
            this.Conditions_RefreshMI.Click += new System.EventHandler(this.Conditions_RefreshMI_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(154, 6);
            // 
            // Conditions_SetAreaFilterMI
            // 
            this.Conditions_SetAreaFilterMI.Name = "Conditions_SetAreaFilterMI";
            this.Conditions_SetAreaFilterMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_SetAreaFilterMI.Text = "Set Area Filter...";
            this.Conditions_SetAreaFilterMI.Click += new System.EventHandler(this.Conditions_SetAreaFilterMI_Click);
            // 
            // Conditions_SetTypeMI
            // 
            this.Conditions_SetTypeMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Condition_Type_AllMI,
            this.Condition_Type_DialogsMI,
            this.Condition_Type_AlarmsMI,
            this.Condition_Type_LimitAlarmsMI,
            this.Condition_Type_DiscreteAlarmsMI});
            this.Conditions_SetTypeMI.Name = "Conditions_SetTypeMI";
            this.Conditions_SetTypeMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_SetTypeMI.Text = "Condition Type";
            // 
            // Condition_Type_AllMI
            // 
            this.Condition_Type_AllMI.Checked = true;
            this.Condition_Type_AllMI.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Condition_Type_AllMI.Name = "Condition_Type_AllMI";
            this.Condition_Type_AllMI.Size = new System.Drawing.Size(148, 22);
            this.Condition_Type_AllMI.Text = "All";
            this.Condition_Type_AllMI.Click += new System.EventHandler(this.Conditions_TypeMI_Click);
            // 
            // Condition_Type_DialogsMI
            // 
            this.Condition_Type_DialogsMI.Name = "Condition_Type_DialogsMI";
            this.Condition_Type_DialogsMI.Size = new System.Drawing.Size(148, 22);
            this.Condition_Type_DialogsMI.Text = "Dialogs";
            this.Condition_Type_DialogsMI.Click += new System.EventHandler(this.Conditions_TypeMI_Click);
            // 
            // Condition_Type_AlarmsMI
            // 
            this.Condition_Type_AlarmsMI.Name = "Condition_Type_AlarmsMI";
            this.Condition_Type_AlarmsMI.Size = new System.Drawing.Size(148, 22);
            this.Condition_Type_AlarmsMI.Text = "Alarms";
            this.Condition_Type_AlarmsMI.Click += new System.EventHandler(this.Conditions_TypeMI_Click);
            // 
            // Condition_Type_LimitAlarmsMI
            // 
            this.Condition_Type_LimitAlarmsMI.Name = "Condition_Type_LimitAlarmsMI";
            this.Condition_Type_LimitAlarmsMI.Size = new System.Drawing.Size(148, 22);
            this.Condition_Type_LimitAlarmsMI.Text = "Limit Alarms";
            this.Condition_Type_LimitAlarmsMI.Click += new System.EventHandler(this.Conditions_TypeMI_Click);
            // 
            // Condition_Type_DiscreteAlarmsMI
            // 
            this.Condition_Type_DiscreteAlarmsMI.Name = "Condition_Type_DiscreteAlarmsMI";
            this.Condition_Type_DiscreteAlarmsMI.Size = new System.Drawing.Size(148, 22);
            this.Condition_Type_DiscreteAlarmsMI.Text = "Discrete Alarms";
            this.Condition_Type_DiscreteAlarmsMI.Click += new System.EventHandler(this.Conditions_TypeMI_Click);
            // 
            // Conditions_SetSeverityMI
            // 
            this.Conditions_SetSeverityMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Conditions_Severity_AllMI,
            this.Conditions_Severity_LowMI,
            this.Conditions_Severity_MediumMI,
            this.Conditions_Severity_HighMI});
            this.Conditions_SetSeverityMI.Name = "Conditions_SetSeverityMI";
            this.Conditions_SetSeverityMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_SetSeverityMI.Text = "Minimum Severity";
            // 
            // Conditions_Severity_AllMI
            // 
            this.Conditions_Severity_AllMI.Checked = true;
            this.Conditions_Severity_AllMI.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Conditions_Severity_AllMI.Name = "Conditions_Severity_AllMI";
            this.Conditions_Severity_AllMI.Size = new System.Drawing.Size(110, 22);
            this.Conditions_Severity_AllMI.Text = "All";
            this.Conditions_Severity_AllMI.Click += new System.EventHandler(this.Conditions_SeverityMI_Click);
            // 
            // Conditions_Severity_LowMI
            // 
            this.Conditions_Severity_LowMI.Name = "Conditions_Severity_LowMI";
            this.Conditions_Severity_LowMI.Size = new System.Drawing.Size(110, 22);
            this.Conditions_Severity_LowMI.Text = "Low";
            this.Conditions_Severity_LowMI.Click += new System.EventHandler(this.Conditions_SeverityMI_Click);
            // 
            // Conditions_Severity_MediumMI
            // 
            this.Conditions_Severity_MediumMI.Name = "Conditions_Severity_MediumMI";
            this.Conditions_Severity_MediumMI.Size = new System.Drawing.Size(110, 22);
            this.Conditions_Severity_MediumMI.Text = "Medium";
            this.Conditions_Severity_MediumMI.Click += new System.EventHandler(this.Conditions_SeverityMI_Click);
            // 
            // Conditions_Severity_HighMI
            // 
            this.Conditions_Severity_HighMI.Name = "Conditions_Severity_HighMI";
            this.Conditions_Severity_HighMI.Size = new System.Drawing.Size(110, 22);
            this.Conditions_Severity_HighMI.Text = "High";
            this.Conditions_Severity_HighMI.Click += new System.EventHandler(this.Conditions_SeverityMI_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(154, 6);
            // 
            // Conditions_EnableMI
            // 
            this.Conditions_EnableMI.Name = "Conditions_EnableMI";
            this.Conditions_EnableMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_EnableMI.Text = "Enable";
            this.Conditions_EnableMI.Click += new System.EventHandler(this.Conditions_EnableMI_Click);
            // 
            // Conditions_DisableMI
            // 
            this.Conditions_DisableMI.Name = "Conditions_DisableMI";
            this.Conditions_DisableMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_DisableMI.Text = "Disable";
            this.Conditions_DisableMI.Click += new System.EventHandler(this.Conditions_DisableMI_Click);
            // 
            // Conditions_AddCommentMI
            // 
            this.Conditions_AddCommentMI.Name = "Conditions_AddCommentMI";
            this.Conditions_AddCommentMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_AddCommentMI.Text = "Add Comment...";
            this.Conditions_AddCommentMI.Click += new System.EventHandler(this.Conditions_AddCommentMI_Click);
            // 
            // Conditions_AcknowledgeMI
            // 
            this.Conditions_AcknowledgeMI.Name = "Conditions_AcknowledgeMI";
            this.Conditions_AcknowledgeMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_AcknowledgeMI.Text = "Acknowledge...";
            this.Conditions_AcknowledgeMI.Click += new System.EventHandler(this.Conditions_AcknowledgeMI_Click);
            // 
            // Conditions_ConfirmMI
            // 
            this.Conditions_ConfirmMI.Name = "Conditions_ConfirmMI";
            this.Conditions_ConfirmMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_ConfirmMI.Text = "Confirm...";
            this.Conditions_ConfirmMI.Click += new System.EventHandler(this.Conditions_ConfirmMI_Click);
            // 
            // Conditions_RespondMI
            // 
            this.Conditions_RespondMI.Name = "Conditions_RespondMI";
            this.Conditions_RespondMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_RespondMI.Text = "Respond...";
            this.Conditions_RespondMI.Click += new System.EventHandler(this.Conditions_RespondMI_Click);
            // 
            // Conditions_ShelvingMI
            // 
            this.Conditions_ShelvingMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Conditions_UnshelveMI,
            this.Conditions_ManualShelveMI,
            this.Conditions_OneShotShelveMI,
            this.Conditions_TimedShelveMI});
            this.Conditions_ShelvingMI.Name = "Conditions_ShelvingMI";
            this.Conditions_ShelvingMI.Size = new System.Drawing.Size(157, 22);
            this.Conditions_ShelvingMI.Text = "Shelving";
            // 
            // Conditions_UnshelveMI
            // 
            this.Conditions_UnshelveMI.Name = "Conditions_UnshelveMI";
            this.Conditions_UnshelveMI.Size = new System.Drawing.Size(154, 22);
            this.Conditions_UnshelveMI.Text = "Unshelve";
            this.Conditions_UnshelveMI.Click += new System.EventHandler(this.Conditions_UnshelveMI_Click);
            // 
            // Conditions_ManualShelveMI
            // 
            this.Conditions_ManualShelveMI.Name = "Conditions_ManualShelveMI";
            this.Conditions_ManualShelveMI.Size = new System.Drawing.Size(154, 22);
            this.Conditions_ManualShelveMI.Text = "Manual Shelve";
            this.Conditions_ManualShelveMI.Click += new System.EventHandler(this.Conditions_ManualShelveMI_Click);
            // 
            // Conditions_OneShotShelveMI
            // 
            this.Conditions_OneShotShelveMI.Name = "Conditions_OneShotShelveMI";
            this.Conditions_OneShotShelveMI.Size = new System.Drawing.Size(154, 22);
            this.Conditions_OneShotShelveMI.Text = "One Shot Shelve";
            this.Conditions_OneShotShelveMI.Click += new System.EventHandler(this.Conditions_OneShotShelveMI_Click);
            // 
            // Conditions_TimedShelveMI
            // 
            this.Conditions_TimedShelveMI.Name = "Conditions_TimedShelveMI";
            this.Conditions_TimedShelveMI.Size = new System.Drawing.Size(154, 22);
            this.Conditions_TimedShelveMI.Text = "Timed Shelve...";
            this.Conditions_TimedShelveMI.Click += new System.EventHandler(this.Conditions_TimedShelveMI_Click);
            // 
            // ViewMI
            // 
            this.ViewMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.View_AuditEventsMI});
            this.ViewMI.Name = "ViewMI";
            this.ViewMI.Size = new System.Drawing.Size(41, 20);
            this.ViewMI.Text = "View";
            // 
            // View_AuditEventsMI
            // 
            this.View_AuditEventsMI.Name = "View_AuditEventsMI";
            this.View_AuditEventsMI.Size = new System.Drawing.Size(147, 22);
            this.View_AuditEventsMI.Text = "Audit Events...";
            this.View_AuditEventsMI.Click += new System.EventHandler(this.View_AuditEventsMI_Click);
            // 
            // HelpMI
            // 
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_ContentsMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(40, 20);
            this.HelpMI.Text = "Help";
            // 
            // Help_ContentsMI
            // 
            this.Help_ContentsMI.Name = "Help_ContentsMI";
            this.Help_ContentsMI.Size = new System.Drawing.Size(118, 22);
            this.Help_ContentsMI.Text = "Contents";
            this.Help_ContentsMI.Click += new System.EventHandler(this.Help_ContentsMI_Click);
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 439);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(626, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ConditionsLV);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 137);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(626, 302);
            this.MainPN.TabIndex = 3;
            // 
            // ConditionsLV
            // 
            this.ConditionsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.SourceCH,
            this.ConditionNameCH,
            this.BranchCH,
            this.ConditionTypeCH,
            this.SeverityCH,
            this.TimeCH,
            this.StateCH,
            this.MessageCH,
            this.CommentCH});
            this.ConditionsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ConditionsLV.FullRowSelect = true;
            this.ConditionsLV.Location = new System.Drawing.Point(2, 2);
            this.ConditionsLV.Name = "ConditionsLV";
            this.ConditionsLV.Size = new System.Drawing.Size(622, 300);
            this.ConditionsLV.TabIndex = 0;
            this.ConditionsLV.UseCompatibleStateImageBehavior = false;
            this.ConditionsLV.View = System.Windows.Forms.View.Details;
            // 
            // SourceCH
            // 
            this.SourceCH.Text = "Source";
            // 
            // ConditionNameCH
            // 
            this.ConditionNameCH.Text = "Condition";
            // 
            // BranchCH
            // 
            this.BranchCH.Text = "Branch";
            // 
            // ConditionTypeCH
            // 
            this.ConditionTypeCH.Text = "Type";
            // 
            // SeverityCH
            // 
            this.SeverityCH.Text = "Severity";
            this.SeverityCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // TimeCH
            // 
            this.TimeCH.Text = "Time";
            this.TimeCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // StateCH
            // 
            this.StateCH.Text = "State";
            // 
            // MessageCH
            // 
            this.MessageCH.Text = "Message";
            // 
            // CommentCH
            // 
            this.CommentCH.Text = "Comment";
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 114);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(626, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 4;
            this.ConnectServerCTRL.UserIdentity = null;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectComplete);
            this.ConnectServerCTRL.ReconnectStarting += new System.EventHandler(this.Server_ReconnectStarting);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectComplete);
            // 
            // clientHeaderBranding1
            // 
            this.clientHeaderBranding1.BackColor = System.Drawing.Color.White;
            this.clientHeaderBranding1.Dock = System.Windows.Forms.DockStyle.Top;
            this.clientHeaderBranding1.Location = new System.Drawing.Point(0, 24);
            this.clientHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 90);
            this.clientHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 90);
            this.clientHeaderBranding1.Name = "clientHeaderBranding1";
            this.clientHeaderBranding1.Padding = new System.Windows.Forms.Padding(3);
            this.clientHeaderBranding1.Size = new System.Drawing.Size(626, 90);
            this.clientHeaderBranding1.TabIndex = 5;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(626, 461);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "UA Alarm Condition Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ListView ConditionsLV;
        private System.Windows.Forms.ColumnHeader SourceCH;
        private System.Windows.Forms.ColumnHeader ConditionTypeCH;
        private System.Windows.Forms.ColumnHeader SeverityCH;
        private System.Windows.Forms.ColumnHeader TimeCH;
        private System.Windows.Forms.ColumnHeader StateCH;
        private System.Windows.Forms.ColumnHeader MessageCH;
        private System.Windows.Forms.ToolStripMenuItem ConditionsMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_SetAreaFilterMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_SetTypeMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_SetSeverityMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem Conditions_EnableMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_DisableMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem Conditions_RefreshMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_AddCommentMI;
        private System.Windows.Forms.ColumnHeader CommentCH;
        private System.Windows.Forms.ToolStripMenuItem Conditions_AcknowledgeMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_ConfirmMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_RespondMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_ShelvingMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_MonitorMI;
        private System.Windows.Forms.ColumnHeader ConditionNameCH;
        private System.Windows.Forms.ToolStripMenuItem Conditions_UnshelveMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_ManualShelveMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_OneShotShelveMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_TimedShelveMI;
        private System.Windows.Forms.ColumnHeader BranchCH;
        private System.Windows.Forms.ToolStripMenuItem Condition_Type_AllMI;
        private System.Windows.Forms.ToolStripMenuItem Condition_Type_AlarmsMI;
        private System.Windows.Forms.ToolStripMenuItem Condition_Type_DialogsMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_Severity_AllMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_Severity_LowMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_Severity_MediumMI;
        private System.Windows.Forms.ToolStripMenuItem Conditions_Severity_HighMI;
        private System.Windows.Forms.ToolStripMenuItem Condition_Type_LimitAlarmsMI;
        private System.Windows.Forms.ToolStripMenuItem Condition_Type_DiscreteAlarmsMI;
        private System.Windows.Forms.ToolStripMenuItem ViewMI;
        private System.Windows.Forms.ToolStripMenuItem View_AuditEventsMI;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
