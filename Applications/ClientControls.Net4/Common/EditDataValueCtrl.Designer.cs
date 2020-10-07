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
    partial class EditDataValueCtrl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.InsertMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ReplaceMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ServerTimestampCK = new System.Windows.Forms.CheckBox();
            this.ServerTimestampDP = new System.Windows.Forms.DateTimePicker();
            this.SourceTimestampDP = new System.Windows.Forms.DateTimePicker();
            this.SourceTimestampLB = new System.Windows.Forms.Label();
            this.StatusCodeLB = new System.Windows.Forms.Label();
            this.ServerTimestampLB = new System.Windows.Forms.Label();
            this.StatusCodeCB = new System.Windows.Forms.ComboBox();
            this.ValueLN = new System.Windows.Forms.Label();
            this.SourceTimestampCK = new System.Windows.Forms.CheckBox();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.DataTypeLB = new System.Windows.Forms.Label();
            this.DataTypeCB = new System.Windows.Forms.ComboBox();
            this.ValueRankLB = new System.Windows.Forms.Label();
            this.ValueRankCB = new System.Windows.Forms.ComboBox();
            this.StatusCodeCK = new System.Windows.Forms.CheckBox();
            this.ValueTB = new System.Windows.Forms.TextBox();
            this.PopupMenu.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.InsertMI,
            this.ReplaceMI,
            this.DeleteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(125, 70);
            // 
            // InsertMI
            // 
            this.InsertMI.Name = "InsertMI";
            this.InsertMI.Size = new System.Drawing.Size(124, 22);
            this.InsertMI.Text = "Insert...";
            // 
            // ReplaceMI
            // 
            this.ReplaceMI.Name = "ReplaceMI";
            this.ReplaceMI.Size = new System.Drawing.Size(124, 22);
            this.ReplaceMI.Text = "Replace...";
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(124, 22);
            this.DeleteMI.Text = "Delete...";
            // 
            // ServerTimestampCK
            // 
            this.ServerTimestampCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ServerTimestampCK.AutoSize = true;
            this.ServerTimestampCK.Location = new System.Drawing.Point(248, 136);
            this.ServerTimestampCK.Name = "ServerTimestampCK";
            this.ServerTimestampCK.Size = new System.Drawing.Size(15, 20);
            this.ServerTimestampCK.TabIndex = 0;
            this.ServerTimestampCK.UseVisualStyleBackColor = true;
            this.ServerTimestampCK.CheckedChanged += new System.EventHandler(this.ServerTimestampCK_CheckedChanged);
            // 
            // ServerTimestampDP
            // 
            this.ServerTimestampDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ServerTimestampDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.ServerTimestampDP.Enabled = false;
            this.ServerTimestampDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.ServerTimestampDP.Location = new System.Drawing.Point(104, 136);
            this.ServerTimestampDP.Name = "ServerTimestampDP";
            this.ServerTimestampDP.Size = new System.Drawing.Size(138, 20);
            this.ServerTimestampDP.TabIndex = 14;
            // 
            // SourceTimestampDP
            // 
            this.SourceTimestampDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.SourceTimestampDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.SourceTimestampDP.Enabled = false;
            this.SourceTimestampDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.SourceTimestampDP.Location = new System.Drawing.Point(104, 110);
            this.SourceTimestampDP.Name = "SourceTimestampDP";
            this.SourceTimestampDP.Size = new System.Drawing.Size(138, 20);
            this.SourceTimestampDP.TabIndex = 11;
            // 
            // SourceTimestampLB
            // 
            this.SourceTimestampLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.SourceTimestampLB.AutoSize = true;
            this.SourceTimestampLB.Location = new System.Drawing.Point(3, 107);
            this.SourceTimestampLB.Name = "SourceTimestampLB";
            this.SourceTimestampLB.Size = new System.Drawing.Size(95, 26);
            this.SourceTimestampLB.TabIndex = 10;
            this.SourceTimestampLB.Text = "Source Timestamp";
            this.SourceTimestampLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StatusCodeLB
            // 
            this.StatusCodeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StatusCodeLB.AutoSize = true;
            this.StatusCodeLB.Location = new System.Drawing.Point(3, 80);
            this.StatusCodeLB.Name = "StatusCodeLB";
            this.StatusCodeLB.Size = new System.Drawing.Size(65, 27);
            this.StatusCodeLB.TabIndex = 7;
            this.StatusCodeLB.Text = "Status Code";
            this.StatusCodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerTimestampLB
            // 
            this.ServerTimestampLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ServerTimestampLB.AutoSize = true;
            this.ServerTimestampLB.Location = new System.Drawing.Point(3, 133);
            this.ServerTimestampLB.Name = "ServerTimestampLB";
            this.ServerTimestampLB.Size = new System.Drawing.Size(92, 26);
            this.ServerTimestampLB.TabIndex = 13;
            this.ServerTimestampLB.Text = "Server Timestamp";
            this.ServerTimestampLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StatusCodeCB
            // 
            this.StatusCodeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StatusCodeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.StatusCodeCB.Enabled = false;
            this.StatusCodeCB.FormattingEnabled = true;
            this.StatusCodeCB.Location = new System.Drawing.Point(104, 83);
            this.StatusCodeCB.Name = "StatusCodeCB";
            this.StatusCodeCB.Size = new System.Drawing.Size(138, 21);
            this.StatusCodeCB.TabIndex = 8;
            // 
            // ValueLN
            // 
            this.ValueLN.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ValueLN.AutoSize = true;
            this.ValueLN.Location = new System.Drawing.Point(3, 0);
            this.ValueLN.Name = "ValueLN";
            this.ValueLN.Size = new System.Drawing.Size(34, 26);
            this.ValueLN.TabIndex = 1;
            this.ValueLN.Text = "Value";
            this.ValueLN.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SourceTimestampCK
            // 
            this.SourceTimestampCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.SourceTimestampCK.AutoSize = true;
            this.SourceTimestampCK.Location = new System.Drawing.Point(248, 110);
            this.SourceTimestampCK.Name = "SourceTimestampCK";
            this.SourceTimestampCK.Size = new System.Drawing.Size(15, 20);
            this.SourceTimestampCK.TabIndex = 12;
            this.SourceTimestampCK.UseVisualStyleBackColor = true;
            this.SourceTimestampCK.CheckedChanged += new System.EventHandler(this.SourceTimestampCK_CheckedChanged);
            // 
            // ControlsPN
            // 
            this.ControlsPN.AutoSize = true;
            this.ControlsPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.ValueLN, 0, 0);
            this.ControlsPN.Controls.Add(this.DataTypeLB, 0, 1);
            this.ControlsPN.Controls.Add(this.DataTypeCB, 1, 1);
            this.ControlsPN.Controls.Add(this.ServerTimestampLB, 0, 5);
            this.ControlsPN.Controls.Add(this.ServerTimestampDP, 1, 5);
            this.ControlsPN.Controls.Add(this.SourceTimestampDP, 1, 4);
            this.ControlsPN.Controls.Add(this.SourceTimestampLB, 0, 4);
            this.ControlsPN.Controls.Add(this.StatusCodeLB, 0, 3);
            this.ControlsPN.Controls.Add(this.StatusCodeCB, 1, 3);
            this.ControlsPN.Controls.Add(this.ServerTimestampCK, 2, 5);
            this.ControlsPN.Controls.Add(this.SourceTimestampCK, 2, 4);
            this.ControlsPN.Controls.Add(this.ValueRankLB, 0, 2);
            this.ControlsPN.Controls.Add(this.ValueRankCB, 1, 2);
            this.ControlsPN.Controls.Add(this.StatusCodeCK, 2, 3);
            this.ControlsPN.Controls.Add(this.ValueTB, 1, 0);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 7;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.Size = new System.Drawing.Size(266, 159);
            this.ControlsPN.TabIndex = 0;
            // 
            // DataTypeLB
            // 
            this.DataTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.DataTypeLB.AutoSize = true;
            this.DataTypeLB.Location = new System.Drawing.Point(3, 26);
            this.DataTypeLB.Name = "DataTypeLB";
            this.DataTypeLB.Size = new System.Drawing.Size(57, 27);
            this.DataTypeLB.TabIndex = 3;
            this.DataTypeLB.Text = "Data Type";
            this.DataTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DataTypeCB
            // 
            this.DataTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.DataTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DataTypeCB.FormattingEnabled = true;
            this.DataTypeCB.Location = new System.Drawing.Point(104, 29);
            this.DataTypeCB.Name = "DataTypeCB";
            this.DataTypeCB.Size = new System.Drawing.Size(138, 21);
            this.DataTypeCB.TabIndex = 4;
            // 
            // ValueRankLB
            // 
            this.ValueRankLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ValueRankLB.AutoSize = true;
            this.ValueRankLB.Location = new System.Drawing.Point(3, 53);
            this.ValueRankLB.Name = "ValueRankLB";
            this.ValueRankLB.Size = new System.Drawing.Size(63, 27);
            this.ValueRankLB.TabIndex = 5;
            this.ValueRankLB.Text = "Value Rank";
            this.ValueRankLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ValueRankCB
            // 
            this.ValueRankCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.ValueRankCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ValueRankCB.FormattingEnabled = true;
            this.ValueRankCB.Location = new System.Drawing.Point(104, 56);
            this.ValueRankCB.Name = "ValueRankCB";
            this.ValueRankCB.Size = new System.Drawing.Size(138, 21);
            this.ValueRankCB.TabIndex = 6;
            // 
            // StatusCodeCK
            // 
            this.StatusCodeCK.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.StatusCodeCK.AutoSize = true;
            this.StatusCodeCK.Location = new System.Drawing.Point(248, 83);
            this.StatusCodeCK.Name = "StatusCodeCK";
            this.StatusCodeCK.Size = new System.Drawing.Size(15, 21);
            this.StatusCodeCK.TabIndex = 9;
            this.StatusCodeCK.UseVisualStyleBackColor = true;
            this.StatusCodeCK.CheckedChanged += new System.EventHandler(this.StatusCodeCK_CheckedChanged);
            // 
            // ValueTB
            // 
            this.ValueTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueTB.Location = new System.Drawing.Point(104, 3);
            this.ValueTB.Name = "ValueTB";
            this.ValueTB.Size = new System.Drawing.Size(138, 20);
            this.ValueTB.TabIndex = 2;
            // 
            // EditDataValueCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.Controls.Add(this.ControlsPN);
            this.Name = "EditDataValueCtrl";
            this.Size = new System.Drawing.Size(266, 159);
            this.PopupMenu.ResumeLayout(false);
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem InsertMI;
        private System.Windows.Forms.ToolStripMenuItem ReplaceMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.CheckBox ServerTimestampCK;
        private System.Windows.Forms.DateTimePicker ServerTimestampDP;
        private System.Windows.Forms.DateTimePicker SourceTimestampDP;
        private System.Windows.Forms.Label SourceTimestampLB;
        private System.Windows.Forms.Label StatusCodeLB;
        private System.Windows.Forms.Label ServerTimestampLB;
        private System.Windows.Forms.ComboBox StatusCodeCB;
        private System.Windows.Forms.Label ValueLN;
        private System.Windows.Forms.CheckBox SourceTimestampCK;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.CheckBox StatusCodeCK;
        private System.Windows.Forms.Label DataTypeLB;
        private System.Windows.Forms.ComboBox DataTypeCB;
        private System.Windows.Forms.Label ValueRankLB;
        private System.Windows.Forms.ComboBox ValueRankCB;
        private System.Windows.Forms.TextBox ValueTB;
    }
}
