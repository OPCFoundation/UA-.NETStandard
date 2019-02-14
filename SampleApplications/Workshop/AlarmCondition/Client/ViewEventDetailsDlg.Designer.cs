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

namespace Quickstarts.AlarmConditionClient
{
    partial class ViewEventDetailsDlg
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
            this.OkBTN = new System.Windows.Forms.Button();
            this.FieldsLV = new System.Windows.Forms.ListView();
            this.FieldCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.DataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.SuspendLayout();
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(329, 735);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 4;
            this.OkBTN.Text = "Close";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // FieldsLV
            // 
            this.FieldsLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.FieldsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.FieldCH,
            this.ValueCH,
            this.DataTypeCH});
            this.FieldsLV.FullRowSelect = true;
            this.FieldsLV.Location = new System.Drawing.Point(3, 2);
            this.FieldsLV.MultiSelect = false;
            this.FieldsLV.Name = "FieldsLV";
            this.FieldsLV.Size = new System.Drawing.Size(727, 727);
            this.FieldsLV.TabIndex = 6;
            this.FieldsLV.UseCompatibleStateImageBehavior = false;
            this.FieldsLV.View = System.Windows.Forms.View.Details;
            // 
            // FieldCH
            // 
            this.FieldCH.Text = "Field";
            this.FieldCH.Width = 151;
            // 
            // ValueCH
            // 
            this.ValueCH.Text = "Value";
            this.ValueCH.Width = 137;
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.Text = "Data Type";
            this.DataTypeCH.Width = 87;
            // 
            // ViewEventDetailsDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(733, 761);
            this.Controls.Add(this.FieldsLV);
            this.Controls.Add(this.OkBTN);
            this.MaximumSize = new System.Drawing.Size(1200, 1200);
            this.MinimumSize = new System.Drawing.Size(400, 91);
            this.Name = "ViewEventDetailsDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "View Event Details";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.ListView FieldsLV;
        private System.Windows.Forms.ColumnHeader FieldCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ColumnHeader DataTypeCH;
    }
}
