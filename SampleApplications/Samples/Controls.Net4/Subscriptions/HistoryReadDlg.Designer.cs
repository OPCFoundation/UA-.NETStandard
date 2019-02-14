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
    partial class HistoryReadDlg
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
            this.BackBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.NextBTN = new System.Windows.Forms.Button();
            this.ReadBTN = new System.Windows.Forms.Button();
            this.SplitterPN = new System.Windows.Forms.SplitContainer();
            this.HistoryReadDetailsCTRL = new Opc.Ua.Sample.HistoryReadDetails();
            this.BrowseCTRL = new Opc.Ua.Sample.Controls.BrowseTreeCtrl();
            this.ReadResultsCTRL = new Opc.Ua.Sample.Controls.DataListCtrl();
            this.ReadValuesCTRL = new Opc.Ua.Sample.Controls.ReadValueListCtrl();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ButtonsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SplitterPN)).BeginInit();
            this.SplitterPN.Panel1.SuspendLayout();
            this.SplitterPN.Panel2.SuspendLayout();
            this.SplitterPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.BackBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Controls.Add(this.NextBTN);
            this.ButtonsPN.Controls.Add(this.ReadBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 395);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(692, 31);
            this.ButtonsPN.TabIndex = 2;
            // 
            // BackBTN
            // 
            this.BackBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.BackBTN.Location = new System.Drawing.Point(451, 4);
            this.BackBTN.Name = "BackBTN";
            this.BackBTN.Size = new System.Drawing.Size(75, 23);
            this.BackBTN.TabIndex = 3;
            this.BackBTN.Text = "< Back";
            this.BackBTN.UseVisualStyleBackColor = true;
            this.BackBTN.Click += new System.EventHandler(this.MoveBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(613, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Close";
            this.CancelBTN.UseVisualStyleBackColor = true;
            this.CancelBTN.Click += new System.EventHandler(this.CancelBTN_Click);
            // 
            // NextBTN
            // 
            this.NextBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.NextBTN.Location = new System.Drawing.Point(532, 4);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 2;
            this.NextBTN.Text = "Next >";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.MoveBTN_Click);
            // 
            // ReadBTN
            // 
            this.ReadBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.ReadBTN.Location = new System.Drawing.Point(532, 4);
            this.ReadBTN.Name = "ReadBTN";
            this.ReadBTN.Size = new System.Drawing.Size(75, 23);
            this.ReadBTN.TabIndex = 4;
            this.ReadBTN.Text = "Read";
            this.ReadBTN.UseVisualStyleBackColor = true;
            this.ReadBTN.Click += new System.EventHandler(this.ReadMI_Click);
            // 
            // SplitterPN
            // 
            this.SplitterPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitterPN.Location = new System.Drawing.Point(3, 3);
            this.SplitterPN.Name = "SplitterPN";
            // 
            // SplitterPN.Panel1
            // 
            this.SplitterPN.Panel1.Controls.Add(this.HistoryReadDetailsCTRL);
            this.SplitterPN.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // SplitterPN.Panel2
            // 
            this.SplitterPN.Panel2.Controls.Add(this.ReadResultsCTRL);
            this.SplitterPN.Panel2.Controls.Add(this.ReadValuesCTRL);
            this.SplitterPN.Size = new System.Drawing.Size(686, 392);
            this.SplitterPN.SplitterDistance = 291;
            this.SplitterPN.TabIndex = 3;
            // 
            // HistoryReadDetailsCTRL
            // 
            this.HistoryReadDetailsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HistoryReadDetailsCTRL.Location = new System.Drawing.Point(0, 0);
            this.HistoryReadDetailsCTRL.Name = "HistoryReadDetailsCTRL";
            this.HistoryReadDetailsCTRL.Size = new System.Drawing.Size(291, 392);
            this.HistoryReadDetailsCTRL.TabIndex = 2;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AllowDrop = true;
            this.BrowseCTRL.AttributesCtrl = null;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.EnableDragging = true;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.SessionTreeCtrl = null;
            this.BrowseCTRL.Size = new System.Drawing.Size(291, 392);
            this.BrowseCTRL.TabIndex = 1;
            this.BrowseCTRL.ItemsSelected += new Opc.Ua.Sample.Controls.NodesSelectedEventHandler(this.BrowseCTRL_ItemsSelected);
            // 
            // ReadResultsCTRL
            // 
            this.ReadResultsCTRL.AutoUpdate = true;
            this.ReadResultsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReadResultsCTRL.Instructions = "Right click to add filter operands.";
            this.ReadResultsCTRL.LatestValue = true;
            this.ReadResultsCTRL.Location = new System.Drawing.Point(0, 0);
            this.ReadResultsCTRL.MonitoredItem = null;
            this.ReadResultsCTRL.Name = "ReadResultsCTRL";
            this.ReadResultsCTRL.Size = new System.Drawing.Size(391, 392);
            this.ReadResultsCTRL.TabIndex = 3;
            // 
            // ReadValuesCTRL
            // 
            this.ReadValuesCTRL.AllowDrop = true;
            this.ReadValuesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReadValuesCTRL.EnableDragging = true;
            this.ReadValuesCTRL.Instructions = null;
            this.ReadValuesCTRL.Location = new System.Drawing.Point(0, 0);
            this.ReadValuesCTRL.Name = "ReadValuesCTRL";
            this.ReadValuesCTRL.Size = new System.Drawing.Size(391, 392);
            this.ReadValuesCTRL.TabIndex = 1;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.SplitterPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.MainPN.Size = new System.Drawing.Size(692, 395);
            this.MainPN.TabIndex = 4;
            // 
            // HistoryReadDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(692, 426);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "HistoryReadDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Read";
            this.ButtonsPN.ResumeLayout(false);
            this.SplitterPN.Panel1.ResumeLayout(false);
            this.SplitterPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.SplitterPN)).EndInit();
            this.SplitterPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private ReadValueListCtrl ReadValuesCTRL;
        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.SplitContainer SplitterPN;
        private BrowseTreeCtrl BrowseCTRL;
        private System.Windows.Forms.Button BackBTN;
        private System.Windows.Forms.Button NextBTN;
        private DataListCtrl ReadResultsCTRL;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Button ReadBTN;
        private Opc.Ua.Sample.HistoryReadDetails HistoryReadDetailsCTRL;
    }
}
