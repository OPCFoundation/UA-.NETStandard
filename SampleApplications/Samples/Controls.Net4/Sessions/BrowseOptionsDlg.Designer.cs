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
    partial class BrowseOptionsDlg
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
            this.ViewVersionCK = new System.Windows.Forms.CheckBox();
            this.ViewVersionNC = new System.Windows.Forms.NumericUpDown();
            this.ViewVersionLB = new System.Windows.Forms.Label();
            this.ViewTimestampCK = new System.Windows.Forms.CheckBox();
            this.ViewTimestampDP = new System.Windows.Forms.DateTimePicker();
            this.ViewTimestampLB = new System.Windows.Forms.Label();
            this.BrowseBTN = new System.Windows.Forms.Button();
            this.ViewIdTB = new System.Windows.Forms.TextBox();
            this.ViewIdLB = new System.Windows.Forms.Label();
            this.ReferenceTypeCTRL = new Opc.Ua.Sample.Controls.ReferenceTypeCtrl();
            this.NodeClassList = new System.Windows.Forms.CheckedListBox();
            this.NodeClassMaskCK = new System.Windows.Forms.CheckBox();
            this.NodeClassMaskLB = new System.Windows.Forms.Label();
            this.IncludeSubtypesCK = new System.Windows.Forms.CheckBox();
            this.BrowseDirectionCB = new System.Windows.Forms.ComboBox();
            this.MaxReferencesReturnedNC = new System.Windows.Forms.NumericUpDown();
            this.BrowseDirectionLB = new System.Windows.Forms.Label();
            this.IncludeSubtypesLB = new System.Windows.Forms.Label();
            this.ReferenceTypeLB = new System.Windows.Forms.Label();
            this.MaxReferencesReturnedLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ViewVersionNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReferencesReturnedNC)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 323);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(346, 31);
            this.ButtonsPN.TabIndex = 1;
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
            this.CancelBTN.Location = new System.Drawing.Point(267, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ViewVersionCK);
            this.MainPN.Controls.Add(this.ViewVersionNC);
            this.MainPN.Controls.Add(this.ViewVersionLB);
            this.MainPN.Controls.Add(this.ViewTimestampCK);
            this.MainPN.Controls.Add(this.ViewTimestampDP);
            this.MainPN.Controls.Add(this.ViewTimestampLB);
            this.MainPN.Controls.Add(this.BrowseBTN);
            this.MainPN.Controls.Add(this.ViewIdTB);
            this.MainPN.Controls.Add(this.ViewIdLB);
            this.MainPN.Controls.Add(this.ReferenceTypeCTRL);
            this.MainPN.Controls.Add(this.NodeClassList);
            this.MainPN.Controls.Add(this.NodeClassMaskCK);
            this.MainPN.Controls.Add(this.NodeClassMaskLB);
            this.MainPN.Controls.Add(this.IncludeSubtypesCK);
            this.MainPN.Controls.Add(this.BrowseDirectionCB);
            this.MainPN.Controls.Add(this.MaxReferencesReturnedNC);
            this.MainPN.Controls.Add(this.BrowseDirectionLB);
            this.MainPN.Controls.Add(this.IncludeSubtypesLB);
            this.MainPN.Controls.Add(this.ReferenceTypeLB);
            this.MainPN.Controls.Add(this.MaxReferencesReturnedLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(346, 323);
            this.MainPN.TabIndex = 2;
            // 
            // ViewVersionCK
            // 
            this.ViewVersionCK.AutoSize = true;
            this.ViewVersionCK.Enabled = false;
            this.ViewVersionCK.Location = new System.Drawing.Point(276, 56);
            this.ViewVersionCK.Name = "ViewVersionCK";
            this.ViewVersionCK.Size = new System.Drawing.Size(15, 14);
            this.ViewVersionCK.TabIndex = 23;
            this.ViewVersionCK.UseVisualStyleBackColor = true;
            this.ViewVersionCK.CheckedChanged += new System.EventHandler(this.ViewVersionCK_CheckedChanged);
            // 
            // ViewVersionNC
            // 
            this.ViewVersionNC.Enabled = false;
            this.ViewVersionNC.Location = new System.Drawing.Point(140, 52);
            this.ViewVersionNC.Name = "ViewVersionNC";
            this.ViewVersionNC.Size = new System.Drawing.Size(130, 20);
            this.ViewVersionNC.TabIndex = 22;
            // 
            // ViewVersionLB
            // 
            this.ViewVersionLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ViewVersionLB.AutoSize = true;
            this.ViewVersionLB.Location = new System.Drawing.Point(4, 56);
            this.ViewVersionLB.Name = "ViewVersionLB";
            this.ViewVersionLB.Size = new System.Drawing.Size(68, 13);
            this.ViewVersionLB.TabIndex = 21;
            this.ViewVersionLB.Text = "View Version";
            this.ViewVersionLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ViewTimestampCK
            // 
            this.ViewTimestampCK.AutoSize = true;
            this.ViewTimestampCK.Enabled = false;
            this.ViewTimestampCK.Location = new System.Drawing.Point(276, 31);
            this.ViewTimestampCK.Name = "ViewTimestampCK";
            this.ViewTimestampCK.Size = new System.Drawing.Size(15, 14);
            this.ViewTimestampCK.TabIndex = 20;
            this.ViewTimestampCK.UseVisualStyleBackColor = true;
            this.ViewTimestampCK.CheckedChanged += new System.EventHandler(this.ViewTimestampCK_CheckedChanged);
            // 
            // ViewTimestampDP
            // 
            this.ViewTimestampDP.CustomFormat = "yyyy-MM-hh HH:mm:ss";
            this.ViewTimestampDP.Enabled = false;
            this.ViewTimestampDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.ViewTimestampDP.Location = new System.Drawing.Point(140, 28);
            this.ViewTimestampDP.Name = "ViewTimestampDP";
            this.ViewTimestampDP.Size = new System.Drawing.Size(130, 20);
            this.ViewTimestampDP.TabIndex = 19;
            this.ViewTimestampDP.Value = new System.DateTime(2000, 1, 1, 0, 0, 0, 0);
            // 
            // ViewTimestampLB
            // 
            this.ViewTimestampLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ViewTimestampLB.AutoSize = true;
            this.ViewTimestampLB.Location = new System.Drawing.Point(4, 32);
            this.ViewTimestampLB.Name = "ViewTimestampLB";
            this.ViewTimestampLB.Size = new System.Drawing.Size(84, 13);
            this.ViewTimestampLB.TabIndex = 18;
            this.ViewTimestampLB.Text = "View Timestamp";
            this.ViewTimestampLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BrowseBTN
            // 
            this.BrowseBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseBTN.Location = new System.Drawing.Point(319, 3);
            this.BrowseBTN.Name = "BrowseBTN";
            this.BrowseBTN.Size = new System.Drawing.Size(25, 21);
            this.BrowseBTN.TabIndex = 17;
            this.BrowseBTN.Text = "...";
            this.BrowseBTN.UseVisualStyleBackColor = true;
            this.BrowseBTN.Click += new System.EventHandler(this.BrowseBTN_Click);
            // 
            // ViewIdTB
            // 
            this.ViewIdTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ViewIdTB.Location = new System.Drawing.Point(140, 4);
            this.ViewIdTB.Name = "ViewIdTB";
            this.ViewIdTB.Size = new System.Drawing.Size(177, 20);
            this.ViewIdTB.TabIndex = 16;
            this.ViewIdTB.TextChanged += new System.EventHandler(this.ViewIdTB_TextChanged);
            // 
            // ViewIdLB
            // 
            this.ViewIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ViewIdLB.AutoSize = true;
            this.ViewIdLB.Location = new System.Drawing.Point(4, 8);
            this.ViewIdLB.Name = "ViewIdLB";
            this.ViewIdLB.Size = new System.Drawing.Size(42, 13);
            this.ViewIdLB.TabIndex = 15;
            this.ViewIdLB.Text = "View Id";
            this.ViewIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReferenceTypeCTRL
            // 
            this.ReferenceTypeCTRL.Location = new System.Drawing.Point(140, 124);
            this.ReferenceTypeCTRL.MaximumSize = new System.Drawing.Size(4096, 21);
            this.ReferenceTypeCTRL.MinimumSize = new System.Drawing.Size(200, 21);
            this.ReferenceTypeCTRL.Name = "ReferenceTypeCTRL";
            this.ReferenceTypeCTRL.SelectedTypeId = null;
            this.ReferenceTypeCTRL.Size = new System.Drawing.Size(200, 21);
            this.ReferenceTypeCTRL.TabIndex = 14;
            // 
            // NodeClassList
            // 
            this.NodeClassList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeClassList.CheckOnClick = true;
            this.NodeClassList.Enabled = false;
            this.NodeClassList.FormattingEnabled = true;
            this.NodeClassList.Items.AddRange(new object[] {
            "Object",
            "ObjectType",
            "Variable",
            "VariableType",
            "ReferenceType",
            "DataType",
            "Method",
            "View"});
            this.NodeClassList.Location = new System.Drawing.Point(5, 196);
            this.NodeClassList.Name = "NodeClassList";
            this.NodeClassList.Size = new System.Drawing.Size(335, 124);
            this.NodeClassList.TabIndex = 8;
            // 
            // NodeClassMaskCK
            // 
            this.NodeClassMaskCK.AutoSize = true;
            this.NodeClassMaskCK.Location = new System.Drawing.Point(140, 176);
            this.NodeClassMaskCK.Name = "NodeClassMaskCK";
            this.NodeClassMaskCK.Size = new System.Drawing.Size(15, 14);
            this.NodeClassMaskCK.TabIndex = 13;
            this.NodeClassMaskCK.UseVisualStyleBackColor = true;
            this.NodeClassMaskCK.CheckedChanged += new System.EventHandler(this.NodeClassMask_CheckedChanged);
            // 
            // NodeClassMaskLB
            // 
            this.NodeClassMaskLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeClassMaskLB.AutoSize = true;
            this.NodeClassMaskLB.Location = new System.Drawing.Point(4, 176);
            this.NodeClassMaskLB.Name = "NodeClassMaskLB";
            this.NodeClassMaskLB.Size = new System.Drawing.Size(90, 13);
            this.NodeClassMaskLB.TabIndex = 12;
            this.NodeClassMaskLB.Text = "Node Class Mask";
            this.NodeClassMaskLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IncludeSubtypesCK
            // 
            this.IncludeSubtypesCK.AutoSize = true;
            this.IncludeSubtypesCK.Location = new System.Drawing.Point(140, 152);
            this.IncludeSubtypesCK.Name = "IncludeSubtypesCK";
            this.IncludeSubtypesCK.Size = new System.Drawing.Size(15, 14);
            this.IncludeSubtypesCK.TabIndex = 11;
            this.IncludeSubtypesCK.UseVisualStyleBackColor = true;
            // 
            // BrowseDirectionCB
            // 
            this.BrowseDirectionCB.FormattingEnabled = true;
            this.BrowseDirectionCB.Location = new System.Drawing.Point(140, 100);
            this.BrowseDirectionCB.Name = "BrowseDirectionCB";
            this.BrowseDirectionCB.Size = new System.Drawing.Size(130, 21);
            this.BrowseDirectionCB.TabIndex = 9;
            // 
            // MaxReferencesReturnedNC
            // 
            this.MaxReferencesReturnedNC.Location = new System.Drawing.Point(140, 76);
            this.MaxReferencesReturnedNC.Name = "MaxReferencesReturnedNC";
            this.MaxReferencesReturnedNC.Size = new System.Drawing.Size(130, 20);
            this.MaxReferencesReturnedNC.TabIndex = 8;
            // 
            // BrowseDirectionLB
            // 
            this.BrowseDirectionLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BrowseDirectionLB.AutoSize = true;
            this.BrowseDirectionLB.Location = new System.Drawing.Point(4, 104);
            this.BrowseDirectionLB.Name = "BrowseDirectionLB";
            this.BrowseDirectionLB.Size = new System.Drawing.Size(87, 13);
            this.BrowseDirectionLB.TabIndex = 6;
            this.BrowseDirectionLB.Text = "Browse Direction";
            this.BrowseDirectionLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IncludeSubtypesLB
            // 
            this.IncludeSubtypesLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IncludeSubtypesLB.AutoSize = true;
            this.IncludeSubtypesLB.Location = new System.Drawing.Point(4, 152);
            this.IncludeSubtypesLB.Name = "IncludeSubtypesLB";
            this.IncludeSubtypesLB.Size = new System.Drawing.Size(96, 13);
            this.IncludeSubtypesLB.TabIndex = 5;
            this.IncludeSubtypesLB.Text = "Include Sub Types";
            this.IncludeSubtypesLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ReferenceTypeLB
            // 
            this.ReferenceTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ReferenceTypeLB.AutoSize = true;
            this.ReferenceTypeLB.Location = new System.Drawing.Point(4, 128);
            this.ReferenceTypeLB.Name = "ReferenceTypeLB";
            this.ReferenceTypeLB.Size = new System.Drawing.Size(84, 13);
            this.ReferenceTypeLB.TabIndex = 4;
            this.ReferenceTypeLB.Text = "Reference Type";
            this.ReferenceTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxReferencesReturnedLB
            // 
            this.MaxReferencesReturnedLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxReferencesReturnedLB.AutoSize = true;
            this.MaxReferencesReturnedLB.Location = new System.Drawing.Point(4, 80);
            this.MaxReferencesReturnedLB.Name = "MaxReferencesReturnedLB";
            this.MaxReferencesReturnedLB.Size = new System.Drawing.Size(132, 13);
            this.MaxReferencesReturnedLB.TabIndex = 2;
            this.MaxReferencesReturnedLB.Text = "Max References Returned";
            this.MaxReferencesReturnedLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BrowseOptionsDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(346, 354);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "BrowseOptionsDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Browse Options";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ViewVersionNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxReferencesReturnedNC)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label MaxReferencesReturnedLB;
        private System.Windows.Forms.Label BrowseDirectionLB;
        private System.Windows.Forms.Label IncludeSubtypesLB;
        private System.Windows.Forms.Label ReferenceTypeLB;
        private System.Windows.Forms.CheckedListBox NodeClassList;
        private System.Windows.Forms.CheckBox IncludeSubtypesCK;
        private System.Windows.Forms.ComboBox BrowseDirectionCB;
        private System.Windows.Forms.NumericUpDown MaxReferencesReturnedNC;
        private System.Windows.Forms.CheckBox NodeClassMaskCK;
        private System.Windows.Forms.Label NodeClassMaskLB;
        private System.Windows.Forms.Label ViewIdLB;
        private Opc.Ua.Sample.Controls.ReferenceTypeCtrl ReferenceTypeCTRL;
        private System.Windows.Forms.Label ViewVersionLB;
        private System.Windows.Forms.CheckBox ViewTimestampCK;
        private System.Windows.Forms.DateTimePicker ViewTimestampDP;
        private System.Windows.Forms.Label ViewTimestampLB;
        private System.Windows.Forms.Button BrowseBTN;
        private System.Windows.Forms.TextBox ViewIdTB;
        private System.Windows.Forms.CheckBox ViewVersionCK;
        private System.Windows.Forms.NumericUpDown ViewVersionNC;
    }
}
