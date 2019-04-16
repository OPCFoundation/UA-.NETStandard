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

namespace Opc.Ua.Sample.Controls
{
    partial class ReadHistoryDlg
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
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.LeftPN = new System.Windows.Forms.Panel();
            this.StopBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.GoBTN = new System.Windows.Forms.Button();
            this.ResampleIntervalLB = new System.Windows.Forms.Label();
            this.ResampleIntervalNP = new System.Windows.Forms.NumericUpDown();
            this.AggregateCB = new System.Windows.Forms.ComboBox();
            this.MaxReturnValuesCK = new System.Windows.Forms.CheckBox();
            this.EndTimeCK = new System.Windows.Forms.CheckBox();
            this.StartTimeCK = new System.Windows.Forms.CheckBox();
            this.AggregateLB = new System.Windows.Forms.Label();
            this.ReturnBoundsCK = new System.Windows.Forms.CheckBox();
            this.ReturnBoundsLB = new System.Windows.Forms.Label();
            this.MaxReturnValuesLB = new System.Windows.Forms.Label();
            this.MaxReturnValuesNP = new System.Windows.Forms.NumericUpDown();
            this.EndTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeLB = new System.Windows.Forms.Label();
            this.StartTimeDP = new System.Windows.Forms.DateTimePicker();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.ReadTypeCB = new System.Windows.Forms.ComboBox();
            this.ReadTypeLB = new System.Windows.Forms.Label();
            this.ResultsLV = new System.Windows.Forms.ListView();
            this.IndexCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.TimestampCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.ValueCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.QualityCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.HistoryInfoCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.LeftPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResampleIntervalNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 439);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(855, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(776, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.LeftPN);
            this.MainPN.Controls.Add(this.ResultsLV);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(855, 439);
            this.MainPN.TabIndex = 1;
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.StopBTN);
            this.LeftPN.Controls.Add(this.NextBTN);
            this.LeftPN.Controls.Add(this.GoBTN);
            this.LeftPN.Controls.Add(this.ResampleIntervalLB);
            this.LeftPN.Controls.Add(this.ResampleIntervalNP);
            this.LeftPN.Controls.Add(this.AggregateCB);
            this.LeftPN.Controls.Add(this.MaxReturnValuesCK);
            this.LeftPN.Controls.Add(this.EndTimeCK);
            this.LeftPN.Controls.Add(this.StartTimeCK);
            this.LeftPN.Controls.Add(this.AggregateLB);
            this.LeftPN.Controls.Add(this.ReturnBoundsCK);
            this.LeftPN.Controls.Add(this.ReturnBoundsLB);
            this.LeftPN.Controls.Add(this.MaxReturnValuesLB);
            this.LeftPN.Controls.Add(this.MaxReturnValuesNP);
            this.LeftPN.Controls.Add(this.EndTimeDP);
            this.LeftPN.Controls.Add(this.EndTimeLB);
            this.LeftPN.Controls.Add(this.StartTimeDP);
            this.LeftPN.Controls.Add(this.StartTimeLB);
            this.LeftPN.Controls.Add(this.ReadTypeCB);
            this.LeftPN.Controls.Add(this.ReadTypeLB);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(292, 439);
            this.LeftPN.TabIndex = 1;
            // 
            // StopBTN
            // 
            this.StopBTN.Location = new System.Drawing.Point(155, 207);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(75, 23);
            this.StopBTN.TabIndex = 19;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.StopBTN_Click);
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(55, 207);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 18;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.NextBTN_Click);
            // 
            // GoBTN
            // 
            this.GoBTN.Location = new System.Drawing.Point(55, 207);
            this.GoBTN.Name = "GoBTN";
            this.GoBTN.Size = new System.Drawing.Size(75, 23);
            this.GoBTN.TabIndex = 17;
            this.GoBTN.Text = "Go";
            this.GoBTN.UseVisualStyleBackColor = true;
            this.GoBTN.Click += new System.EventHandler(this.GoBTN_Click);
            // 
            // ResampleIntervalLB
            // 
            this.ResampleIntervalLB.AutoSize = true;
            this.ResampleIntervalLB.Location = new System.Drawing.Point(6, 159);
            this.ResampleIntervalLB.Name = "ResampleIntervalLB";
            this.ResampleIntervalLB.Size = new System.Drawing.Size(119, 13);
            this.ResampleIntervalLB.TabIndex = 16;
            this.ResampleIntervalLB.Text = "Processing Interval (ms)";
            // 
            // ResampleIntervalNP
            // 
            this.ResampleIntervalNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.ResampleIntervalNP.Location = new System.Drawing.Point(126, 157);
            this.ResampleIntervalNP.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.ResampleIntervalNP.Name = "ResampleIntervalNP";
            this.ResampleIntervalNP.Size = new System.Drawing.Size(138, 20);
            this.ResampleIntervalNP.TabIndex = 15;
            // 
            // AggregateCB
            // 
            this.AggregateCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AggregateCB.FormattingEnabled = true;
            this.AggregateCB.Location = new System.Drawing.Point(126, 130);
            this.AggregateCB.Name = "AggregateCB";
            this.AggregateCB.Size = new System.Drawing.Size(138, 21);
            this.AggregateCB.TabIndex = 14;
            // 
            // MaxReturnValuesCK
            // 
            this.MaxReturnValuesCK.AutoSize = true;
            this.MaxReturnValuesCK.Location = new System.Drawing.Point(270, 86);
            this.MaxReturnValuesCK.Name = "MaxReturnValuesCK";
            this.MaxReturnValuesCK.Size = new System.Drawing.Size(15, 14);
            this.MaxReturnValuesCK.TabIndex = 13;
            this.MaxReturnValuesCK.UseVisualStyleBackColor = true;
            this.MaxReturnValuesCK.CheckedChanged += new System.EventHandler(this.MaxReturnValuesCK_CheckedChanged);
            // 
            // EndTimeCK
            // 
            this.EndTimeCK.AutoSize = true;
            this.EndTimeCK.Location = new System.Drawing.Point(270, 61);
            this.EndTimeCK.Name = "EndTimeCK";
            this.EndTimeCK.Size = new System.Drawing.Size(15, 14);
            this.EndTimeCK.TabIndex = 12;
            this.EndTimeCK.UseVisualStyleBackColor = true;
            this.EndTimeCK.CheckedChanged += new System.EventHandler(this.EndTimeCK_CheckedChanged);
            // 
            // StartTimeCK
            // 
            this.StartTimeCK.AutoSize = true;
            this.StartTimeCK.Location = new System.Drawing.Point(270, 35);
            this.StartTimeCK.Name = "StartTimeCK";
            this.StartTimeCK.Size = new System.Drawing.Size(15, 14);
            this.StartTimeCK.TabIndex = 11;
            this.StartTimeCK.UseVisualStyleBackColor = true;
            this.StartTimeCK.CheckedChanged += new System.EventHandler(this.StartTimeCK_CheckedChanged);
            // 
            // AggregateLB
            // 
            this.AggregateLB.AutoSize = true;
            this.AggregateLB.Location = new System.Drawing.Point(6, 133);
            this.AggregateLB.Name = "AggregateLB";
            this.AggregateLB.Size = new System.Drawing.Size(56, 13);
            this.AggregateLB.TabIndex = 10;
            this.AggregateLB.Text = "Aggregate";
            // 
            // ReturnBoundsCK
            // 
            this.ReturnBoundsCK.AutoSize = true;
            this.ReturnBoundsCK.Location = new System.Drawing.Point(126, 110);
            this.ReturnBoundsCK.Name = "ReturnBoundsCK";
            this.ReturnBoundsCK.Size = new System.Drawing.Size(15, 14);
            this.ReturnBoundsCK.TabIndex = 9;
            this.ReturnBoundsCK.UseVisualStyleBackColor = true;
            // 
            // ReturnBoundsLB
            // 
            this.ReturnBoundsLB.AutoSize = true;
            this.ReturnBoundsLB.Location = new System.Drawing.Point(6, 110);
            this.ReturnBoundsLB.Name = "ReturnBoundsLB";
            this.ReturnBoundsLB.Size = new System.Drawing.Size(78, 13);
            this.ReturnBoundsLB.TabIndex = 8;
            this.ReturnBoundsLB.Text = "Return Bounds";
            // 
            // MaxReturnValuesLB
            // 
            this.MaxReturnValuesLB.AutoSize = true;
            this.MaxReturnValuesLB.Location = new System.Drawing.Point(6, 86);
            this.MaxReturnValuesLB.Name = "MaxReturnValuesLB";
            this.MaxReturnValuesLB.Size = new System.Drawing.Size(109, 13);
            this.MaxReturnValuesLB.TabIndex = 7;
            this.MaxReturnValuesLB.Text = "Max Values Returned";
            // 
            // MaxReturnValuesNP
            // 
            this.MaxReturnValuesNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Location = new System.Drawing.Point(126, 84);
            this.MaxReturnValuesNP.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Name = "MaxReturnValuesNP";
            this.MaxReturnValuesNP.Size = new System.Drawing.Size(138, 20);
            this.MaxReturnValuesNP.TabIndex = 6;
            // 
            // EndTimeDP
            // 
            this.EndTimeDP.CustomFormat = "hh:mm:ss yyyy-MM-dd";
            this.EndTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.EndTimeDP.Location = new System.Drawing.Point(126, 58);
            this.EndTimeDP.Name = "EndTimeDP";
            this.EndTimeDP.Size = new System.Drawing.Size(138, 20);
            this.EndTimeDP.TabIndex = 5;
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(6, 61);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(52, 13);
            this.EndTimeLB.TabIndex = 4;
            this.EndTimeLB.Text = "End Time";
            // 
            // StartTimeDP
            // 
            this.StartTimeDP.CustomFormat = "hh:mm:ss yyyy-MM-dd";
            this.StartTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.StartTimeDP.Location = new System.Drawing.Point(126, 32);
            this.StartTimeDP.Name = "StartTimeDP";
            this.StartTimeDP.Size = new System.Drawing.Size(138, 20);
            this.StartTimeDP.TabIndex = 3;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(6, 35);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 13);
            this.StartTimeLB.TabIndex = 2;
            this.StartTimeLB.Text = "Start Time";
            // 
            // ReadTypeCB
            // 
            this.ReadTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ReadTypeCB.FormattingEnabled = true;
            this.ReadTypeCB.Location = new System.Drawing.Point(126, 5);
            this.ReadTypeCB.Name = "ReadTypeCB";
            this.ReadTypeCB.Size = new System.Drawing.Size(138, 21);
            this.ReadTypeCB.TabIndex = 1;
            this.ReadTypeCB.SelectedIndexChanged += new System.EventHandler(this.ReadTypeCB_SelectedIndexChanged);
            // 
            // ReadTypeLB
            // 
            this.ReadTypeLB.AutoSize = true;
            this.ReadTypeLB.Location = new System.Drawing.Point(6, 8);
            this.ReadTypeLB.Name = "ReadTypeLB";
            this.ReadTypeLB.Size = new System.Drawing.Size(60, 13);
            this.ReadTypeLB.TabIndex = 0;
            this.ReadTypeLB.Text = "Read Type";
            // 
            // ResultsLV
            // 
            this.ResultsLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ResultsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.IndexCH,
            this.TimestampCH,
            this.ValueCH,
            this.QualityCH,
            this.HistoryInfoCH});
            this.ResultsLV.FullRowSelect = true;
            this.ResultsLV.Location = new System.Drawing.Point(286, 3);
            this.ResultsLV.Name = "ResultsLV";
            this.ResultsLV.Size = new System.Drawing.Size(565, 433);
            this.ResultsLV.TabIndex = 0;
            this.ResultsLV.UseCompatibleStateImageBehavior = false;
            this.ResultsLV.View = System.Windows.Forms.View.Details;
            // 
            // IndexCH
            // 
            this.IndexCH.Text = "Index";
            this.IndexCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // TimestampCH
            // 
            this.TimestampCH.Text = "Time";
            // 
            // ValueCH
            // 
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 151;
            // 
            // QualityCH
            // 
            this.QualityCH.Text = "Quality";
            this.QualityCH.Width = 97;
            // 
            // HistoryInfoCH
            // 
            this.HistoryInfoCH.Text = "History Info";
            this.HistoryInfoCH.Width = 122;
            // 
            // ReadHistoryDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(855, 470);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "ReadHistoryDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Read History";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.LeftPN.ResumeLayout(false);
            this.LeftPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResampleIntervalNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ListView ResultsLV;
        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.ComboBox ReadTypeCB;
        private System.Windows.Forms.Label ReadTypeLB;
        private System.Windows.Forms.DateTimePicker EndTimeDP;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.DateTimePicker StartTimeDP;
        private System.Windows.Forms.CheckBox MaxReturnValuesCK;
        private System.Windows.Forms.CheckBox EndTimeCK;
        private System.Windows.Forms.CheckBox StartTimeCK;
        private System.Windows.Forms.Label AggregateLB;
        private System.Windows.Forms.CheckBox ReturnBoundsCK;
        private System.Windows.Forms.Label ReturnBoundsLB;
        private System.Windows.Forms.Label MaxReturnValuesLB;
        private System.Windows.Forms.NumericUpDown MaxReturnValuesNP;
        private System.Windows.Forms.Label ResampleIntervalLB;
        private System.Windows.Forms.NumericUpDown ResampleIntervalNP;
        private System.Windows.Forms.ComboBox AggregateCB;
        private System.Windows.Forms.ColumnHeader TimestampCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ColumnHeader QualityCH;
        private System.Windows.Forms.ColumnHeader HistoryInfoCH;
        private System.Windows.Forms.ColumnHeader IndexCH;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button GoBTN;
    }
}
