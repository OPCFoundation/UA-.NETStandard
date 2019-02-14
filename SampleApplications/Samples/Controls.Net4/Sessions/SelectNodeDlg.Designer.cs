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
    partial class SelectNodeDlg
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
            this.NodeClassCB = new System.Windows.Forms.ComboBox();
            this.NodeClassLB = new System.Windows.Forms.Label();
            this.DisplayNameTB = new System.Windows.Forms.TextBox();
            this.DisplayNameLB = new System.Windows.Forms.Label();
            this.IdentifierTypeCB = new System.Windows.Forms.ComboBox();
            this.IdentifierTypeLB = new System.Windows.Forms.Label();
            this.BrowseCTRL = new Opc.Ua.Sample.Controls.BrowseTreeCtrl();
            this.NamespaceUriCB = new System.Windows.Forms.ComboBox();
            this.NamespaceUriLB = new System.Windows.Forms.Label();
            this.NodeIdentifierTB = new System.Windows.Forms.TextBox();
            this.NodeIdentifierLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 421);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(426, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(347, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.NodeClassCB);
            this.MainPN.Controls.Add(this.NodeClassLB);
            this.MainPN.Controls.Add(this.DisplayNameTB);
            this.MainPN.Controls.Add(this.DisplayNameLB);
            this.MainPN.Controls.Add(this.IdentifierTypeCB);
            this.MainPN.Controls.Add(this.IdentifierTypeLB);
            this.MainPN.Controls.Add(this.BrowseCTRL);
            this.MainPN.Controls.Add(this.NamespaceUriCB);
            this.MainPN.Controls.Add(this.NamespaceUriLB);
            this.MainPN.Controls.Add(this.NodeIdentifierTB);
            this.MainPN.Controls.Add(this.NodeIdentifierLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(426, 421);
            this.MainPN.TabIndex = 1;
            // 
            // NodeClassCB
            // 
            this.NodeClassCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.NodeClassCB.FormattingEnabled = true;
            this.NodeClassCB.Location = new System.Drawing.Point(98, 100);
            this.NodeClassCB.Name = "NodeClassCB";
            this.NodeClassCB.Size = new System.Drawing.Size(122, 21);
            this.NodeClassCB.TabIndex = 35;
            // 
            // NodeClassLB
            // 
            this.NodeClassLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeClassLB.AutoSize = true;
            this.NodeClassLB.Location = new System.Drawing.Point(4, 104);
            this.NodeClassLB.Name = "NodeClassLB";
            this.NodeClassLB.Size = new System.Drawing.Size(61, 13);
            this.NodeClassLB.TabIndex = 34;
            this.NodeClassLB.Text = "Node Class";
            this.NodeClassLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameTB
            // 
            this.DisplayNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameTB.Location = new System.Drawing.Point(98, 5);
            this.DisplayNameTB.Name = "DisplayNameTB";
            this.DisplayNameTB.Size = new System.Drawing.Size(323, 20);
            this.DisplayNameTB.TabIndex = 33;
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
            this.DisplayNameLB.TabIndex = 32;
            this.DisplayNameLB.Text = "Display Name";
            this.DisplayNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IdentifierTypeCB
            // 
            this.IdentifierTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.IdentifierTypeCB.FormattingEnabled = true;
            this.IdentifierTypeCB.Location = new System.Drawing.Point(98, 28);
            this.IdentifierTypeCB.Name = "IdentifierTypeCB";
            this.IdentifierTypeCB.Size = new System.Drawing.Size(122, 21);
            this.IdentifierTypeCB.TabIndex = 31;
            this.IdentifierTypeCB.SelectedIndexChanged += new System.EventHandler(this.IdentifierTypeCB_SelectedIndexChanged);
            // 
            // IdentifierTypeLB
            // 
            this.IdentifierTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IdentifierTypeLB.AutoSize = true;
            this.IdentifierTypeLB.Location = new System.Drawing.Point(4, 32);
            this.IdentifierTypeLB.Name = "IdentifierTypeLB";
            this.IdentifierTypeLB.Size = new System.Drawing.Size(74, 13);
            this.IdentifierTypeLB.TabIndex = 30;
            this.IdentifierTypeLB.Text = "Identifier Type";
            this.IdentifierTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseCTRL.AttributesCtrl = null;
            this.BrowseCTRL.EnableDragging = false;
            this.BrowseCTRL.Location = new System.Drawing.Point(4, 127);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.SessionTreeCtrl = null;
            this.BrowseCTRL.Size = new System.Drawing.Size(418, 291);
            this.BrowseCTRL.TabIndex = 29;
            this.BrowseCTRL.NodeSelected += new Opc.Ua.Client.Controls.TreeNodeActionEventHandler(this.BrowseCTRL_NodeSelected);
            // 
            // NamespaceUriCB
            // 
            this.NamespaceUriCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NamespaceUriCB.FormattingEnabled = true;
            this.NamespaceUriCB.Location = new System.Drawing.Point(98, 76);
            this.NamespaceUriCB.Name = "NamespaceUriCB";
            this.NamespaceUriCB.Size = new System.Drawing.Size(323, 21);
            this.NamespaceUriCB.TabIndex = 28;
            this.NamespaceUriCB.SelectedIndexChanged += new System.EventHandler(this.NamespaceUriCB_SelectedIndexChanged);
            this.NamespaceUriCB.TextChanged += new System.EventHandler(this.NamespaceUriCB_TextChanged);
            // 
            // NamespaceUriLB
            // 
            this.NamespaceUriLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NamespaceUriLB.AutoSize = true;
            this.NamespaceUriLB.Location = new System.Drawing.Point(4, 80);
            this.NamespaceUriLB.Name = "NamespaceUriLB";
            this.NamespaceUriLB.Size = new System.Drawing.Size(86, 13);
            this.NamespaceUriLB.TabIndex = 27;
            this.NamespaceUriLB.Text = "Namespace URI";
            this.NamespaceUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeIdentifierTB
            // 
            this.NodeIdentifierTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeIdentifierTB.Location = new System.Drawing.Point(98, 52);
            this.NodeIdentifierTB.Name = "NodeIdentifierTB";
            this.NodeIdentifierTB.Size = new System.Drawing.Size(323, 20);
            this.NodeIdentifierTB.TabIndex = 26;
            this.NodeIdentifierTB.TextChanged += new System.EventHandler(this.NodeIdentifierTB_TextChanged);
            // 
            // NodeIdentifierLB
            // 
            this.NodeIdentifierLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeIdentifierLB.AutoSize = true;
            this.NodeIdentifierLB.Location = new System.Drawing.Point(4, 56);
            this.NodeIdentifierLB.Name = "NodeIdentifierLB";
            this.NodeIdentifierLB.Size = new System.Drawing.Size(76, 13);
            this.NodeIdentifierLB.TabIndex = 17;
            this.NodeIdentifierLB.Text = "Node Identifier";
            this.NodeIdentifierLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SelectNodeDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(426, 452);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(300, 300);
            this.Name = "SelectNodeDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Select Node";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.TextBox NodeIdentifierTB;
        private System.Windows.Forms.Label NodeIdentifierLB;
        private System.Windows.Forms.Label NamespaceUriLB;
        private BrowseTreeCtrl BrowseCTRL;
        private System.Windows.Forms.ComboBox NamespaceUriCB;
        private System.Windows.Forms.ComboBox IdentifierTypeCB;
        private System.Windows.Forms.Label IdentifierTypeLB;
        private System.Windows.Forms.TextBox DisplayNameTB;
        private System.Windows.Forms.Label DisplayNameLB;
        private System.Windows.Forms.ComboBox NodeClassCB;
        private System.Windows.Forms.Label NodeClassLB;
    }
}
