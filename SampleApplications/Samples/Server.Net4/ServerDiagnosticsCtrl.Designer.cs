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
    partial class ServerDiagnosticsCtrl
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
            this.components = new System.ComponentModel.Container();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.SessionsGB = new System.Windows.Forms.GroupBox();
            this.SessionsLV = new System.Windows.Forms.ListView();
            this.SessionIdCH = new System.Windows.Forms.ColumnHeader();
            this.SessionNameCH = new System.Windows.Forms.ColumnHeader();
            this.UserNameCH = new System.Windows.Forms.ColumnHeader();
            this.LastContactTimeCH = new System.Windows.Forms.ColumnHeader();
            this.SubscriptionsGB = new System.Windows.Forms.GroupBox();
            this.SubscriptionsLV = new System.Windows.Forms.ListView();
            this.SubscriptionIdCH = new System.Windows.Forms.ColumnHeader();
            this.PublishingIntervalCH = new System.Windows.Forms.ColumnHeader();
            this.ItemCountCH = new System.Windows.Forms.ColumnHeader();
            this.SequenceNumberCH = new System.Windows.Forms.ColumnHeader();
            this.AddressPN = new System.Windows.Forms.Panel();
            this.EndpointsLB = new System.Windows.Forms.Label();
            this.UrlCB = new System.Windows.Forms.ComboBox();
            this.StatusBAR = new System.Windows.Forms.StatusStrip();
            this.ServerStatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerStateLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerTimeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.UpdateTimerCTRL = new System.Windows.Forms.Timer(this.components);
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SessionsGB.SuspendLayout();
            this.SubscriptionsGB.SuspendLayout();
            this.AddressPN.SuspendLayout();
            this.StatusBAR.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 32);
            this.MainPN.Name = "MainPN";
            this.MainPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.SessionsGB);
            this.MainPN.Panel1.Padding = new System.Windows.Forms.Padding(2);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.SubscriptionsGB);
            this.MainPN.Panel2.Padding = new System.Windows.Forms.Padding(2);
            this.MainPN.Size = new System.Drawing.Size(532, 291);
            this.MainPN.SplitterDistance = 131;
            this.MainPN.TabIndex = 3;
            // 
            // SessionsGB
            // 
            this.SessionsGB.Controls.Add(this.SessionsLV);
            this.SessionsGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SessionsGB.Location = new System.Drawing.Point(2, 2);
            this.SessionsGB.Name = "SessionsGB";
            this.SessionsGB.Size = new System.Drawing.Size(528, 127);
            this.SessionsGB.TabIndex = 2;
            this.SessionsGB.TabStop = false;
            this.SessionsGB.Text = "Sessions";
            // 
            // SessionsLV
            // 
            this.SessionsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.SessionIdCH,
            this.SessionNameCH,
            this.UserNameCH,
            this.LastContactTimeCH});
            this.SessionsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SessionsLV.FullRowSelect = true;
            this.SessionsLV.Location = new System.Drawing.Point(3, 16);
            this.SessionsLV.Name = "SessionsLV";
            this.SessionsLV.Size = new System.Drawing.Size(522, 108);
            this.SessionsLV.TabIndex = 0;
            this.SessionsLV.UseCompatibleStateImageBehavior = false;
            this.SessionsLV.View = System.Windows.Forms.View.Details;
            // 
            // SessionIdCH
            // 
            this.SessionIdCH.Text = "SessionId";
            this.SessionIdCH.Width = 101;
            // 
            // SessionNameCH
            // 
            this.SessionNameCH.Text = "Name";
            this.SessionNameCH.Width = 90;
            // 
            // UserNameCH
            // 
            this.UserNameCH.Text = "User";
            this.UserNameCH.Width = 90;
            // 
            // LastContactTimeCH
            // 
            this.LastContactTimeCH.Text = "Last Contact";
            this.LastContactTimeCH.Width = 126;
            // 
            // SubscriptionsGB
            // 
            this.SubscriptionsGB.Controls.Add(this.SubscriptionsLV);
            this.SubscriptionsGB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SubscriptionsGB.Location = new System.Drawing.Point(2, 2);
            this.SubscriptionsGB.Name = "SubscriptionsGB";
            this.SubscriptionsGB.Size = new System.Drawing.Size(528, 152);
            this.SubscriptionsGB.TabIndex = 1;
            this.SubscriptionsGB.TabStop = false;
            this.SubscriptionsGB.Text = "Subscriptions";
            // 
            // SubscriptionsLV
            // 
            this.SubscriptionsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.SubscriptionIdCH,
            this.PublishingIntervalCH,
            this.ItemCountCH,
            this.SequenceNumberCH});
            this.SubscriptionsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SubscriptionsLV.FullRowSelect = true;
            this.SubscriptionsLV.Location = new System.Drawing.Point(3, 16);
            this.SubscriptionsLV.Name = "SubscriptionsLV";
            this.SubscriptionsLV.Size = new System.Drawing.Size(522, 133);
            this.SubscriptionsLV.TabIndex = 1;
            this.SubscriptionsLV.UseCompatibleStateImageBehavior = false;
            this.SubscriptionsLV.View = System.Windows.Forms.View.Details;
            // 
            // SubscriptionIdCH
            // 
            this.SubscriptionIdCH.Text = "SubscriptionId";
            this.SubscriptionIdCH.Width = 90;
            // 
            // PublishingIntervalCH
            // 
            this.PublishingIntervalCH.Text = "Publishing Interval";
            this.PublishingIntervalCH.Width = 101;
            // 
            // ItemCountCH
            // 
            this.ItemCountCH.Text = "Item Count";
            this.ItemCountCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.ItemCountCH.Width = 126;
            // 
            // SequenceNumberCH
            // 
            this.SequenceNumberCH.Text = "Seq No";
            this.SequenceNumberCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // AddressPN
            // 
            this.AddressPN.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.AddressPN.Controls.Add(this.EndpointsLB);
            this.AddressPN.Controls.Add(this.UrlCB);
            this.AddressPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.AddressPN.Location = new System.Drawing.Point(0, 0);
            this.AddressPN.Name = "AddressPN";
            this.AddressPN.Padding = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.AddressPN.Size = new System.Drawing.Size(532, 32);
            this.AddressPN.TabIndex = 4;
            // 
            // EndpointsLB
            // 
            this.EndpointsLB.AutoSize = true;
            this.EndpointsLB.Location = new System.Drawing.Point(0, 7);
            this.EndpointsLB.Name = "EndpointsLB";
            this.EndpointsLB.Size = new System.Drawing.Size(113, 13);
            this.EndpointsLB.TabIndex = 2;
            this.EndpointsLB.Text = "Server Endpoint URLs";
            this.EndpointsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UrlCB
            // 
            this.UrlCB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UrlCB.FormattingEnabled = true;
            this.UrlCB.Location = new System.Drawing.Point(119, 4);
            this.UrlCB.Name = "UrlCB";
            this.UrlCB.Size = new System.Drawing.Size(406, 21);
            this.UrlCB.TabIndex = 1;
            // 
            // StatusBAR
            // 
            this.StatusBAR.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerStatusLB,
            this.ServerStateLB,
            this.ServerTimeLB});
            this.StatusBAR.Location = new System.Drawing.Point(0, 323);
            this.StatusBAR.Name = "StatusBAR";
            this.StatusBAR.Size = new System.Drawing.Size(532, 22);
            this.StatusBAR.TabIndex = 5;
            this.StatusBAR.Text = "statusStrip1";
            // 
            // ServerStatusLB
            // 
            this.ServerStatusLB.Name = "ServerStatusLB";
            this.ServerStatusLB.Size = new System.Drawing.Size(42, 17);
            this.ServerStatusLB.Text = "Status:";
            // 
            // ServerStateLB
            // 
            this.ServerStateLB.Name = "ServerStateLB";
            this.ServerStateLB.Size = new System.Drawing.Size(52, 17);
            this.ServerStateLB.Text = "Running";
            // 
            // ServerTimeLB
            // 
            this.ServerTimeLB.Name = "ServerTimeLB";
            this.ServerTimeLB.Size = new System.Drawing.Size(49, 17);
            this.ServerTimeLB.Text = "00:00:00";
            // 
            // UpdateTimerCTRL
            // 
            this.UpdateTimerCTRL.Interval = 1000;
            this.UpdateTimerCTRL.Tick += new System.EventHandler(this.UpdateTimerCTRL_Tick);
            // 
            // ServerDiagnosticsCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBAR);
            this.Controls.Add(this.AddressPN);
            this.Name = "ServerDiagnosticsCtrl";
            this.Size = new System.Drawing.Size(532, 345);
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.SessionsGB.ResumeLayout(false);
            this.SubscriptionsGB.ResumeLayout(false);
            this.AddressPN.ResumeLayout(false);
            this.AddressPN.PerformLayout();
            this.StatusBAR.ResumeLayout(false);
            this.StatusBAR.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.SplitContainer MainPN;
        private System.Windows.Forms.GroupBox SessionsGB;
        private System.Windows.Forms.ListView SessionsLV;
        private System.Windows.Forms.ColumnHeader SessionIdCH;
        private System.Windows.Forms.ColumnHeader SessionNameCH;
        private System.Windows.Forms.ColumnHeader UserNameCH;
        private System.Windows.Forms.ColumnHeader LastContactTimeCH;
        private System.Windows.Forms.GroupBox SubscriptionsGB;
        private System.Windows.Forms.ListView SubscriptionsLV;
        private System.Windows.Forms.ColumnHeader SubscriptionIdCH;
        private System.Windows.Forms.ColumnHeader PublishingIntervalCH;
        private System.Windows.Forms.ColumnHeader ItemCountCH;
        private System.Windows.Forms.ColumnHeader SequenceNumberCH;
        private System.Windows.Forms.Panel AddressPN;
        private System.Windows.Forms.Label EndpointsLB;
        private System.Windows.Forms.ComboBox UrlCB;
        private System.Windows.Forms.StatusStrip StatusBAR;
        private System.Windows.Forms.ToolStripStatusLabel ServerStatusLB;
        private System.Windows.Forms.ToolStripStatusLabel ServerStateLB;
        private System.Windows.Forms.ToolStripStatusLabel ServerTimeLB;
        private System.Windows.Forms.Timer UpdateTimerCTRL;
    }
}
