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

namespace Opc.Ua.Server.Controls
{
    partial class ExceptionDlg
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
            this.BottomPN = new System.Windows.Forms.Panel();
            this.ShowStackTracesCK = new System.Windows.Forms.CheckBox();
            this.CloseBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ExceptionBrowser = new System.Windows.Forms.WebBrowser();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.ShowStackTracesCK);
            this.BottomPN.Controls.Add(this.CloseBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 181);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(780, 29);
            this.BottomPN.TabIndex = 1;
            // 
            // ShowStackTracesCK
            // 
            this.ShowStackTracesCK.AutoSize = true;
            this.ShowStackTracesCK.Location = new System.Drawing.Point(3, 7);
            this.ShowStackTracesCK.Name = "ShowStackTracesCK";
            this.ShowStackTracesCK.Size = new System.Drawing.Size(138, 17);
            this.ShowStackTracesCK.TabIndex = 1;
            this.ShowStackTracesCK.Text = "Show Exception Details";
            this.ShowStackTracesCK.UseVisualStyleBackColor = true;
            this.ShowStackTracesCK.CheckedChanged += new System.EventHandler(this.ShowStackTracesCK_CheckedChanged);
            // 
            // CloseBTN
            // 
            this.CloseBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)));
            this.CloseBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseBTN.Location = new System.Drawing.Point(353, 3);
            this.CloseBTN.Name = "CloseBTN";
            this.CloseBTN.Size = new System.Drawing.Size(75, 23);
            this.CloseBTN.TabIndex = 0;
            this.CloseBTN.Text = "Close";
            this.CloseBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.AutoSize = true;
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ExceptionBrowser);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(780, 181);
            this.MainPN.TabIndex = 1;
            // 
            // ExceptionBrowser
            // 
            this.ExceptionBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ExceptionBrowser.Location = new System.Drawing.Point(0, 0);
            this.ExceptionBrowser.MinimumSize = new System.Drawing.Size(20, 20);
            this.ExceptionBrowser.Name = "ExceptionBrowser";
            this.ExceptionBrowser.ScriptErrorsSuppressed = true;
            this.ExceptionBrowser.Size = new System.Drawing.Size(780, 181);
            this.ExceptionBrowser.TabIndex = 1;
            // 
            // ExceptionDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(780, 210);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.MaximumSize = new System.Drawing.Size(4096, 4096);
            this.Name = "ExceptionDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Exception";
            this.BottomPN.ResumeLayout(false);
            this.BottomPN.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Button CloseBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.WebBrowser ExceptionBrowser;
        private System.Windows.Forms.CheckBox ShowStackTracesCK;

    }
}
