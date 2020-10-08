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
    partial class EditComplexValue2Dlg
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditComplexValue2Dlg));
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.UpdateBTN = new System.Windows.Forms.Button();
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ValueTB = new System.Windows.Forms.TextBox();
            this.StatusCTRL = new System.Windows.Forms.StatusStrip();
            this.DataTypeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.DataTypeTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.EncodingCB = new System.Windows.Forms.ToolStripDropDownButton();
            this.Encoding_DefaultXMLMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Encoding_DefaultBinaryMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.StatusCTRL.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(582, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 2;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.UpdateBTN);
            this.BottomPN.Controls.Add(this.RefreshBTN);
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 273);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(660, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // UpdateBTN
            // 
            this.UpdateBTN.Location = new System.Drawing.Point(165, 4);
            this.UpdateBTN.Name = "UpdateBTN";
            this.UpdateBTN.Size = new System.Drawing.Size(75, 23);
            this.UpdateBTN.TabIndex = 0;
            this.UpdateBTN.Text = "Update";
            this.UpdateBTN.UseVisualStyleBackColor = true;
            this.UpdateBTN.Click += new System.EventHandler(this.UpdateBTN_Click);
            // 
            // RefreshBTN
            // 
            this.RefreshBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RefreshBTN.Location = new System.Drawing.Point(84, 4);
            this.RefreshBTN.Name = "RefreshBTN";
            this.RefreshBTN.Size = new System.Drawing.Size(75, 23);
            this.RefreshBTN.TabIndex = 3;
            this.RefreshBTN.Text = "Refresh";
            this.RefreshBTN.UseVisualStyleBackColor = true;
            this.RefreshBTN.Click += new System.EventHandler(this.RefreshBTN_Click);
            // 
            // MainPN
            // 
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ValueTB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(3);
            this.MainPN.Size = new System.Drawing.Size(660, 273);
            this.MainPN.TabIndex = 2;
            // 
            // ValueTB
            // 
            this.ValueTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueTB.Location = new System.Drawing.Point(3, 3);
            this.ValueTB.MaxLength = 10240000;
            this.ValueTB.Multiline = true;
            this.ValueTB.Name = "ValueTB";
            this.ValueTB.Size = new System.Drawing.Size(654, 267);
            this.ValueTB.TabIndex = 0;
            this.ValueTB.TextChanged += new System.EventHandler(this.ValueTB_TextChanged);
            // 
            // StatusCTRL
            // 
            this.StatusCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DataTypeLB,
            this.DataTypeTB,
            this.EncodingCB});
            this.StatusCTRL.Location = new System.Drawing.Point(0, 303);
            this.StatusCTRL.Name = "StatusCTRL";
            this.StatusCTRL.Size = new System.Drawing.Size(660, 22);
            this.StatusCTRL.TabIndex = 1;
            this.StatusCTRL.Text = "statusStrip1";
            // 
            // DataTypeLB
            // 
            this.DataTypeLB.Name = "DataTypeLB";
            this.DataTypeLB.Size = new System.Drawing.Size(60, 17);
            this.DataTypeLB.Text = "Data Type";
            // 
            // DataTypeTB
            // 
            this.DataTypeTB.Name = "DataTypeTB";
            this.DataTypeTB.Size = new System.Drawing.Size(55, 17);
            this.DataTypeTB.Text = "Structure";
            // 
            // EncodingCB
            // 
            this.EncodingCB.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.EncodingCB.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Encoding_DefaultXMLMI,
            this.Encoding_DefaultBinaryMI});
            this.EncodingCB.Image = ((System.Drawing.Image)(resources.GetObject("EncodingCB.Image")));
            this.EncodingCB.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.EncodingCB.Name = "EncodingCB";
            this.EncodingCB.Size = new System.Drawing.Size(85, 20);
            this.EncodingCB.Text = "Default XML";
            this.EncodingCB.TextImageRelation = System.Windows.Forms.TextImageRelation.TextBeforeImage;
            // 
            // Encoding_DefaultXMLMI
            // 
            this.Encoding_DefaultXMLMI.Name = "Encoding_DefaultXMLMI";
            this.Encoding_DefaultXMLMI.Size = new System.Drawing.Size(148, 22);
            this.Encoding_DefaultXMLMI.Text = "Default XML";
            // 
            // Encoding_DefaultBinaryMI
            // 
            this.Encoding_DefaultBinaryMI.Name = "Encoding_DefaultBinaryMI";
            this.Encoding_DefaultBinaryMI.Size = new System.Drawing.Size(148, 22);
            this.Encoding_DefaultBinaryMI.Text = "Default Binary";
            // 
            // EditComplexValue2Dlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(660, 325);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.Controls.Add(this.StatusCTRL);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditComplexValue2Dlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Value";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.StatusCTRL.ResumeLayout(false);
            this.StatusCTRL.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.TextBox ValueTB;
        private System.Windows.Forms.Button UpdateBTN;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.StatusStrip StatusCTRL;
        private System.Windows.Forms.ToolStripStatusLabel DataTypeLB;
        private System.Windows.Forms.ToolStripStatusLabel DataTypeTB;
        private System.Windows.Forms.ToolStripDropDownButton EncodingCB;
        private System.Windows.Forms.ToolStripMenuItem Encoding_DefaultXMLMI;
        private System.Windows.Forms.ToolStripMenuItem Encoding_DefaultBinaryMI;
    }
}
