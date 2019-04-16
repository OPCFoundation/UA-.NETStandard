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
    partial class MonitoredItemEditDlg
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
            this.RelativePathTB = new System.Windows.Forms.TextBox();
            this.RelativePathLB = new System.Windows.Forms.Label();
            this.NodeClassCB = new System.Windows.Forms.ComboBox();
            this.NodeClassLB = new System.Windows.Forms.Label();
            this.NodeIdCTRL = new Opc.Ua.Client.Controls.NodeIdCtrl();
            this.MonitoringModeCB = new System.Windows.Forms.ComboBox();
            this.EncodingCB = new System.Windows.Forms.ComboBox();
            this.IndexRangeTB = new System.Windows.Forms.TextBox();
            this.AttributeIdCB = new System.Windows.Forms.ComboBox();
            this.DiscardOldestLB = new System.Windows.Forms.Label();
            this.QueueSizeLB = new System.Windows.Forms.Label();
            this.SamplingIntervalLB = new System.Windows.Forms.Label();
            this.MonitoringModeLB = new System.Windows.Forms.Label();
            this.AttributeIdLB = new System.Windows.Forms.Label();
            this.DisplayNameTB = new System.Windows.Forms.TextBox();
            this.QueueSizeNC = new System.Windows.Forms.NumericUpDown();
            this.SamplingIntervalNC = new System.Windows.Forms.NumericUpDown();
            this.IndexRangeLB = new System.Windows.Forms.Label();
            this.DisableOldestCK = new System.Windows.Forms.CheckBox();
            this.StartNodeIdLB = new System.Windows.Forms.Label();
            this.EncodingLB = new System.Windows.Forms.Label();
            this.DisplayNameLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.QueueSizeNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNC)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 280);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(370, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 0;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(291, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.RelativePathTB);
            this.MainPN.Controls.Add(this.RelativePathLB);
            this.MainPN.Controls.Add(this.NodeClassCB);
            this.MainPN.Controls.Add(this.NodeClassLB);
            this.MainPN.Controls.Add(this.NodeIdCTRL);
            this.MainPN.Controls.Add(this.MonitoringModeCB);
            this.MainPN.Controls.Add(this.EncodingCB);
            this.MainPN.Controls.Add(this.IndexRangeTB);
            this.MainPN.Controls.Add(this.AttributeIdCB);
            this.MainPN.Controls.Add(this.DiscardOldestLB);
            this.MainPN.Controls.Add(this.QueueSizeLB);
            this.MainPN.Controls.Add(this.SamplingIntervalLB);
            this.MainPN.Controls.Add(this.MonitoringModeLB);
            this.MainPN.Controls.Add(this.AttributeIdLB);
            this.MainPN.Controls.Add(this.DisplayNameTB);
            this.MainPN.Controls.Add(this.QueueSizeNC);
            this.MainPN.Controls.Add(this.SamplingIntervalNC);
            this.MainPN.Controls.Add(this.IndexRangeLB);
            this.MainPN.Controls.Add(this.DisableOldestCK);
            this.MainPN.Controls.Add(this.StartNodeIdLB);
            this.MainPN.Controls.Add(this.EncodingLB);
            this.MainPN.Controls.Add(this.DisplayNameLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(370, 280);
            this.MainPN.TabIndex = 1;
            // 
            // RelativePathTB
            // 
            this.RelativePathTB.Location = new System.Drawing.Point(106, 56);
            this.RelativePathTB.Name = "RelativePathTB";
            this.RelativePathTB.Size = new System.Drawing.Size(259, 20);
            this.RelativePathTB.TabIndex = 39;
            // 
            // RelativePathLB
            // 
            this.RelativePathLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RelativePathLB.AutoSize = true;
            this.RelativePathLB.Location = new System.Drawing.Point(5, 59);
            this.RelativePathLB.Name = "RelativePathLB";
            this.RelativePathLB.Size = new System.Drawing.Size(71, 13);
            this.RelativePathLB.TabIndex = 38;
            this.RelativePathLB.Text = "Relative Path";
            this.RelativePathLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeClassCB
            // 
            this.NodeClassCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.NodeClassCB.FormattingEnabled = true;
            this.NodeClassCB.Location = new System.Drawing.Point(106, 82);
            this.NodeClassCB.Name = "NodeClassCB";
            this.NodeClassCB.Size = new System.Drawing.Size(122, 21);
            this.NodeClassCB.TabIndex = 37;
            // 
            // NodeClassLB
            // 
            this.NodeClassLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeClassLB.AutoSize = true;
            this.NodeClassLB.Location = new System.Drawing.Point(5, 85);
            this.NodeClassLB.Name = "NodeClassLB";
            this.NodeClassLB.Size = new System.Drawing.Size(61, 13);
            this.NodeClassLB.TabIndex = 36;
            this.NodeClassLB.Text = "Node Class";
            this.NodeClassLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // NodeIdCTRL
            // 
            this.NodeIdCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.NodeIdCTRL.Location = new System.Drawing.Point(106, 30);
            this.NodeIdCTRL.MaximumSize = new System.Drawing.Size(4096, 20);
            this.NodeIdCTRL.MinimumSize = new System.Drawing.Size(100, 20);
            this.NodeIdCTRL.Name = "NodeIdCTRL";
            this.NodeIdCTRL.Size = new System.Drawing.Size(259, 20);
            this.NodeIdCTRL.TabIndex = 22;
            this.NodeIdCTRL.IdentifierChanged += new System.EventHandler(this.NodeIdCTRL_IdentifierChanged);
            // 
            // MonitoringModeCB
            // 
            this.MonitoringModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MonitoringModeCB.FormattingEnabled = true;
            this.MonitoringModeCB.Location = new System.Drawing.Point(106, 188);
            this.MonitoringModeCB.Name = "MonitoringModeCB";
            this.MonitoringModeCB.Size = new System.Drawing.Size(202, 21);
            this.MonitoringModeCB.TabIndex = 13;
            // 
            // EncodingCB
            // 
            this.EncodingCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncodingCB.FormattingEnabled = true;
            this.EncodingCB.Location = new System.Drawing.Point(106, 161);
            this.EncodingCB.Name = "EncodingCB";
            this.EncodingCB.Size = new System.Drawing.Size(202, 21);
            this.EncodingCB.TabIndex = 11;
            // 
            // IndexRangeTB
            // 
            this.IndexRangeTB.Location = new System.Drawing.Point(106, 135);
            this.IndexRangeTB.Name = "IndexRangeTB";
            this.IndexRangeTB.Size = new System.Drawing.Size(88, 20);
            this.IndexRangeTB.TabIndex = 9;
            // 
            // AttributeIdCB
            // 
            this.AttributeIdCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AttributeIdCB.FormattingEnabled = true;
            this.AttributeIdCB.Location = new System.Drawing.Point(106, 108);
            this.AttributeIdCB.Name = "AttributeIdCB";
            this.AttributeIdCB.Size = new System.Drawing.Size(202, 21);
            this.AttributeIdCB.TabIndex = 7;
            // 
            // DiscardOldestLB
            // 
            this.DiscardOldestLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DiscardOldestLB.AutoSize = true;
            this.DiscardOldestLB.Location = new System.Drawing.Point(4, 267);
            this.DiscardOldestLB.Name = "DiscardOldestLB";
            this.DiscardOldestLB.Size = new System.Drawing.Size(76, 13);
            this.DiscardOldestLB.TabIndex = 18;
            this.DiscardOldestLB.Text = "Discard Oldest";
            this.DiscardOldestLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // QueueSizeLB
            // 
            this.QueueSizeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.QueueSizeLB.AutoSize = true;
            this.QueueSizeLB.Location = new System.Drawing.Point(5, 243);
            this.QueueSizeLB.Name = "QueueSizeLB";
            this.QueueSizeLB.Size = new System.Drawing.Size(62, 13);
            this.QueueSizeLB.TabIndex = 16;
            this.QueueSizeLB.Text = "Queue Size";
            this.QueueSizeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SamplingIntervalLB
            // 
            this.SamplingIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SamplingIntervalLB.AutoSize = true;
            this.SamplingIntervalLB.Location = new System.Drawing.Point(4, 217);
            this.SamplingIntervalLB.Name = "SamplingIntervalLB";
            this.SamplingIntervalLB.Size = new System.Drawing.Size(88, 13);
            this.SamplingIntervalLB.TabIndex = 14;
            this.SamplingIntervalLB.Text = "Sampling Interval";
            this.SamplingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MonitoringModeLB
            // 
            this.MonitoringModeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MonitoringModeLB.AutoSize = true;
            this.MonitoringModeLB.Location = new System.Drawing.Point(4, 191);
            this.MonitoringModeLB.Name = "MonitoringModeLB";
            this.MonitoringModeLB.Size = new System.Drawing.Size(86, 13);
            this.MonitoringModeLB.TabIndex = 12;
            this.MonitoringModeLB.Text = "Monitoring Mode";
            this.MonitoringModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AttributeIdLB
            // 
            this.AttributeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.AttributeIdLB.AutoSize = true;
            this.AttributeIdLB.Location = new System.Drawing.Point(5, 111);
            this.AttributeIdLB.Name = "AttributeIdLB";
            this.AttributeIdLB.Size = new System.Drawing.Size(46, 13);
            this.AttributeIdLB.TabIndex = 6;
            this.AttributeIdLB.Text = "Attribute";
            this.AttributeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameTB
            // 
            this.DisplayNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameTB.Location = new System.Drawing.Point(106, 4);
            this.DisplayNameTB.Name = "DisplayNameTB";
            this.DisplayNameTB.Size = new System.Drawing.Size(260, 20);
            this.DisplayNameTB.TabIndex = 1;
            // 
            // QueueSizeNC
            // 
            this.QueueSizeNC.Location = new System.Drawing.Point(106, 241);
            this.QueueSizeNC.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.QueueSizeNC.Name = "QueueSizeNC";
            this.QueueSizeNC.Size = new System.Drawing.Size(88, 20);
            this.QueueSizeNC.TabIndex = 17;
            this.QueueSizeNC.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // SamplingIntervalNC
            // 
            this.SamplingIntervalNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.SamplingIntervalNC.Location = new System.Drawing.Point(106, 215);
            this.SamplingIntervalNC.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.SamplingIntervalNC.Name = "SamplingIntervalNC";
            this.SamplingIntervalNC.Size = new System.Drawing.Size(88, 20);
            this.SamplingIntervalNC.TabIndex = 15;
            this.SamplingIntervalNC.Value = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            // 
            // IndexRangeLB
            // 
            this.IndexRangeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IndexRangeLB.AutoSize = true;
            this.IndexRangeLB.Location = new System.Drawing.Point(4, 138);
            this.IndexRangeLB.Name = "IndexRangeLB";
            this.IndexRangeLB.Size = new System.Drawing.Size(68, 13);
            this.IndexRangeLB.TabIndex = 8;
            this.IndexRangeLB.Text = "Index Range";
            this.IndexRangeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisableOldestCK
            // 
            this.DisableOldestCK.AutoSize = true;
            this.DisableOldestCK.Location = new System.Drawing.Point(106, 267);
            this.DisableOldestCK.Name = "DisableOldestCK";
            this.DisableOldestCK.Size = new System.Drawing.Size(15, 14);
            this.DisableOldestCK.TabIndex = 19;
            this.DisableOldestCK.UseVisualStyleBackColor = true;
            // 
            // StartNodeIdLB
            // 
            this.StartNodeIdLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartNodeIdLB.AutoSize = true;
            this.StartNodeIdLB.Location = new System.Drawing.Point(5, 34);
            this.StartNodeIdLB.Name = "StartNodeIdLB";
            this.StartNodeIdLB.Size = new System.Drawing.Size(47, 13);
            this.StartNodeIdLB.TabIndex = 2;
            this.StartNodeIdLB.Text = "Node ID";
            this.StartNodeIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EncodingLB
            // 
            this.EncodingLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EncodingLB.AutoSize = true;
            this.EncodingLB.Location = new System.Drawing.Point(5, 164);
            this.EncodingLB.Name = "EncodingLB";
            this.EncodingLB.Size = new System.Drawing.Size(78, 13);
            this.EncodingLB.TabIndex = 10;
            this.EncodingLB.Text = "Data Encoding";
            this.EncodingLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameLB
            // 
            this.DisplayNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameLB.AutoSize = true;
            this.DisplayNameLB.Location = new System.Drawing.Point(5, 7);
            this.DisplayNameLB.Name = "DisplayNameLB";
            this.DisplayNameLB.Size = new System.Drawing.Size(72, 13);
            this.DisplayNameLB.TabIndex = 0;
            this.DisplayNameLB.Text = "Display Name";
            this.DisplayNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MonitoredItemEditDlg
            // 
            this.AcceptButton = this.OkBTN;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(370, 311);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MonitoredItemEditDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Monitored Item";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.QueueSizeNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SamplingIntervalNC)).EndInit();
            this.ResumeLayout(false);

        }
        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label DisplayNameLB;
        private System.Windows.Forms.Label StartNodeIdLB;
        private System.Windows.Forms.Label EncodingLB;
        private System.Windows.Forms.CheckBox DisableOldestCK;
        private System.Windows.Forms.Label IndexRangeLB;
        private System.Windows.Forms.TextBox DisplayNameTB;
        private System.Windows.Forms.NumericUpDown QueueSizeNC;
        private System.Windows.Forms.NumericUpDown SamplingIntervalNC;
        private System.Windows.Forms.Label DiscardOldestLB;
        private System.Windows.Forms.Label QueueSizeLB;
        private System.Windows.Forms.Label SamplingIntervalLB;
        private System.Windows.Forms.Label MonitoringModeLB;
        private System.Windows.Forms.Label AttributeIdLB;
        private System.Windows.Forms.ComboBox MonitoringModeCB;
        private System.Windows.Forms.ComboBox EncodingCB;
        private System.Windows.Forms.TextBox IndexRangeTB;
        private System.Windows.Forms.ComboBox AttributeIdCB;
        private Opc.Ua.Client.Controls.NodeIdCtrl NodeIdCTRL;
        private System.Windows.Forms.ComboBox NodeClassCB;
        private System.Windows.Forms.Label NodeClassLB;
        private System.Windows.Forms.TextBox RelativePathTB;
        private System.Windows.Forms.Label RelativePathLB;
    }
}
