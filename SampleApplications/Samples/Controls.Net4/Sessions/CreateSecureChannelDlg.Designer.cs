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
    partial class CreateSecureChannelDlg
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
            this.UseBinaryEncodingLabel = new System.Windows.Forms.Label();
            this.UseBinaryEncodingCK = new System.Windows.Forms.CheckBox();
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.DetailsBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.MaxByteStringLengthNC = new System.Windows.Forms.NumericUpDown();
            this.MaxByteStringLengthLB = new System.Windows.Forms.Label();
            this.MaxStringLengthNC = new System.Windows.Forms.NumericUpDown();
            this.MaxArrayLengthNC = new System.Windows.Forms.NumericUpDown();
            this.MaxMessageSizeNC = new System.Windows.Forms.NumericUpDown();
            this.MaxMessageSizeLB = new System.Windows.Forms.Label();
            this.MaxStringLengthLB = new System.Windows.Forms.Label();
            this.MaxArrayLengthLB = new System.Windows.Forms.Label();
            this.SendTimeoutNC = new System.Windows.Forms.NumericUpDown();
            this.NetworkTimeoutLB = new System.Windows.Forms.Label();
            this.EndpointCB = new System.Windows.Forms.ComboBox();
            this.EndpointLB = new System.Windows.Forms.Label();
            this.OperationTimeoutNC = new System.Windows.Forms.NumericUpDown();
            this.OperationTimeoutLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxByteStringLengthNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxStringLengthNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxArrayLengthNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxMessageSizeNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SendTimeoutNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.OperationTimeoutNC)).BeginInit();
            this.SuspendLayout();
            // 
            // UseBinaryEncodingLabel
            // 
            this.UseBinaryEncodingLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UseBinaryEncodingLabel.AutoSize = true;
            this.UseBinaryEncodingLabel.Location = new System.Drawing.Point(4, 32);
            this.UseBinaryEncodingLabel.Name = "UseBinaryEncodingLabel";
            this.UseBinaryEncodingLabel.Size = new System.Drawing.Size(106, 13);
            this.UseBinaryEncodingLabel.TabIndex = 10;
            this.UseBinaryEncodingLabel.Text = "Use Binary Encoding";
            this.UseBinaryEncodingLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UseBinaryEncodingCK
            // 
            this.UseBinaryEncodingCK.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UseBinaryEncodingCK.AutoSize = true;
            this.UseBinaryEncodingCK.Checked = true;
            this.UseBinaryEncodingCK.CheckState = System.Windows.Forms.CheckState.Checked;
            this.UseBinaryEncodingCK.Location = new System.Drawing.Point(122, 32);
            this.UseBinaryEncodingCK.Name = "UseBinaryEncodingCK";
            this.UseBinaryEncodingCK.Size = new System.Drawing.Size(15, 14);
            this.UseBinaryEncodingCK.TabIndex = 11;
            this.UseBinaryEncodingCK.UseVisualStyleBackColor = true;
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.DetailsBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 200);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(387, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // DetailsBTN
            // 
            this.DetailsBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.DetailsBTN.Location = new System.Drawing.Point(156, 4);
            this.DetailsBTN.Name = "DetailsBTN";
            this.DetailsBTN.Size = new System.Drawing.Size(75, 23);
            this.DetailsBTN.TabIndex = 2;
            this.DetailsBTN.Text = "Details...";
            this.DetailsBTN.UseVisualStyleBackColor = true;
            this.DetailsBTN.Click += new System.EventHandler(this.DetailsBTN_Click);
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
            this.CancelBTN.Location = new System.Drawing.Point(308, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.MaxByteStringLengthNC);
            this.MainPN.Controls.Add(this.MaxByteStringLengthLB);
            this.MainPN.Controls.Add(this.MaxStringLengthNC);
            this.MainPN.Controls.Add(this.MaxArrayLengthNC);
            this.MainPN.Controls.Add(this.MaxMessageSizeNC);
            this.MainPN.Controls.Add(this.MaxMessageSizeLB);
            this.MainPN.Controls.Add(this.MaxStringLengthLB);
            this.MainPN.Controls.Add(this.MaxArrayLengthLB);
            this.MainPN.Controls.Add(this.SendTimeoutNC);
            this.MainPN.Controls.Add(this.NetworkTimeoutLB);
            this.MainPN.Controls.Add(this.EndpointCB);
            this.MainPN.Controls.Add(this.EndpointLB);
            this.MainPN.Controls.Add(this.OperationTimeoutNC);
            this.MainPN.Controls.Add(this.OperationTimeoutLB);
            this.MainPN.Controls.Add(this.UseBinaryEncodingLabel);
            this.MainPN.Controls.Add(this.UseBinaryEncodingCK);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(387, 200);
            this.MainPN.TabIndex = 1;
            // 
            // MaxByteStringLengthNC
            // 
            this.MaxByteStringLengthNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.MaxByteStringLengthNC.Location = new System.Drawing.Point(122, 174);
            this.MaxByteStringLengthNC.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            this.MaxByteStringLengthNC.Name = "MaxByteStringLengthNC";
            this.MaxByteStringLengthNC.Size = new System.Drawing.Size(120, 20);
            this.MaxByteStringLengthNC.TabIndex = 36;
            // 
            // MaxByteStringLengthLB
            // 
            this.MaxByteStringLengthLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxByteStringLengthLB.AutoSize = true;
            this.MaxByteStringLengthLB.Location = new System.Drawing.Point(4, 178);
            this.MaxByteStringLengthLB.Name = "MaxByteStringLengthLB";
            this.MaxByteStringLengthLB.Size = new System.Drawing.Size(93, 13);
            this.MaxByteStringLengthLB.TabIndex = 35;
            this.MaxByteStringLengthLB.Text = "Max String Length";
            this.MaxByteStringLengthLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxStringLengthNC
            // 
            this.MaxStringLengthNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.MaxStringLengthNC.Location = new System.Drawing.Point(122, 148);
            this.MaxStringLengthNC.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            this.MaxStringLengthNC.Name = "MaxStringLengthNC";
            this.MaxStringLengthNC.Size = new System.Drawing.Size(120, 20);
            this.MaxStringLengthNC.TabIndex = 34;
            // 
            // MaxArrayLengthNC
            // 
            this.MaxArrayLengthNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.MaxArrayLengthNC.Location = new System.Drawing.Point(122, 124);
            this.MaxArrayLengthNC.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            this.MaxArrayLengthNC.Name = "MaxArrayLengthNC";
            this.MaxArrayLengthNC.Size = new System.Drawing.Size(120, 20);
            this.MaxArrayLengthNC.TabIndex = 33;
            // 
            // MaxMessageSizeNC
            // 
            this.MaxMessageSizeNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.MaxMessageSizeNC.Location = new System.Drawing.Point(122, 100);
            this.MaxMessageSizeNC.Maximum = new decimal(new int[] {
            2147483647,
            0,
            0,
            0});
            this.MaxMessageSizeNC.Name = "MaxMessageSizeNC";
            this.MaxMessageSizeNC.Size = new System.Drawing.Size(120, 20);
            this.MaxMessageSizeNC.TabIndex = 32;
            // 
            // MaxMessageSizeLB
            // 
            this.MaxMessageSizeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxMessageSizeLB.AutoSize = true;
            this.MaxMessageSizeLB.Location = new System.Drawing.Point(4, 104);
            this.MaxMessageSizeLB.Name = "MaxMessageSizeLB";
            this.MaxMessageSizeLB.Size = new System.Drawing.Size(96, 13);
            this.MaxMessageSizeLB.TabIndex = 30;
            this.MaxMessageSizeLB.Text = "Max Message Size";
            this.MaxMessageSizeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxStringLengthLB
            // 
            this.MaxStringLengthLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxStringLengthLB.AutoSize = true;
            this.MaxStringLengthLB.Location = new System.Drawing.Point(4, 152);
            this.MaxStringLengthLB.Name = "MaxStringLengthLB";
            this.MaxStringLengthLB.Size = new System.Drawing.Size(93, 13);
            this.MaxStringLengthLB.TabIndex = 28;
            this.MaxStringLengthLB.Text = "Max String Length";
            this.MaxStringLengthLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxArrayLengthLB
            // 
            this.MaxArrayLengthLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxArrayLengthLB.AutoSize = true;
            this.MaxArrayLengthLB.Location = new System.Drawing.Point(4, 128);
            this.MaxArrayLengthLB.Name = "MaxArrayLengthLB";
            this.MaxArrayLengthLB.Size = new System.Drawing.Size(90, 13);
            this.MaxArrayLengthLB.TabIndex = 26;
            this.MaxArrayLengthLB.Text = "Max Array Length";
            this.MaxArrayLengthLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SendTimeoutNC
            // 
            this.SendTimeoutNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.SendTimeoutNC.Location = new System.Drawing.Point(122, 52);
            this.SendTimeoutNC.Maximum = new decimal(new int[] {
            3600000,
            0,
            0,
            0});
            this.SendTimeoutNC.Name = "SendTimeoutNC";
            this.SendTimeoutNC.Size = new System.Drawing.Size(120, 20);
            this.SendTimeoutNC.TabIndex = 13;
            // 
            // NetworkTimeoutLB
            // 
            this.NetworkTimeoutLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NetworkTimeoutLB.AutoSize = true;
            this.NetworkTimeoutLB.Location = new System.Drawing.Point(4, 56);
            this.NetworkTimeoutLB.Name = "NetworkTimeoutLB";
            this.NetworkTimeoutLB.Size = new System.Drawing.Size(88, 13);
            this.NetworkTimeoutLB.TabIndex = 12;
            this.NetworkTimeoutLB.Text = "Network Timeout";
            this.NetworkTimeoutLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndpointCB
            // 
            this.EndpointCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EndpointCB.FormattingEnabled = true;
            this.EndpointCB.Location = new System.Drawing.Point(122, 4);
            this.EndpointCB.Name = "EndpointCB";
            this.EndpointCB.Size = new System.Drawing.Size(261, 21);
            this.EndpointCB.TabIndex = 1;
            this.EndpointCB.SelectedIndexChanged += new System.EventHandler(this.EndpointCB_SelectedIndexChanged);
            // 
            // EndpointLB
            // 
            this.EndpointLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EndpointLB.AutoSize = true;
            this.EndpointLB.Location = new System.Drawing.Point(4, 8);
            this.EndpointLB.Name = "EndpointLB";
            this.EndpointLB.Size = new System.Drawing.Size(49, 13);
            this.EndpointLB.TabIndex = 0;
            this.EndpointLB.Text = "Endpoint";
            this.EndpointLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // OperationTimeoutNC
            // 
            this.OperationTimeoutNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.OperationTimeoutNC.Location = new System.Drawing.Point(122, 76);
            this.OperationTimeoutNC.Maximum = new decimal(new int[] {
            3600000,
            0,
            0,
            0});
            this.OperationTimeoutNC.Name = "OperationTimeoutNC";
            this.OperationTimeoutNC.Size = new System.Drawing.Size(120, 20);
            this.OperationTimeoutNC.TabIndex = 9;
            // 
            // OperationTimeoutLB
            // 
            this.OperationTimeoutLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.OperationTimeoutLB.AutoSize = true;
            this.OperationTimeoutLB.Location = new System.Drawing.Point(4, 80);
            this.OperationTimeoutLB.Name = "OperationTimeoutLB";
            this.OperationTimeoutLB.Size = new System.Drawing.Size(94, 13);
            this.OperationTimeoutLB.TabIndex = 8;
            this.OperationTimeoutLB.Text = "Operation Timeout";
            this.OperationTimeoutLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CreateSecureChannelDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(387, 231);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "CreateSecureChannelDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Create Secure Channel";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxByteStringLengthNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxStringLengthNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxArrayLengthNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxMessageSizeNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SendTimeoutNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.OperationTimeoutNC)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label UseBinaryEncodingLabel;
        private System.Windows.Forms.CheckBox UseBinaryEncodingCK;
        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label OperationTimeoutLB;
        private System.Windows.Forms.NumericUpDown OperationTimeoutNC;
        private System.Windows.Forms.ComboBox EndpointCB;
        private System.Windows.Forms.Label EndpointLB;
        private System.Windows.Forms.NumericUpDown SendTimeoutNC;
        private System.Windows.Forms.Label NetworkTimeoutLB;
        private System.Windows.Forms.Button DetailsBTN;
        private System.Windows.Forms.Label MaxMessageSizeLB;
        private System.Windows.Forms.Label MaxStringLengthLB;
        private System.Windows.Forms.Label MaxArrayLengthLB;
        private System.Windows.Forms.NumericUpDown MaxStringLengthNC;
        private System.Windows.Forms.NumericUpDown MaxArrayLengthNC;
        private System.Windows.Forms.NumericUpDown MaxMessageSizeNC;
        private System.Windows.Forms.NumericUpDown MaxByteStringLengthNC;
        private System.Windows.Forms.Label MaxByteStringLengthLB;
    }
}
