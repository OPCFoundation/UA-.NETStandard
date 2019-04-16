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
    partial class WriteValueEditDlg
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
            this.DisplayNameLB = new System.Windows.Forms.Label();
            this.StartNodeIdLB = new System.Windows.Forms.Label();
            this.IndexRangeLB = new System.Windows.Forms.Label();
            this.DisplayNameTB = new System.Windows.Forms.TextBox();
            this.AttributeIdLB = new System.Windows.Forms.Label();
            this.AttributeIdCB = new System.Windows.Forms.ComboBox();
            this.IndexRangeTB = new System.Windows.Forms.TextBox();
            this.NodeIdCTRL = new Opc.Ua.Client.Controls.NodeIdCtrl();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 103);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(311, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(232, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // DisplayNameLB
            // 
            this.DisplayNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameLB.AutoSize = true;
            this.DisplayNameLB.Location = new System.Drawing.Point(4, 8);
            this.DisplayNameLB.Name = "DisplayNameLB";
            this.DisplayNameLB.Size = new System.Drawing.Size(72, 13);
            this.DisplayNameLB.TabIndex = 1;
            this.DisplayNameLB.Text = "Display Name";
            this.DisplayNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartNodeIdLB
            // 
            this.StartNodeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartNodeIdLB.AutoSize = true;
            this.StartNodeIdLB.Location = new System.Drawing.Point(5, 34);
            this.StartNodeIdLB.Name = "StartNodeIdLB";
            this.StartNodeIdLB.Size = new System.Drawing.Size(47, 13);
            this.StartNodeIdLB.TabIndex = 2;
            this.StartNodeIdLB.Text = "Node ID";
            this.StartNodeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IndexRangeLB
            // 
            this.IndexRangeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IndexRangeLB.AutoSize = true;
            this.IndexRangeLB.Location = new System.Drawing.Point(4, 86);
            this.IndexRangeLB.Name = "IndexRangeLB";
            this.IndexRangeLB.Size = new System.Drawing.Size(68, 13);
            this.IndexRangeLB.TabIndex = 6;
            this.IndexRangeLB.Text = "Index Range";
            this.IndexRangeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameTB
            // 
            this.DisplayNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameTB.Location = new System.Drawing.Point(88, 4);
            this.DisplayNameTB.Name = "DisplayNameTB";
            this.DisplayNameTB.Size = new System.Drawing.Size(219, 20);
            this.DisplayNameTB.TabIndex = 0;
            // 
            // AttributeIdLB
            // 
            this.AttributeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.AttributeIdLB.AutoSize = true;
            this.AttributeIdLB.Location = new System.Drawing.Point(5, 59);
            this.AttributeIdLB.Name = "AttributeIdLB";
            this.AttributeIdLB.Size = new System.Drawing.Size(46, 13);
            this.AttributeIdLB.TabIndex = 4;
            this.AttributeIdLB.Text = "Attribute";
            this.AttributeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AttributeIdCB
            // 
            this.AttributeIdCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AttributeIdCB.FormattingEnabled = true;
            this.AttributeIdCB.Location = new System.Drawing.Point(88, 56);
            this.AttributeIdCB.Name = "AttributeIdCB";
            this.AttributeIdCB.Size = new System.Drawing.Size(220, 21);
            this.AttributeIdCB.TabIndex = 5;
            // 
            // IndexRangeTB
            // 
            this.IndexRangeTB.Location = new System.Drawing.Point(88, 83);
            this.IndexRangeTB.Name = "IndexRangeTB";
            this.IndexRangeTB.Size = new System.Drawing.Size(106, 20);
            this.IndexRangeTB.TabIndex = 7;
            // 
            // NodeIdCTRL
            // 
            this.NodeIdCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeIdCTRL.Location = new System.Drawing.Point(88, 30);
            this.NodeIdCTRL.MaximumSize = new System.Drawing.Size(4096, 20);
            this.NodeIdCTRL.MinimumSize = new System.Drawing.Size(100, 20);
            this.NodeIdCTRL.Name = "NodeIdCTRL";
            this.NodeIdCTRL.Size = new System.Drawing.Size(218, 20);
            this.NodeIdCTRL.TabIndex = 3;
            this.NodeIdCTRL.IdentifierChanged += new System.EventHandler(this.NodeIdCTRL_IdentifierChanged);
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.NodeIdCTRL);
            this.MainPN.Controls.Add(this.IndexRangeTB);
            this.MainPN.Controls.Add(this.AttributeIdCB);
            this.MainPN.Controls.Add(this.AttributeIdLB);
            this.MainPN.Controls.Add(this.DisplayNameTB);
            this.MainPN.Controls.Add(this.IndexRangeLB);
            this.MainPN.Controls.Add(this.StartNodeIdLB);
            this.MainPN.Controls.Add(this.DisplayNameLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(311, 103);
            this.MainPN.TabIndex = 1;
            // 
            // WriteValueEditDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(311, 134);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "WriteValueEditDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Write Value";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Label DisplayNameLB;
        private System.Windows.Forms.Label StartNodeIdLB;
        private System.Windows.Forms.Label IndexRangeLB;
        private System.Windows.Forms.TextBox DisplayNameTB;
        private System.Windows.Forms.Label AttributeIdLB;
        private System.Windows.Forms.ComboBox AttributeIdCB;
        private System.Windows.Forms.TextBox IndexRangeTB;
        private Opc.Ua.Client.Controls.NodeIdCtrl NodeIdCTRL;
        private System.Windows.Forms.Panel MainPN;
    }
}
