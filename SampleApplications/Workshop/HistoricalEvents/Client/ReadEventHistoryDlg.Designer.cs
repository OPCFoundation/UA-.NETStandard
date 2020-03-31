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

namespace Quickstarts.HistoricalEvents.Client
{
    partial class ReadEventHistoryDlg
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
            this.ResultsLV = new Quickstarts.HistoricalEvents.Client.EventListView();
            this.LeftPN = new System.Windows.Forms.Panel();
            this.EventFilterBTN = new System.Windows.Forms.Button();
            this.EventFilterTB = new System.Windows.Forms.TextBox();
            this.EventAreaTB = new System.Windows.Forms.TextBox();
            this.EventTypeTB = new System.Windows.Forms.TextBox();
            this.EventFilterLB = new System.Windows.Forms.Label();
            this.EventTypeBTN = new System.Windows.Forms.Button();
            this.EventAreaLB = new System.Windows.Forms.Label();
            this.EventTypeLB = new System.Windows.Forms.Label();
            this.EventAreaBTN = new System.Windows.Forms.Button();
            this.StopBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.GoBTN = new System.Windows.Forms.Button();
            this.MaxReturnValuesCK = new System.Windows.Forms.CheckBox();
            this.EndTimeCK = new System.Windows.Forms.CheckBox();
            this.StartTimeCK = new System.Windows.Forms.CheckBox();
            this.MaxReturnValuesLB = new System.Windows.Forms.Label();
            this.MaxReturnValuesNP = new System.Windows.Forms.NumericUpDown();
            this.EndTimeDP = new System.Windows.Forms.DateTimePicker();
            this.EndTimeLB = new System.Windows.Forms.Label();
            this.StartTimeDP = new System.Windows.Forms.DateTimePicker();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.LeftPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 410);
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
            this.MainPN.Controls.Add(this.ResultsLV);
            this.MainPN.Controls.Add(this.LeftPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(855, 410);
            this.MainPN.TabIndex = 1;
            // 
            // ResultsLV
            // 
            this.ResultsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsLV.IsSubscribed = false;
            this.ResultsLV.Location = new System.Drawing.Point(260, 0);
            this.ResultsLV.Name = "ResultsLV";
            this.ResultsLV.Size = new System.Drawing.Size(595, 410);
            this.ResultsLV.TabIndex = 1;
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.EventFilterBTN);
            this.LeftPN.Controls.Add(this.EventFilterTB);
            this.LeftPN.Controls.Add(this.EventAreaTB);
            this.LeftPN.Controls.Add(this.EventTypeTB);
            this.LeftPN.Controls.Add(this.EventFilterLB);
            this.LeftPN.Controls.Add(this.EventTypeBTN);
            this.LeftPN.Controls.Add(this.EventAreaLB);
            this.LeftPN.Controls.Add(this.EventTypeLB);
            this.LeftPN.Controls.Add(this.EventAreaBTN);
            this.LeftPN.Controls.Add(this.StopBTN);
            this.LeftPN.Controls.Add(this.NextBTN);
            this.LeftPN.Controls.Add(this.GoBTN);
            this.LeftPN.Controls.Add(this.MaxReturnValuesCK);
            this.LeftPN.Controls.Add(this.EndTimeCK);
            this.LeftPN.Controls.Add(this.StartTimeCK);
            this.LeftPN.Controls.Add(this.MaxReturnValuesLB);
            this.LeftPN.Controls.Add(this.MaxReturnValuesNP);
            this.LeftPN.Controls.Add(this.EndTimeDP);
            this.LeftPN.Controls.Add(this.EndTimeLB);
            this.LeftPN.Controls.Add(this.StartTimeDP);
            this.LeftPN.Controls.Add(this.StartTimeLB);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.MinimumSize = new System.Drawing.Size(260, 225);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(260, 410);
            this.LeftPN.TabIndex = 0;
            // 
            // EventFilterBTN
            // 
            this.EventFilterBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.EventFilterBTN.Location = new System.Drawing.Point(228, 62);
            this.EventFilterBTN.Name = "EventFilterBTN";
            this.EventFilterBTN.Size = new System.Drawing.Size(24, 23);
            this.EventFilterBTN.TabIndex = 8;
            this.EventFilterBTN.Text = "...";
            this.EventFilterBTN.UseVisualStyleBackColor = true;
            this.EventFilterBTN.Click += new System.EventHandler(this.EventFilterBTN_Click);
            // 
            // EventFilterTB
            // 
            this.EventFilterTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.EventFilterTB.BackColor = System.Drawing.SystemColors.Window;
            this.EventFilterTB.Location = new System.Drawing.Point(84, 64);
            this.EventFilterTB.Name = "EventFilterTB";
            this.EventFilterTB.ReadOnly = true;
            this.EventFilterTB.Size = new System.Drawing.Size(138, 20);
            this.EventFilterTB.TabIndex = 7;
            // 
            // EventAreaTB
            // 
            this.EventAreaTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.EventAreaTB.BackColor = System.Drawing.SystemColors.Window;
            this.EventAreaTB.Location = new System.Drawing.Point(84, 12);
            this.EventAreaTB.Name = "EventAreaTB";
            this.EventAreaTB.ReadOnly = true;
            this.EventAreaTB.Size = new System.Drawing.Size(138, 20);
            this.EventAreaTB.TabIndex = 1;
            // 
            // EventTypeTB
            // 
            this.EventTypeTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.EventTypeTB.BackColor = System.Drawing.SystemColors.Window;
            this.EventTypeTB.Location = new System.Drawing.Point(84, 38);
            this.EventTypeTB.Name = "EventTypeTB";
            this.EventTypeTB.ReadOnly = true;
            this.EventTypeTB.Size = new System.Drawing.Size(138, 20);
            this.EventTypeTB.TabIndex = 4;
            // 
            // EventFilterLB
            // 
            this.EventFilterLB.AutoSize = true;
            this.EventFilterLB.Location = new System.Drawing.Point(5, 67);
            this.EventFilterLB.Name = "EventFilterLB";
            this.EventFilterLB.Size = new System.Drawing.Size(60, 13);
            this.EventFilterLB.TabIndex = 6;
            this.EventFilterLB.Text = "Event Filter";
            // 
            // EventTypeBTN
            // 
            this.EventTypeBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.EventTypeBTN.Location = new System.Drawing.Point(228, 36);
            this.EventTypeBTN.Name = "EventTypeBTN";
            this.EventTypeBTN.Size = new System.Drawing.Size(24, 23);
            this.EventTypeBTN.TabIndex = 5;
            this.EventTypeBTN.Text = "...";
            this.EventTypeBTN.UseVisualStyleBackColor = true;
            this.EventTypeBTN.Click += new System.EventHandler(this.EventTypeBTN_Click);
            // 
            // EventAreaLB
            // 
            this.EventAreaLB.AutoSize = true;
            this.EventAreaLB.Location = new System.Drawing.Point(5, 15);
            this.EventAreaLB.Name = "EventAreaLB";
            this.EventAreaLB.Size = new System.Drawing.Size(60, 13);
            this.EventAreaLB.TabIndex = 0;
            this.EventAreaLB.Text = "Event Area";
            // 
            // EventTypeLB
            // 
            this.EventTypeLB.AutoSize = true;
            this.EventTypeLB.Location = new System.Drawing.Point(5, 41);
            this.EventTypeLB.Name = "EventTypeLB";
            this.EventTypeLB.Size = new System.Drawing.Size(62, 13);
            this.EventTypeLB.TabIndex = 3;
            this.EventTypeLB.Text = "Event Type";
            // 
            // EventAreaBTN
            // 
            this.EventAreaBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.EventAreaBTN.Location = new System.Drawing.Point(228, 10);
            this.EventAreaBTN.Name = "EventAreaBTN";
            this.EventAreaBTN.Size = new System.Drawing.Size(24, 23);
            this.EventAreaBTN.TabIndex = 2;
            this.EventAreaBTN.Text = "...";
            this.EventAreaBTN.UseVisualStyleBackColor = true;
            this.EventAreaBTN.Click += new System.EventHandler(this.EventAreaBTN_Click);
            // 
            // StopBTN
            // 
            this.StopBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.StopBTN.Location = new System.Drawing.Point(145, 184);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(75, 23);
            this.StopBTN.TabIndex = 19;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.StopBTN_Click);
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(35, 184);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 18;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.NextBTN_Click);
            // 
            // GoBTN
            // 
            this.GoBTN.Location = new System.Drawing.Point(35, 184);
            this.GoBTN.Name = "GoBTN";
            this.GoBTN.Size = new System.Drawing.Size(75, 23);
            this.GoBTN.TabIndex = 17;
            this.GoBTN.Text = "Go";
            this.GoBTN.UseVisualStyleBackColor = true;
            this.GoBTN.Click += new System.EventHandler(this.GoBTN_Click);
            // 
            // MaxReturnValuesCK
            // 
            this.MaxReturnValuesCK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxReturnValuesCK.AutoSize = true;
            this.MaxReturnValuesCK.Location = new System.Drawing.Point(233, 145);
            this.MaxReturnValuesCK.Name = "MaxReturnValuesCK";
            this.MaxReturnValuesCK.Size = new System.Drawing.Size(15, 14);
            this.MaxReturnValuesCK.TabIndex = 17;
            this.MaxReturnValuesCK.UseVisualStyleBackColor = true;
            this.MaxReturnValuesCK.CheckedChanged += new System.EventHandler(this.MaxReturnValuesCK_CheckedChanged);
            // 
            // EndTimeCK
            // 
            this.EndTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.EndTimeCK.AutoSize = true;
            this.EndTimeCK.Location = new System.Drawing.Point(233, 119);
            this.EndTimeCK.Name = "EndTimeCK";
            this.EndTimeCK.Size = new System.Drawing.Size(15, 14);
            this.EndTimeCK.TabIndex = 14;
            this.EndTimeCK.UseVisualStyleBackColor = true;
            this.EndTimeCK.CheckedChanged += new System.EventHandler(this.EndTimeCK_CheckedChanged);
            // 
            // StartTimeCK
            // 
            this.StartTimeCK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.StartTimeCK.AutoSize = true;
            this.StartTimeCK.Location = new System.Drawing.Point(233, 93);
            this.StartTimeCK.Name = "StartTimeCK";
            this.StartTimeCK.Size = new System.Drawing.Size(15, 14);
            this.StartTimeCK.TabIndex = 11;
            this.StartTimeCK.UseVisualStyleBackColor = true;
            this.StartTimeCK.CheckedChanged += new System.EventHandler(this.StartTimeCK_CheckedChanged);
            // 
            // MaxReturnValuesLB
            // 
            this.MaxReturnValuesLB.AutoSize = true;
            this.MaxReturnValuesLB.Location = new System.Drawing.Point(5, 145);
            this.MaxReturnValuesLB.Name = "MaxReturnValuesLB";
            this.MaxReturnValuesLB.Size = new System.Drawing.Size(65, 13);
            this.MaxReturnValuesLB.TabIndex = 15;
            this.MaxReturnValuesLB.Text = "Max Results";
            // 
            // MaxReturnValuesNP
            // 
            this.MaxReturnValuesNP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxReturnValuesNP.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Location = new System.Drawing.Point(84, 140);
            this.MaxReturnValuesNP.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.MaxReturnValuesNP.Name = "MaxReturnValuesNP";
            this.MaxReturnValuesNP.Size = new System.Drawing.Size(138, 20);
            this.MaxReturnValuesNP.TabIndex = 16;
            // 
            // EndTimeDP
            // 
            this.EndTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.EndTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.EndTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.EndTimeDP.Location = new System.Drawing.Point(84, 116);
            this.EndTimeDP.Name = "EndTimeDP";
            this.EndTimeDP.Size = new System.Drawing.Size(138, 20);
            this.EndTimeDP.TabIndex = 13;
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(5, 119);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(52, 13);
            this.EndTimeLB.TabIndex = 12;
            this.EndTimeLB.Text = "End Time";
            // 
            // StartTimeDP
            // 
            this.StartTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StartTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.StartTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.StartTimeDP.Location = new System.Drawing.Point(84, 90);
            this.StartTimeDP.Name = "StartTimeDP";
            this.StartTimeDP.Size = new System.Drawing.Size(138, 20);
            this.StartTimeDP.TabIndex = 10;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(5, 93);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 13);
            this.StartTimeLB.TabIndex = 9;
            this.StartTimeLB.Text = "Start Time";
            // 
            // ReadEventHistoryDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(855, 441);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.MaximumSize = new System.Drawing.Size(1024, 2014);
            this.MinimumSize = new System.Drawing.Size(395, 234);
            this.Name = "ReadEventHistoryDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Read Event History";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.LeftPN.ResumeLayout(false);
            this.LeftPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReturnValuesNP)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.DateTimePicker EndTimeDP;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.DateTimePicker StartTimeDP;
        private System.Windows.Forms.CheckBox MaxReturnValuesCK;
        private System.Windows.Forms.CheckBox EndTimeCK;
        private System.Windows.Forms.CheckBox StartTimeCK;
        private System.Windows.Forms.Label MaxReturnValuesLB;
        private System.Windows.Forms.NumericUpDown MaxReturnValuesNP;
        private System.Windows.Forms.Button StopBTN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button GoBTN;
        private EventListView ResultsLV;
        private System.Windows.Forms.Button EventFilterBTN;
        private System.Windows.Forms.TextBox EventFilterTB;
        private System.Windows.Forms.TextBox EventAreaTB;
        private System.Windows.Forms.TextBox EventTypeTB;
        private System.Windows.Forms.Label EventFilterLB;
        private System.Windows.Forms.Button EventTypeBTN;
        private System.Windows.Forms.Label EventAreaLB;
        private System.Windows.Forms.Label EventTypeLB;
        private System.Windows.Forms.Button EventAreaBTN;
    }
}
