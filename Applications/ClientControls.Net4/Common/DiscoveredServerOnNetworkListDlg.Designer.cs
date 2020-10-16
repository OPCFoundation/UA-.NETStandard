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
    partial class DiscoveredServerOnNetworkListDlg
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
            this.TopPN = new System.Windows.Forms.Panel();
            this.HostNameLB = new System.Windows.Forms.Label();
            this.StartingRecordLB = new System.Windows.Forms.Label();
            this.CapabilityFilterTB = new System.Windows.Forms.TextBox();
            this.StartingRecordUP = new System.Windows.Forms.NumericUpDown();
            this.CapabilityFilterLB = new System.Windows.Forms.Label();
            this.ServersCTRL = new Opc.Ua.Client.Controls.DiscoveredServerOnNetworkListCtrl();
            this.HostNameCTRL = new Opc.Ua.Client.Controls.SelectHostCtrl();
            this.MaxRecordsUP = new System.Windows.Forms.NumericUpDown();
            this.MaxRecordsLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.TopPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.StartingRecordUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxRecordsUP)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(2, 424);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(673, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(594, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ServersCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(2, 55);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
            this.MainPN.Size = new System.Drawing.Size(673, 369);
            this.MainPN.TabIndex = 2;
            // 
            // TopPN
            // 
            this.TopPN.Controls.Add(this.MaxRecordsUP);
            this.TopPN.Controls.Add(this.MaxRecordsLB);
            this.TopPN.Controls.Add(this.CapabilityFilterLB);
            this.TopPN.Controls.Add(this.StartingRecordUP);
            this.TopPN.Controls.Add(this.CapabilityFilterTB);
            this.TopPN.Controls.Add(this.StartingRecordLB);
            this.TopPN.Controls.Add(this.HostNameLB);
            this.TopPN.Controls.Add(this.HostNameCTRL);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(2, 2);
            this.TopPN.Name = "TopPN";
            this.TopPN.Size = new System.Drawing.Size(673, 53);
            this.TopPN.TabIndex = 1;
            // 
            // HostNameLB
            // 
            this.HostNameLB.AutoSize = true;
            this.HostNameLB.Location = new System.Drawing.Point(0, 32);
            this.HostNameLB.Name = "HostNameLB";
            this.HostNameLB.Size = new System.Drawing.Size(60, 13);
            this.HostNameLB.TabIndex = 0;
            this.HostNameLB.Text = "Host Name";
            this.HostNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartingRecordLB
            // 
            this.StartingRecordLB.AutoSize = true;
            this.StartingRecordLB.Location = new System.Drawing.Point(0, 6);
            this.StartingRecordLB.Name = "StartingRecordLB";
            this.StartingRecordLB.Size = new System.Drawing.Size(87, 13);
            this.StartingRecordLB.TabIndex = 2;
            this.StartingRecordLB.Text = "StartingRecordId";
            this.StartingRecordLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CapabilityFilterTB
            // 
            this.CapabilityFilterTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CapabilityFilterTB.Location = new System.Drawing.Point(414, 5);
            this.CapabilityFilterTB.Name = "CapabilityFilterTB";
            this.CapabilityFilterTB.Size = new System.Drawing.Size(184, 20);
            this.CapabilityFilterTB.TabIndex = 4;
            // 
            // StartingRecordUP
            // 
            this.StartingRecordUP.Location = new System.Drawing.Point(88, 3);
            this.StartingRecordUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.StartingRecordUP.Name = "StartingRecordUP";
            this.StartingRecordUP.Size = new System.Drawing.Size(45, 20);
            this.StartingRecordUP.TabIndex = 15;
            // 
            // CapabilityFilterLB
            // 
            this.CapabilityFilterLB.AutoSize = true;
            this.CapabilityFilterLB.Location = new System.Drawing.Point(303, 7);
            this.CapabilityFilterLB.Name = "CapabilityFilterLB";
            this.CapabilityFilterLB.Size = new System.Drawing.Size(105, 13);
            this.CapabilityFilterLB.TabIndex = 16;
            this.CapabilityFilterLB.Text = "ServerCapabilityFilter";
            this.CapabilityFilterLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServersCTRL
            // 
            this.ServersCTRL.Cursor = System.Windows.Forms.Cursors.Default;
            this.ServersCTRL.DiscoveryTimeout = 0;
            this.ServersCTRL.DiscoveryUrl = null;
            this.ServersCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersCTRL.Instructions = null;
            this.ServersCTRL.Location = new System.Drawing.Point(0, 3);
            this.ServersCTRL.Name = "ServersCTRL";
            this.ServersCTRL.Size = new System.Drawing.Size(673, 366);
            this.ServersCTRL.TabIndex = 0;
            this.ServersCTRL.ItemsPicked += new Opc.Ua.Client.Controls.ListItemActionEventHandler(this.ServersCTRL_ItemsPicked);
            this.ServersCTRL.ItemsSelected += new Opc.Ua.Client.Controls.ListItemActionEventHandler(this.ServersCTRL_ItemsSelected);
            // 
            // HostNameCTRL
            // 
            this.HostNameCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.HostNameCTRL.CommandText = "Discover";
            this.HostNameCTRL.Location = new System.Drawing.Point(87, 30);
            this.HostNameCTRL.Margin = new System.Windows.Forms.Padding(0);
            this.HostNameCTRL.MaximumSize = new System.Drawing.Size(4096, 24);
            this.HostNameCTRL.MinimumSize = new System.Drawing.Size(400, 21);
            this.HostNameCTRL.Name = "HostNameCTRL";
            this.HostNameCTRL.Padding = new System.Windows.Forms.Padding(2, 0, 0, 0);
            this.HostNameCTRL.Size = new System.Drawing.Size(586, 21);
            this.HostNameCTRL.TabIndex = 1;
            this.HostNameCTRL.HostSelected += new System.EventHandler<Opc.Ua.Client.Controls.SelectHostCtrlEventArgs>(this.HostNameCTRL_HostSelected);
            this.HostNameCTRL.HostConnected += new System.EventHandler<Opc.Ua.Client.Controls.SelectHostCtrlEventArgs>(this.HostNameCTRL_HostConnected);
            // 
            // MaxRecordsUP
            // 
            this.MaxRecordsUP.Location = new System.Drawing.Point(252, 4);
            this.MaxRecordsUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.MaxRecordsUP.Name = "MaxRecordsUP";
            this.MaxRecordsUP.Size = new System.Drawing.Size(45, 20);
            this.MaxRecordsUP.TabIndex = 18;
            // 
            // MaxRecordsLB
            // 
            this.MaxRecordsLB.AutoSize = true;
            this.MaxRecordsLB.Location = new System.Drawing.Point(138, 6);
            this.MaxRecordsLB.Name = "MaxRecordsLB";
            this.MaxRecordsLB.Size = new System.Drawing.Size(112, 13);
            this.MaxRecordsLB.TabIndex = 17;
            this.MaxRecordsLB.Text = "MaxRecordsToReturn";
            this.MaxRecordsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DiscoveredServerOnNetworkListDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(677, 455);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.TopPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "DiscoveredServerOnNetworkListDlg";
            this.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Discover Servers On Network";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.StartingRecordUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxRecordsUP)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private DiscoveredServerOnNetworkListCtrl ServersCTRL;
        private SelectHostCtrl HostNameCTRL;
        private System.Windows.Forms.Panel TopPN;
        private System.Windows.Forms.Label HostNameLB;
        private System.Windows.Forms.Label StartingRecordLB;
        private System.Windows.Forms.TextBox CapabilityFilterTB;
        private System.Windows.Forms.NumericUpDown StartingRecordUP;
        private System.Windows.Forms.Label CapabilityFilterLB;
        private System.Windows.Forms.NumericUpDown MaxRecordsUP;
        private System.Windows.Forms.Label MaxRecordsLB;
    }
}
