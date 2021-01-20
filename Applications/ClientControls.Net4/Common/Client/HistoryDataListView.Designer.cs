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

namespace Opc.Ua.Client.Controls
{
    partial class HistoryDataListView
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.AddValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EditValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.RemoveValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.InsertAnnotationMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ShowServerTimestampMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ResultsDV = new System.Windows.Forms.DataGridView();
            this.SourceTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusCodeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.HistoryInfoCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UpdateTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UpdateTimeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UserNameCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UpdateResultCN = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.LeftPN = new System.Windows.Forms.Panel();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.PropertyCB = new System.Windows.Forms.ComboBox();
            this.PropertyLB = new System.Windows.Forms.Label();
            this.UseSimpleBoundsCK = new System.Windows.Forms.CheckBox();
            this.UseSimpleBoundsLB = new System.Windows.Forms.Label();
            this.SamplingIntervalUnitsLB = new System.Windows.Forms.Label();
            this.TimeStepUnitsLB = new System.Windows.Forms.Label();
            this.SamplingIntervalNP = new System.Windows.Forms.NumericUpDown();
            this.TimeStepNP = new System.Windows.Forms.NumericUpDown();
            this.SamplingIntervalLB = new System.Windows.Forms.Label();
            this.TimeStepLB = new System.Windows.Forms.Label();
            this.NodeIdBTN = new System.Windows.Forms.Button();
            this.NodeIdTB = new System.Windows.Forms.TextBox();
            this.StartTimeCK = new System.Windows.Forms.CheckBox();
            this.NodeIdLB = new System.Windows.Forms.Label();
            this.ReturnBoundsLB = new System.Windows.Forms.Label();
            this.ResampleIntervalLB = new System.Windows.Forms.Label();
            this.ReadTypeCB = new System.Windows.Forms.ComboBox();
            this.EndTimeLB = new System.Windows.Forms.Label();
            this.ProcessingIntervalNP = new System.Windows.Forms.NumericUpDown();
            this.ReadTypeLB = new System.Windows.Forms.Label();
            this.AggregateCB = new System.Windows.Forms.ComboBox();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.StartTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeCK = new System.Windows.Forms.CheckBox();
            this.AggregateLB = new System.Windows.Forms.Label();
            this.MaxReturnValuesLB = new System.Windows.Forms.Label();
            this.MaxReturnValuesNP = new System.Windows.Forms.NumericUpDown();
            this.MaxReturnValuesCK = new System.Windows.Forms.CheckBox();
            this.ReturnBoundsCK = new System.Windows.Forms.CheckBox();
            this.ResampleIntervalUnitsLB = new System.Windows.Forms.Label();
            this.StatusTB = new System.Windows.Forms.TextBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.StopBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.TimeShiftBTN = new System.Windows.Forms.Button();
            this.GoBTN = new System.Windows.Forms.Button();
            this.DetectLimitsBTN = new System.Windows.Forms.Button();
            this.RightPN = new System.Windows.Forms.Panel();
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).BeginInit();
            this.LeftPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ProcessingIntervalNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).BeginInit();
            this.panel1.SuspendLayout();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.AddValueMI,
            this.EditValueMI,
            this.RemoveValueMI,
            this.InsertAnnotationMI,
            this.ShowServerTimestampMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(348, 194);
            // 
            // AddValueMI
            // 
            this.AddValueMI.Enabled = false;
            this.AddValueMI.Name = "AddValueMI";
            this.AddValueMI.Size = new System.Drawing.Size(347, 38);
            this.AddValueMI.Text = "Add...";
            // 
            // EditValueMI
            // 
            this.EditValueMI.Enabled = false;
            this.EditValueMI.Name = "EditValueMI";
            this.EditValueMI.Size = new System.Drawing.Size(347, 38);
            this.EditValueMI.Text = "Edit...";
            this.EditValueMI.Click += new System.EventHandler(this.EditValueMI_Click);
            // 
            // RemoveValueMI
            // 
            this.RemoveValueMI.Enabled = false;
            this.RemoveValueMI.Name = "RemoveValueMI";
            this.RemoveValueMI.Size = new System.Drawing.Size(347, 38);
            this.RemoveValueMI.Text = "Remove";
            // 
            // InsertAnnotationMI
            // 
            this.InsertAnnotationMI.Name = "InsertAnnotationMI";
            this.InsertAnnotationMI.Size = new System.Drawing.Size(347, 38);
            this.InsertAnnotationMI.Text = "Insert Annotation...";
            this.InsertAnnotationMI.Click += new System.EventHandler(this.InsertAnnotationMI_Click);
            // 
            // ShowServerTimestampMI
            // 
            this.ShowServerTimestampMI.CheckOnClick = true;
            this.ShowServerTimestampMI.Name = "ShowServerTimestampMI";
            this.ShowServerTimestampMI.Size = new System.Drawing.Size(347, 38);
            this.ShowServerTimestampMI.Text = "Show Server Timestamp";
            this.ShowServerTimestampMI.CheckedChanged += new System.EventHandler(this.ShowServerTimestampMI_CheckedChanged);
            // 
            // ResultsDV
            // 
            this.ResultsDV.AllowUserToAddRows = false;
            this.ResultsDV.AllowUserToDeleteRows = false;
            this.ResultsDV.AllowUserToResizeRows = false;
            this.ResultsDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ResultsDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.SourceTimestampCH,
            this.ServerTimestampCH,
            this.ValueCH,
            this.StatusCodeCH,
            this.HistoryInfoCH,
            this.UpdateTypeCH,
            this.UpdateTimeCH,
            this.UserNameCH,
            this.UpdateResultCN});
            this.ResultsDV.ContextMenuStrip = this.PopupMenu;
            this.ResultsDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsDV.Location = new System.Drawing.Point(0, 0);
            this.ResultsDV.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.ResultsDV.Name = "ResultsDV";
            this.ResultsDV.RowHeadersWidth = 82;
            this.ResultsDV.Size = new System.Drawing.Size(1310, 1096);
            this.ResultsDV.TabIndex = 0;
            // 
            // SourceTimestampCH
            // 
            this.SourceTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.SourceTimestampCH.DataPropertyName = "SourceTimestamp";
            this.SourceTimestampCH.HeaderText = "SourceTimestamp";
            this.SourceTimestampCH.MinimumWidth = 10;
            this.SourceTimestampCH.Name = "SourceTimestampCH";
            this.SourceTimestampCH.ReadOnly = true;
            this.SourceTimestampCH.Width = 230;
            // 
            // ServerTimestampCH
            // 
            this.ServerTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.ServerTimestampCH.DataPropertyName = "ServerTimestamp";
            this.ServerTimestampCH.HeaderText = "ServerTimestamp";
            this.ServerTimestampCH.MinimumWidth = 10;
            this.ServerTimestampCH.Name = "ServerTimestampCH";
            this.ServerTimestampCH.ReadOnly = true;
            this.ServerTimestampCH.Visible = false;
            this.ServerTimestampCH.Width = 200;
            // 
            // ValueCH
            // 
            this.ValueCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ValueCH.DataPropertyName = "Value";
            this.ValueCH.HeaderText = "Value";
            this.ValueCH.MinimumWidth = 10;
            this.ValueCH.Name = "ValueCH";
            this.ValueCH.ReadOnly = true;
            // 
            // StatusCodeCH
            // 
            this.StatusCodeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.StatusCodeCH.DataPropertyName = "StatusCode";
            this.StatusCodeCH.HeaderText = "StatusCode";
            this.StatusCodeCH.MinimumWidth = 10;
            this.StatusCodeCH.Name = "StatusCodeCH";
            this.StatusCodeCH.ReadOnly = true;
            this.StatusCodeCH.Width = 169;
            // 
            // HistoryInfoCH
            // 
            this.HistoryInfoCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.HistoryInfoCH.DataPropertyName = "HistoryInfo";
            this.HistoryInfoCH.HeaderText = "HistoryInfo";
            this.HistoryInfoCH.MinimumWidth = 10;
            this.HistoryInfoCH.Name = "HistoryInfoCH";
            this.HistoryInfoCH.Width = 159;
            // 
            // UpdateTypeCH
            // 
            this.UpdateTypeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.UpdateTypeCH.DataPropertyName = "UpdateType";
            this.UpdateTypeCH.HeaderText = "UpdateType";
            this.UpdateTypeCH.MinimumWidth = 10;
            this.UpdateTypeCH.Name = "UpdateTypeCH";
            this.UpdateTypeCH.ReadOnly = true;
            this.UpdateTypeCH.Visible = false;
            this.UpdateTypeCH.Width = 200;
            // 
            // UpdateTimeCH
            // 
            this.UpdateTimeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.UpdateTimeCH.DataPropertyName = "UpdateTime";
            this.UpdateTimeCH.HeaderText = "UpdateTime";
            this.UpdateTimeCH.MinimumWidth = 10;
            this.UpdateTimeCH.Name = "UpdateTimeCH";
            this.UpdateTimeCH.ReadOnly = true;
            this.UpdateTimeCH.Visible = false;
            this.UpdateTimeCH.Width = 200;
            // 
            // UserNameCH
            // 
            this.UserNameCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.UserNameCH.DataPropertyName = "UserName";
            this.UserNameCH.HeaderText = "UserName";
            this.UserNameCH.MinimumWidth = 10;
            this.UserNameCH.Name = "UserNameCH";
            this.UserNameCH.ReadOnly = true;
            this.UserNameCH.Visible = false;
            this.UserNameCH.Width = 200;
            // 
            // UpdateResultCN
            // 
            this.UpdateResultCN.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.UpdateResultCN.DataPropertyName = "UpdateResult";
            this.UpdateResultCN.HeaderText = "Update Result";
            this.UpdateResultCN.MinimumWidth = 10;
            this.UpdateResultCN.Name = "UpdateResultCN";
            this.UpdateResultCN.ReadOnly = true;
            this.UpdateResultCN.Visible = false;
            this.UpdateResultCN.Width = 200;
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.ControlsPN);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(612, 1096);
            this.LeftPN.TabIndex = 2;
            // 
            // ControlsPN
            // 
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 230F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.PropertyCB, 1, 1);
            this.ControlsPN.Controls.Add(this.PropertyLB, 0, 1);
            this.ControlsPN.Controls.Add(this.UseSimpleBoundsCK, 1, 7);
            this.ControlsPN.Controls.Add(this.UseSimpleBoundsLB, 0, 7);
            this.ControlsPN.Controls.Add(this.SamplingIntervalUnitsLB, 2, 3);
            this.ControlsPN.Controls.Add(this.TimeStepUnitsLB, 2, 11);
            this.ControlsPN.Controls.Add(this.SamplingIntervalNP, 1, 3);
            this.ControlsPN.Controls.Add(this.TimeStepNP, 1, 11);
            this.ControlsPN.Controls.Add(this.SamplingIntervalLB, 0, 3);
            this.ControlsPN.Controls.Add(this.TimeStepLB, 0, 11);
            this.ControlsPN.Controls.Add(this.NodeIdBTN, 2, 0);
            this.ControlsPN.Controls.Add(this.NodeIdTB, 1, 0);
            this.ControlsPN.Controls.Add(this.StartTimeCK, 2, 4);
            this.ControlsPN.Controls.Add(this.NodeIdLB, 0, 0);
            this.ControlsPN.Controls.Add(this.ReturnBoundsLB, 0, 8);
            this.ControlsPN.Controls.Add(this.ResampleIntervalLB, 0, 10);
            this.ControlsPN.Controls.Add(this.ReadTypeCB, 1, 2);
            this.ControlsPN.Controls.Add(this.EndTimeLB, 0, 5);
            this.ControlsPN.Controls.Add(this.ProcessingIntervalNP, 1, 10);
            this.ControlsPN.Controls.Add(this.ReadTypeLB, 0, 2);
            this.ControlsPN.Controls.Add(this.AggregateCB, 1, 9);
            this.ControlsPN.Controls.Add(this.StartTimeLB, 0, 4);
            this.ControlsPN.Controls.Add(this.StartTimeDP, 1, 4);
            this.ControlsPN.Controls.Add(this.EndTimeDP, 1, 5);
            this.ControlsPN.Controls.Add(this.EndTimeCK, 2, 5);
            this.ControlsPN.Controls.Add(this.AggregateLB, 0, 9);
            this.ControlsPN.Controls.Add(this.MaxReturnValuesLB, 0, 6);
            this.ControlsPN.Controls.Add(this.MaxReturnValuesNP, 1, 6);
            this.ControlsPN.Controls.Add(this.MaxReturnValuesCK, 2, 6);
            this.ControlsPN.Controls.Add(this.ReturnBoundsCK, 1, 8);
            this.ControlsPN.Controls.Add(this.ResampleIntervalUnitsLB, 2, 10);
            this.ControlsPN.Controls.Add(this.StatusTB, 0, 13);
            this.ControlsPN.Controls.Add(this.panel1, 0, 12);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 14;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 110F));
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 38F));
            this.ControlsPN.Size = new System.Drawing.Size(612, 748);
            this.ControlsPN.TabIndex = 0;
            // 
            // PropertyCB
            // 
            this.PropertyCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.PropertyCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.PropertyCB.FormattingEnabled = true;
            this.PropertyCB.Location = new System.Drawing.Point(236, 78);
            this.PropertyCB.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.PropertyCB.Name = "PropertyCB";
            this.PropertyCB.Size = new System.Drawing.Size(272, 33);
            this.PropertyCB.TabIndex = 6;
            // 
            // PropertyLB
            // 
            this.PropertyLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.PropertyLB.AutoSize = true;
            this.PropertyLB.Location = new System.Drawing.Point(6, 72);
            this.PropertyLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.PropertyLB.Name = "PropertyLB";
            this.PropertyLB.Size = new System.Drawing.Size(155, 45);
            this.PropertyLB.TabIndex = 5;
            this.PropertyLB.Text = "Property Name";
            this.PropertyLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UseSimpleBoundsCK
            // 
            this.UseSimpleBoundsCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.UseSimpleBoundsCK.AutoSize = true;
            this.ControlsPN.SetColumnSpan(this.UseSimpleBoundsCK, 2);
            this.UseSimpleBoundsCK.Location = new System.Drawing.Point(236, 347);
            this.UseSimpleBoundsCK.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.UseSimpleBoundsCK.Name = "UseSimpleBoundsCK";
            this.UseSimpleBoundsCK.Size = new System.Drawing.Size(28, 27);
            this.UseSimpleBoundsCK.TabIndex = 22;
            this.UseSimpleBoundsCK.UseVisualStyleBackColor = true;
            this.UseSimpleBoundsCK.Visible = false;
            // 
            // UseSimpleBoundsLB
            // 
            this.UseSimpleBoundsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.UseSimpleBoundsLB.AutoSize = true;
            this.UseSimpleBoundsLB.Location = new System.Drawing.Point(6, 341);
            this.UseSimpleBoundsLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.UseSimpleBoundsLB.Name = "UseSimpleBoundsLB";
            this.UseSimpleBoundsLB.Size = new System.Drawing.Size(200, 39);
            this.UseSimpleBoundsLB.TabIndex = 21;
            this.UseSimpleBoundsLB.Text = "Use Simple Bounds";
            this.UseSimpleBoundsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.UseSimpleBoundsLB.Visible = false;
            // 
            // SamplingIntervalUnitsLB
            // 
            this.SamplingIntervalUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.SamplingIntervalUnitsLB.AutoSize = true;
            this.SamplingIntervalUnitsLB.Location = new System.Drawing.Point(524, 162);
            this.SamplingIntervalUnitsLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.SamplingIntervalUnitsLB.Name = "SamplingIntervalUnitsLB";
            this.SamplingIntervalUnitsLB.Size = new System.Drawing.Size(40, 43);
            this.SamplingIntervalUnitsLB.TabIndex = 11;
            this.SamplingIntervalUnitsLB.Text = "ms";
            this.SamplingIntervalUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TimeStepUnitsLB
            // 
            this.TimeStepUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepUnitsLB.AutoSize = true;
            this.TimeStepUnitsLB.Location = new System.Drawing.Point(524, 507);
            this.TimeStepUnitsLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.TimeStepUnitsLB.Name = "TimeStepUnitsLB";
            this.TimeStepUnitsLB.Size = new System.Drawing.Size(40, 43);
            this.TimeStepUnitsLB.TabIndex = 32;
            this.TimeStepUnitsLB.Text = "ms";
            this.TimeStepUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SamplingIntervalNP
            // 
            this.SamplingIntervalNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.SamplingIntervalNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.SamplingIntervalNP.Location = new System.Drawing.Point(236, 168);
            this.SamplingIntervalNP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.SamplingIntervalNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.SamplingIntervalNP.Name = "SamplingIntervalNP";
            this.SamplingIntervalNP.Size = new System.Drawing.Size(276, 31);
            this.SamplingIntervalNP.TabIndex = 10;
            // 
            // TimeStepNP
            // 
            this.TimeStepNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.TimeStepNP.Location = new System.Drawing.Point(236, 513);
            this.TimeStepNP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.TimeStepNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.TimeStepNP.Name = "TimeStepNP";
            this.TimeStepNP.Size = new System.Drawing.Size(276, 31);
            this.TimeStepNP.TabIndex = 31;
            this.TimeStepNP.Value = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            // 
            // SamplingIntervalLB
            // 
            this.SamplingIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.SamplingIntervalLB.AutoSize = true;
            this.SamplingIntervalLB.Location = new System.Drawing.Point(6, 162);
            this.SamplingIntervalLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.SamplingIntervalLB.Name = "SamplingIntervalLB";
            this.SamplingIntervalLB.Size = new System.Drawing.Size(177, 43);
            this.SamplingIntervalLB.TabIndex = 9;
            this.SamplingIntervalLB.Text = "Sampling Interval";
            this.SamplingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TimeStepLB
            // 
            this.TimeStepLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepLB.AutoSize = true;
            this.TimeStepLB.Location = new System.Drawing.Point(6, 507);
            this.TimeStepLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.TimeStepLB.Name = "TimeStepLB";
            this.TimeStepLB.Size = new System.Drawing.Size(109, 43);
            this.TimeStepLB.TabIndex = 30;
            this.TimeStepLB.Text = "Time Step";
            this.TimeStepLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeIdBTN
            // 
            this.NodeIdBTN.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdBTN.Location = new System.Drawing.Point(524, 6);
            this.NodeIdBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.NodeIdBTN.Name = "NodeIdBTN";
            this.NodeIdBTN.Size = new System.Drawing.Size(48, 60);
            this.NodeIdBTN.TabIndex = 4;
            this.NodeIdBTN.Text = "...";
            this.NodeIdBTN.UseVisualStyleBackColor = true;
            this.NodeIdBTN.Click += new System.EventHandler(this.NodeIdBTN_Click);
            // 
            // NodeIdTB
            // 
            this.NodeIdTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdTB.Location = new System.Drawing.Point(236, 6);
            this.NodeIdTB.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.NodeIdTB.Name = "NodeIdTB";
            this.NodeIdTB.Size = new System.Drawing.Size(272, 31);
            this.NodeIdTB.TabIndex = 3;
            // 
            // StartTimeCK
            // 
            this.StartTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeCK.AutoSize = true;
            this.StartTimeCK.Location = new System.Drawing.Point(524, 211);
            this.StartTimeCK.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.StartTimeCK.Name = "StartTimeCK";
            this.StartTimeCK.Size = new System.Drawing.Size(28, 31);
            this.StartTimeCK.TabIndex = 14;
            this.StartTimeCK.UseVisualStyleBackColor = true;
            this.StartTimeCK.CheckedChanged += new System.EventHandler(this.StartTimeCK_CheckedChanged);
            // 
            // NodeIdLB
            // 
            this.NodeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdLB.AutoSize = true;
            this.NodeIdLB.Location = new System.Drawing.Point(6, 0);
            this.NodeIdLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.NodeIdLB.Name = "NodeIdLB";
            this.NodeIdLB.Size = new System.Drawing.Size(91, 72);
            this.NodeIdLB.TabIndex = 2;
            this.NodeIdLB.Text = "Variable";
            this.NodeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReturnBoundsLB
            // 
            this.ReturnBoundsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ReturnBoundsLB.AutoSize = true;
            this.ReturnBoundsLB.Location = new System.Drawing.Point(6, 380);
            this.ReturnBoundsLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.ReturnBoundsLB.Name = "ReturnBoundsLB";
            this.ReturnBoundsLB.Size = new System.Drawing.Size(155, 39);
            this.ReturnBoundsLB.TabIndex = 23;
            this.ReturnBoundsLB.Text = "Return Bounds";
            this.ReturnBoundsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.ReturnBoundsLB.Visible = false;
            // 
            // ResampleIntervalLB
            // 
            this.ResampleIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ResampleIntervalLB.AutoSize = true;
            this.ResampleIntervalLB.Location = new System.Drawing.Point(6, 464);
            this.ResampleIntervalLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.ResampleIntervalLB.Name = "ResampleIntervalLB";
            this.ResampleIntervalLB.Size = new System.Drawing.Size(195, 43);
            this.ResampleIntervalLB.TabIndex = 27;
            this.ResampleIntervalLB.Text = "Processing Interval";
            this.ResampleIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReadTypeCB
            // 
            this.ReadTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ReadTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ReadTypeCB.FormattingEnabled = true;
            this.ReadTypeCB.Location = new System.Drawing.Point(236, 123);
            this.ReadTypeCB.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.ReadTypeCB.Name = "ReadTypeCB";
            this.ReadTypeCB.Size = new System.Drawing.Size(272, 33);
            this.ReadTypeCB.TabIndex = 8;
            this.ReadTypeCB.SelectedIndexChanged += new System.EventHandler(this.ReadTypeCB_SelectedIndexChanged);
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(6, 248);
            this.EndTimeLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(103, 43);
            this.EndTimeLB.TabIndex = 15;
            this.EndTimeLB.Text = "End Time";
            this.EndTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProcessingIntervalNP
            // 
            this.ProcessingIntervalNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ProcessingIntervalNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.ProcessingIntervalNP.Location = new System.Drawing.Point(236, 470);
            this.ProcessingIntervalNP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.ProcessingIntervalNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.ProcessingIntervalNP.Name = "ProcessingIntervalNP";
            this.ProcessingIntervalNP.Size = new System.Drawing.Size(276, 31);
            this.ProcessingIntervalNP.TabIndex = 28;
            this.ProcessingIntervalNP.Value = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            // 
            // ReadTypeLB
            // 
            this.ReadTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ReadTypeLB.AutoSize = true;
            this.ReadTypeLB.Location = new System.Drawing.Point(6, 117);
            this.ReadTypeLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.ReadTypeLB.Name = "ReadTypeLB";
            this.ReadTypeLB.Size = new System.Drawing.Size(117, 45);
            this.ReadTypeLB.TabIndex = 7;
            this.ReadTypeLB.Text = "Read Type";
            this.ReadTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AggregateCB
            // 
            this.AggregateCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AggregateCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AggregateCB.FormattingEnabled = true;
            this.AggregateCB.Location = new System.Drawing.Point(236, 425);
            this.AggregateCB.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.AggregateCB.Name = "AggregateCB";
            this.AggregateCB.Size = new System.Drawing.Size(272, 33);
            this.AggregateCB.TabIndex = 26;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(6, 205);
            this.StartTimeLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(110, 43);
            this.StartTimeLB.TabIndex = 12;
            this.StartTimeLB.Text = "Start Time";
            this.StartTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeDP
            // 
            this.StartTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.StartTimeDP.Enabled = false;
            this.StartTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.StartTimeDP.Location = new System.Drawing.Point(236, 211);
            this.StartTimeDP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.StartTimeDP.Name = "StartTimeDP";
            this.StartTimeDP.Size = new System.Drawing.Size(272, 31);
            this.StartTimeDP.TabIndex = 13;
            this.StartTimeDP.ValueChanged += new System.EventHandler(this.StartTimeDP_ValueChanged);
            // 
            // EndTimeDP
            // 
            this.EndTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.EndTimeDP.Enabled = false;
            this.EndTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.EndTimeDP.Location = new System.Drawing.Point(236, 254);
            this.EndTimeDP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.EndTimeDP.Name = "EndTimeDP";
            this.EndTimeDP.Size = new System.Drawing.Size(272, 31);
            this.EndTimeDP.TabIndex = 16;
            this.EndTimeDP.ValueChanged += new System.EventHandler(this.StartTimeDP_ValueChanged);
            // 
            // EndTimeCK
            // 
            this.EndTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeCK.AutoSize = true;
            this.EndTimeCK.Location = new System.Drawing.Point(524, 254);
            this.EndTimeCK.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.EndTimeCK.Name = "EndTimeCK";
            this.EndTimeCK.Size = new System.Drawing.Size(28, 31);
            this.EndTimeCK.TabIndex = 17;
            this.EndTimeCK.UseVisualStyleBackColor = true;
            this.EndTimeCK.CheckedChanged += new System.EventHandler(this.EndTimeCK_CheckedChanged);
            // 
            // AggregateLB
            // 
            this.AggregateLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AggregateLB.AutoSize = true;
            this.AggregateLB.Location = new System.Drawing.Point(6, 419);
            this.AggregateLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.AggregateLB.Name = "AggregateLB";
            this.AggregateLB.Size = new System.Drawing.Size(111, 45);
            this.AggregateLB.TabIndex = 25;
            this.AggregateLB.Text = "Aggregate";
            this.AggregateLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxReturnValuesLB
            // 
            this.MaxReturnValuesLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.MaxReturnValuesLB.AutoSize = true;
            this.MaxReturnValuesLB.Location = new System.Drawing.Point(6, 291);
            this.MaxReturnValuesLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.MaxReturnValuesLB.Name = "MaxReturnValuesLB";
            this.MaxReturnValuesLB.Size = new System.Drawing.Size(131, 50);
            this.MaxReturnValuesLB.TabIndex = 18;
            this.MaxReturnValuesLB.Text = "Max Values Returned";
            this.MaxReturnValuesLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxReturnValuesNP
            // 
            this.MaxReturnValuesNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.MaxReturnValuesNP.Enabled = false;
            this.MaxReturnValuesNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Location = new System.Drawing.Point(236, 297);
            this.MaxReturnValuesNP.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.MaxReturnValuesNP.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Name = "MaxReturnValuesNP";
            this.MaxReturnValuesNP.Size = new System.Drawing.Size(276, 31);
            this.MaxReturnValuesNP.TabIndex = 19;
            // 
            // MaxReturnValuesCK
            // 
            this.MaxReturnValuesCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.MaxReturnValuesCK.AutoSize = true;
            this.MaxReturnValuesCK.Location = new System.Drawing.Point(524, 297);
            this.MaxReturnValuesCK.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.MaxReturnValuesCK.Name = "MaxReturnValuesCK";
            this.MaxReturnValuesCK.Size = new System.Drawing.Size(28, 38);
            this.MaxReturnValuesCK.TabIndex = 20;
            this.MaxReturnValuesCK.UseVisualStyleBackColor = true;
            this.MaxReturnValuesCK.CheckedChanged += new System.EventHandler(this.MaxReturnValuesCK_CheckedChanged);
            // 
            // ReturnBoundsCK
            // 
            this.ReturnBoundsCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ReturnBoundsCK.AutoSize = true;
            this.ControlsPN.SetColumnSpan(this.ReturnBoundsCK, 2);
            this.ReturnBoundsCK.Location = new System.Drawing.Point(236, 386);
            this.ReturnBoundsCK.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.ReturnBoundsCK.Name = "ReturnBoundsCK";
            this.ReturnBoundsCK.Size = new System.Drawing.Size(28, 27);
            this.ReturnBoundsCK.TabIndex = 24;
            this.ReturnBoundsCK.UseVisualStyleBackColor = true;
            this.ReturnBoundsCK.Visible = false;
            // 
            // ResampleIntervalUnitsLB
            // 
            this.ResampleIntervalUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.ResampleIntervalUnitsLB.AutoSize = true;
            this.ResampleIntervalUnitsLB.Location = new System.Drawing.Point(524, 464);
            this.ResampleIntervalUnitsLB.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.ResampleIntervalUnitsLB.Name = "ResampleIntervalUnitsLB";
            this.ResampleIntervalUnitsLB.Size = new System.Drawing.Size(40, 43);
            this.ResampleIntervalUnitsLB.TabIndex = 29;
            this.ResampleIntervalUnitsLB.Text = "ms";
            this.ResampleIntervalUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StatusTB
            // 
            this.StatusTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTB.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.ControlsPN.SetColumnSpan(this.StatusTB, 3);
            this.StatusTB.Location = new System.Drawing.Point(6, 666);
            this.StatusTB.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.StatusTB.Multiline = true;
            this.StatusTB.Name = "StatusTB";
            this.StatusTB.ReadOnly = true;
            this.StatusTB.Size = new System.Drawing.Size(600, 76);
            this.StatusTB.TabIndex = 34;
            // 
            // panel1
            // 
            this.ControlsPN.SetColumnSpan(this.panel1, 3);
            this.panel1.Controls.Add(this.StopBTN);
            this.panel1.Controls.Add(this.NextBTN);
            this.panel1.Controls.Add(this.TimeShiftBTN);
            this.panel1.Controls.Add(this.GoBTN);
            this.panel1.Controls.Add(this.DetectLimitsBTN);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(6, 556);
            this.panel1.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(600, 98);
            this.panel1.TabIndex = 33;
            // 
            // StopBTN
            // 
            this.StopBTN.Location = new System.Drawing.Point(416, 27);
            this.StopBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(150, 44);
            this.StopBTN.TabIndex = 2;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.StopBTN_Click);
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(226, 27);
            this.NextBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(150, 44);
            this.NextBTN.TabIndex = 1;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.NextBTN_Click);
            // 
            // TimeShiftBTN
            // 
            this.TimeShiftBTN.Location = new System.Drawing.Point(42, 27);
            this.TimeShiftBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.TimeShiftBTN.Name = "TimeShiftBTN";
            this.TimeShiftBTN.Size = new System.Drawing.Size(150, 44);
            this.TimeShiftBTN.TabIndex = 0;
            this.TimeShiftBTN.Text = "Time Shift";
            this.TimeShiftBTN.UseVisualStyleBackColor = true;
            this.TimeShiftBTN.Visible = false;
            this.TimeShiftBTN.Click += new System.EventHandler(this.TimeShiftBTN_Click);
            // 
            // GoBTN
            // 
            this.GoBTN.Location = new System.Drawing.Point(226, 27);
            this.GoBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.GoBTN.Name = "GoBTN";
            this.GoBTN.Size = new System.Drawing.Size(150, 44);
            this.GoBTN.TabIndex = 37;
            this.GoBTN.Text = "Go";
            this.GoBTN.UseVisualStyleBackColor = true;
            this.GoBTN.Click += new System.EventHandler(this.GoBTN_Click);
            // 
            // DetectLimitsBTN
            // 
            this.DetectLimitsBTN.Location = new System.Drawing.Point(42, 27);
            this.DetectLimitsBTN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.DetectLimitsBTN.Name = "DetectLimitsBTN";
            this.DetectLimitsBTN.Size = new System.Drawing.Size(150, 44);
            this.DetectLimitsBTN.TabIndex = 41;
            this.DetectLimitsBTN.Text = "Auto Detect";
            this.DetectLimitsBTN.UseVisualStyleBackColor = true;
            this.DetectLimitsBTN.Click += new System.EventHandler(this.DetectLimitsBTN_Click);
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.ResultsDV);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(612, 0);
            this.RightPN.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(1310, 1096);
            this.RightPN.TabIndex = 3;
            // 
            // HistoryDataListView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Controls.Add(this.LeftPN);
            this.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.Name = "HistoryDataListView";
            this.Size = new System.Drawing.Size(1922, 1096);
            this.PopupMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).EndInit();
            this.LeftPN.ResumeLayout(false);
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ProcessingIntervalNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).EndInit();
            this.panel1.ResumeLayout(false);
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.DataGridView ResultsDV;
        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.ToolStripMenuItem RemoveValueMI;
        private System.Windows.Forms.ToolStripMenuItem AddValueMI;
        private System.Windows.Forms.ToolStripMenuItem InsertAnnotationMI;
        private System.Windows.Forms.ToolStripMenuItem EditValueMI;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.ComboBox PropertyCB;
        private System.Windows.Forms.Label PropertyLB;
        private System.Windows.Forms.CheckBox UseSimpleBoundsCK;
        private System.Windows.Forms.Label UseSimpleBoundsLB;
        private System.Windows.Forms.Label SamplingIntervalUnitsLB;
        private System.Windows.Forms.Label TimeStepUnitsLB;
        private System.Windows.Forms.NumericUpDown SamplingIntervalNP;
        private System.Windows.Forms.NumericUpDown TimeStepNP;
        private System.Windows.Forms.Label SamplingIntervalLB;
        private System.Windows.Forms.Label TimeStepLB;
        private System.Windows.Forms.Button NodeIdBTN;
        private System.Windows.Forms.TextBox NodeIdTB;
        private System.Windows.Forms.CheckBox StartTimeCK;
        private System.Windows.Forms.Label NodeIdLB;
        private System.Windows.Forms.Label ReturnBoundsLB;
        private System.Windows.Forms.Label ResampleIntervalLB;
        private System.Windows.Forms.ComboBox ReadTypeCB;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.NumericUpDown ProcessingIntervalNP;
        private System.Windows.Forms.Label ReadTypeLB;
        private System.Windows.Forms.ComboBox AggregateCB;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.DateTimePicker StartTimeDP;
        private System.Windows.Forms.DateTimePicker EndTimeDP;
        private System.Windows.Forms.CheckBox EndTimeCK;
        private System.Windows.Forms.Label AggregateLB;
        private System.Windows.Forms.Label MaxReturnValuesLB;
        private System.Windows.Forms.NumericUpDown MaxReturnValuesNP;
        private System.Windows.Forms.CheckBox MaxReturnValuesCK;
        private System.Windows.Forms.CheckBox ReturnBoundsCK;
        private System.Windows.Forms.Label ResampleIntervalUnitsLB;
        private System.Windows.Forms.TextBox StatusTB;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button TimeShiftBTN;
        private System.Windows.Forms.Button GoBTN;
        private System.Windows.Forms.Button DetectLimitsBTN;
        private System.Windows.Forms.ToolStripMenuItem ShowServerTimestampMI;
        private System.Windows.Forms.DataGridViewTextBoxColumn SourceTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusCodeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn HistoryInfoCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UpdateTypeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UpdateTimeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UserNameCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UpdateResultCN;
    }
}
