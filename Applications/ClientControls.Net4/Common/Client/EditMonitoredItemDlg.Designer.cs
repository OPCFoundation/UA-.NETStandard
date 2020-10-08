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
    partial class EditMonitoredItemDlg
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
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.TriggerTypeCB = new System.Windows.Forms.ComboBox();
            this.TriggerTypeLB = new System.Windows.Forms.Label();
            this.DeadbandValueLB = new System.Windows.Forms.Label();
            this.DeadbandValueUP = new System.Windows.Forms.NumericUpDown();
            this.DeadbandTypeCB = new System.Windows.Forms.ComboBox();
            this.MonitoringModeCB = new System.Windows.Forms.ComboBox();
            this.MonitoringModeLB = new System.Windows.Forms.Label();
            this.DeadbandTypeLB = new System.Windows.Forms.Label();
            this.DiscardOldestLB = new System.Windows.Forms.Label();
            this.QueueSizeLB = new System.Windows.Forms.Label();
            this.SamplingIntervalLB = new System.Windows.Forms.Label();
            this.NodeLB = new System.Windows.Forms.Label();
            this.IndexRangeTB = new System.Windows.Forms.TextBox();
            this.AttributeCB = new System.Windows.Forms.ComboBox();
            this.AttributeLB = new System.Windows.Forms.Label();
            this.IndexRangeLB = new System.Windows.Forms.Label();
            this.DataEncodingLB = new System.Windows.Forms.Label();
            this.DataEncodingCB = new System.Windows.Forms.ComboBox();
            this.NodeTB = new System.Windows.Forms.TextBox();
            this.NodeBTN = new Opc.Ua.Client.Controls.SelectNodeCtrl();
            this.QueueSizeUP = new System.Windows.Forms.NumericUpDown();
            this.SamplingIntervalUP = new System.Windows.Forms.NumericUpDown();
            this.DiscardOldestCK = new System.Windows.Forms.CheckBox();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DeadbandValueUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.QueueSizeUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalUP)).BeginInit();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(293, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 285);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(371, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // MainPN
            // 
            this.MainPN.AutoSize = true;
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ControlsPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(371, 285);
            this.MainPN.TabIndex = 1;
            // 
            // ControlsPN
            // 
            this.ControlsPN.AutoSize = true;
            this.ControlsPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.TriggerTypeCB, 1, 10);
            this.ControlsPN.Controls.Add(this.TriggerTypeLB, 0, 10);
            this.ControlsPN.Controls.Add(this.DeadbandValueLB, 0, 9);
            this.ControlsPN.Controls.Add(this.DeadbandValueUP, 1, 9);
            this.ControlsPN.Controls.Add(this.DeadbandTypeCB, 1, 8);
            this.ControlsPN.Controls.Add(this.MonitoringModeCB, 1, 4);
            this.ControlsPN.Controls.Add(this.MonitoringModeLB, 0, 4);
            this.ControlsPN.Controls.Add(this.DeadbandTypeLB, 0, 8);
            this.ControlsPN.Controls.Add(this.DiscardOldestLB, 0, 7);
            this.ControlsPN.Controls.Add(this.QueueSizeLB, 0, 6);
            this.ControlsPN.Controls.Add(this.SamplingIntervalLB, 0, 5);
            this.ControlsPN.Controls.Add(this.NodeLB, 0, 0);
            this.ControlsPN.Controls.Add(this.IndexRangeTB, 1, 2);
            this.ControlsPN.Controls.Add(this.AttributeCB, 1, 1);
            this.ControlsPN.Controls.Add(this.AttributeLB, 0, 1);
            this.ControlsPN.Controls.Add(this.IndexRangeLB, 0, 2);
            this.ControlsPN.Controls.Add(this.DataEncodingLB, 0, 3);
            this.ControlsPN.Controls.Add(this.DataEncodingCB, 1, 3);
            this.ControlsPN.Controls.Add(this.NodeTB, 1, 0);
            this.ControlsPN.Controls.Add(this.NodeBTN, 2, 0);
            this.ControlsPN.Controls.Add(this.QueueSizeUP, 1, 6);
            this.ControlsPN.Controls.Add(this.SamplingIntervalUP, 1, 5);
            this.ControlsPN.Controls.Add(this.DiscardOldestCK, 1, 7);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 12;
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
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ControlsPN.Size = new System.Drawing.Size(371, 285);
            this.ControlsPN.TabIndex = 0;
            // 
            // TriggerTypeCB
            // 
            this.TriggerTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.TriggerTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TriggerTypeCB.FormattingEnabled = true;
            this.TriggerTypeCB.Location = new System.Drawing.Point(97, 261);
            this.TriggerTypeCB.Name = "TriggerTypeCB";
            this.TriggerTypeCB.Size = new System.Drawing.Size(138, 21);
            this.TriggerTypeCB.TabIndex = 22;
            // 
            // TriggerTypeLB
            // 
            this.TriggerTypeLB.AutoSize = true;
            this.TriggerTypeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TriggerTypeLB.Location = new System.Drawing.Point(3, 258);
            this.TriggerTypeLB.Name = "TriggerTypeLB";
            this.TriggerTypeLB.Size = new System.Drawing.Size(88, 27);
            this.TriggerTypeLB.TabIndex = 21;
            this.TriggerTypeLB.Text = "Trigger Type";
            this.TriggerTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DeadbandValueLB
            // 
            this.DeadbandValueLB.AutoSize = true;
            this.DeadbandValueLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DeadbandValueLB.Location = new System.Drawing.Point(3, 232);
            this.DeadbandValueLB.Name = "DeadbandValueLB";
            this.DeadbandValueLB.Size = new System.Drawing.Size(88, 26);
            this.DeadbandValueLB.TabIndex = 19;
            this.DeadbandValueLB.Text = "Deadband Value";
            this.DeadbandValueLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DeadbandValueUP
            // 
            this.DeadbandValueUP.Location = new System.Drawing.Point(97, 235);
            this.DeadbandValueUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.DeadbandValueUP.Name = "DeadbandValueUP";
            this.DeadbandValueUP.Size = new System.Drawing.Size(138, 20);
            this.DeadbandValueUP.TabIndex = 20;
            // 
            // DeadbandTypeCB
            // 
            this.DeadbandTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.DeadbandTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DeadbandTypeCB.FormattingEnabled = true;
            this.DeadbandTypeCB.Location = new System.Drawing.Point(97, 208);
            this.DeadbandTypeCB.Name = "DeadbandTypeCB";
            this.DeadbandTypeCB.Size = new System.Drawing.Size(138, 21);
            this.DeadbandTypeCB.TabIndex = 18;
            // 
            // MonitoringModeCB
            // 
            this.MonitoringModeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.MonitoringModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MonitoringModeCB.FormattingEnabled = true;
            this.MonitoringModeCB.Location = new System.Drawing.Point(97, 109);
            this.MonitoringModeCB.Name = "MonitoringModeCB";
            this.MonitoringModeCB.Size = new System.Drawing.Size(138, 21);
            this.MonitoringModeCB.TabIndex = 10;
            // 
            // MonitoringModeLB
            // 
            this.MonitoringModeLB.AutoSize = true;
            this.MonitoringModeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MonitoringModeLB.Location = new System.Drawing.Point(3, 106);
            this.MonitoringModeLB.Name = "MonitoringModeLB";
            this.MonitoringModeLB.Size = new System.Drawing.Size(88, 27);
            this.MonitoringModeLB.TabIndex = 9;
            this.MonitoringModeLB.Text = "Monitoring Mode";
            this.MonitoringModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DeadbandTypeLB
            // 
            this.DeadbandTypeLB.AutoSize = true;
            this.DeadbandTypeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DeadbandTypeLB.Location = new System.Drawing.Point(3, 205);
            this.DeadbandTypeLB.Name = "DeadbandTypeLB";
            this.DeadbandTypeLB.Size = new System.Drawing.Size(88, 27);
            this.DeadbandTypeLB.TabIndex = 17;
            this.DeadbandTypeLB.Text = "Deadband Type";
            this.DeadbandTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DiscardOldestLB
            // 
            this.DiscardOldestLB.AutoSize = true;
            this.DiscardOldestLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscardOldestLB.Location = new System.Drawing.Point(3, 185);
            this.DiscardOldestLB.Name = "DiscardOldestLB";
            this.DiscardOldestLB.Size = new System.Drawing.Size(88, 20);
            this.DiscardOldestLB.TabIndex = 15;
            this.DiscardOldestLB.Text = "Discard Oldest";
            this.DiscardOldestLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // QueueSizeLB
            // 
            this.QueueSizeLB.AutoSize = true;
            this.QueueSizeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.QueueSizeLB.Location = new System.Drawing.Point(3, 159);
            this.QueueSizeLB.Name = "QueueSizeLB";
            this.QueueSizeLB.Size = new System.Drawing.Size(88, 26);
            this.QueueSizeLB.TabIndex = 13;
            this.QueueSizeLB.Text = "Queue Size";
            this.QueueSizeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SamplingIntervalLB
            // 
            this.SamplingIntervalLB.AutoSize = true;
            this.SamplingIntervalLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SamplingIntervalLB.Location = new System.Drawing.Point(3, 133);
            this.SamplingIntervalLB.Name = "SamplingIntervalLB";
            this.SamplingIntervalLB.Size = new System.Drawing.Size(88, 26);
            this.SamplingIntervalLB.TabIndex = 11;
            this.SamplingIntervalLB.Text = "Sampling Interval";
            this.SamplingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeLB
            // 
            this.NodeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeLB.AutoSize = true;
            this.NodeLB.Location = new System.Drawing.Point(3, 0);
            this.NodeLB.Name = "NodeLB";
            this.NodeLB.Size = new System.Drawing.Size(33, 26);
            this.NodeLB.TabIndex = 0;
            this.NodeLB.Text = "Node";
            this.NodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IndexRangeTB
            // 
            this.IndexRangeTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IndexRangeTB.Location = new System.Drawing.Point(97, 56);
            this.IndexRangeTB.Name = "IndexRangeTB";
            this.IndexRangeTB.Size = new System.Drawing.Size(247, 20);
            this.IndexRangeTB.TabIndex = 6;
            // 
            // AttributeCB
            // 
            this.AttributeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AttributeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AttributeCB.FormattingEnabled = true;
            this.AttributeCB.Location = new System.Drawing.Point(97, 29);
            this.AttributeCB.Name = "AttributeCB";
            this.AttributeCB.Size = new System.Drawing.Size(138, 21);
            this.AttributeCB.TabIndex = 4;
            // 
            // AttributeLB
            // 
            this.AttributeLB.AutoSize = true;
            this.AttributeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributeLB.Location = new System.Drawing.Point(3, 26);
            this.AttributeLB.Name = "AttributeLB";
            this.AttributeLB.Size = new System.Drawing.Size(88, 27);
            this.AttributeLB.TabIndex = 3;
            this.AttributeLB.Text = "Attribute";
            this.AttributeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IndexRangeLB
            // 
            this.IndexRangeLB.AutoSize = true;
            this.IndexRangeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IndexRangeLB.Location = new System.Drawing.Point(3, 53);
            this.IndexRangeLB.Name = "IndexRangeLB";
            this.IndexRangeLB.Size = new System.Drawing.Size(88, 26);
            this.IndexRangeLB.TabIndex = 5;
            this.IndexRangeLB.Text = "Index Range";
            this.IndexRangeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DataEncodingLB
            // 
            this.DataEncodingLB.AutoSize = true;
            this.DataEncodingLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataEncodingLB.Location = new System.Drawing.Point(3, 79);
            this.DataEncodingLB.Name = "DataEncodingLB";
            this.DataEncodingLB.Size = new System.Drawing.Size(88, 27);
            this.DataEncodingLB.TabIndex = 7;
            this.DataEncodingLB.Text = "Data Encoding";
            this.DataEncodingLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DataEncodingCB
            // 
            this.DataEncodingCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.DataEncodingCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DataEncodingCB.FormattingEnabled = true;
            this.DataEncodingCB.Location = new System.Drawing.Point(97, 82);
            this.DataEncodingCB.Name = "DataEncodingCB";
            this.DataEncodingCB.Size = new System.Drawing.Size(138, 21);
            this.DataEncodingCB.TabIndex = 8;
            // 
            // NodeTB
            // 
            this.NodeTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodeTB.Location = new System.Drawing.Point(97, 3);
            this.NodeTB.Name = "NodeTB";
            this.NodeTB.ReadOnly = true;
            this.NodeTB.Size = new System.Drawing.Size(247, 20);
            this.NodeTB.TabIndex = 1;
            // 
            // NodeBTN
            // 
            this.NodeBTN.Location = new System.Drawing.Point(347, 0);
            this.NodeBTN.Margin = new System.Windows.Forms.Padding(0);
            this.NodeBTN.Name = "NodeBTN";
            this.NodeBTN.NodeControl = this.NodeTB;
            this.NodeBTN.ReferenceTypeIds = null;
            this.NodeBTN.RootId = null;
            this.NodeBTN.SelectedNode = null;
            this.NodeBTN.SelectedReference = null;
            this.NodeBTN.Session = null;
            this.NodeBTN.Size = new System.Drawing.Size(24, 24);
            this.NodeBTN.TabIndex = 2;
            this.NodeBTN.View = null;
            // 
            // QueueSizeUP
            // 
            this.QueueSizeUP.Location = new System.Drawing.Point(97, 162);
            this.QueueSizeUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.QueueSizeUP.Name = "QueueSizeUP";
            this.QueueSizeUP.Size = new System.Drawing.Size(138, 20);
            this.QueueSizeUP.TabIndex = 14;
            // 
            // SamplingIntervalUP
            // 
            this.SamplingIntervalUP.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.SamplingIntervalUP.Location = new System.Drawing.Point(97, 136);
            this.SamplingIntervalUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.SamplingIntervalUP.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            -2147483648});
            this.SamplingIntervalUP.Name = "SamplingIntervalUP";
            this.SamplingIntervalUP.Size = new System.Drawing.Size(138, 20);
            this.SamplingIntervalUP.TabIndex = 12;
            this.SamplingIntervalUP.ThousandsSeparator = true;
            // 
            // DiscardOldestCK
            // 
            this.DiscardOldestCK.AutoSize = true;
            this.DiscardOldestCK.Checked = true;
            this.DiscardOldestCK.CheckState = System.Windows.Forms.CheckState.Checked;
            this.DiscardOldestCK.Location = new System.Drawing.Point(97, 188);
            this.DiscardOldestCK.Name = "DiscardOldestCK";
            this.DiscardOldestCK.Size = new System.Drawing.Size(15, 14);
            this.DiscardOldestCK.TabIndex = 16;
            this.DiscardOldestCK.UseVisualStyleBackColor = true;
            // 
            // EditMonitoredItemDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(371, 315);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditMonitoredItemDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Monitored Item";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DeadbandValueUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.QueueSizeUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalUP)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.Label NodeLB;
        private System.Windows.Forms.TextBox IndexRangeTB;
        private System.Windows.Forms.ComboBox AttributeCB;
        private System.Windows.Forms.Label AttributeLB;
        private System.Windows.Forms.Label IndexRangeLB;
        private System.Windows.Forms.Label DataEncodingLB;
        private System.Windows.Forms.ComboBox DataEncodingCB;
        private System.Windows.Forms.TextBox NodeTB;
        private SelectNodeCtrl NodeBTN;
        private System.Windows.Forms.ComboBox MonitoringModeCB;
        private System.Windows.Forms.Label MonitoringModeLB;
        private System.Windows.Forms.Label DeadbandTypeLB;
        private System.Windows.Forms.Label DiscardOldestLB;
        private System.Windows.Forms.Label QueueSizeLB;
        private System.Windows.Forms.Label SamplingIntervalLB;
        private System.Windows.Forms.NumericUpDown QueueSizeUP;
        private System.Windows.Forms.NumericUpDown SamplingIntervalUP;
        private System.Windows.Forms.CheckBox DiscardOldestCK;
        private System.Windows.Forms.ComboBox TriggerTypeCB;
        private System.Windows.Forms.Label TriggerTypeLB;
        private System.Windows.Forms.Label DeadbandValueLB;
        private System.Windows.Forms.NumericUpDown DeadbandValueUP;
        private System.Windows.Forms.ComboBox DeadbandTypeCB;
    }
}
