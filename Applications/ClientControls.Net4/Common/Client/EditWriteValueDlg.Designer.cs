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
    partial class EditWriteValueDlg
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
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.SourceTimestampCK = new System.Windows.Forms.CheckBox();
            this.ServerTimestampCK = new System.Windows.Forms.CheckBox();
            this.ServerTimestampLB = new System.Windows.Forms.Label();
            this.ServerTimestampTB = new System.Windows.Forms.TextBox();
            this.SourceTimestampTB = new System.Windows.Forms.TextBox();
            this.SourceTimestampLB = new System.Windows.Forms.Label();
            this.StatusCodeTB = new System.Windows.Forms.TextBox();
            this.StatusCodeLB = new System.Windows.Forms.Label();
            this.ValueTB = new System.Windows.Forms.TextBox();
            this.NodeLB = new System.Windows.Forms.Label();
            this.IndexRangeTB = new System.Windows.Forms.TextBox();
            this.AttributeCB = new System.Windows.Forms.ComboBox();
            this.AttributeLB = new System.Windows.Forms.Label();
            this.IndexRangeLB = new System.Windows.Forms.Label();
            this.ValueLB = new System.Windows.Forms.Label();
            this.NodeTB = new System.Windows.Forms.TextBox();
            this.NodeBTN = new Opc.Ua.Client.Controls.SelectNodeCtrl();
            this.ValueBTN = new Opc.Ua.Client.Controls.EditValue2Ctrl();
            this.StatusCodeCK = new System.Windows.Forms.CheckBox();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(282, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 189);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(360, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // MainPN
            // 
            this.MainPN.AutoSize = true;
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ControlsPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(360, 189);
            this.MainPN.TabIndex = 1;
            // 
            // ControlsPN
            // 
            this.ControlsPN.AutoSize = true;
            this.ControlsPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.SourceTimestampCK, 2, 5);
            this.ControlsPN.Controls.Add(this.ServerTimestampCK, 2, 6);
            this.ControlsPN.Controls.Add(this.ServerTimestampLB, 0, 6);
            this.ControlsPN.Controls.Add(this.ServerTimestampTB, 1, 6);
            this.ControlsPN.Controls.Add(this.SourceTimestampTB, 1, 5);
            this.ControlsPN.Controls.Add(this.SourceTimestampLB, 0, 5);
            this.ControlsPN.Controls.Add(this.StatusCodeTB, 1, 4);
            this.ControlsPN.Controls.Add(this.StatusCodeLB, 0, 4);
            this.ControlsPN.Controls.Add(this.ValueTB, 1, 3);
            this.ControlsPN.Controls.Add(this.NodeLB, 0, 0);
            this.ControlsPN.Controls.Add(this.IndexRangeTB, 1, 2);
            this.ControlsPN.Controls.Add(this.AttributeCB, 1, 1);
            this.ControlsPN.Controls.Add(this.AttributeLB, 0, 1);
            this.ControlsPN.Controls.Add(this.IndexRangeLB, 0, 2);
            this.ControlsPN.Controls.Add(this.ValueLB, 0, 3);
            this.ControlsPN.Controls.Add(this.NodeTB, 1, 0);
            this.ControlsPN.Controls.Add(this.NodeBTN, 2, 0);
            this.ControlsPN.Controls.Add(this.ValueBTN, 2, 3);
            this.ControlsPN.Controls.Add(this.StatusCodeCK, 2, 4);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 8;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.Size = new System.Drawing.Size(360, 189);
            this.ControlsPN.TabIndex = 1;
            // 
            // SourceTimestampCK
            // 
            this.SourceTimestampCK.AutoSize = true;
            this.SourceTimestampCK.Location = new System.Drawing.Point(341, 138);
            this.SourceTimestampCK.Margin = new System.Windows.Forms.Padding(5, 7, 3, 3);
            this.SourceTimestampCK.Name = "SourceTimestampCK";
            this.SourceTimestampCK.Size = new System.Drawing.Size(15, 14);
            this.SourceTimestampCK.TabIndex = 30;
            this.SourceTimestampCK.UseVisualStyleBackColor = true;
            this.SourceTimestampCK.CheckedChanged += new System.EventHandler(this.SourceTimestampCK_CheckedChanged);
            // 
            // ServerTimestampCK
            // 
            this.ServerTimestampCK.AutoSize = true;
            this.ServerTimestampCK.Location = new System.Drawing.Point(341, 164);
            this.ServerTimestampCK.Margin = new System.Windows.Forms.Padding(5, 7, 3, 3);
            this.ServerTimestampCK.Name = "ServerTimestampCK";
            this.ServerTimestampCK.Size = new System.Drawing.Size(15, 14);
            this.ServerTimestampCK.TabIndex = 29;
            this.ServerTimestampCK.UseVisualStyleBackColor = true;
            this.ServerTimestampCK.CheckedChanged += new System.EventHandler(this.ServerTimestampCK_CheckedChanged);
            // 
            // ServerTimestampLB
            // 
            this.ServerTimestampLB.AutoSize = true;
            this.ServerTimestampLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerTimestampLB.Location = new System.Drawing.Point(3, 157);
            this.ServerTimestampLB.Name = "ServerTimestampLB";
            this.ServerTimestampLB.Size = new System.Drawing.Size(95, 26);
            this.ServerTimestampLB.TabIndex = 27;
            this.ServerTimestampLB.Text = "Server Timestamp";
            this.ServerTimestampLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerTimestampTB
            // 
            this.ServerTimestampTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerTimestampTB.Enabled = false;
            this.ServerTimestampTB.Location = new System.Drawing.Point(104, 160);
            this.ServerTimestampTB.Name = "ServerTimestampTB";
            this.ServerTimestampTB.Size = new System.Drawing.Size(229, 20);
            this.ServerTimestampTB.TabIndex = 26;
            // 
            // SourceTimestampTB
            // 
            this.SourceTimestampTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SourceTimestampTB.Enabled = false;
            this.SourceTimestampTB.Location = new System.Drawing.Point(104, 134);
            this.SourceTimestampTB.Name = "SourceTimestampTB";
            this.SourceTimestampTB.Size = new System.Drawing.Size(229, 20);
            this.SourceTimestampTB.TabIndex = 25;
            // 
            // SourceTimestampLB
            // 
            this.SourceTimestampLB.AutoSize = true;
            this.SourceTimestampLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SourceTimestampLB.Location = new System.Drawing.Point(3, 131);
            this.SourceTimestampLB.Name = "SourceTimestampLB";
            this.SourceTimestampLB.Size = new System.Drawing.Size(95, 26);
            this.SourceTimestampLB.TabIndex = 24;
            this.SourceTimestampLB.Text = "Source Timestamp";
            this.SourceTimestampLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StatusCodeTB
            // 
            this.StatusCodeTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StatusCodeTB.Enabled = false;
            this.StatusCodeTB.Location = new System.Drawing.Point(104, 108);
            this.StatusCodeTB.Name = "StatusCodeTB";
            this.StatusCodeTB.Size = new System.Drawing.Size(229, 20);
            this.StatusCodeTB.TabIndex = 23;
            // 
            // StatusCodeLB
            // 
            this.StatusCodeLB.AutoSize = true;
            this.StatusCodeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StatusCodeLB.Location = new System.Drawing.Point(3, 105);
            this.StatusCodeLB.Name = "StatusCodeLB";
            this.StatusCodeLB.Size = new System.Drawing.Size(95, 26);
            this.StatusCodeLB.TabIndex = 22;
            this.StatusCodeLB.Text = "Status Code";
            this.StatusCodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ValueTB
            // 
            this.ValueTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueTB.Location = new System.Drawing.Point(104, 82);
            this.ValueTB.Name = "ValueTB";
            this.ValueTB.ReadOnly = true;
            this.ValueTB.Size = new System.Drawing.Size(229, 20);
            this.ValueTB.TabIndex = 20;
            // 
            // NodeLB
            // 
            this.NodeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.NodeLB.AutoSize = true;
            this.NodeLB.Location = new System.Drawing.Point(3, 0);
            this.NodeLB.Name = "NodeLB";
            this.NodeLB.Size = new System.Drawing.Size(33, 26);
            this.NodeLB.TabIndex = 19;
            this.NodeLB.Text = "Node";
            this.NodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IndexRangeTB
            // 
            this.IndexRangeTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IndexRangeTB.Location = new System.Drawing.Point(104, 56);
            this.IndexRangeTB.Name = "IndexRangeTB";
            this.IndexRangeTB.Size = new System.Drawing.Size(229, 20);
            this.IndexRangeTB.TabIndex = 18;
            // 
            // AttributeCB
            // 
            this.AttributeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AttributeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AttributeCB.FormattingEnabled = true;
            this.AttributeCB.Location = new System.Drawing.Point(104, 29);
            this.AttributeCB.Name = "AttributeCB";
            this.AttributeCB.Size = new System.Drawing.Size(147, 21);
            this.AttributeCB.TabIndex = 17;
            // 
            // AttributeLB
            // 
            this.AttributeLB.AutoSize = true;
            this.AttributeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributeLB.Location = new System.Drawing.Point(3, 26);
            this.AttributeLB.Name = "AttributeLB";
            this.AttributeLB.Size = new System.Drawing.Size(95, 27);
            this.AttributeLB.TabIndex = 16;
            this.AttributeLB.Text = "Attribute";
            this.AttributeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IndexRangeLB
            // 
            this.IndexRangeLB.AutoSize = true;
            this.IndexRangeLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IndexRangeLB.Location = new System.Drawing.Point(3, 53);
            this.IndexRangeLB.Name = "IndexRangeLB";
            this.IndexRangeLB.Size = new System.Drawing.Size(95, 26);
            this.IndexRangeLB.TabIndex = 3;
            this.IndexRangeLB.Text = "Index Range";
            this.IndexRangeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ValueLB
            // 
            this.ValueLB.AutoSize = true;
            this.ValueLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueLB.Location = new System.Drawing.Point(3, 79);
            this.ValueLB.Name = "ValueLB";
            this.ValueLB.Size = new System.Drawing.Size(95, 26);
            this.ValueLB.TabIndex = 5;
            this.ValueLB.Text = "Value";
            this.ValueLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeTB
            // 
            this.NodeTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NodeTB.Location = new System.Drawing.Point(104, 3);
            this.NodeTB.Name = "NodeTB";
            this.NodeTB.ReadOnly = true;
            this.NodeTB.Size = new System.Drawing.Size(229, 20);
            this.NodeTB.TabIndex = 2;
            // 
            // NodeBTN
            // 
            this.NodeBTN.Location = new System.Drawing.Point(336, 0);
            this.NodeBTN.Margin = new System.Windows.Forms.Padding(0);
            this.NodeBTN.Name = "NodeBTN";
            this.NodeBTN.NodeControl = this.NodeTB;
            this.NodeBTN.ReferenceTypeIds = null;
            this.NodeBTN.RootId = null;
            this.NodeBTN.SelectedNode = null;
            this.NodeBTN.SelectedReference = null;
            this.NodeBTN.Session = null;
            this.NodeBTN.Size = new System.Drawing.Size(24, 24);
            this.NodeBTN.TabIndex = 15;
            this.NodeBTN.View = null;
            // 
            // ValueBTN
            // 
            this.ValueBTN.CurrentValueControl = this.ValueTB;
            this.ValueBTN.Location = new System.Drawing.Point(336, 79);
            this.ValueBTN.Margin = new System.Windows.Forms.Padding(0);
            this.ValueBTN.Name = "ValueBTN";
            this.ValueBTN.Size = new System.Drawing.Size(24, 24);
            this.ValueBTN.TabIndex = 21;
            // 
            // StatusCodeCK
            // 
            this.StatusCodeCK.AutoSize = true;
            this.StatusCodeCK.Location = new System.Drawing.Point(341, 112);
            this.StatusCodeCK.Margin = new System.Windows.Forms.Padding(5, 7, 3, 3);
            this.StatusCodeCK.Name = "StatusCodeCK";
            this.StatusCodeCK.Size = new System.Drawing.Size(15, 14);
            this.StatusCodeCK.TabIndex = 28;
            this.StatusCodeCK.UseVisualStyleBackColor = true;
            this.StatusCodeCK.CheckedChanged += new System.EventHandler(this.StatusCodeCK_CheckedChanged);
            // 
            // EditWriteValueDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(360, 219);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditWriteValueDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Write Request";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.Label NodeLB;
        private System.Windows.Forms.TextBox IndexRangeTB;
        private System.Windows.Forms.ComboBox AttributeCB;
        private System.Windows.Forms.Label AttributeLB;
        private System.Windows.Forms.Label IndexRangeLB;
        private System.Windows.Forms.Label ValueLB;
        private System.Windows.Forms.TextBox NodeTB;
        private SelectNodeCtrl NodeBTN;
        private System.Windows.Forms.TextBox ValueTB;
        private EditValue2Ctrl ValueBTN;
        private System.Windows.Forms.TextBox StatusCodeTB;
        private System.Windows.Forms.Label StatusCodeLB;
        private System.Windows.Forms.Label ServerTimestampLB;
        private System.Windows.Forms.TextBox ServerTimestampTB;
        private System.Windows.Forms.TextBox SourceTimestampTB;
        private System.Windows.Forms.Label SourceTimestampLB;
        private System.Windows.Forms.CheckBox SourceTimestampCK;
        private System.Windows.Forms.CheckBox ServerTimestampCK;
        private System.Windows.Forms.CheckBox StatusCodeCK;
    }
}
