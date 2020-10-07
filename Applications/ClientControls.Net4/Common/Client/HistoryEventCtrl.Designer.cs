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
    partial class HistoryEventCtrl
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
            this.LeftPN = new System.Windows.Forms.Panel();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.panel1 = new System.Windows.Forms.Panel();
            this.StopBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.TimeShiftBTN = new System.Windows.Forms.Button();
            this.GoBTN = new System.Windows.Forms.Button();
            this.DetectLimitsBTN = new System.Windows.Forms.Button();
            this.StatusTB = new System.Windows.Forms.TextBox();
            this.TimeStepUnitsLB = new System.Windows.Forms.Label();
            this.TimeStepNP = new System.Windows.Forms.NumericUpDown();
            this.TimeStepLB = new System.Windows.Forms.Label();
            this.NodeIdBTN = new System.Windows.Forms.Button();
            this.NodeIdTB = new System.Windows.Forms.TextBox();
            this.StartTimeCK = new System.Windows.Forms.CheckBox();
            this.NodeIdLB = new System.Windows.Forms.Label();
            this.ReadTypeCB = new System.Windows.Forms.ComboBox();
            this.EndTimeLB = new System.Windows.Forms.Label();
            this.ReadTypeLB = new System.Windows.Forms.Label();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.StartTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeCK = new System.Windows.Forms.CheckBox();
            this.RightPN = new System.Windows.Forms.Panel();
            this.EventsCTRL = new Opc.Ua.Client.Controls.EventListViewCtrl();
            this.FilterLB = new System.Windows.Forms.Label();
            this.FilterTB = new System.Windows.Forms.TextBox();
            this.FilterBTN = new System.Windows.Forms.Button();
            this.LeftPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).BeginInit();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.ControlsPN);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(306, 400);
            this.LeftPN.TabIndex = 2;
            // 
            // ControlsPN
            // 
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 115F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.FilterTB, 1, 4);
            this.ControlsPN.Controls.Add(this.FilterLB, 0, 4);
            this.ControlsPN.Controls.Add(this.TimeStepUnitsLB, 2, 5);
            this.ControlsPN.Controls.Add(this.TimeStepNP, 1, 5);
            this.ControlsPN.Controls.Add(this.TimeStepLB, 0, 5);
            this.ControlsPN.Controls.Add(this.NodeIdBTN, 2, 0);
            this.ControlsPN.Controls.Add(this.NodeIdTB, 1, 0);
            this.ControlsPN.Controls.Add(this.StartTimeCK, 2, 2);
            this.ControlsPN.Controls.Add(this.NodeIdLB, 0, 0);
            this.ControlsPN.Controls.Add(this.ReadTypeCB, 1, 1);
            this.ControlsPN.Controls.Add(this.EndTimeLB, 0, 3);
            this.ControlsPN.Controls.Add(this.ReadTypeLB, 0, 1);
            this.ControlsPN.Controls.Add(this.StartTimeLB, 0, 2);
            this.ControlsPN.Controls.Add(this.StartTimeDP, 1, 2);
            this.ControlsPN.Controls.Add(this.EndTimeDP, 1, 3);
            this.ControlsPN.Controls.Add(this.EndTimeCK, 2, 3);
            this.ControlsPN.Controls.Add(this.StatusTB, 0, 7);
            this.ControlsPN.Controls.Add(this.panel1, 0, 6);
            this.ControlsPN.Controls.Add(this.FilterBTN, 2, 4);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 8;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 57F));
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 22F));
            this.ControlsPN.Size = new System.Drawing.Size(306, 389);
            this.ControlsPN.TabIndex = 0;
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
            this.panel1.Location = new System.Drawing.Point(3, 160);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(300, 51);
            this.panel1.TabIndex = 33;
            // 
            // StopBTN
            // 
            this.StopBTN.Location = new System.Drawing.Point(208, 14);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(75, 23);
            this.StopBTN.TabIndex = 2;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(113, 14);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 1;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.NextBTN_Click);
            // 
            // TimeShiftBTN
            // 
            this.TimeShiftBTN.Location = new System.Drawing.Point(21, 14);
            this.TimeShiftBTN.Name = "TimeShiftBTN";
            this.TimeShiftBTN.Size = new System.Drawing.Size(75, 23);
            this.TimeShiftBTN.TabIndex = 0;
            this.TimeShiftBTN.Text = "Time Shift";
            this.TimeShiftBTN.UseVisualStyleBackColor = true;
            this.TimeShiftBTN.Visible = false;
            this.TimeShiftBTN.Click += new System.EventHandler(this.TimeShiftBTN_Click);
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
            // DetectLimitsBTN
            // 
            this.DetectLimitsBTN.Location = new System.Drawing.Point(21, 14);
            this.DetectLimitsBTN.Name = "DetectLimitsBTN";
            this.DetectLimitsBTN.Size = new System.Drawing.Size(75, 23);
            this.DetectLimitsBTN.TabIndex = 41;
            this.DetectLimitsBTN.Text = "Auto Detect";
            this.DetectLimitsBTN.UseVisualStyleBackColor = true;
            // 
            // StatusTB
            // 
            this.StatusTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTB.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.ControlsPN.SetColumnSpan(this.StatusTB, 3);
            this.StatusTB.Location = new System.Drawing.Point(3, 217);
            this.StatusTB.Multiline = true;
            this.StatusTB.Name = "StatusTB";
            this.StatusTB.ReadOnly = true;
            this.StatusTB.Size = new System.Drawing.Size(300, 169);
            this.StatusTB.TabIndex = 34;
            // 
            // TimeStepUnitsLB
            // 
            this.TimeStepUnitsLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepUnitsLB.AutoSize = true;
            this.TimeStepUnitsLB.Location = new System.Drawing.Point(262, 131);
            this.TimeStepUnitsLB.Name = "TimeStepUnitsLB";
            this.TimeStepUnitsLB.Size = new System.Drawing.Size(20, 26);
            this.TimeStepUnitsLB.TabIndex = 32;
            this.TimeStepUnitsLB.Text = "ms";
            this.TimeStepUnitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
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
            this.TimeStepNP.Location = new System.Drawing.Point(118, 134);
            this.TimeStepNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.TimeStepNP.Name = "TimeStepNP";
            this.TimeStepNP.Size = new System.Drawing.Size(138, 20);
            this.TimeStepNP.TabIndex = 31;
            this.TimeStepNP.Value = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            // 
            // TimeStepLB
            // 
            this.TimeStepLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.TimeStepLB.AutoSize = true;
            this.TimeStepLB.Location = new System.Drawing.Point(3, 131);
            this.TimeStepLB.Name = "TimeStepLB";
            this.TimeStepLB.Size = new System.Drawing.Size(55, 26);
            this.TimeStepLB.TabIndex = 30;
            this.TimeStepLB.Text = "Time Step";
            this.TimeStepLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeIdBTN
            // 
            this.NodeIdBTN.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeIdBTN.Location = new System.Drawing.Point(262, 1);
            this.NodeIdBTN.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.NodeIdBTN.Name = "NodeIdBTN";
            this.NodeIdBTN.Size = new System.Drawing.Size(24, 24);
            this.NodeIdBTN.TabIndex = 4;
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
            this.NodeIdTB.TabIndex = 3;
            // 
            // StartTimeCK
            // 
            this.StartTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeCK.AutoSize = true;
            this.StartTimeCK.Location = new System.Drawing.Point(262, 56);
            this.StartTimeCK.Name = "StartTimeCK";
            this.StartTimeCK.Size = new System.Drawing.Size(15, 20);
            this.StartTimeCK.TabIndex = 14;
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
            this.NodeIdLB.Size = new System.Drawing.Size(40, 26);
            this.NodeIdLB.TabIndex = 2;
            this.NodeIdLB.Text = "Notifier";
            this.NodeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReadTypeCB
            // 
            this.ReadTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ReadTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ReadTypeCB.FormattingEnabled = true;
            this.ReadTypeCB.Location = new System.Drawing.Point(118, 29);
            this.ReadTypeCB.Name = "ReadTypeCB";
            this.ReadTypeCB.Size = new System.Drawing.Size(138, 21);
            this.ReadTypeCB.TabIndex = 8;
            this.ReadTypeCB.SelectedIndexChanged += new System.EventHandler(this.ReadTypeCB_SelectedIndexChanged);
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(3, 79);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(52, 26);
            this.EndTimeLB.TabIndex = 15;
            this.EndTimeLB.Text = "End Time";
            this.EndTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReadTypeLB
            // 
            this.ReadTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ReadTypeLB.AutoSize = true;
            this.ReadTypeLB.Location = new System.Drawing.Point(3, 26);
            this.ReadTypeLB.Name = "ReadTypeLB";
            this.ReadTypeLB.Size = new System.Drawing.Size(60, 27);
            this.ReadTypeLB.TabIndex = 7;
            this.ReadTypeLB.Text = "Read Type";
            this.ReadTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(3, 53);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 26);
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
            this.StartTimeDP.Location = new System.Drawing.Point(118, 56);
            this.StartTimeDP.Name = "StartTimeDP";
            this.StartTimeDP.Size = new System.Drawing.Size(138, 20);
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
            this.EndTimeDP.Location = new System.Drawing.Point(118, 82);
            this.EndTimeDP.Name = "EndTimeDP";
            this.EndTimeDP.Size = new System.Drawing.Size(138, 20);
            this.EndTimeDP.TabIndex = 16;
            this.EndTimeDP.ValueChanged += new System.EventHandler(this.StartTimeDP_ValueChanged);
            // 
            // EndTimeCK
            // 
            this.EndTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.EndTimeCK.AutoSize = true;
            this.EndTimeCK.Location = new System.Drawing.Point(262, 82);
            this.EndTimeCK.Name = "EndTimeCK";
            this.EndTimeCK.Size = new System.Drawing.Size(15, 20);
            this.EndTimeCK.TabIndex = 17;
            this.EndTimeCK.UseVisualStyleBackColor = true;
            this.EndTimeCK.CheckedChanged += new System.EventHandler(this.EndTimeCK_CheckedChanged);
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.EventsCTRL);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(306, 0);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(494, 400);
            this.RightPN.TabIndex = 3;
            // 
            // EventsCTRL
            // 
            this.EventsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventsCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventsCTRL.Name = "EventsCTRL";
            this.EventsCTRL.Size = new System.Drawing.Size(494, 400);
            this.EventsCTRL.TabIndex = 0;
            // 
            // FilterLB
            // 
            this.FilterLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.FilterLB.AutoSize = true;
            this.FilterLB.Location = new System.Drawing.Point(3, 105);
            this.FilterLB.Name = "FilterLB";
            this.FilterLB.Size = new System.Drawing.Size(29, 26);
            this.FilterLB.TabIndex = 35;
            this.FilterLB.Text = "Filter";
            this.FilterLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // FilterTB
            // 
            this.FilterTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.FilterTB.Location = new System.Drawing.Point(118, 108);
            this.FilterTB.Name = "FilterTB";
            this.FilterTB.Size = new System.Drawing.Size(138, 20);
            this.FilterTB.TabIndex = 36;
            // 
            // FilterBTN
            // 
            this.FilterBTN.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.FilterBTN.Location = new System.Drawing.Point(262, 106);
            this.FilterBTN.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.FilterBTN.Name = "FilterBTN";
            this.FilterBTN.Size = new System.Drawing.Size(24, 24);
            this.FilterBTN.TabIndex = 37;
            this.FilterBTN.Text = "...";
            this.FilterBTN.UseVisualStyleBackColor = true;
            // 
            // HistoryEventCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Controls.Add(this.LeftPN);
            this.Name = "HistoryEventCtrl";
            this.Size = new System.Drawing.Size(800, 400);
            this.LeftPN.ResumeLayout(false);
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.TimeStepNP)).EndInit();
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.Label TimeStepUnitsLB;
        private System.Windows.Forms.NumericUpDown TimeStepNP;
        private System.Windows.Forms.Label TimeStepLB;
        private System.Windows.Forms.Button NodeIdBTN;
        private System.Windows.Forms.TextBox NodeIdTB;
        private System.Windows.Forms.CheckBox StartTimeCK;
        private System.Windows.Forms.Label NodeIdLB;
        private System.Windows.Forms.ComboBox ReadTypeCB;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.Label ReadTypeLB;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.DateTimePicker StartTimeDP;
        private System.Windows.Forms.DateTimePicker EndTimeDP;
        private System.Windows.Forms.CheckBox EndTimeCK;
        private System.Windows.Forms.TextBox StatusTB;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button TimeShiftBTN;
        private System.Windows.Forms.Button GoBTN;
        private System.Windows.Forms.Button DetectLimitsBTN;
        private EventListViewCtrl EventsCTRL;
        private System.Windows.Forms.TextBox FilterTB;
        private System.Windows.Forms.Label FilterLB;
        private System.Windows.Forms.Button FilterBTN;
    }
}
