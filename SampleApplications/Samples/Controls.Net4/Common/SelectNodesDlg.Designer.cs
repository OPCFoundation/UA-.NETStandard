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
    partial class SelectNodesDlg
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
            this.BrowsePanel = new System.Windows.Forms.SplitContainer();
            this.BrowseCTRL = new Opc.Ua.Sample.Controls.BrowseTreeCtrl();
            this.AttributesCTRL = new Opc.Ua.Sample.Controls.AttributeListCtrl();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.NodeListCTRL = new Opc.Ua.Sample.Controls.NodeListCtrl();
            this.ButtonsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.BrowsePanel)).BeginInit();
            this.BrowsePanel.Panel1.SuspendLayout();
            this.BrowsePanel.Panel2.SuspendLayout();
            this.BrowsePanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MainPN)).BeginInit();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 435);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(792, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 0;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(713, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // BrowsePanel
            // 
            this.BrowsePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowsePanel.Location = new System.Drawing.Point(0, 0);
            this.BrowsePanel.Name = "BrowsePanel";
            // 
            // BrowsePanel.Panel1
            // 
            this.BrowsePanel.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // BrowsePanel.Panel2
            // 
            this.BrowsePanel.Panel2.Controls.Add(this.AttributesCTRL);
            this.BrowsePanel.Size = new System.Drawing.Size(792, 285);
            this.BrowsePanel.SplitterDistance = 375;
            this.BrowsePanel.TabIndex = 0;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AllowDrop = true;
            this.BrowseCTRL.AttributesCtrl = this.AttributesCTRL;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.EnableDragging = true;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.SessionTreeCtrl = null;
            this.BrowseCTRL.Size = new System.Drawing.Size(375, 285);
            this.BrowseCTRL.TabIndex = 1;
            this.BrowseCTRL.ItemsSelected += new Opc.Ua.Sample.Controls.NodesSelectedEventHandler(this.BrowseCTRL_NodesSelected);
            // 
            // AttributesCTRL
            // 
            this.AttributesCTRL.AllowDrop = true;
            this.AttributesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesCTRL.EnableDragging = true;
            this.AttributesCTRL.Instructions = null;
            this.AttributesCTRL.Location = new System.Drawing.Point(0, 0);
            this.AttributesCTRL.Name = "AttributesCTRL";
            this.AttributesCTRL.ReadOnly = false;
            this.AttributesCTRL.Size = new System.Drawing.Size(413, 285);
            this.AttributesCTRL.TabIndex = 1;
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.BrowsePanel);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.NodeListCTRL);
            this.MainPN.Size = new System.Drawing.Size(792, 435);
            this.MainPN.SplitterDistance = 285;
            this.MainPN.TabIndex = 1;
            // 
            // NodeListCTRL
            // 
            this.NodeListCTRL.AllowDrop = true;
            this.NodeListCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodeListCTRL.EnableDragging = true;
            this.NodeListCTRL.Instructions = null;
            this.NodeListCTRL.Location = new System.Drawing.Point(0, 0);
            this.NodeListCTRL.Name = "NodeListCTRL";
            this.NodeListCTRL.Size = new System.Drawing.Size(792, 146);
            this.NodeListCTRL.TabIndex = 0;
            // 
            // SelectNodesDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(792, 466);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "SelectNodesDlg";
            this.Text = "Select Nodes";
            this.ButtonsPN.ResumeLayout(false);
            this.BrowsePanel.Panel1.ResumeLayout(false);
            this.BrowsePanel.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.BrowsePanel)).EndInit();
            this.BrowsePanel.ResumeLayout(false);
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.MainPN)).EndInit();
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.SplitContainer BrowsePanel;
        private BrowseTreeCtrl BrowseCTRL;
        private AttributeListCtrl AttributesCTRL;
        private System.Windows.Forms.SplitContainer MainPN;
        private NodeListCtrl NodeListCTRL;
    }
}
