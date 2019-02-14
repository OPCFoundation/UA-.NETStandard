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

namespace Opc.Ua.Sample
{
    partial class HistoryReadDetails
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
            this.NodeLB = new System.Windows.Forms.Label();
            this.NodeCB = new System.Windows.Forms.ComboBox();
            this.StartTimeCTRL = new System.Windows.Forms.DateTimePicker();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.EndTimeLB = new System.Windows.Forms.Label();
            this.EndTimeCTRL = new System.Windows.Forms.DateTimePicker();
            this.MaxValuesCTRL = new System.Windows.Forms.NumericUpDown();
            this.MaxValuesLB = new System.Windows.Forms.Label();
            this.IsModifiedCHK = new System.Windows.Forms.CheckBox();
            this.QueryTypeCB = new System.Windows.Forms.ComboBox();
            this.QueryTypeLB = new System.Windows.Forms.Label();
            this.IsModifiedLB = new System.Windows.Forms.Label();
            this.StartTimeSpecifiedCHK = new System.Windows.Forms.CheckBox();
            this.EndTimeSpecifiedCHK = new System.Windows.Forms.CheckBox();
            this.MaxValuesSpecifiedCHK = new System.Windows.Forms.CheckBox();
            this.IncludeBoundsLB = new System.Windows.Forms.Label();
            this.IncludeBoundsCHK = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.MaxValuesCTRL)).BeginInit();
            this.SuspendLayout();
            // 
            // NodeLB
            // 
            this.NodeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeLB.AutoSize = true;
            this.NodeLB.Location = new System.Drawing.Point(0, 4);
            this.NodeLB.Name = "NodeLB";
            this.NodeLB.Size = new System.Drawing.Size(45, 13);
            this.NodeLB.TabIndex = 0;
            this.NodeLB.Text = "Variable";
            this.NodeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeCB
            // 
            this.NodeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.NodeCB.FormattingEnabled = true;
            this.NodeCB.Location = new System.Drawing.Point(85, 0);
            this.NodeCB.Name = "NodeCB";
            this.NodeCB.Size = new System.Drawing.Size(195, 21);
            this.NodeCB.TabIndex = 1;
            // 
            // StartTimeCTRL
            // 
            this.StartTimeCTRL.CustomFormat = "yyyy-MM-dd HH:mm:ss ";
            this.StartTimeCTRL.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.StartTimeCTRL.Location = new System.Drawing.Point(85, 54);
            this.StartTimeCTRL.Name = "StartTimeCTRL";
            this.StartTimeCTRL.Size = new System.Drawing.Size(139, 20);
            this.StartTimeCTRL.TabIndex = 5;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(0, 60);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 13);
            this.StartTimeLB.TabIndex = 4;
            this.StartTimeLB.Text = "Start Time";
            this.StartTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndTimeLB
            // 
            this.EndTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.EndTimeLB.AutoSize = true;
            this.EndTimeLB.Location = new System.Drawing.Point(0, 85);
            this.EndTimeLB.Name = "EndTimeLB";
            this.EndTimeLB.Size = new System.Drawing.Size(52, 13);
            this.EndTimeLB.TabIndex = 7;
            this.EndTimeLB.Text = "End Time";
            this.EndTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndTimeCTRL
            // 
            this.EndTimeCTRL.CustomFormat = "yyyy-MM-dd HH:mm:ss ";
            this.EndTimeCTRL.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.EndTimeCTRL.Location = new System.Drawing.Point(85, 80);
            this.EndTimeCTRL.Name = "EndTimeCTRL";
            this.EndTimeCTRL.Size = new System.Drawing.Size(139, 20);
            this.EndTimeCTRL.TabIndex = 8;
            // 
            // MaxValuesCTRL
            // 
            this.MaxValuesCTRL.Location = new System.Drawing.Point(85, 106);
            this.MaxValuesCTRL.Name = "MaxValuesCTRL";
            this.MaxValuesCTRL.Size = new System.Drawing.Size(139, 20);
            this.MaxValuesCTRL.TabIndex = 11;
            // 
            // MaxValuesLB
            // 
            this.MaxValuesLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxValuesLB.AutoSize = true;
            this.MaxValuesLB.Location = new System.Drawing.Point(0, 111);
            this.MaxValuesLB.Name = "MaxValuesLB";
            this.MaxValuesLB.Size = new System.Drawing.Size(62, 13);
            this.MaxValuesLB.TabIndex = 10;
            this.MaxValuesLB.Text = "Max Values";
            this.MaxValuesLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IsModifiedCHK
            // 
            this.IsModifiedCHK.AutoSize = true;
            this.IsModifiedCHK.Location = new System.Drawing.Point(85, 158);
            this.IsModifiedCHK.Name = "IsModifiedCHK";
            this.IsModifiedCHK.Size = new System.Drawing.Size(15, 14);
            this.IsModifiedCHK.TabIndex = 16;
            this.IsModifiedCHK.UseVisualStyleBackColor = true;
            // 
            // QueryTypeCB
            // 
            this.QueryTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.QueryTypeCB.FormattingEnabled = true;
            this.QueryTypeCB.Location = new System.Drawing.Point(85, 27);
            this.QueryTypeCB.Name = "QueryTypeCB";
            this.QueryTypeCB.Size = new System.Drawing.Size(195, 21);
            this.QueryTypeCB.TabIndex = 3;
            // 
            // QueryTypeLB
            // 
            this.QueryTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.QueryTypeLB.AutoSize = true;
            this.QueryTypeLB.Location = new System.Drawing.Point(0, 32);
            this.QueryTypeLB.Name = "QueryTypeLB";
            this.QueryTypeLB.Size = new System.Drawing.Size(60, 13);
            this.QueryTypeLB.TabIndex = 2;
            this.QueryTypeLB.Text = "Read Type";
            this.QueryTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IsModifiedLB
            // 
            this.IsModifiedLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.IsModifiedLB.AutoSize = true;
            this.IsModifiedLB.Location = new System.Drawing.Point(0, 158);
            this.IsModifiedLB.Name = "IsModifiedLB";
            this.IsModifiedLB.Size = new System.Drawing.Size(76, 13);
            this.IsModifiedLB.TabIndex = 15;
            this.IsModifiedLB.Text = "Read Modified";
            this.IsModifiedLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeSpecifiedCHK
            // 
            this.StartTimeSpecifiedCHK.AutoSize = true;
            this.StartTimeSpecifiedCHK.Location = new System.Drawing.Point(230, 57);
            this.StartTimeSpecifiedCHK.Name = "StartTimeSpecifiedCHK";
            this.StartTimeSpecifiedCHK.Size = new System.Drawing.Size(15, 14);
            this.StartTimeSpecifiedCHK.TabIndex = 6;
            this.StartTimeSpecifiedCHK.UseVisualStyleBackColor = true;
            // 
            // EndTimeSpecifiedCHK
            // 
            this.EndTimeSpecifiedCHK.AutoSize = true;
            this.EndTimeSpecifiedCHK.Location = new System.Drawing.Point(230, 83);
            this.EndTimeSpecifiedCHK.Name = "EndTimeSpecifiedCHK";
            this.EndTimeSpecifiedCHK.Size = new System.Drawing.Size(15, 14);
            this.EndTimeSpecifiedCHK.TabIndex = 9;
            this.EndTimeSpecifiedCHK.UseVisualStyleBackColor = true;
            // 
            // MaxValuesSpecifiedCHK
            // 
            this.MaxValuesSpecifiedCHK.AutoSize = true;
            this.MaxValuesSpecifiedCHK.Location = new System.Drawing.Point(230, 109);
            this.MaxValuesSpecifiedCHK.Name = "MaxValuesSpecifiedCHK";
            this.MaxValuesSpecifiedCHK.Size = new System.Drawing.Size(15, 14);
            this.MaxValuesSpecifiedCHK.TabIndex = 12;
            this.MaxValuesSpecifiedCHK.UseVisualStyleBackColor = true;
            // 
            // IncludeBoundsLB
            // 
            this.IncludeBoundsLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.IncludeBoundsLB.AutoSize = true;
            this.IncludeBoundsLB.Location = new System.Drawing.Point(0, 135);
            this.IncludeBoundsLB.Name = "IncludeBoundsLB";
            this.IncludeBoundsLB.Size = new System.Drawing.Size(81, 13);
            this.IncludeBoundsLB.TabIndex = 13;
            this.IncludeBoundsLB.Text = "Include Bounds";
            this.IncludeBoundsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IncludeBoundsCHK
            // 
            this.IncludeBoundsCHK.AutoSize = true;
            this.IncludeBoundsCHK.Location = new System.Drawing.Point(85, 135);
            this.IncludeBoundsCHK.Name = "IncludeBoundsCHK";
            this.IncludeBoundsCHK.Size = new System.Drawing.Size(15, 14);
            this.IncludeBoundsCHK.TabIndex = 14;
            this.IncludeBoundsCHK.UseVisualStyleBackColor = true;
            // 
            // HistoryReadDetails
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.IncludeBoundsLB);
            this.Controls.Add(this.IncludeBoundsCHK);
            this.Controls.Add(this.MaxValuesSpecifiedCHK);
            this.Controls.Add(this.EndTimeSpecifiedCHK);
            this.Controls.Add(this.StartTimeSpecifiedCHK);
            this.Controls.Add(this.IsModifiedLB);
            this.Controls.Add(this.QueryTypeLB);
            this.Controls.Add(this.QueryTypeCB);
            this.Controls.Add(this.IsModifiedCHK);
            this.Controls.Add(this.MaxValuesLB);
            this.Controls.Add(this.MaxValuesCTRL);
            this.Controls.Add(this.EndTimeLB);
            this.Controls.Add(this.EndTimeCTRL);
            this.Controls.Add(this.StartTimeLB);
            this.Controls.Add(this.StartTimeCTRL);
            this.Controls.Add(this.NodeCB);
            this.Controls.Add(this.NodeLB);
            this.Name = "HistoryReadDetails";
            this.Size = new System.Drawing.Size(304, 237);
            ((System.ComponentModel.ISupportInitialize)(this.MaxValuesCTRL)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label NodeLB;
        private System.Windows.Forms.ComboBox NodeCB;
        private System.Windows.Forms.DateTimePicker StartTimeCTRL;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.Label EndTimeLB;
        private System.Windows.Forms.DateTimePicker EndTimeCTRL;
        private System.Windows.Forms.NumericUpDown MaxValuesCTRL;
        private System.Windows.Forms.Label MaxValuesLB;
        private System.Windows.Forms.CheckBox IsModifiedCHK;
        private System.Windows.Forms.ComboBox QueryTypeCB;
        private System.Windows.Forms.Label QueryTypeLB;
        private System.Windows.Forms.Label IsModifiedLB;
        private System.Windows.Forms.CheckBox StartTimeSpecifiedCHK;
        private System.Windows.Forms.CheckBox EndTimeSpecifiedCHK;
        private System.Windows.Forms.CheckBox MaxValuesSpecifiedCHK;
        private System.Windows.Forms.Label IncludeBoundsLB;
        private System.Windows.Forms.CheckBox IncludeBoundsCHK;
    }
}
