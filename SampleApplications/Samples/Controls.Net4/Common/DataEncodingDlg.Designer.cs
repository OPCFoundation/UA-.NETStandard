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
    partial class DataEncodingDlg
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
            this.TypeNameTB = new System.Windows.Forms.TextBox();
            this.ShowEntireDictionaryLN = new System.Windows.Forms.Label();
            this.ShowEntireDictionaryCHK = new System.Windows.Forms.CheckBox();
            this.TypeSystemNameTB = new System.Windows.Forms.TextBox();
            this.DictionaryNameTB = new System.Windows.Forms.TextBox();
            this.DescriptionPN = new System.Windows.Forms.Panel();
            this.DescriptionTB = new System.Windows.Forms.RichTextBox();
            this.TypeSystemNameLB = new System.Windows.Forms.Label();
            this.EncodingCB = new System.Windows.Forms.ComboBox();
            this.DictionaryNameLB = new System.Windows.Forms.Label();
            this.TypeNameLB = new System.Windows.Forms.Label();
            this.EncodingLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.DescriptionPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 335);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(492, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(413, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.TypeNameTB);
            this.MainPN.Controls.Add(this.ShowEntireDictionaryLN);
            this.MainPN.Controls.Add(this.ShowEntireDictionaryCHK);
            this.MainPN.Controls.Add(this.TypeSystemNameTB);
            this.MainPN.Controls.Add(this.DictionaryNameTB);
            this.MainPN.Controls.Add(this.DescriptionPN);
            this.MainPN.Controls.Add(this.TypeSystemNameLB);
            this.MainPN.Controls.Add(this.EncodingCB);
            this.MainPN.Controls.Add(this.DictionaryNameLB);
            this.MainPN.Controls.Add(this.TypeNameLB);
            this.MainPN.Controls.Add(this.EncodingLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(492, 335);
            this.MainPN.TabIndex = 1;
            // 
            // TypeNameTB
            // 
            this.TypeNameTB.Location = new System.Drawing.Point(105, 76);
            this.TypeNameTB.Name = "TypeNameTB";
            this.TypeNameTB.Size = new System.Drawing.Size(202, 20);
            this.TypeNameTB.TabIndex = 23;
            // 
            // ShowEntireDictionaryLN
            // 
            this.ShowEntireDictionaryLN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ShowEntireDictionaryLN.AutoSize = true;
            this.ShowEntireDictionaryLN.Location = new System.Drawing.Point(353, 80);
            this.ShowEntireDictionaryLN.Name = "ShowEntireDictionaryLN";
            this.ShowEntireDictionaryLN.Size = new System.Drawing.Size(114, 13);
            this.ShowEntireDictionaryLN.TabIndex = 18;
            this.ShowEntireDictionaryLN.Text = "Show Entire Dictionary";
            this.ShowEntireDictionaryLN.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ShowEntireDictionaryCHK
            // 
            this.ShowEntireDictionaryCHK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ShowEntireDictionaryCHK.AutoSize = true;
            this.ShowEntireDictionaryCHK.Location = new System.Drawing.Point(472, 81);
            this.ShowEntireDictionaryCHK.Name = "ShowEntireDictionaryCHK";
            this.ShowEntireDictionaryCHK.Size = new System.Drawing.Size(15, 14);
            this.ShowEntireDictionaryCHK.TabIndex = 19;
            this.ShowEntireDictionaryCHK.UseVisualStyleBackColor = true;
            this.ShowEntireDictionaryCHK.CheckStateChanged += new System.EventHandler(this.EncodingCB_SelectedIndexChanged);
            // 
            // TypeSystemNameTB
            // 
            this.TypeSystemNameTB.Location = new System.Drawing.Point(105, 28);
            this.TypeSystemNameTB.Name = "TypeSystemNameTB";
            this.TypeSystemNameTB.Size = new System.Drawing.Size(202, 20);
            this.TypeSystemNameTB.TabIndex = 22;
            // 
            // DictionaryNameTB
            // 
            this.DictionaryNameTB.Location = new System.Drawing.Point(105, 52);
            this.DictionaryNameTB.Name = "DictionaryNameTB";
            this.DictionaryNameTB.Size = new System.Drawing.Size(202, 20);
            this.DictionaryNameTB.TabIndex = 21;
            // 
            // DescriptionPN
            // 
            this.DescriptionPN.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DescriptionPN.Controls.Add(this.DescriptionTB);
            this.DescriptionPN.Location = new System.Drawing.Point(0, 100);
            this.DescriptionPN.Name = "DescriptionPN";
            this.DescriptionPN.Size = new System.Drawing.Size(493, 235);
            this.DescriptionPN.TabIndex = 20;
            // 
            // DescriptionTB
            // 
            this.DescriptionTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DescriptionTB.Location = new System.Drawing.Point(3, 2);
            this.DescriptionTB.Name = "DescriptionTB";
            this.DescriptionTB.Size = new System.Drawing.Size(486, 233);
            this.DescriptionTB.TabIndex = 17;
            this.DescriptionTB.Text = "";
            this.DescriptionTB.WordWrap = false;
            // 
            // TypeSystemNameLB
            // 
            this.TypeSystemNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TypeSystemNameLB.AutoSize = true;
            this.TypeSystemNameLB.Location = new System.Drawing.Point(4, 32);
            this.TypeSystemNameLB.Name = "TypeSystemNameLB";
            this.TypeSystemNameLB.Size = new System.Drawing.Size(99, 13);
            this.TypeSystemNameLB.TabIndex = 15;
            this.TypeSystemNameLB.Text = "Type System Name";
            this.TypeSystemNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EncodingCB
            // 
            this.EncodingCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncodingCB.FormattingEnabled = true;
            this.EncodingCB.Location = new System.Drawing.Point(105, 4);
            this.EncodingCB.Name = "EncodingCB";
            this.EncodingCB.Size = new System.Drawing.Size(202, 21);
            this.EncodingCB.TabIndex = 11;
            this.EncodingCB.SelectedIndexChanged += new System.EventHandler(this.EncodingCB_SelectedIndexChanged);
            // 
            // DictionaryNameLB
            // 
            this.DictionaryNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DictionaryNameLB.AutoSize = true;
            this.DictionaryNameLB.Location = new System.Drawing.Point(4, 56);
            this.DictionaryNameLB.Name = "DictionaryNameLB";
            this.DictionaryNameLB.Size = new System.Drawing.Size(85, 13);
            this.DictionaryNameLB.TabIndex = 14;
            this.DictionaryNameLB.Text = "Dictionary Name";
            this.DictionaryNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TypeNameLB
            // 
            this.TypeNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TypeNameLB.AutoSize = true;
            this.TypeNameLB.Location = new System.Drawing.Point(4, 80);
            this.TypeNameLB.Name = "TypeNameLB";
            this.TypeNameLB.Size = new System.Drawing.Size(62, 13);
            this.TypeNameLB.TabIndex = 12;
            this.TypeNameLB.Text = "Type Name";
            this.TypeNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EncodingLB
            // 
            this.EncodingLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EncodingLB.AutoSize = true;
            this.EncodingLB.Location = new System.Drawing.Point(4, 8);
            this.EncodingLB.Name = "EncodingLB";
            this.EncodingLB.Size = new System.Drawing.Size(52, 13);
            this.EncodingLB.TabIndex = 10;
            this.EncodingLB.Text = "Encoding";
            this.EncodingLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DataEncodingDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(492, 366);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "DataEncodingDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Data Encoding";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.DescriptionPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label EncodingLB;
        private System.Windows.Forms.Label DictionaryNameLB;
        private System.Windows.Forms.Label TypeNameLB;
        private System.Windows.Forms.ComboBox EncodingCB;
        private System.Windows.Forms.Label TypeSystemNameLB;
        private System.Windows.Forms.Label ShowEntireDictionaryLN;
        private System.Windows.Forms.Panel DescriptionPN;
        private System.Windows.Forms.CheckBox ShowEntireDictionaryCHK;
        private System.Windows.Forms.TextBox TypeNameTB;
        private System.Windows.Forms.TextBox TypeSystemNameTB;
        private System.Windows.Forms.TextBox DictionaryNameTB;
        private System.Windows.Forms.RichTextBox DescriptionTB;
    }
}
