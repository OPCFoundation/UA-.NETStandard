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
    partial class WriteDlg
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
            this.WriteBTN = new System.Windows.Forms.Button();
            this.SplitterPN = new System.Windows.Forms.SplitContainer();
            this.BrowseCTRL = new Opc.Ua.Sample.Controls.BrowseTreeCtrl();
            this.WriteResultsCTRL = new Opc.Ua.Sample.Controls.DataListCtrl();
            this.WriteValuesCTRL = new Opc.Ua.Sample.Controls.WriteValueListCtrl();
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
            this.ButtonsPN.Controls.Add(this.WriteBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 395);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(867, 31);
            this.ButtonsPN.TabIndex = 2;
            // 
            // BackBTN
            // 
            this.BackBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.BackBTN.Location = new System.Drawing.Point(626, 4);
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
            this.CancelBTN.Location = new System.Drawing.Point(788, 4);
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
            this.NextBTN.Location = new System.Drawing.Point(707, 4);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 2;
            this.NextBTN.Text = "Next >";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.MoveBTN_Click);
            // 
            // WriteBTN
            // 
            this.WriteBTN.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.WriteBTN.Location = new System.Drawing.Point(707, 4);
            this.WriteBTN.Name = "WriteBTN";
            this.WriteBTN.Size = new System.Drawing.Size(75, 23);
            this.WriteBTN.TabIndex = 4;
            this.WriteBTN.Text = "Write";
            this.WriteBTN.UseVisualStyleBackColor = true;
            this.WriteBTN.Click += new System.EventHandler(this.WriteMI_Click);
            // 
            // SplitterPN
            // 
            this.SplitterPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitterPN.Location = new System.Drawing.Point(3, 3);
            this.SplitterPN.Name = "SplitterPN";
            // 
            // SplitterPN.Panel1
            // 
            this.SplitterPN.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // SplitterPN.Panel2
            // 
            this.SplitterPN.Panel2.Controls.Add(this.WriteResultsCTRL);
            this.SplitterPN.Panel2.Controls.Add(this.WriteValuesCTRL);
            this.SplitterPN.Size = new System.Drawing.Size(861, 392);
            this.SplitterPN.SplitterDistance = 297;
            this.SplitterPN.TabIndex = 3;
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
            this.BrowseCTRL.Size = new System.Drawing.Size(297, 392);
            this.BrowseCTRL.TabIndex = 1;
            this.BrowseCTRL.ItemsSelected += new Opc.Ua.Sample.Controls.NodesSelectedEventHandler(this.BrowseCTRL_ItemsSelected);
            // 
            // WriteResultsCTRL
            // 
            this.WriteResultsCTRL.AutoUpdate = true;
            this.WriteResultsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.WriteResultsCTRL.Instructions = "Right click to add filter operands.";
            this.WriteResultsCTRL.LatestValue = true;
            this.WriteResultsCTRL.Location = new System.Drawing.Point(0, 0);
            this.WriteResultsCTRL.MonitoredItem = null;
            this.WriteResultsCTRL.Name = "WriteResultsCTRL";
            this.WriteResultsCTRL.Size = new System.Drawing.Size(560, 392);
            this.WriteResultsCTRL.TabIndex = 3;
            // 
            // WriteValuesCTRL
            // 
            this.WriteValuesCTRL.AllowDrop = true;
            this.WriteValuesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.WriteValuesCTRL.EnableDragging = true;
            this.WriteValuesCTRL.Instructions = null;
            this.WriteValuesCTRL.Location = new System.Drawing.Point(0, 0);
            this.WriteValuesCTRL.Name = "WriteValuesCTRL";
            this.WriteValuesCTRL.Size = new System.Drawing.Size(560, 392);
            this.WriteValuesCTRL.TabIndex = 1;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.SplitterPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.MainPN.Size = new System.Drawing.Size(867, 395);
            this.MainPN.TabIndex = 4;
            // 
            // WriteDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(867, 426);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.Name = "WriteDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Write";
            this.ButtonsPN.ResumeLayout(false);
            this.SplitterPN.Panel1.ResumeLayout(false);
            this.SplitterPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.SplitterPN)).EndInit();
            this.SplitterPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private WriteValueListCtrl WriteValuesCTRL;
        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.SplitContainer SplitterPN;
        private BrowseTreeCtrl BrowseCTRL;
        private System.Windows.Forms.Button BackBTN;
        private System.Windows.Forms.Button NextBTN;
        private DataListCtrl WriteResultsCTRL;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Button WriteBTN;
    }
}
