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
    partial class CertificateStoreCtrl
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
            this.StorePathCB = new System.Windows.Forms.ComboBox();
            this.BrowseBTN = new System.Windows.Forms.Button();
            this.StoreTypeCB = new System.Windows.Forms.ComboBox();
            this.StoreTypeLB = new System.Windows.Forms.Label();
            this.StorePathLB = new System.Windows.Forms.Label();
            this.LeftPN = new System.Windows.Forms.Panel();
            this.RightPN = new System.Windows.Forms.Panel();
            this.LeftPN.SuspendLayout();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // StorePathCB
            // 
            this.StorePathCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StorePathCB.FormattingEnabled = true;
            this.StorePathCB.Location = new System.Drawing.Point(0, 27);
            this.StorePathCB.Name = "StorePathCB";
            this.StorePathCB.Size = new System.Drawing.Size(221, 21);
            this.StorePathCB.TabIndex = 1;
            this.StorePathCB.SelectedIndexChanged += new System.EventHandler(this.StorePathCB_SelectedIndexChanged);
            this.StorePathCB.DropDown += new System.EventHandler(this.StorePathCB_DropDown);
            this.StorePathCB.TextChanged += new System.EventHandler(this.StorePathCB_TextChanged);
            // 
            // BrowseBTN
            // 
            this.BrowseBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseBTN.Location = new System.Drawing.Point(227, 26);
            this.BrowseBTN.Name = "BrowseBTN";
            this.BrowseBTN.Size = new System.Drawing.Size(75, 23);
            this.BrowseBTN.TabIndex = 2;
            this.BrowseBTN.Text = "Browse...";
            this.BrowseBTN.UseVisualStyleBackColor = true;
            this.BrowseBTN.Click += new System.EventHandler(this.BrowseStoreBTN_Click);
            // 
            // StoreTypeCB
            // 
            this.StoreTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.StoreTypeCB.FormattingEnabled = true;
            this.StoreTypeCB.Location = new System.Drawing.Point(0, 0);
            this.StoreTypeCB.Name = "StoreTypeCB";
            this.StoreTypeCB.Size = new System.Drawing.Size(111, 21);
            this.StoreTypeCB.TabIndex = 0;
            this.StoreTypeCB.SelectedIndexChanged += new System.EventHandler(this.StoreTypeCB_SelectedIndexChanged);
            // 
            // StoreTypeLB
            // 
            this.StoreTypeLB.AutoSize = true;
            this.StoreTypeLB.Location = new System.Drawing.Point(0, 3);
            this.StoreTypeLB.Name = "StoreTypeLB";
            this.StoreTypeLB.Size = new System.Drawing.Size(59, 13);
            this.StoreTypeLB.TabIndex = 0;
            this.StoreTypeLB.Text = "Store Type";
            // 
            // StorePathLB
            // 
            this.StorePathLB.AutoSize = true;
            this.StorePathLB.Location = new System.Drawing.Point(0, 30);
            this.StorePathLB.Name = "StorePathLB";
            this.StorePathLB.Size = new System.Drawing.Size(57, 13);
            this.StorePathLB.TabIndex = 1;
            this.StorePathLB.Text = "Store Path";
            // 
            // LeftPN
            // 
            this.LeftPN.Controls.Add(this.StorePathLB);
            this.LeftPN.Controls.Add(this.StoreTypeLB);
            this.LeftPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPN.Location = new System.Drawing.Point(0, 0);
            this.LeftPN.Name = "LeftPN";
            this.LeftPN.Size = new System.Drawing.Size(75, 51);
            this.LeftPN.TabIndex = 0;
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.StoreTypeCB);
            this.RightPN.Controls.Add(this.StorePathCB);
            this.RightPN.Controls.Add(this.BrowseBTN);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(75, 0);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(302, 51);
            this.RightPN.TabIndex = 9;
            // 
            // CertificateStoreCtrl
            // 
            this.Controls.Add(this.RightPN);
            this.Controls.Add(this.LeftPN);
            this.MinimumSize = new System.Drawing.Size(300, 51);
            this.Name = "CertificateStoreCtrl";
            this.Size = new System.Drawing.Size(377, 51);
            this.LeftPN.ResumeLayout(false);
            this.LeftPN.PerformLayout();
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox StorePathCB;
        private System.Windows.Forms.Button BrowseBTN;
        private System.Windows.Forms.ComboBox StoreTypeCB;
        private System.Windows.Forms.Label StoreTypeLB;
        private System.Windows.Forms.Label StorePathLB;
        private System.Windows.Forms.Panel LeftPN;
        private System.Windows.Forms.Panel RightPN;

    }
}
