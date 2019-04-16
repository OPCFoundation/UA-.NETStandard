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

namespace Quickstarts.HistoricalAccess.Client
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
            this.ViewDetailsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteHistoryMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ResultsDV = new System.Windows.Forms.DataGridView();
            this.SourceTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusCodeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.HistoryInfoCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UpdateTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UpdateTimeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.UserNameCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.LeftPN = new System.Windows.Forms.Panel();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
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
            this.ResampleIntervalNP = new System.Windows.Forms.NumericUpDown();
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
            this.DetectLimitsBTN = new System.Windows.Forms.Button();
            this.GoBTN = new System.Windows.Forms.Button();
            this.StopBTN = new System.Windows.Forms.Button();
            this.SubscribeBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.RightPN = new System.Windows.Forms.Panel();
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).BeginInit();
            this.LeftPN.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ResampleIntervalNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).BeginInit();
            this.panel1.SuspendLayout();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ViewDetailsMI,
            this.DeleteHistoryMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(68, 48);
            // 
            // ViewDetailsMI
            // 
            this.ViewDetailsMI.Name = "ViewDetailsMI";
            this.ViewDetailsMI.Size = new System.Drawing.Size(67, 22);
            // 
            // DeleteHistoryMI
            // 
            this.DeleteHistoryMI.Name = "DeleteHistoryMI";
            this.DeleteHistoryMI.Size = new System.Drawing.Size(67, 22);
            // 
            // ResultsDV
            // 
            this.ResultsDV.AllowUserToAddRows = false;
            this.ResultsDV.AllowUserToDeleteRows = false;
            this.ResultsDV.AllowUserToOrderColumns = true;
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
            this.UserNameCH});
            this.ResultsDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsDV.Location = new System.Drawing.Point(0, 0);
            this.ResultsDV.Name = "ResultsDV";
            this.ResultsDV.Size = new System.Drawing.Size(655, 570);
            this.ResultsDV.TabIndex = 1;
            // 
            // SourceTimestampCH
            // 
            this.SourceTimestampCH.DataPropertyName = "SourceTimestamp";
            this.SourceTimestampCH.HeaderText = "SourceTimestamp";
            this.SourceTimestampCH.Name = "SourceTimestampCH";
            this.SourceTimestampCH.ReadOnly = true;
            // 
            // ServerTimestampCH
            // 
            this.ServerTimestampCH.DataPropertyName = "ServerTimestamp";
            this.ServerTimestampCH.HeaderText = "ServerTimestamp";
            this.ServerTimestampCH.Name = "ServerTimestampCH";
            this.ServerTimestampCH.ReadOnly = true;
            // 
            // ValueCH
            // 
            this.ValueCH.DataPropertyName = "Value";
            this.ValueCH.HeaderText = "Value";
            this.ValueCH.Name = "ValueCH";
            this.ValueCH.ReadOnly = true;
            // 
            // StatusCodeCH
            // 
            this.StatusCodeCH.DataPropertyName = "StatusCode";
            this.StatusCodeCH.HeaderText = "StatusCode";
            this.StatusCodeCH.Name = "StatusCodeCH";
            this.StatusCodeCH.ReadOnly = true;
            // 
            // HistoryInfoCH
            // 
            this.HistoryInfoCH.DataPropertyName = "HistoryInfo";
            this.HistoryInfoCH.HeaderText = "HistoryInfo";
            this.HistoryInfoCH.Name = "HistoryInfoCH";
            // 
            // UpdateTypeCH
            // 
            this.UpdateTypeCH.DataPropertyName = "UpdateType";
            this.UpdateTypeCH.HeaderText = "UpdateType";
            this.UpdateTypeCH.Name = "UpdateTypeCH";
            this.UpdateTypeCH.ReadOnly = true;
            this.UpdateTypeCH.Visible = false;
            // 
            // UpdateTimeCH
            // 
            this.UpdateTimeCH.DataPropertyName = "UpdateTime";
            this.UpdateTimeCH.HeaderText = "UpdateTime";
            this.UpdateTimeCH.Name = "UpdateTimeCH";
            this.UpdateTimeCH.ReadOnly = true;
            this.UpdateTimeCH.Visible = false;
            // 
            // UserNameCH
            // 
            this.UserNameCH.DataPropertyName = "UserName";
            this.UserNameCH.HeaderText = "UserName";
            this.UserNameCH.Name = "UserNameCH";
            this.UserNameCH.ReadOnly = true;
            this.UserNameCH.Visible = false;
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.tableLayoutPanel1);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(306, 570);
            this.LeftPN.TabIndex = 2;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 3;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 115F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.SamplingIntervalUnitsLB, 2, 2);
            this.tableLayoutPanel1.Controls.Add(this.TimeStepUnitsLB, 2, 9);
            this.tableLayoutPanel1.Controls.Add(this.SamplingIntervalNP, 1, 2);
            this.tableLayoutPanel1.Controls.Add(this.TimeStepNP, 1, 9);
            this.tableLayoutPanel1.Controls.Add(this.SamplingIntervalLB, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.TimeStepLB, 0, 9);
            this.tableLayoutPanel1.Controls.Add(this.NodeIdBTN, 2, 0);
            this.tableLayoutPanel1.Controls.Add(this.NodeIdTB, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.StartTimeCK, 2, 3);
            this.tableLayoutPanel1.Controls.Add(this.NodeIdLB, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.ReturnBoundsLB, 0, 6);
            this.tableLayoutPanel1.Controls.Add(this.ResampleIntervalLB, 0, 8);
            this.tableLayoutPanel1.Controls.Add(this.ReadTypeCB, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.EndTimeLB, 0, 4);
            this.tableLayoutPanel1.Controls.Add(this.ResampleIntervalNP, 1, 8);
            this.tableLayoutPanel1.Controls.Add(this.ReadTypeLB, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.AggregateCB, 1, 7);
            this.tableLayoutPanel1.Controls.Add(this.StartTimeLB, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.StartTimeDP, 1, 3);
            this.tableLayoutPanel1.Controls.Add(this.EndTimeDP, 1, 4);
            this.tableLayoutPanel1.Controls.Add(this.EndTimeCK, 2, 4);
            this.tableLayoutPanel1.Controls.Add(this.AggregateLB, 0, 7);
            this.tableLayoutPanel1.Controls.Add(this.MaxReturnValuesLB, 0, 5);
            this.tableLayoutPanel1.Controls.Add(this.MaxReturnValuesNP, 1, 5);
            this.tableLayoutPanel1.Controls.Add(this.MaxReturnValuesCK, 2, 5);
            this.tableLayoutPanel1.Controls.Add(this.ReturnBoundsCK, 1, 6);
            this.tableLayoutPanel1.Controls.Add(this.ResampleIntervalUnitsLB, 2, 8);
            this.tableLayoutPanel1.Controls.Add(this.StatusTB, 0, 11);
            this.tableLayoutPanel1.Controls.Add(this.panel1, 0, 10);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 12;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 57F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 22F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(306, 389);
            this.tableLayoutPanel1.TabIndex = 43;
            // 
            // SamplingIntervalUnitsLB
            // 
            this.SamplingIntervalUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.SamplingIntervalUnitsLB.AutoSize = true;
            this.SamplingIntervalUnitsLB.Location = new System.Drawing.Point(262, 54);
            this.SamplingIntervalUnitsLB.Name = "SamplingIntervalUnitsLB";
            this.SamplingIntervalUnitsLB.Size = new System.Drawing.Size(20, 26);
            this.SamplingIntervalUnitsLB.TabIndex = 50;
            this.SamplingIntervalUnitsLB.Text = "ms";
            this.SamplingIntervalUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TimeStepUnitsLB
            // 
            this.TimeStepUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepUnitsLB.AutoSize = true;
            this.TimeStepUnitsLB.Location = new System.Drawing.Point(262, 231);
            this.TimeStepUnitsLB.Name = "TimeStepUnitsLB";
            this.TimeStepUnitsLB.Size = new System.Drawing.Size(20, 26);
            this.TimeStepUnitsLB.TabIndex = 45;
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
            this.SamplingIntervalNP.Location = new System.Drawing.Point(118, 57);
            this.SamplingIntervalNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.SamplingIntervalNP.Name = "SamplingIntervalNP";
            this.SamplingIntervalNP.Size = new System.Drawing.Size(138, 20);
            this.SamplingIntervalNP.TabIndex = 49;
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
            this.TimeStepNP.Location = new System.Drawing.Point(118, 234);
            this.TimeStepNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.TimeStepNP.Name = "TimeStepNP";
            this.TimeStepNP.Size = new System.Drawing.Size(138, 20);
            this.TimeStepNP.TabIndex = 44;
            this.TimeStepNP.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // SamplingIntervalLB
            // 
            this.SamplingIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.SamplingIntervalLB.AutoSize = true;
            this.SamplingIntervalLB.Location = new System.Drawing.Point(3, 54);
            this.SamplingIntervalLB.Name = "SamplingIntervalLB";
            this.SamplingIntervalLB.Size = new System.Drawing.Size(88, 26);
            this.SamplingIntervalLB.TabIndex = 48;
            this.SamplingIntervalLB.Text = "Sampling Interval";
            this.SamplingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TimeStepLB
            // 
            this.TimeStepLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepLB.AutoSize = true;
            this.TimeStepLB.Location = new System.Drawing.Point(3, 231);
            this.TimeStepLB.Name = "TimeStepLB";
            this.TimeStepLB.Size = new System.Drawing.Size(55, 26);
            this.TimeStepLB.TabIndex = 44;
            this.TimeStepLB.Text = "Time Step";
            this.TimeStepLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeIdBTN
            // 
            this.NodeIdBTN.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdBTN.Location = new System.Drawing.Point(262, 3);
            this.NodeIdBTN.Name = "NodeIdBTN";
            this.NodeIdBTN.Size = new System.Drawing.Size(24, 21);
            this.NodeIdBTN.TabIndex = 42;
            this.NodeIdBTN.Text = "...";
            this.NodeIdBTN.UseVisualStyleBackColor = true;
            this.NodeIdBTN.Click += new System.EventHandler(this.NodeIdBTN_Click);
            // 
            // NodeIdTB
            // 
            this.NodeIdTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdTB.Location = new System.Drawing.Point(118, 3);
            this.NodeIdTB.Name = "NodeIdTB";
            this.NodeIdTB.Size = new System.Drawing.Size(138, 20);
            this.NodeIdTB.TabIndex = 41;
            // 
            // StartTimeCK
            // 
            this.StartTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeCK.AutoSize = true;
            this.StartTimeCK.Location = new System.Drawing.Point(262, 83);
            this.StartTimeCK.Name = "StartTimeCK";
            this.StartTimeCK.Size = new System.Drawing.Size(15, 20);
            this.StartTimeCK.TabIndex = 31;
            this.StartTimeCK.UseVisualStyleBackColor = true;
            this.StartTimeCK.CheckedChanged += new System.EventHandler(this.StartTimeCK_CheckedChanged);
            // 
            // NodeIdLB
            // 
            this.NodeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdLB.AutoSize = true;
            this.NodeIdLB.Location = new System.Drawing.Point(3, 0);
            this.NodeIdLB.Name = "NodeIdLB";
            this.NodeIdLB.Size = new System.Drawing.Size(45, 27);
            this.NodeIdLB.TabIndex = 40;
            this.NodeIdLB.Text = "Variable";
            this.NodeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReturnBoundsLB
            // 
            this.ReturnBoundsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ReturnBoundsLB.AutoSize = true;
            this.ReturnBoundsLB.Location = new System.Drawing.Point(3, 158);
            this.ReturnBoundsLB.Name = "ReturnBoundsLB";
            this.ReturnBoundsLB.Size = new System.Drawing.Size(78, 20);
            this.ReturnBoundsLB.TabIndex = 28;
            this.ReturnBoundsLB.Text = "Return Bounds";
            this.ReturnBoundsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.ReturnBoundsLB.Visible = false;
            // 
            // ResampleIntervalLB
            // 
            this.ResampleIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ResampleIntervalLB.AutoSize = true;
            this.ResampleIntervalLB.Location = new System.Drawing.Point(3, 205);
            this.ResampleIntervalLB.Name = "ResampleIntervalLB";
            this.ResampleIntervalLB.Size = new System.Drawing.Size(97, 26);
            this.ResampleIntervalLB.TabIndex = 36;
            this.ResampleIntervalLB.Text = "Processing Interval";
            this.ResampleIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReadTypeCB
            // 
            this.ReadTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ReadTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ReadTypeCB.FormattingEnabled = true;
            this.ReadTypeCB.Location = new System.Drawing.Point(118, 30);
            this.ReadTypeCB.Name = "ReadTypeCB";
            this.ReadTypeCB.Size = new System.Drawing.Size(138, 21);
            this.ReadTypeCB.TabIndex = 21;
            this.ReadTypeCB.SelectedIndexChanged += new System.EventHandler(this.ReadTypeCB_SelectedIndexChanged);
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(3, 106);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(52, 26);
            this.EndTimeLB.TabIndex = 24;
            this.EndTimeLB.Text = "End Time";
            this.EndTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ResampleIntervalNP
            // 
            this.ResampleIntervalNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ResampleIntervalNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.ResampleIntervalNP.Location = new System.Drawing.Point(118, 208);
            this.ResampleIntervalNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.ResampleIntervalNP.Name = "ResampleIntervalNP";
            this.ResampleIntervalNP.Size = new System.Drawing.Size(138, 20);
            this.ResampleIntervalNP.TabIndex = 35;
            this.ResampleIntervalNP.Value = new decimal(new int[] {
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
            this.ReadTypeLB.Location = new System.Drawing.Point(3, 27);
            this.ReadTypeLB.Name = "ReadTypeLB";
            this.ReadTypeLB.Size = new System.Drawing.Size(60, 27);
            this.ReadTypeLB.TabIndex = 20;
            this.ReadTypeLB.Text = "Read Type";
            this.ReadTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AggregateCB
            // 
            this.AggregateCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.AggregateCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AggregateCB.FormattingEnabled = true;
            this.AggregateCB.Location = new System.Drawing.Point(118, 181);
            this.AggregateCB.Name = "AggregateCB";
            this.AggregateCB.Size = new System.Drawing.Size(138, 21);
            this.AggregateCB.TabIndex = 34;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(3, 80);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 26);
            this.StartTimeLB.TabIndex = 22;
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
            this.StartTimeDP.Location = new System.Drawing.Point(118, 83);
            this.StartTimeDP.Name = "StartTimeDP";
            this.StartTimeDP.Size = new System.Drawing.Size(138, 20);
            this.StartTimeDP.TabIndex = 23;
            this.StartTimeDP.ValueChanged += new System.EventHandler(this.StartTimeDP_ValueChanged);
            // 
            // EndTimeDP
            // 
            this.EndTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.EndTimeDP.Enabled = false;
            this.EndTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.EndTimeDP.Location = new System.Drawing.Point(118, 109);
            this.EndTimeDP.Name = "EndTimeDP";
            this.EndTimeDP.Size = new System.Drawing.Size(138, 20);
            this.EndTimeDP.TabIndex = 25;
            this.EndTimeDP.ValueChanged += new System.EventHandler(this.StartTimeDP_ValueChanged);
            // 
            // EndTimeCK
            // 
            this.EndTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeCK.AutoSize = true;
            this.EndTimeCK.Location = new System.Drawing.Point(262, 109);
            this.EndTimeCK.Name = "EndTimeCK";
            this.EndTimeCK.Size = new System.Drawing.Size(15, 20);
            this.EndTimeCK.TabIndex = 32;
            this.EndTimeCK.UseVisualStyleBackColor = true;
            this.EndTimeCK.CheckedChanged += new System.EventHandler(this.EndTimeCK_CheckedChanged);
            // 
            // AggregateLB
            // 
            this.AggregateLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.AggregateLB.AutoSize = true;
            this.AggregateLB.Location = new System.Drawing.Point(3, 178);
            this.AggregateLB.Name = "AggregateLB";
            this.AggregateLB.Size = new System.Drawing.Size(56, 27);
            this.AggregateLB.TabIndex = 30;
            this.AggregateLB.Text = "Aggregate";
            this.AggregateLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxReturnValuesLB
            // 
            this.MaxReturnValuesLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.MaxReturnValuesLB.AutoSize = true;
            this.MaxReturnValuesLB.Location = new System.Drawing.Point(3, 132);
            this.MaxReturnValuesLB.Name = "MaxReturnValuesLB";
            this.MaxReturnValuesLB.Size = new System.Drawing.Size(109, 26);
            this.MaxReturnValuesLB.TabIndex = 27;
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
            this.MaxReturnValuesNP.Location = new System.Drawing.Point(118, 135);
            this.MaxReturnValuesNP.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Name = "MaxReturnValuesNP";
            this.MaxReturnValuesNP.Size = new System.Drawing.Size(138, 20);
            this.MaxReturnValuesNP.TabIndex = 26;
            // 
            // MaxReturnValuesCK
            // 
            this.MaxReturnValuesCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.MaxReturnValuesCK.AutoSize = true;
            this.MaxReturnValuesCK.Location = new System.Drawing.Point(262, 135);
            this.MaxReturnValuesCK.Name = "MaxReturnValuesCK";
            this.MaxReturnValuesCK.Size = new System.Drawing.Size(15, 20);
            this.MaxReturnValuesCK.TabIndex = 33;
            this.MaxReturnValuesCK.UseVisualStyleBackColor = true;
            this.MaxReturnValuesCK.CheckedChanged += new System.EventHandler(this.MaxReturnValuesCK_CheckedChanged);
            // 
            // ReturnBoundsCK
            // 
            this.ReturnBoundsCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ReturnBoundsCK.AutoSize = true;
            this.ReturnBoundsCK.Location = new System.Drawing.Point(118, 161);
            this.ReturnBoundsCK.Name = "ReturnBoundsCK";
            this.ReturnBoundsCK.Size = new System.Drawing.Size(15, 14);
            this.ReturnBoundsCK.TabIndex = 29;
            this.ReturnBoundsCK.UseVisualStyleBackColor = true;
            this.ReturnBoundsCK.Visible = false;
            // 
            // ResampleIntervalUnitsLB
            // 
            this.ResampleIntervalUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ResampleIntervalUnitsLB.AutoSize = true;
            this.ResampleIntervalUnitsLB.Location = new System.Drawing.Point(262, 205);
            this.ResampleIntervalUnitsLB.Name = "ResampleIntervalUnitsLB";
            this.ResampleIntervalUnitsLB.Size = new System.Drawing.Size(20, 26);
            this.ResampleIntervalUnitsLB.TabIndex = 46;
            this.ResampleIntervalUnitsLB.Text = "ms";
            this.ResampleIntervalUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StatusTB
            // 
            this.StatusTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTB.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.tableLayoutPanel1.SetColumnSpan(this.StatusTB, 3);
            this.StatusTB.Location = new System.Drawing.Point(3, 317);
            this.StatusTB.Multiline = true;
            this.StatusTB.Name = "StatusTB";
            this.StatusTB.ReadOnly = true;
            this.StatusTB.Size = new System.Drawing.Size(300, 69);
            this.StatusTB.TabIndex = 47;
            // 
            // panel1
            // 
            this.tableLayoutPanel1.SetColumnSpan(this.panel1, 3);
            this.panel1.Controls.Add(this.DetectLimitsBTN);
            this.panel1.Controls.Add(this.GoBTN);
            this.panel1.Controls.Add(this.StopBTN);
            this.panel1.Controls.Add(this.SubscribeBTN);
            this.panel1.Controls.Add(this.NextBTN);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(3, 260);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(300, 51);
            this.panel1.TabIndex = 43;
            // 
            // DetectLimitsBTN
            // 
            this.DetectLimitsBTN.Location = new System.Drawing.Point(21, 14);
            this.DetectLimitsBTN.Name = "DetectLimitsBTN";
            this.DetectLimitsBTN.Size = new System.Drawing.Size(75, 23);
            this.DetectLimitsBTN.TabIndex = 41;
            this.DetectLimitsBTN.Text = "Auto Detect";
            this.DetectLimitsBTN.UseVisualStyleBackColor = true;
            this.DetectLimitsBTN.Click += new System.EventHandler(this.DetectLimitsBTN_Click);
            // 
            // GoBTN
            // 
            this.GoBTN.Location = new System.Drawing.Point(113, 14);
            this.GoBTN.Name = "GoBTN";
            this.GoBTN.Size = new System.Drawing.Size(75, 23);
            this.GoBTN.TabIndex = 37;
            this.GoBTN.Text = "Go";
            this.GoBTN.UseVisualStyleBackColor = true;
            this.GoBTN.Click += new System.EventHandler(this.GoBTN_Click);
            // 
            // StopBTN
            // 
            this.StopBTN.Location = new System.Drawing.Point(208, 14);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(75, 23);
            this.StopBTN.TabIndex = 39;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.StopBTN_Click);
            // 
            // SubscribeBTN
            // 
            this.SubscribeBTN.Location = new System.Drawing.Point(113, 14);
            this.SubscribeBTN.Name = "SubscribeBTN";
            this.SubscribeBTN.Size = new System.Drawing.Size(75, 23);
            this.SubscribeBTN.TabIndex = 40;
            this.SubscribeBTN.Text = "Subscribe";
            this.SubscribeBTN.UseVisualStyleBackColor = true;
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(113, 14);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 38;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.ResultsDV);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(306, 0);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(655, 570);
            this.RightPN.TabIndex = 3;
            // 
            // HistoryDataListView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Controls.Add(this.LeftPN);
            this.Name = "HistoryDataListView";
            this.Size = new System.Drawing.Size(961, 570);
            this.PopupMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).EndInit();
            this.LeftPN.ResumeLayout(false);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ResampleIntervalNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).EndInit();
            this.panel1.ResumeLayout(false);
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem ViewDetailsMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteHistoryMI;
        private System.Windows.Forms.DataGridView ResultsDV;
        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.Button NodeIdBTN;
        private System.Windows.Forms.TextBox NodeIdTB;
        private System.Windows.Forms.Label NodeIdLB;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button GoBTN;
        private System.Windows.Forms.Label ResampleIntervalLB;
        private System.Windows.Forms.NumericUpDown ResampleIntervalNP;
        private System.Windows.Forms.ComboBox AggregateCB;
        private System.Windows.Forms.CheckBox MaxReturnValuesCK;
        private System.Windows.Forms.CheckBox EndTimeCK;
        private System.Windows.Forms.CheckBox StartTimeCK;
        private System.Windows.Forms.Label AggregateLB;
        private System.Windows.Forms.CheckBox ReturnBoundsCK;
        private System.Windows.Forms.Label ReturnBoundsLB;
        private System.Windows.Forms.Label MaxReturnValuesLB;
        private System.Windows.Forms.NumericUpDown MaxReturnValuesNP;
        private System.Windows.Forms.DateTimePicker EndTimeDP;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.DateTimePicker StartTimeDP;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.ComboBox ReadTypeCB;
        private System.Windows.Forms.Label ReadTypeLB;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label TimeStepUnitsLB;
        private System.Windows.Forms.NumericUpDown TimeStepNP;
        private System.Windows.Forms.Label TimeStepLB;
        private System.Windows.Forms.Label ResampleIntervalUnitsLB;
        private System.Windows.Forms.Button SubscribeBTN;
        private System.Windows.Forms.DataGridViewTextBoxColumn SourceTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusCodeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn HistoryInfoCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UpdateTypeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UpdateTimeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn UserNameCH;
        private System.Windows.Forms.TextBox StatusTB;
        private System.Windows.Forms.Label SamplingIntervalUnitsLB;
        private System.Windows.Forms.NumericUpDown SamplingIntervalNP;
        private System.Windows.Forms.Label SamplingIntervalLB;
        private System.Windows.Forms.Button DetectLimitsBTN;
    }
}
