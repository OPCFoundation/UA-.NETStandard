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
    partial class FilterOperandEditDlg
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
            this.AttributePN = new System.Windows.Forms.Panel();
            this.TypeDefinitionIdCTRL = new Opc.Ua.Client.Controls.NodeIdCtrl();
            this.BrowsePathTB = new System.Windows.Forms.TextBox();
            this.TypeDefinitionIdLB = new System.Windows.Forms.Label();
            this.AttributeIdCB = new System.Windows.Forms.ComboBox();
            this.AliasTB = new System.Windows.Forms.TextBox();
            this.AttributeIdLB = new System.Windows.Forms.Label();
            this.BrowsePathLB = new System.Windows.Forms.Label();
            this.AliasLB = new System.Windows.Forms.Label();
            this.IndexRangeLB = new System.Windows.Forms.Label();
            this.IndexRangeTB = new System.Windows.Forms.TextBox();
            this.LiteralPN = new System.Windows.Forms.Panel();
            this.ValueTB = new System.Windows.Forms.TextBox();
            this.ValueLB = new System.Windows.Forms.Label();
            this.DataTypeCB = new System.Windows.Forms.ComboBox();
            this.DataTypeLB = new System.Windows.Forms.Label();
            this.ElementPN = new System.Windows.Forms.Panel();
            this.ElementsCB = new System.Windows.Forms.ComboBox();
            this.IndexLB = new System.Windows.Forms.Label();
            this.OperandTypeLB = new System.Windows.Forms.Label();
            this.TopPN = new System.Windows.Forms.Panel();
            this.OperandTypeCB = new System.Windows.Forms.ComboBox();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.AttributePN.SuspendLayout();
            this.LiteralPN.SuspendLayout();
            this.ElementPN.SuspendLayout();
            this.TopPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 177);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(509, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(432, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.AttributePN);
            this.MainPN.Controls.Add(this.LiteralPN);
            this.MainPN.Controls.Add(this.ElementPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 28);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(509, 149);
            this.MainPN.TabIndex = 16;
            // 
            // AttributePN
            // 
            this.AttributePN.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.AttributePN.Controls.Add(this.TypeDefinitionIdCTRL);
            this.AttributePN.Controls.Add(this.BrowsePathTB);
            this.AttributePN.Controls.Add(this.TypeDefinitionIdLB);
            this.AttributePN.Controls.Add(this.AttributeIdCB);
            this.AttributePN.Controls.Add(this.AliasTB);
            this.AttributePN.Controls.Add(this.AttributeIdLB);
            this.AttributePN.Controls.Add(this.BrowsePathLB);
            this.AttributePN.Controls.Add(this.AliasLB);
            this.AttributePN.Controls.Add(this.IndexRangeLB);
            this.AttributePN.Controls.Add(this.IndexRangeTB);
            this.AttributePN.Location = new System.Drawing.Point(0, 0);
            this.AttributePN.Name = "AttributePN";
            this.AttributePN.Size = new System.Drawing.Size(506, 150);
            this.AttributePN.TabIndex = 20;
            // 
            // TypeDefinitionIdCTRL
            // 
            this.TypeDefinitionIdCTRL.Location = new System.Drawing.Point(86, 2);
            this.TypeDefinitionIdCTRL.MaximumSize = new System.Drawing.Size(4096, 20);
            this.TypeDefinitionIdCTRL.MinimumSize = new System.Drawing.Size(100, 20);
            this.TypeDefinitionIdCTRL.Name = "TypeDefinitionIdCTRL";
            this.TypeDefinitionIdCTRL.Size = new System.Drawing.Size(200, 20);
            this.TypeDefinitionIdCTRL.TabIndex = 17;
            // 
            // BrowsePathTB
            // 
            this.BrowsePathTB.Location = new System.Drawing.Point(86, 28);
            this.BrowsePathTB.Name = "BrowsePathTB";
            this.BrowsePathTB.Size = new System.Drawing.Size(315, 20);
            this.BrowsePathTB.TabIndex = 12;
            // 
            // TypeDefinitionIdLB
            // 
            this.TypeDefinitionIdLB.AutoSize = true;
            this.TypeDefinitionIdLB.Location = new System.Drawing.Point(4, 5);
            this.TypeDefinitionIdLB.Name = "TypeDefinitionIdLB";
            this.TypeDefinitionIdLB.Size = new System.Drawing.Size(45, 13);
            this.TypeDefinitionIdLB.TabIndex = 4;
            this.TypeDefinitionIdLB.Text = "Type ID";
            // 
            // AttributeIdCB
            // 
            this.AttributeIdCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AttributeIdCB.FormattingEnabled = true;
            this.AttributeIdCB.Location = new System.Drawing.Point(86, 54);
            this.AttributeIdCB.Name = "AttributeIdCB";
            this.AttributeIdCB.Size = new System.Drawing.Size(160, 21);
            this.AttributeIdCB.TabIndex = 13;
            // 
            // AliasTB
            // 
            this.AliasTB.Location = new System.Drawing.Point(86, 107);
            this.AliasTB.Name = "AliasTB";
            this.AliasTB.Size = new System.Drawing.Size(160, 20);
            this.AliasTB.TabIndex = 15;
            // 
            // AttributeIdLB
            // 
            this.AttributeIdLB.AutoSize = true;
            this.AttributeIdLB.Location = new System.Drawing.Point(4, 57);
            this.AttributeIdLB.Name = "AttributeIdLB";
            this.AttributeIdLB.Size = new System.Drawing.Size(46, 13);
            this.AttributeIdLB.TabIndex = 6;
            this.AttributeIdLB.Text = "Attribute";
            // 
            // BrowsePathLB
            // 
            this.BrowsePathLB.AutoSize = true;
            this.BrowsePathLB.Location = new System.Drawing.Point(4, 31);
            this.BrowsePathLB.Name = "BrowsePathLB";
            this.BrowsePathLB.Size = new System.Drawing.Size(67, 13);
            this.BrowsePathLB.TabIndex = 5;
            this.BrowsePathLB.Text = "Browse Path";
            // 
            // AliasLB
            // 
            this.AliasLB.AutoSize = true;
            this.AliasLB.Location = new System.Drawing.Point(4, 110);
            this.AliasLB.Name = "AliasLB";
            this.AliasLB.Size = new System.Drawing.Size(29, 13);
            this.AliasLB.TabIndex = 7;
            this.AliasLB.Text = "Alias";
            // 
            // IndexRangeLB
            // 
            this.IndexRangeLB.AutoSize = true;
            this.IndexRangeLB.Location = new System.Drawing.Point(4, 84);
            this.IndexRangeLB.Name = "IndexRangeLB";
            this.IndexRangeLB.Size = new System.Drawing.Size(68, 13);
            this.IndexRangeLB.TabIndex = 8;
            this.IndexRangeLB.Text = "Index Range";
            // 
            // IndexRangeTB
            // 
            this.IndexRangeTB.Location = new System.Drawing.Point(86, 81);
            this.IndexRangeTB.Name = "IndexRangeTB";
            this.IndexRangeTB.Size = new System.Drawing.Size(77, 20);
            this.IndexRangeTB.TabIndex = 14;
            // 
            // LiteralPN
            // 
            this.LiteralPN.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.LiteralPN.Controls.Add(this.ValueTB);
            this.LiteralPN.Controls.Add(this.ValueLB);
            this.LiteralPN.Controls.Add(this.DataTypeCB);
            this.LiteralPN.Controls.Add(this.DataTypeLB);
            this.LiteralPN.Location = new System.Drawing.Point(0, 0);
            this.LiteralPN.Name = "LiteralPN";
            this.LiteralPN.Size = new System.Drawing.Size(506, 150);
            this.LiteralPN.TabIndex = 22;
            // 
            // ValueTB
            // 
            this.ValueTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ValueTB.Location = new System.Drawing.Point(86, 28);
            this.ValueTB.Multiline = true;
            this.ValueTB.Name = "ValueTB";
            this.ValueTB.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.ValueTB.Size = new System.Drawing.Size(420, 122);
            this.ValueTB.TabIndex = 10;
            // 
            // ValueLB
            // 
            this.ValueLB.AutoSize = true;
            this.ValueLB.Location = new System.Drawing.Point(5, 31);
            this.ValueLB.Name = "ValueLB";
            this.ValueLB.Size = new System.Drawing.Size(34, 13);
            this.ValueLB.TabIndex = 3;
            this.ValueLB.Text = "Value";
            // 
            // DataTypeCB
            // 
            this.DataTypeCB.FormattingEnabled = true;
            this.DataTypeCB.Location = new System.Drawing.Point(86, 2);
            this.DataTypeCB.Name = "DataTypeCB";
            this.DataTypeCB.Size = new System.Drawing.Size(207, 21);
            this.DataTypeCB.TabIndex = 12;
            // 
            // DataTypeLB
            // 
            this.DataTypeLB.AutoSize = true;
            this.DataTypeLB.Location = new System.Drawing.Point(4, 5);
            this.DataTypeLB.Name = "DataTypeLB";
            this.DataTypeLB.Size = new System.Drawing.Size(57, 13);
            this.DataTypeLB.TabIndex = 11;
            this.DataTypeLB.Text = "Data Type";
            // 
            // ElementPN
            // 
            this.ElementPN.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ElementPN.Controls.Add(this.ElementsCB);
            this.ElementPN.Controls.Add(this.IndexLB);
            this.ElementPN.Location = new System.Drawing.Point(0, 0);
            this.ElementPN.Name = "ElementPN";
            this.ElementPN.Size = new System.Drawing.Size(506, 150);
            this.ElementPN.TabIndex = 21;
            // 
            // ElementsCB
            // 
            this.ElementsCB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ElementsCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ElementsCB.FormattingEnabled = true;
            this.ElementsCB.Location = new System.Drawing.Point(86, 2);
            this.ElementsCB.Name = "ElementsCB";
            this.ElementsCB.Size = new System.Drawing.Size(420, 21);
            this.ElementsCB.TabIndex = 9;
            // 
            // IndexLB
            // 
            this.IndexLB.AutoSize = true;
            this.IndexLB.Location = new System.Drawing.Point(5, 5);
            this.IndexLB.Name = "IndexLB";
            this.IndexLB.Size = new System.Drawing.Size(33, 13);
            this.IndexLB.TabIndex = 2;
            this.IndexLB.Text = "Index";
            // 
            // OperandTypeLB
            // 
            this.OperandTypeLB.AutoSize = true;
            this.OperandTypeLB.Location = new System.Drawing.Point(4, 6);
            this.OperandTypeLB.Name = "OperandTypeLB";
            this.OperandTypeLB.Size = new System.Drawing.Size(75, 13);
            this.OperandTypeLB.TabIndex = 17;
            this.OperandTypeLB.Text = "Operand Type";
            // 
            // TopPN
            // 
            this.TopPN.Controls.Add(this.OperandTypeCB);
            this.TopPN.Controls.Add(this.OperandTypeLB);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(0, 0);
            this.TopPN.Name = "TopPN";
            this.TopPN.Size = new System.Drawing.Size(509, 28);
            this.TopPN.TabIndex = 18;
            // 
            // OperandTypeCB
            // 
            this.OperandTypeCB.FormattingEnabled = true;
            this.OperandTypeCB.Location = new System.Drawing.Point(86, 3);
            this.OperandTypeCB.Name = "OperandTypeCB";
            this.OperandTypeCB.Size = new System.Drawing.Size(207, 21);
            this.OperandTypeCB.TabIndex = 15;
            this.OperandTypeCB.SelectedIndexChanged += new System.EventHandler(this.OperandTypeCB_SelectedIndexChanged);
            // 
            // FilterOperandEditDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(509, 208);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.Controls.Add(this.TopPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "FilterOperandEditDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Filter Operand";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.AttributePN.ResumeLayout(false);
            this.AttributePN.PerformLayout();
            this.LiteralPN.ResumeLayout(false);
            this.LiteralPN.PerformLayout();
            this.ElementPN.ResumeLayout(false);
            this.ElementPN.PerformLayout();
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label IndexLB;
        private System.Windows.Forms.ComboBox ElementsCB;
        private System.Windows.Forms.Label IndexRangeLB;
        private System.Windows.Forms.Label AliasLB;
        private System.Windows.Forms.Label AttributeIdLB;
        private System.Windows.Forms.Label BrowsePathLB;
        private System.Windows.Forms.Label TypeDefinitionIdLB;
        private System.Windows.Forms.TextBox AliasTB;
        private System.Windows.Forms.TextBox IndexRangeTB;
        private System.Windows.Forms.ComboBox AttributeIdCB;
        private System.Windows.Forms.TextBox BrowsePathTB;
        private System.Windows.Forms.ComboBox DataTypeCB;
        private System.Windows.Forms.Label DataTypeLB;
        private System.Windows.Forms.Label ValueLB;
        private System.Windows.Forms.TextBox ValueTB;
        private System.Windows.Forms.Label OperandTypeLB;
        private System.Windows.Forms.Panel TopPN;
        private System.Windows.Forms.ComboBox OperandTypeCB;
        private System.Windows.Forms.Panel AttributePN;
        private System.Windows.Forms.Panel LiteralPN;
        private System.Windows.Forms.Panel ElementPN;
        private Opc.Ua.Client.Controls.NodeIdCtrl TypeDefinitionIdCTRL;
    }
}
