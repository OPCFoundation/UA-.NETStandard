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
    partial class EditAnnotationDlg
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
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.AnnotationTimeLB = new System.Windows.Forms.Label();
            this.AnnotationTimeDP = new System.Windows.Forms.DateTimePicker();
            this.UserNameLB = new System.Windows.Forms.Label();
            this.UserNameTB = new System.Windows.Forms.TextBox();
            this.CommentLB = new System.Windows.Forms.Label();
            this.CommentTB = new System.Windows.Forms.TextBox();
            this.BottomPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(375, 4);
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
            this.BottomPN.Location = new System.Drawing.Point(0, 80);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(453, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // ControlsPN
            // 
            this.ControlsPN.ColumnCount = 2;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 115F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.AnnotationTimeLB, 0, 2);
            this.ControlsPN.Controls.Add(this.AnnotationTimeDP, 1, 2);
            this.ControlsPN.Controls.Add(this.UserNameLB, 0, 1);
            this.ControlsPN.Controls.Add(this.UserNameTB, 1, 1);
            this.ControlsPN.Controls.Add(this.CommentLB, 0, 0);
            this.ControlsPN.Controls.Add(this.CommentTB, 1, 0);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 4;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.Size = new System.Drawing.Size(453, 80);
            this.ControlsPN.TabIndex = 1;
            // 
            // AnnotationTimeLB
            // 
            this.AnnotationTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AnnotationTimeLB.AutoSize = true;
            this.AnnotationTimeLB.Location = new System.Drawing.Point(3, 52);
            this.AnnotationTimeLB.Name = "AnnotationTimeLB";
            this.AnnotationTimeLB.Size = new System.Drawing.Size(84, 26);
            this.AnnotationTimeLB.TabIndex = 4;
            this.AnnotationTimeLB.Text = "Annotation Time";
            this.AnnotationTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AnnotationTimeDP
            // 
            this.AnnotationTimeDP.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.AnnotationTimeDP.CustomFormat = "HH:mm:ss yyyy-MM-dd";
            this.AnnotationTimeDP.Enabled = false;
            this.AnnotationTimeDP.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.AnnotationTimeDP.Location = new System.Drawing.Point(118, 55);
            this.AnnotationTimeDP.Name = "AnnotationTimeDP";
            this.AnnotationTimeDP.Size = new System.Drawing.Size(138, 20);
            this.AnnotationTimeDP.TabIndex = 5;
            // 
            // UserNameLB
            // 
            this.UserNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.UserNameLB.AutoSize = true;
            this.UserNameLB.Location = new System.Drawing.Point(3, 26);
            this.UserNameLB.Name = "UserNameLB";
            this.UserNameLB.Size = new System.Drawing.Size(60, 26);
            this.UserNameLB.TabIndex = 2;
            this.UserNameLB.Text = "User Name";
            this.UserNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserNameTB
            // 
            this.UserNameTB.Location = new System.Drawing.Point(118, 29);
            this.UserNameTB.Name = "UserNameTB";
            this.UserNameTB.Size = new System.Drawing.Size(138, 20);
            this.UserNameTB.TabIndex = 3;
            // 
            // CommentLB
            // 
            this.CommentLB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.CommentLB.AutoSize = true;
            this.CommentLB.Location = new System.Drawing.Point(3, 0);
            this.CommentLB.Name = "CommentLB";
            this.CommentLB.Size = new System.Drawing.Size(50, 26);
            this.CommentLB.TabIndex = 0;
            this.CommentLB.Text = "Message";
            this.CommentLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CommentTB
            // 
            this.CommentTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CommentTB.Location = new System.Drawing.Point(118, 3);
            this.CommentTB.Multiline = true;
            this.CommentTB.Name = "CommentTB";
            this.CommentTB.Size = new System.Drawing.Size(332, 20);
            this.CommentTB.TabIndex = 1;
            // 
            // EditAnnotationDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(453, 110);
            this.Controls.Add(this.ControlsPN);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditAnnotationDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Annotation";
            this.BottomPN.ResumeLayout(false);
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.Label AnnotationTimeLB;
        private System.Windows.Forms.DateTimePicker AnnotationTimeDP;
        private System.Windows.Forms.Label UserNameLB;
        private System.Windows.Forms.TextBox UserNameTB;
        private System.Windows.Forms.Label CommentLB;
        private System.Windows.Forms.TextBox CommentTB;
    }
}
