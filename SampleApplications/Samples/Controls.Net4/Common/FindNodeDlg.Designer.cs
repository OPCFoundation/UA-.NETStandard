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
    partial class FindNodeDlg
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
            this.RelativePathLB = new System.Windows.Forms.Label();
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.FindBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.NodesCTRL = new Opc.Ua.Sample.Controls.AttributeListCtrl();
            this.RelativePath = new System.Windows.Forms.TextBox();
            this.StartNode = new System.Windows.Forms.TextBox();
            this.StartNodeLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // RelativePathLB
            // 
            this.RelativePathLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RelativePathLB.AutoSize = true;
            this.RelativePathLB.Location = new System.Drawing.Point(4, 34);
            this.RelativePathLB.Name = "RelativePathLB";
            this.RelativePathLB.Size = new System.Drawing.Size(71, 13);
            this.RelativePathLB.TabIndex = 0;
            this.RelativePathLB.Text = "Relative Path";
            this.RelativePathLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.FindBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 198);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(472, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // FindBTN
            // 
            this.FindBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)));
            this.FindBTN.Location = new System.Drawing.Point(199, 4);
            this.FindBTN.Name = "FindBTN";
            this.FindBTN.Size = new System.Drawing.Size(75, 23);
            this.FindBTN.TabIndex = 2;
            this.FindBTN.Text = "Find";
            this.FindBTN.UseVisualStyleBackColor = true;
            this.FindBTN.Click += new System.EventHandler(this.OkBTN_Click);
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
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(393, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.NodesCTRL);
            this.panel1.Controls.Add(this.RelativePath);
            this.panel1.Controls.Add(this.StartNode);
            this.panel1.Controls.Add(this.StartNodeLB);
            this.panel1.Controls.Add(this.RelativePathLB);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(472, 198);
            this.panel1.TabIndex = 1;
            // 
            // NodesCTRL
            // 
            this.NodesCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodesCTRL.Instructions = null;
            this.NodesCTRL.Location = new System.Drawing.Point(4, 57);
            this.NodesCTRL.Name = "NodesCTRL";
            this.NodesCTRL.ReadOnly = false;
            this.NodesCTRL.Size = new System.Drawing.Size(465, 138);
            this.NodesCTRL.TabIndex = 5;
            // 
            // RelativePath
            // 
            this.RelativePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RelativePath.Location = new System.Drawing.Point(79, 31);
            this.RelativePath.Name = "RelativePath";
            this.RelativePath.Size = new System.Drawing.Size(389, 20);
            this.RelativePath.TabIndex = 4;
            // 
            // StartNode
            // 
            this.StartNode.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartNode.Location = new System.Drawing.Point(79, 7);
            this.StartNode.Name = "StartNode";
            this.StartNode.Size = new System.Drawing.Size(389, 20);
            this.StartNode.TabIndex = 3;
            // 
            // StartNodeLB
            // 
            this.StartNodeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartNodeLB.AutoSize = true;
            this.StartNodeLB.Location = new System.Drawing.Point(4, 10);
            this.StartNodeLB.Name = "StartNodeLB";
            this.StartNodeLB.Size = new System.Drawing.Size(58, 13);
            this.StartNodeLB.TabIndex = 2;
            this.StartNodeLB.Text = "Start Node";
            this.StartNodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // FindNodeDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(472, 229);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "FindNodeDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Find Node";
            this.ButtonsPN.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label RelativePathLB;
        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TextBox RelativePath;
        private System.Windows.Forms.TextBox StartNode;
        private System.Windows.Forms.Label StartNodeLB;
        private System.Windows.Forms.Button FindBTN;
        private AttributeListCtrl NodesCTRL;
    }
}
