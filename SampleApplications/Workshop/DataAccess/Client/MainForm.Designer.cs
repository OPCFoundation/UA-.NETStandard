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

namespace Quickstarts.DataAccessClient
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
            this.components = new System.ComponentModel.Container();
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_SetLocaleMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_SetUserMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FileMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_LoadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_SaveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.TopPN = new System.Windows.Forms.SplitContainer();
            this.BrowseNodesTV = new System.Windows.Forms.TreeView();
            this.BrowsingMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Browse_MonitorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Browse_WriteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Browse_ReadHistoryMI = new System.Windows.Forms.ToolStripMenuItem();
            this.AttributesLV = new System.Windows.Forms.ListView();
            this.AttributeNameCH = new System.Windows.Forms.ColumnHeader();
            this.AttributeDataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.AttributeValueCH = new System.Windows.Forms.ColumnHeader();
            this.MonitoredItemsLV = new System.Windows.Forms.ListView();
            this.MonitoredItemIdCH = new System.Windows.Forms.ColumnHeader();
            this.VariableNameCH = new System.Windows.Forms.ColumnHeader();
            this.MonitoringModeCH = new System.Windows.Forms.ColumnHeader();
            this.SamplingIntevalCH = new System.Windows.Forms.ColumnHeader();
            this.DeadbandCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.QualityCH = new System.Windows.Forms.ColumnHeader();
            this.TimestampCH = new System.Windows.Forms.ColumnHeader();
            this.LastOperationStatusCH = new System.Windows.Forms.ColumnHeader();
            this.MonitoringMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Monitoring_DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_WriteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_MonitoringModeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_MonitoringMode_DisabledMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_MonitoringMode_SamplingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_MonitoringMode_ReportingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_SamplingIntervalMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_SamplingInterval_FastMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_SamplingInterval_1000MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_SamplingInterval_2500MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_SamplingInterval_5000MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_DeadbandMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_NoneMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_AbsoluteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Absolute_5MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Absolute_10MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Absolute_25MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_PercentageMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Percentage_1MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Percentage_5MI = new System.Windows.Forms.ToolStripMenuItem();
            this.Monitoring_Deadband_Percentage_10MI = new System.Windows.Forms.ToolStripMenuItem();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.fILEToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.MenuBar.SuspendLayout();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.TopPN.Panel1.SuspendLayout();
            this.TopPN.Panel2.SuspendLayout();
            this.TopPN.SuspendLayout();
            this.BrowsingMenu.SuspendLayout();
            this.MonitoringMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fILEToolStripMenuItem,
            this.ServerMI,
            this.ViewMI,
            this.FileMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(884, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // ServerMI
            // 
            this.ServerMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Server_DiscoverMI,
            this.Server_ConnectMI,
            this.Server_DisconnectMI,
            this.Server_SetLocaleMI,
            this.Server_SetUserMI});
            this.ServerMI.Name = "ServerMI";
            this.ServerMI.Size = new System.Drawing.Size(51, 20);
            this.ServerMI.Text = "Server";
            // 
            // Server_DiscoverMI
            // 
            this.Server_DiscoverMI.Name = "Server_DiscoverMI";
            this.Server_DiscoverMI.Size = new System.Drawing.Size(148, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            // 
            // Server_ConnectMI
            // 
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(148, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            // 
            // Server_DisconnectMI
            // 
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(148, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            // 
            // Server_SetLocaleMI
            // 
            this.Server_SetLocaleMI.Name = "Server_SetLocaleMI";
            this.Server_SetLocaleMI.Size = new System.Drawing.Size(148, 22);
            this.Server_SetLocaleMI.Text = "Select Locale...";
            this.Server_SetLocaleMI.Click += new System.EventHandler(this.Server_SetLocaleMI_Click);
            // 
            // Server_SetUserMI
            // 
            this.Server_SetUserMI.Name = "Server_SetUserMI";
            this.Server_SetUserMI.Size = new System.Drawing.Size(148, 22);
            this.Server_SetUserMI.Text = "Set User...";
            this.Server_SetUserMI.Click += new System.EventHandler(this.Server_SetUserMI_Click);
            // 
            // ViewMI
            // 
            this.ViewMI.Name = "ViewMI";
            this.ViewMI.Size = new System.Drawing.Size(41, 20);
            this.ViewMI.Text = "View";
            // 
            // FileMI
            // 
            this.FileMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.File_LoadMI,
            this.File_SaveMI});
            this.FileMI.Name = "FileMI";
            this.FileMI.Size = new System.Drawing.Size(97, 20);
            this.FileMI.Text = "Monitored Items";
            // 
            // File_LoadMI
            // 
            this.File_LoadMI.Name = "File_LoadMI";
            this.File_LoadMI.Size = new System.Drawing.Size(110, 22);
            this.File_LoadMI.Text = "Load...";
            this.File_LoadMI.Click += new System.EventHandler(this.File_LoadMI_Click);
            // 
            // File_SaveMI
            // 
            this.File_SaveMI.Name = "File_SaveMI";
            this.File_SaveMI.Size = new System.Drawing.Size(110, 22);
            this.File_SaveMI.Text = "Save...";
            this.File_SaveMI.Click += new System.EventHandler(this.File_SaveMI_Click);
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
            this.Help_ContentsMI.Size = new System.Drawing.Size(152, 22);
            this.Help_ContentsMI.Text = "Contents";
            this.Help_ContentsMI.Click += new System.EventHandler(this.Help_ContentsMI_Click);
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 524);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.TopPN);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.MonitoredItemsLV);
            this.MainPN.Size = new System.Drawing.Size(884, 402);
            this.MainPN.SplitterDistance = 278;
            this.MainPN.TabIndex = 1;
            // 
            // TopPN
            // 
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TopPN.Location = new System.Drawing.Point(0, 0);
            this.TopPN.Name = "TopPN";
            // 
            // TopPN.Panel1
            // 
            this.TopPN.Panel1.Controls.Add(this.BrowseNodesTV);
            // 
            // TopPN.Panel2
            // 
            this.TopPN.Panel2.Controls.Add(this.AttributesLV);
            this.TopPN.Size = new System.Drawing.Size(884, 278);
            this.TopPN.SplitterDistance = 391;
            this.TopPN.TabIndex = 0;
            // 
            // BrowseNodesTV
            // 
            this.BrowseNodesTV.ContextMenuStrip = this.BrowsingMenu;
            this.BrowseNodesTV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseNodesTV.Location = new System.Drawing.Point(0, 0);
            this.BrowseNodesTV.Name = "BrowseNodesTV";
            this.BrowseNodesTV.Size = new System.Drawing.Size(391, 278);
            this.BrowseNodesTV.TabIndex = 0;
            this.BrowseNodesTV.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.BrowseNodesTV_BeforeExpand);
            this.BrowseNodesTV.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.BrowseNodesTV_AfterSelect);
            this.BrowseNodesTV.MouseDown += new System.Windows.Forms.MouseEventHandler(this.BrowseNodesTV_MouseDown);
            // 
            // BrowsingMenu
            // 
            this.BrowsingMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Browse_MonitorMI,
            this.Browse_WriteMI,
            this.Browse_ReadHistoryMI});
            this.BrowsingMenu.Name = "BrowsingMenu";
            this.BrowsingMenu.Size = new System.Drawing.Size(149, 70);
            this.BrowsingMenu.Opening += new System.ComponentModel.CancelEventHandler(this.BrowsingMenu_Opening);
            // 
            // Browse_MonitorMI
            // 
            this.Browse_MonitorMI.Name = "Browse_MonitorMI";
            this.Browse_MonitorMI.Size = new System.Drawing.Size(148, 22);
            this.Browse_MonitorMI.Text = "Monitor";
            this.Browse_MonitorMI.Click += new System.EventHandler(this.Browse_MonitorMI_Click);
            // 
            // Browse_WriteMI
            // 
            this.Browse_WriteMI.Name = "Browse_WriteMI";
            this.Browse_WriteMI.Size = new System.Drawing.Size(148, 22);
            this.Browse_WriteMI.Text = "Write...";
            this.Browse_WriteMI.Click += new System.EventHandler(this.Browse_WriteMI_Click);
            // 
            // Browse_ReadHistoryMI
            // 
            this.Browse_ReadHistoryMI.Name = "Browse_ReadHistoryMI";
            this.Browse_ReadHistoryMI.Size = new System.Drawing.Size(148, 22);
            this.Browse_ReadHistoryMI.Text = "Read History...";
            this.Browse_ReadHistoryMI.Click += new System.EventHandler(this.Browse_ReadHistoryMI_Click);
            // 
            // AttributesLV
            // 
            this.AttributesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.AttributeNameCH,
            this.AttributeDataTypeCH,
            this.AttributeValueCH});
            this.AttributesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesLV.FullRowSelect = true;
            this.AttributesLV.Location = new System.Drawing.Point(0, 0);
            this.AttributesLV.Name = "AttributesLV";
            this.AttributesLV.Size = new System.Drawing.Size(489, 278);
            this.AttributesLV.TabIndex = 0;
            this.AttributesLV.UseCompatibleStateImageBehavior = false;
            this.AttributesLV.View = System.Windows.Forms.View.Details;
            // 
            // AttributeNameCH
            // 
            this.AttributeNameCH.Text = "Name";
            // 
            // AttributeDataTypeCH
            // 
            this.AttributeDataTypeCH.DisplayIndex = 2;
            this.AttributeDataTypeCH.Text = "Data Type";
            this.AttributeDataTypeCH.Width = 102;
            // 
            // AttributeValueCH
            // 
            this.AttributeValueCH.DisplayIndex = 1;
            this.AttributeValueCH.Text = "Value";
            // 
            // MonitoredItemsLV
            // 
            this.MonitoredItemsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.MonitoredItemIdCH,
            this.VariableNameCH,
            this.MonitoringModeCH,
            this.SamplingIntevalCH,
            this.DeadbandCH,
            this.ValueCH,
            this.QualityCH,
            this.TimestampCH,
            this.LastOperationStatusCH});
            this.MonitoredItemsLV.ContextMenuStrip = this.MonitoringMenu;
            this.MonitoredItemsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MonitoredItemsLV.FullRowSelect = true;
            this.MonitoredItemsLV.Location = new System.Drawing.Point(0, 0);
            this.MonitoredItemsLV.Name = "MonitoredItemsLV";
            this.MonitoredItemsLV.Size = new System.Drawing.Size(884, 120);
            this.MonitoredItemsLV.TabIndex = 0;
            this.MonitoredItemsLV.UseCompatibleStateImageBehavior = false;
            this.MonitoredItemsLV.View = System.Windows.Forms.View.Details;
            // 
            // MonitoredItemIdCH
            // 
            this.MonitoredItemIdCH.Text = "ID";
            // 
            // VariableNameCH
            // 
            this.VariableNameCH.Text = "Variable";
            // 
            // MonitoringModeCH
            // 
            this.MonitoringModeCH.Text = "Mode";
            // 
            // SamplingIntevalCH
            // 
            this.SamplingIntevalCH.Text = "Sampling Rate";
            this.SamplingIntevalCH.Width = 98;
            // 
            // DeadbandCH
            // 
            this.DeadbandCH.Text = "Deadband";
            this.DeadbandCH.Width = 89;
            // 
            // ValueCH
            // 
            this.ValueCH.Text = "Value";
            // 
            // QualityCH
            // 
            this.QualityCH.Text = "Quality";
            // 
            // TimestampCH
            // 
            this.TimestampCH.Text = "Timestamp";
            this.TimestampCH.Width = 109;
            // 
            // LastOperationStatusCH
            // 
            this.LastOperationStatusCH.Text = "Last Error";
            // 
            // MonitoringMenu
            // 
            this.MonitoringMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_DeleteMI,
            this.Monitoring_WriteMI,
            this.Monitoring_MonitoringModeMI,
            this.Monitoring_SamplingIntervalMI,
            this.Monitoring_DeadbandMI});
            this.MonitoringMenu.Name = "MonitoringMenu";
            this.MonitoringMenu.Size = new System.Drawing.Size(156, 114);
            // 
            // Monitoring_DeleteMI
            // 
            this.Monitoring_DeleteMI.Name = "Monitoring_DeleteMI";
            this.Monitoring_DeleteMI.Size = new System.Drawing.Size(155, 22);
            this.Monitoring_DeleteMI.Text = "Delete";
            this.Monitoring_DeleteMI.Click += new System.EventHandler(this.Monitoring_DeleteMI_Click);
            // 
            // Monitoring_WriteMI
            // 
            this.Monitoring_WriteMI.Name = "Monitoring_WriteMI";
            this.Monitoring_WriteMI.Size = new System.Drawing.Size(155, 22);
            this.Monitoring_WriteMI.Text = "Write...";
            this.Monitoring_WriteMI.Click += new System.EventHandler(this.Monitoring_WriteMI_Click);
            // 
            // Monitoring_MonitoringModeMI
            // 
            this.Monitoring_MonitoringModeMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_MonitoringMode_DisabledMI,
            this.Monitoring_MonitoringMode_SamplingMI,
            this.Monitoring_MonitoringMode_ReportingMI});
            this.Monitoring_MonitoringModeMI.Name = "Monitoring_MonitoringModeMI";
            this.Monitoring_MonitoringModeMI.Size = new System.Drawing.Size(155, 22);
            this.Monitoring_MonitoringModeMI.Text = "Monitoring Mode";
            // 
            // Monitoring_MonitoringMode_DisabledMI
            // 
            this.Monitoring_MonitoringMode_DisabledMI.Name = "Monitoring_MonitoringMode_DisabledMI";
            this.Monitoring_MonitoringMode_DisabledMI.Size = new System.Drawing.Size(121, 22);
            this.Monitoring_MonitoringMode_DisabledMI.Text = "Disabled";
            this.Monitoring_MonitoringMode_DisabledMI.Click += new System.EventHandler(this.Monitoring_MonitoringMode_Click);
            // 
            // Monitoring_MonitoringMode_SamplingMI
            // 
            this.Monitoring_MonitoringMode_SamplingMI.Name = "Monitoring_MonitoringMode_SamplingMI";
            this.Monitoring_MonitoringMode_SamplingMI.Size = new System.Drawing.Size(121, 22);
            this.Monitoring_MonitoringMode_SamplingMI.Text = "Sampling";
            this.Monitoring_MonitoringMode_SamplingMI.Click += new System.EventHandler(this.Monitoring_MonitoringMode_Click);
            // 
            // Monitoring_MonitoringMode_ReportingMI
            // 
            this.Monitoring_MonitoringMode_ReportingMI.Name = "Monitoring_MonitoringMode_ReportingMI";
            this.Monitoring_MonitoringMode_ReportingMI.Size = new System.Drawing.Size(121, 22);
            this.Monitoring_MonitoringMode_ReportingMI.Text = "Reporting";
            this.Monitoring_MonitoringMode_ReportingMI.Click += new System.EventHandler(this.Monitoring_MonitoringMode_Click);
            // 
            // Monitoring_SamplingIntervalMI
            // 
            this.Monitoring_SamplingIntervalMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_SamplingInterval_FastMI,
            this.Monitoring_SamplingInterval_1000MI,
            this.Monitoring_SamplingInterval_2500MI,
            this.Monitoring_SamplingInterval_5000MI});
            this.Monitoring_SamplingIntervalMI.Name = "Monitoring_SamplingIntervalMI";
            this.Monitoring_SamplingIntervalMI.Size = new System.Drawing.Size(155, 22);
            this.Monitoring_SamplingIntervalMI.Text = "Samping Interval";
            // 
            // Monitoring_SamplingInterval_FastMI
            // 
            this.Monitoring_SamplingInterval_FastMI.Name = "Monitoring_SamplingInterval_FastMI";
            this.Monitoring_SamplingInterval_FastMI.Size = new System.Drawing.Size(150, 22);
            this.Monitoring_SamplingInterval_FastMI.Text = "Fast as Possible";
            this.Monitoring_SamplingInterval_FastMI.Click += new System.EventHandler(this.Monitoring_SamplingInterval_Click);
            // 
            // Monitoring_SamplingInterval_1000MI
            // 
            this.Monitoring_SamplingInterval_1000MI.Name = "Monitoring_SamplingInterval_1000MI";
            this.Monitoring_SamplingInterval_1000MI.Size = new System.Drawing.Size(150, 22);
            this.Monitoring_SamplingInterval_1000MI.Text = "1000ms";
            this.Monitoring_SamplingInterval_1000MI.Click += new System.EventHandler(this.Monitoring_SamplingInterval_Click);
            // 
            // Monitoring_SamplingInterval_2500MI
            // 
            this.Monitoring_SamplingInterval_2500MI.Name = "Monitoring_SamplingInterval_2500MI";
            this.Monitoring_SamplingInterval_2500MI.Size = new System.Drawing.Size(150, 22);
            this.Monitoring_SamplingInterval_2500MI.Text = "2500ms";
            this.Monitoring_SamplingInterval_2500MI.Click += new System.EventHandler(this.Monitoring_SamplingInterval_Click);
            // 
            // Monitoring_SamplingInterval_5000MI
            // 
            this.Monitoring_SamplingInterval_5000MI.Name = "Monitoring_SamplingInterval_5000MI";
            this.Monitoring_SamplingInterval_5000MI.Size = new System.Drawing.Size(150, 22);
            this.Monitoring_SamplingInterval_5000MI.Text = "5000ms";
            this.Monitoring_SamplingInterval_5000MI.Click += new System.EventHandler(this.Monitoring_SamplingInterval_Click);
            // 
            // Monitoring_DeadbandMI
            // 
            this.Monitoring_DeadbandMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_Deadband_NoneMI,
            this.Monitoring_Deadband_AbsoluteMI,
            this.Monitoring_Deadband_PercentageMI});
            this.Monitoring_DeadbandMI.Name = "Monitoring_DeadbandMI";
            this.Monitoring_DeadbandMI.Size = new System.Drawing.Size(155, 22);
            this.Monitoring_DeadbandMI.Text = "Deadband";
            // 
            // Monitoring_Deadband_NoneMI
            // 
            this.Monitoring_Deadband_NoneMI.Name = "Monitoring_Deadband_NoneMI";
            this.Monitoring_Deadband_NoneMI.Size = new System.Drawing.Size(129, 22);
            this.Monitoring_Deadband_NoneMI.Text = "None";
            this.Monitoring_Deadband_NoneMI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_AbsoluteMI
            // 
            this.Monitoring_Deadband_AbsoluteMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_Deadband_Absolute_5MI,
            this.Monitoring_Deadband_Absolute_10MI,
            this.Monitoring_Deadband_Absolute_25MI});
            this.Monitoring_Deadband_AbsoluteMI.Name = "Monitoring_Deadband_AbsoluteMI";
            this.Monitoring_Deadband_AbsoluteMI.Size = new System.Drawing.Size(129, 22);
            this.Monitoring_Deadband_AbsoluteMI.Text = "Absolute";
            // 
            // Monitoring_Deadband_Absolute_5MI
            // 
            this.Monitoring_Deadband_Absolute_5MI.Name = "Monitoring_Deadband_Absolute_5MI";
            this.Monitoring_Deadband_Absolute_5MI.Size = new System.Drawing.Size(86, 22);
            this.Monitoring_Deadband_Absolute_5MI.Text = "5";
            this.Monitoring_Deadband_Absolute_5MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_Absolute_10MI
            // 
            this.Monitoring_Deadband_Absolute_10MI.Name = "Monitoring_Deadband_Absolute_10MI";
            this.Monitoring_Deadband_Absolute_10MI.Size = new System.Drawing.Size(86, 22);
            this.Monitoring_Deadband_Absolute_10MI.Text = "10";
            this.Monitoring_Deadband_Absolute_10MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_Absolute_25MI
            // 
            this.Monitoring_Deadband_Absolute_25MI.Name = "Monitoring_Deadband_Absolute_25MI";
            this.Monitoring_Deadband_Absolute_25MI.Size = new System.Drawing.Size(86, 22);
            this.Monitoring_Deadband_Absolute_25MI.Text = "25";
            this.Monitoring_Deadband_Absolute_25MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_PercentageMI
            // 
            this.Monitoring_Deadband_PercentageMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Monitoring_Deadband_Percentage_1MI,
            this.Monitoring_Deadband_Percentage_5MI,
            this.Monitoring_Deadband_Percentage_10MI});
            this.Monitoring_Deadband_PercentageMI.Name = "Monitoring_Deadband_PercentageMI";
            this.Monitoring_Deadband_PercentageMI.Size = new System.Drawing.Size(129, 22);
            this.Monitoring_Deadband_PercentageMI.Text = "Percentage";
            // 
            // Monitoring_Deadband_Percentage_1MI
            // 
            this.Monitoring_Deadband_Percentage_1MI.Name = "Monitoring_Deadband_Percentage_1MI";
            this.Monitoring_Deadband_Percentage_1MI.Size = new System.Drawing.Size(97, 22);
            this.Monitoring_Deadband_Percentage_1MI.Text = "1%";
            this.Monitoring_Deadband_Percentage_1MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_Percentage_5MI
            // 
            this.Monitoring_Deadband_Percentage_5MI.Name = "Monitoring_Deadband_Percentage_5MI";
            this.Monitoring_Deadband_Percentage_5MI.Size = new System.Drawing.Size(97, 22);
            this.Monitoring_Deadband_Percentage_5MI.Text = "5%";
            this.Monitoring_Deadband_Percentage_5MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // Monitoring_Deadband_Percentage_10MI
            // 
            this.Monitoring_Deadband_Percentage_10MI.Name = "Monitoring_Deadband_Percentage_10MI";
            this.Monitoring_Deadband_Percentage_10MI.Size = new System.Drawing.Size(97, 22);
            this.Monitoring_Deadband_Percentage_10MI.Text = "10%";
            this.Monitoring_Deadband_Percentage_10MI.Click += new System.EventHandler(this.Monitoring_Deadband_Click);
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 99);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(884, 23);
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
            this.clientHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 75);
            this.clientHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 75);
            this.clientHeaderBranding1.Name = "clientHeaderBranding1";
            this.clientHeaderBranding1.Padding = new System.Windows.Forms.Padding(3);
            this.clientHeaderBranding1.Size = new System.Drawing.Size(884, 75);
            this.clientHeaderBranding1.TabIndex = 5;
            // 
            // fILEToolStripMenuItem
            // 
            this.fILEToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.exitToolStripMenuItem});
            this.fILEToolStripMenuItem.Name = "fILEToolStripMenuItem";
            this.fILEToolStripMenuItem.Size = new System.Drawing.Size(35, 20);
            this.fILEToolStripMenuItem.Text = "&File";
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.exitToolStripMenuItem.Text = "E&xit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.ExitToolStripMenuItem_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "UA Data Access Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.TopPN.Panel1.ResumeLayout(false);
            this.TopPN.Panel2.ResumeLayout(false);
            this.TopPN.ResumeLayout(false);
            this.BrowsingMenu.ResumeLayout(false);
            this.MonitoringMenu.ResumeLayout(false);
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
        private System.Windows.Forms.ToolStripMenuItem ViewMI;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.SplitContainer MainPN;
        private System.Windows.Forms.SplitContainer TopPN;
        private System.Windows.Forms.TreeView BrowseNodesTV;
        private System.Windows.Forms.ListView AttributesLV;
        private System.Windows.Forms.ColumnHeader AttributeNameCH;
        private System.Windows.Forms.ColumnHeader AttributeDataTypeCH;
        private System.Windows.Forms.ColumnHeader AttributeValueCH;
        private System.Windows.Forms.ListView MonitoredItemsLV;
        private System.Windows.Forms.ColumnHeader MonitoredItemIdCH;
        private System.Windows.Forms.ColumnHeader VariableNameCH;
        private System.Windows.Forms.ColumnHeader MonitoringModeCH;
        private System.Windows.Forms.ColumnHeader SamplingIntevalCH;
        private System.Windows.Forms.ColumnHeader DeadbandCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ColumnHeader QualityCH;
        private System.Windows.Forms.ColumnHeader TimestampCH;
        private System.Windows.Forms.ColumnHeader LastOperationStatusCH;
        private System.Windows.Forms.ContextMenuStrip BrowsingMenu;
        private System.Windows.Forms.ToolStripMenuItem Browse_MonitorMI;
        private System.Windows.Forms.ToolStripMenuItem Browse_WriteMI;
        private System.Windows.Forms.ContextMenuStrip MonitoringMenu;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_DeleteMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_MonitoringModeMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_MonitoringMode_DisabledMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_MonitoringMode_SamplingMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_MonitoringMode_ReportingMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_SamplingIntervalMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_SamplingInterval_FastMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_SamplingInterval_1000MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_SamplingInterval_2500MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_SamplingInterval_5000MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_DeadbandMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_NoneMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_AbsoluteMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Absolute_5MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Absolute_10MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Absolute_25MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_PercentageMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Percentage_1MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Percentage_5MI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_Deadband_Percentage_10MI;
        private System.Windows.Forms.ToolStripMenuItem Browse_ReadHistoryMI;
        private System.Windows.Forms.ToolStripMenuItem Monitoring_WriteMI;
        private System.Windows.Forms.ToolStripMenuItem FileMI;
        private System.Windows.Forms.ToolStripMenuItem File_LoadMI;
        private System.Windows.Forms.ToolStripMenuItem File_SaveMI;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private System.Windows.Forms.ToolStripMenuItem Server_SetLocaleMI;
        private System.Windows.Forms.ToolStripMenuItem Server_SetUserMI;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
        private System.Windows.Forms.ToolStripMenuItem fILEToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
    }
}
