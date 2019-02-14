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
    partial class PerformanceTestDlg
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
            this.TestAllBTN = new System.Windows.Forms.Button();
            this.LoadBTN = new System.Windows.Forms.Button();
            this.SaveBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ResultsCTRL = new Opc.Ua.Sample.Controls.PerformanceResultsListCtrl();
            this.EndpointSelectorCTRL = new Opc.Ua.Client.Controls.EndpointSelectorCtrl();
            this.ProgressCTRL = new System.Windows.Forms.ProgressBar();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.TestAllBTN);
            this.ButtonsPN.Controls.Add(this.LoadBTN);
            this.ButtonsPN.Controls.Add(this.SaveBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 245);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(769, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // TestAllBTN
            // 
            this.TestAllBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.TestAllBTN.Location = new System.Drawing.Point(247, 4);
            this.TestAllBTN.Name = "TestAllBTN";
            this.TestAllBTN.Size = new System.Drawing.Size(75, 23);
            this.TestAllBTN.TabIndex = 4;
            this.TestAllBTN.Text = "Test All";
            this.TestAllBTN.UseVisualStyleBackColor = true;
            this.TestAllBTN.Click += new System.EventHandler(this.TestAllBTN_Click);
            // 
            // LoadBTN
            // 
            this.LoadBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.LoadBTN.Location = new System.Drawing.Point(166, 4);
            this.LoadBTN.Name = "LoadBTN";
            this.LoadBTN.Size = new System.Drawing.Size(75, 23);
            this.LoadBTN.TabIndex = 3;
            this.LoadBTN.Text = "Load...";
            this.LoadBTN.UseVisualStyleBackColor = true;
            this.LoadBTN.Click += new System.EventHandler(this.LoadBTN_Click);
            // 
            // SaveBTN
            // 
            this.SaveBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.SaveBTN.Location = new System.Drawing.Point(85, 4);
            this.SaveBTN.Name = "SaveBTN";
            this.SaveBTN.Size = new System.Drawing.Size(75, 23);
            this.SaveBTN.TabIndex = 2;
            this.SaveBTN.Text = "Save...";
            this.SaveBTN.UseVisualStyleBackColor = true;
            this.SaveBTN.Click += new System.EventHandler(this.SaveBTN_Click);
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "Stop";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(690, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ResultsCTRL);
            this.MainPN.Controls.Add(this.EndpointSelectorCTRL);
            this.MainPN.Controls.Add(this.ProgressCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(3, 2, 3, 0);
            this.MainPN.Size = new System.Drawing.Size(769, 245);
            this.MainPN.TabIndex = 1;
            // 
            // ResultsCTRL
            // 
            this.ResultsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsCTRL.Instructions = null;
            this.ResultsCTRL.Location = new System.Drawing.Point(3, 30);
            this.ResultsCTRL.Name = "ResultsCTRL";
            this.ResultsCTRL.Padding = new System.Windows.Forms.Padding(1, 3, 1, 4);
            this.ResultsCTRL.Size = new System.Drawing.Size(763, 192);
            this.ResultsCTRL.TabIndex = 2;
            // 
            // EndpointSelectorCTRL
            // 
            this.EndpointSelectorCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.EndpointSelectorCTRL.Location = new System.Drawing.Point(3, 2);
            this.EndpointSelectorCTRL.MaximumSize = new System.Drawing.Size(2048, 28);
            this.EndpointSelectorCTRL.MinimumSize = new System.Drawing.Size(100, 28);
            this.EndpointSelectorCTRL.Name = "EndpointSelectorCTRL";
            this.EndpointSelectorCTRL.Padding = new System.Windows.Forms.Padding(2, 0, 0, 0);
            this.EndpointSelectorCTRL.SelectedEndpoint = null;
            this.EndpointSelectorCTRL.Size = new System.Drawing.Size(763, 28);
            this.EndpointSelectorCTRL.TabIndex = 1;
            this.EndpointSelectorCTRL.ConnectEndpoint += new Opc.Ua.Client.Controls.ConnectEndpointEventHandler(this.EndpointSelectorCTRL_ConnectEndpoint);
            // 
            // ProgressCTRL
            // 
            this.ProgressCTRL.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ProgressCTRL.Location = new System.Drawing.Point(3, 222);
            this.ProgressCTRL.Name = "ProgressCTRL";
            this.ProgressCTRL.Size = new System.Drawing.Size(763, 23);
            this.ProgressCTRL.TabIndex = 0;
            // 
            // PerformanceTestDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(769, 276);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "PerformanceTestDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Stack Overhead Test";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private Opc.Ua.Client.Controls.EndpointSelectorCtrl EndpointSelectorCTRL;
        private System.Windows.Forms.ProgressBar ProgressCTRL;
        private PerformanceResultsListCtrl ResultsCTRL;
        private System.Windows.Forms.Button SaveBTN;
        private System.Windows.Forms.Button LoadBTN;
        private System.Windows.Forms.Button TestAllBTN;
    }
}
