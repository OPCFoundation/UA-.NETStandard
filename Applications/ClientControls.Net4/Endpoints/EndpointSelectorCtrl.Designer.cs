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
    partial class EndpointSelectorCtrl
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
            this.EndpointCB = new System.Windows.Forms.ComboBox();
            this.ConnectButton = new System.Windows.Forms.Button();
            this.ConnectPN = new System.Windows.Forms.Panel();
            this.ConnectPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // EndpointCB
            // 
            this.EndpointCB.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.EndpointCB.FormattingEnabled = true;
            this.EndpointCB.Location = new System.Drawing.Point(3, 4);
            this.EndpointCB.Name = "EndpointCB";
            this.EndpointCB.Size = new System.Drawing.Size(622, 21);
            this.EndpointCB.TabIndex = 0;
            this.EndpointCB.SelectedIndexChanged += new System.EventHandler(this.EndpointCB_SelectedIndexChanged);
            // 
            // ConnectButton
            // 
            this.ConnectButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ConnectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ConnectButton.Location = new System.Drawing.Point(3, 3);
            this.ConnectButton.Name = "ConnectButton";
            this.ConnectButton.Size = new System.Drawing.Size(102, 22);
            this.ConnectButton.TabIndex = 1;
            this.ConnectButton.Text = "Connect";
            this.ConnectButton.UseVisualStyleBackColor = true;
            this.ConnectButton.Click += new System.EventHandler(this.ConnectButton_Click);
            // 
            // ConnectPN
            // 
            this.ConnectPN.Controls.Add(this.ConnectButton);
            this.ConnectPN.Dock = System.Windows.Forms.DockStyle.Right;
            this.ConnectPN.Location = new System.Drawing.Point(626, 0);
            this.ConnectPN.Name = "ConnectPN";
            this.ConnectPN.Padding = new System.Windows.Forms.Padding(3);
            this.ConnectPN.Size = new System.Drawing.Size(108, 28);
            this.ConnectPN.TabIndex = 2;
            // 
            // EndpointSelectorCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ConnectPN);
            this.Controls.Add(this.EndpointCB);
            this.MaximumSize = new System.Drawing.Size(2048, 28);
            this.MinimumSize = new System.Drawing.Size(100, 28);
            this.Name = "EndpointSelectorCtrl";
            this.Padding = new System.Windows.Forms.Padding(2, 0, 0, 0);
            this.Size = new System.Drawing.Size(734, 28);
            this.ConnectPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox EndpointCB;
        private System.Windows.Forms.Button ConnectButton;
        private System.Windows.Forms.Panel ConnectPN;
    }
}
