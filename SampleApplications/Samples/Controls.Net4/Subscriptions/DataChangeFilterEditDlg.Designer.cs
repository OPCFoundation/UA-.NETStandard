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
    partial class DataChangeFilterEditDlg
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
            this.DeadbandTypeCB = new System.Windows.Forms.ComboBox();
            this.TriggerCB = new System.Windows.Forms.ComboBox();
            this.DeadbandLB = new System.Windows.Forms.Label();
            this.DeadbandTypeLB = new System.Windows.Forms.Label();
            this.DeadbandNC = new System.Windows.Forms.NumericUpDown();
            this.TriggerLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DeadbandNC)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 78);
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
            // MainPN
            // 
            this.MainPN.Controls.Add(this.DeadbandTypeCB);
            this.MainPN.Controls.Add(this.TriggerCB);
            this.MainPN.Controls.Add(this.DeadbandLB);
            this.MainPN.Controls.Add(this.DeadbandTypeLB);
            this.MainPN.Controls.Add(this.DeadbandNC);
            this.MainPN.Controls.Add(this.TriggerLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(311, 78);
            this.MainPN.TabIndex = 1;
            // 
            // DeadbandTypeCB
            // 
            this.DeadbandTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DeadbandTypeCB.FormattingEnabled = true;
            this.DeadbandTypeCB.Location = new System.Drawing.Point(105, 30);
            this.DeadbandTypeCB.Name = "DeadbandTypeCB";
            this.DeadbandTypeCB.Size = new System.Drawing.Size(202, 21);
            this.DeadbandTypeCB.TabIndex = 13;
            this.DeadbandTypeCB.SelectedIndexChanged += new System.EventHandler(this.DeadbandTypeCB_SelectedIndexChanged);
            // 
            // TriggerCB
            // 
            this.TriggerCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TriggerCB.FormattingEnabled = true;
            this.TriggerCB.Location = new System.Drawing.Point(105, 3);
            this.TriggerCB.Name = "TriggerCB";
            this.TriggerCB.Size = new System.Drawing.Size(202, 21);
            this.TriggerCB.TabIndex = 11;
            // 
            // DeadbandLB
            // 
            this.DeadbandLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DeadbandLB.AutoSize = true;
            this.DeadbandLB.Location = new System.Drawing.Point(3, 59);
            this.DeadbandLB.Name = "DeadbandLB";
            this.DeadbandLB.Size = new System.Drawing.Size(57, 13);
            this.DeadbandLB.TabIndex = 14;
            this.DeadbandLB.Text = "Deadband";
            this.DeadbandLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DeadbandTypeLB
            // 
            this.DeadbandTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DeadbandTypeLB.AutoSize = true;
            this.DeadbandTypeLB.Location = new System.Drawing.Point(3, 33);
            this.DeadbandTypeLB.Name = "DeadbandTypeLB";
            this.DeadbandTypeLB.Size = new System.Drawing.Size(84, 13);
            this.DeadbandTypeLB.TabIndex = 12;
            this.DeadbandTypeLB.Text = "Deadband Type";
            this.DeadbandTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DeadbandNC
            // 
            this.DeadbandNC.Enabled = false;
            this.DeadbandNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.DeadbandNC.Location = new System.Drawing.Point(105, 57);
            this.DeadbandNC.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.DeadbandNC.Name = "DeadbandNC";
            this.DeadbandNC.Size = new System.Drawing.Size(202, 20);
            this.DeadbandNC.TabIndex = 15;
            this.DeadbandNC.Value = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            // 
            // TriggerLB
            // 
            this.TriggerLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TriggerLB.AutoSize = true;
            this.TriggerLB.Location = new System.Drawing.Point(3, 6);
            this.TriggerLB.Name = "TriggerLB";
            this.TriggerLB.Size = new System.Drawing.Size(40, 13);
            this.TriggerLB.TabIndex = 10;
            this.TriggerLB.Text = "Trigger";
            this.TriggerLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DataChangeFilterEditDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(311, 109);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "DataChangeFilterEditDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Data Change Filter";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.DeadbandNC)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label TriggerLB;
        private System.Windows.Forms.NumericUpDown DeadbandNC;
        private System.Windows.Forms.Label DeadbandLB;
        private System.Windows.Forms.Label DeadbandTypeLB;
        private System.Windows.Forms.ComboBox DeadbandTypeCB;
        private System.Windows.Forms.ComboBox TriggerCB;
    }
}
