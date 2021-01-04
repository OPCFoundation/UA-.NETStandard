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
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.LeftPN = new System.Windows.Forms.SplitContainer();
            this.BrowseCTRL = new Opc.Ua.Client.Controls.BrowseTreeCtrl();
            this.AttributesCTRL = new Opc.Ua.Client.Controls.AttributeListCtrl();
            this.ReferencesCTRL = new Opc.Ua.Client.Controls.BrowseListCtrl();
            this.RightPN = new System.Windows.Forms.SplitContainer();
            this.NodesCTRL = new Opc.Ua.Client.Controls.NodeListCtrl();
            this.ButtonsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MainPN)).BeginInit();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.LeftPN)).BeginInit();
            this.LeftPN.Panel1.SuspendLayout();
            this.LeftPN.Panel2.SuspendLayout();
            this.LeftPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.RightPN)).BeginInit();
            this.RightPN.Panel1.SuspendLayout();
            this.RightPN.Panel2.SuspendLayout();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 435);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(1016, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 0;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(937, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.LeftPN);
            this.MainPN.Panel1.Padding = new System.Windows.Forms.Padding(3, 3, 0, 0);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.RightPN);
            this.MainPN.Panel2.Padding = new System.Windows.Forms.Padding(0, 3, 3, 0);
            this.MainPN.Size = new System.Drawing.Size(1016, 435);
            this.MainPN.SplitterDistance = 380;
            this.MainPN.TabIndex = 33;
            // 
            // LeftPN
            // 
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.LeftPN.Location = new System.Drawing.Point(3, 3);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // LeftPN.Panel1
            // 
            this.LeftPN.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // LeftPN.Panel2
            // 
            this.LeftPN.Panel2.Controls.Add(this.ReferencesCTRL);
            this.LeftPN.Size = new System.Drawing.Size(377, 432);
            this.LeftPN.SplitterDistance = 255;
            this.LeftPN.TabIndex = 0;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesCTRL = this.AttributesCTRL;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.EnableDragging = true;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.MinimumSize = new System.Drawing.Size(325, 216);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.ReferencesCTRL = this.ReferencesCTRL;
            this.BrowseCTRL.Size = new System.Drawing.Size(377, 255);
            this.BrowseCTRL.TabIndex = 29;
            this.BrowseCTRL.NodesSelected += new System.EventHandler<Opc.Ua.Client.Controls.BrowseTreeCtrl.NodesSelectedEventArgs>(this.BrowseCTRL_NodesSelected);
            // 
            // AttributesCTRL
            // 
            this.AttributesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesCTRL.Instructions = null;
            this.AttributesCTRL.Location = new System.Drawing.Point(0, 0);
            this.AttributesCTRL.Name = "AttributesCTRL";
            this.AttributesCTRL.Size = new System.Drawing.Size(312, 432);
            this.AttributesCTRL.TabIndex = 32;
            // 
            // ReferencesCTRL
            // 
            this.ReferencesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReferencesCTRL.Instructions = null;
            this.ReferencesCTRL.Location = new System.Drawing.Point(0, 0);
            this.ReferencesCTRL.Name = "ReferencesCTRL";
            this.ReferencesCTRL.Size = new System.Drawing.Size(377, 173);
            this.ReferencesCTRL.TabIndex = 31;
            // 
            // RightPN
            // 
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(0, 3);
            this.RightPN.Name = "RightPN";
            // 
            // RightPN.Panel1
            // 
            this.RightPN.Panel1.Controls.Add(this.NodesCTRL);
            // 
            // RightPN.Panel2
            // 
            this.RightPN.Panel2.Controls.Add(this.AttributesCTRL);
            this.RightPN.Size = new System.Drawing.Size(629, 432);
            this.RightPN.SplitterDistance = 313;
            this.RightPN.TabIndex = 0;
            // 
            // NodesCTRL
            // 
            this.NodesCTRL.AllowDrop = true;
            this.NodesCTRL.AttributesCTRL = this.AttributesCTRL;
            this.NodesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodesCTRL.EnableDragging = true;
            this.NodesCTRL.Instructions = null;
            this.NodesCTRL.Location = new System.Drawing.Point(0, 0);
            this.NodesCTRL.Name = "NodesCTRL";
            this.NodesCTRL.ReferencesCTRL = this.ReferencesCTRL;
            this.NodesCTRL.Size = new System.Drawing.Size(313, 432);
            this.NodesCTRL.TabIndex = 30;
            // 
            // SelectNodesDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1016, 466);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "SelectNodesDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Select Nodes";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.MainPN)).EndInit();
            this.MainPN.ResumeLayout(false);
            this.LeftPN.Panel1.ResumeLayout(false);
            this.LeftPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.LeftPN)).EndInit();
            this.LeftPN.ResumeLayout(false);
            this.RightPN.Panel1.ResumeLayout(false);
            this.RightPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.RightPN)).EndInit();
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private Opc.Ua.Client.Controls.BrowseTreeCtrl BrowseCTRL;
        private Opc.Ua.Client.Controls.NodeListCtrl NodesCTRL;
        private Opc.Ua.Client.Controls.AttributeListCtrl AttributesCTRL;
        private Opc.Ua.Client.Controls.BrowseListCtrl ReferencesCTRL;
        private System.Windows.Forms.SplitContainer MainPN;
        private System.Windows.Forms.SplitContainer LeftPN;
        private System.Windows.Forms.SplitContainer RightPN;
    }
}
