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
    partial class GdsDiscoverServersDlg
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
            this.components = new System.ComponentModel.Container();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.BrowseCK = new System.Windows.Forms.CheckBox();
            this.SearchBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.ServersLV = new System.Windows.Forms.ListView();
            this.ApplicationNameCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.ApplicationTypeCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.DNSNamesCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.ProtocolsCH = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.DetailsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.TopPN = new System.Windows.Forms.TableLayoutPanel();
            this.ProductUriTB = new System.Windows.Forms.TextBox();
            this.ProductUriCB = new System.Windows.Forms.ComboBox();
            this.ProductUriLB = new System.Windows.Forms.Label();
            this.SystemElementTB = new System.Windows.Forms.TextBox();
            this.SystemElementLB = new System.Windows.Forms.Label();
            this.MachineNameCB = new System.Windows.Forms.ComboBox();
            this.ApplicationUriCB = new System.Windows.Forms.ComboBox();
            this.ApplicationUriTB = new System.Windows.Forms.TextBox();
            this.MachineNameLB = new System.Windows.Forms.Label();
            this.MachineNameTB = new System.Windows.Forms.TextBox();
            this.ApplicationUriLB = new System.Windows.Forms.Label();
            this.ApplicationNameTB = new System.Windows.Forms.TextBox();
            this.ApplicationNameLB = new System.Windows.Forms.Label();
            this.ApplicationNameCB = new System.Windows.Forms.ComboBox();
            this.SystemElementBTN = new Opc.Ua.Client.Controls.SelectNodeCtrl();
            this.SearchPN = new System.Windows.Forms.Panel();
            this.BrowseCTRL = new Opc.Ua.Client.Controls.BrowseNodeCtrl();
            this.ServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.BottomPN.SuspendLayout();
            this.PopupMenu.SuspendLayout();
            this.TopPN.SuspendLayout();
            this.SearchPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.BrowseCK);
            this.BottomPN.Controls.Add(this.SearchBTN);
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 332);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(784, 30);
            this.BottomPN.TabIndex = 6;
            // 
            // BrowseCK
            // 
            this.BrowseCK.AutoSize = true;
            this.BrowseCK.Location = new System.Drawing.Point(84, 4);
            this.BrowseCK.Name = "BrowseCK";
            this.BrowseCK.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.BrowseCK.Size = new System.Drawing.Size(61, 23);
            this.BrowseCK.TabIndex = 5;
            this.BrowseCK.Text = "Browse";
            this.BrowseCK.UseVisualStyleBackColor = true;
            this.BrowseCK.CheckedChanged += new System.EventHandler(this.BrowseCK_CheckedChanged);
            // 
            // SearchBTN
            // 
            this.SearchBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.SearchBTN.Location = new System.Drawing.Point(3, 4);
            this.SearchBTN.Name = "SearchBTN";
            this.SearchBTN.Size = new System.Drawing.Size(75, 23);
            this.SearchBTN.TabIndex = 2;
            this.SearchBTN.Text = "Search";
            this.SearchBTN.UseVisualStyleBackColor = true;
            this.SearchBTN.Click += new System.EventHandler(this.SearchBTN_Click);
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(625, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(706, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // ServersLV
            // 
            this.ServersLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.ApplicationNameCH,
            this.ApplicationTypeCH,
            this.DNSNamesCH,
            this.ProtocolsCH});
            this.ServersLV.ContextMenuStrip = this.PopupMenu;
            this.ServersLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersLV.FullRowSelect = true;
            this.ServersLV.Location = new System.Drawing.Point(0, 138);
            this.ServersLV.Name = "ServersLV";
            this.ServersLV.Size = new System.Drawing.Size(784, 163);
            this.ServersLV.TabIndex = 7;
            this.ServersLV.UseCompatibleStateImageBehavior = false;
            this.ServersLV.View = System.Windows.Forms.View.Details;
            this.ServersLV.SelectedIndexChanged += new System.EventHandler(this.ServersLV_SelectedIndexChanged);
            // 
            // ApplicationNameCH
            // 
            this.ApplicationNameCH.Text = "Application Name";
            this.ApplicationNameCH.Width = 114;
            // 
            // ApplicationTypeCH
            // 
            this.ApplicationTypeCH.Text = "Application Type";
            this.ApplicationTypeCH.Width = 112;
            // 
            // DNSNamesCH
            // 
            this.DNSNamesCH.Text = "DNS Names";
            this.DNSNamesCH.Width = 95;
            // 
            // ProtocolsCH
            // 
            this.ProtocolsCH.Text = "Protocols";
            this.ProtocolsCH.Width = 147;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DetailsMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(119, 26);
            // 
            // DetailsMI
            // 
            this.DetailsMI.Name = "DetailsMI";
            this.DetailsMI.Size = new System.Drawing.Size(118, 22);
            this.DetailsMI.Text = "Details...";
            // 
            // TopPN
            // 
            this.TopPN.ColumnCount = 4;
            this.TopPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TopPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TopPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TopPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TopPN.Controls.Add(this.ProductUriTB, 2, 4);
            this.TopPN.Controls.Add(this.ProductUriCB, 1, 4);
            this.TopPN.Controls.Add(this.ProductUriLB, 0, 4);
            this.TopPN.Controls.Add(this.SystemElementTB, 1, 0);
            this.TopPN.Controls.Add(this.SystemElementLB, 0, 0);
            this.TopPN.Controls.Add(this.MachineNameCB, 1, 2);
            this.TopPN.Controls.Add(this.ApplicationUriCB, 1, 3);
            this.TopPN.Controls.Add(this.ApplicationUriTB, 2, 3);
            this.TopPN.Controls.Add(this.MachineNameLB, 0, 2);
            this.TopPN.Controls.Add(this.MachineNameTB, 2, 2);
            this.TopPN.Controls.Add(this.ApplicationUriLB, 0, 3);
            this.TopPN.Controls.Add(this.ApplicationNameTB, 2, 1);
            this.TopPN.Controls.Add(this.ApplicationNameLB, 0, 1);
            this.TopPN.Controls.Add(this.ApplicationNameCB, 1, 1);
            this.TopPN.Controls.Add(this.SystemElementBTN, 3, 0);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(0, 0);
            this.TopPN.Name = "TopPN";
            this.TopPN.RowCount = 7;
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TopPN.Size = new System.Drawing.Size(784, 138);
            this.TopPN.TabIndex = 8;
            // 
            // ProductUriTB
            // 
            this.ProductUriTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriTB.Location = new System.Drawing.Point(197, 110);
            this.ProductUriTB.Name = "ProductUriTB";
            this.ProductUriTB.Size = new System.Drawing.Size(388, 20);
            this.ProductUriTB.TabIndex = 15;
            // 
            // ProductUriCB
            // 
            this.ProductUriCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ProductUriCB.FormattingEnabled = true;
            this.ProductUriCB.Location = new System.Drawing.Point(99, 110);
            this.ProductUriCB.Name = "ProductUriCB";
            this.ProductUriCB.Size = new System.Drawing.Size(92, 21);
            this.ProductUriCB.TabIndex = 14;
            // 
            // ProductUriLB
            // 
            this.ProductUriLB.AutoSize = true;
            this.ProductUriLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriLB.Location = new System.Drawing.Point(3, 107);
            this.ProductUriLB.Name = "ProductUriLB";
            this.ProductUriLB.Size = new System.Drawing.Size(90, 27);
            this.ProductUriLB.TabIndex = 13;
            this.ProductUriLB.Text = "Product URI";
            this.ProductUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SystemElementTB
            // 
            this.TopPN.SetColumnSpan(this.SystemElementTB, 2);
            this.SystemElementTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SystemElementTB.Location = new System.Drawing.Point(99, 3);
            this.SystemElementTB.Name = "SystemElementTB";
            this.SystemElementTB.ReadOnly = true;
            this.SystemElementTB.Size = new System.Drawing.Size(486, 20);
            this.SystemElementTB.TabIndex = 12;
            // 
            // SystemElementLB
            // 
            this.SystemElementLB.AutoSize = true;
            this.SystemElementLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SystemElementLB.Location = new System.Drawing.Point(3, 0);
            this.SystemElementLB.Name = "SystemElementLB";
            this.SystemElementLB.Size = new System.Drawing.Size(90, 26);
            this.SystemElementLB.TabIndex = 11;
            this.SystemElementLB.Text = "System Element";
            this.SystemElementLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MachineNameCB
            // 
            this.MachineNameCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MachineNameCB.FormattingEnabled = true;
            this.MachineNameCB.Location = new System.Drawing.Point(99, 56);
            this.MachineNameCB.Name = "MachineNameCB";
            this.MachineNameCB.Size = new System.Drawing.Size(92, 21);
            this.MachineNameCB.TabIndex = 10;
            // 
            // ApplicationUriCB
            // 
            this.ApplicationUriCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ApplicationUriCB.FormattingEnabled = true;
            this.ApplicationUriCB.Location = new System.Drawing.Point(99, 83);
            this.ApplicationUriCB.Name = "ApplicationUriCB";
            this.ApplicationUriCB.Size = new System.Drawing.Size(92, 21);
            this.ApplicationUriCB.TabIndex = 9;
            // 
            // ApplicationUriTB
            // 
            this.ApplicationUriTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriTB.Location = new System.Drawing.Point(197, 83);
            this.ApplicationUriTB.Name = "ApplicationUriTB";
            this.ApplicationUriTB.Size = new System.Drawing.Size(388, 20);
            this.ApplicationUriTB.TabIndex = 6;
            // 
            // MachineNameLB
            // 
            this.MachineNameLB.AutoSize = true;
            this.MachineNameLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MachineNameLB.Location = new System.Drawing.Point(3, 53);
            this.MachineNameLB.Name = "MachineNameLB";
            this.MachineNameLB.Size = new System.Drawing.Size(90, 27);
            this.MachineNameLB.TabIndex = 5;
            this.MachineNameLB.Text = "Machine Name";
            this.MachineNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MachineNameTB
            // 
            this.MachineNameTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MachineNameTB.Location = new System.Drawing.Point(197, 56);
            this.MachineNameTB.Name = "MachineNameTB";
            this.MachineNameTB.Size = new System.Drawing.Size(388, 20);
            this.MachineNameTB.TabIndex = 4;
            // 
            // ApplicationUriLB
            // 
            this.ApplicationUriLB.AutoSize = true;
            this.ApplicationUriLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriLB.Location = new System.Drawing.Point(3, 80);
            this.ApplicationUriLB.Name = "ApplicationUriLB";
            this.ApplicationUriLB.Size = new System.Drawing.Size(90, 27);
            this.ApplicationUriLB.TabIndex = 3;
            this.ApplicationUriLB.Text = "Application URI";
            this.ApplicationUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameTB
            // 
            this.ApplicationNameTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameTB.Location = new System.Drawing.Point(197, 29);
            this.ApplicationNameTB.Name = "ApplicationNameTB";
            this.ApplicationNameTB.Size = new System.Drawing.Size(388, 20);
            this.ApplicationNameTB.TabIndex = 2;
            // 
            // ApplicationNameLB
            // 
            this.ApplicationNameLB.AutoSize = true;
            this.ApplicationNameLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameLB.Location = new System.Drawing.Point(3, 26);
            this.ApplicationNameLB.Name = "ApplicationNameLB";
            this.ApplicationNameLB.Size = new System.Drawing.Size(90, 27);
            this.ApplicationNameLB.TabIndex = 1;
            this.ApplicationNameLB.Text = "Application Name";
            this.ApplicationNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameCB
            // 
            this.ApplicationNameCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ApplicationNameCB.FormattingEnabled = true;
            this.ApplicationNameCB.Location = new System.Drawing.Point(99, 29);
            this.ApplicationNameCB.Name = "ApplicationNameCB";
            this.ApplicationNameCB.Size = new System.Drawing.Size(92, 21);
            this.ApplicationNameCB.TabIndex = 8;
            // 
            // SystemElementBTN
            // 
            this.SystemElementBTN.Location = new System.Drawing.Point(589, 1);
            this.SystemElementBTN.Margin = new System.Windows.Forms.Padding(1);
            this.SystemElementBTN.Name = "SystemElementBTN";
            this.SystemElementBTN.NodeControl = this.SystemElementTB;
            this.SystemElementBTN.ReferenceTypeIds = null;
            this.SystemElementBTN.RootId = null;
            this.SystemElementBTN.SelectedNode = null;
            this.SystemElementBTN.SelectedReference = null;
            this.SystemElementBTN.Session = null;
            this.SystemElementBTN.Size = new System.Drawing.Size(24, 24);
            this.SystemElementBTN.TabIndex = 16;
            this.SystemElementBTN.View = null;
            // 
            // SearchPN
            // 
            this.SearchPN.Controls.Add(this.ServersLV);
            this.SearchPN.Controls.Add(this.TopPN);
            this.SearchPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SearchPN.Location = new System.Drawing.Point(0, 31);
            this.SearchPN.Name = "SearchPN";
            this.SearchPN.Size = new System.Drawing.Size(784, 301);
            this.SearchPN.TabIndex = 11;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesListCollapsed = false;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 31);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.Size = new System.Drawing.Size(784, 301);
            this.BrowseCTRL.SplitterDistance = 387;
            this.BrowseCTRL.TabIndex = 9;
            this.BrowseCTRL.View = null;
            this.BrowseCTRL.AfterSelect += new System.EventHandler(this.BrowseCTRL_AfterSelect);
            // 
            // ServerCTRL
            // 
            this.ServerCTRL.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.ServerCTRL.Configuration = null;
            this.ServerCTRL.DisableDomainCheck = false;
            this.ServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerCTRL.Location = new System.Drawing.Point(0, 0);
            this.ServerCTRL.MaximumSize = new System.Drawing.Size(2048, 31);
            this.ServerCTRL.MinimumSize = new System.Drawing.Size(500, 31);
            this.ServerCTRL.Name = "ServerCTRL";
            this.ServerCTRL.Padding = new System.Windows.Forms.Padding(3);
            this.ServerCTRL.PreferredLocales = null;
            this.ServerCTRL.ServerStatusControl = null;
            this.ServerCTRL.ServerUrl = "";
            this.ServerCTRL.SessionName = null;
            this.ServerCTRL.Size = new System.Drawing.Size(784, 31);
            this.ServerCTRL.StatusStrip = null;
            this.ServerCTRL.StatusUpateTimeControl = null;
            this.ServerCTRL.TabIndex = 10;
            this.ServerCTRL.UserIdentity = null;
            this.ServerCTRL.UseSecurity = true;
            this.ServerCTRL.ReconnectComplete += new System.EventHandler(this.ServerCTRL_ReconnectComplete);
            this.ServerCTRL.ConnectComplete += new System.EventHandler(this.ServerCTRL_ConnectComplete);
            // 
            // GdsDiscoverServersDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 362);
            this.Controls.Add(this.SearchPN);
            this.Controls.Add(this.BrowseCTRL);
            this.Controls.Add(this.ServerCTRL);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "GdsDiscoverServersDlg";
            this.Text = "Global Directory Service";
            this.BottomPN.ResumeLayout(false);
            this.BottomPN.PerformLayout();
            this.PopupMenu.ResumeLayout(false);
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            this.SearchPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button SearchBTN;
        private System.Windows.Forms.ListView ServersLV;
        private System.Windows.Forms.ColumnHeader ApplicationNameCH;
        private System.Windows.Forms.ColumnHeader DNSNamesCH;
        private System.Windows.Forms.ColumnHeader ProtocolsCH;
        private System.Windows.Forms.TableLayoutPanel TopPN;
        private System.Windows.Forms.TextBox ProductUriTB;
        private System.Windows.Forms.ComboBox ProductUriCB;
        private System.Windows.Forms.Label ProductUriLB;
        private System.Windows.Forms.TextBox SystemElementTB;
        private System.Windows.Forms.Label SystemElementLB;
        private System.Windows.Forms.ComboBox MachineNameCB;
        private System.Windows.Forms.ComboBox ApplicationUriCB;
        private System.Windows.Forms.TextBox ApplicationUriTB;
        private System.Windows.Forms.Label MachineNameLB;
        private System.Windows.Forms.TextBox MachineNameTB;
        private System.Windows.Forms.Label ApplicationUriLB;
        private System.Windows.Forms.TextBox ApplicationNameTB;
        private System.Windows.Forms.Label ApplicationNameLB;
        private System.Windows.Forms.ComboBox ApplicationNameCB;
        private Opc.Ua.Client.Controls.SelectNodeCtrl SystemElementBTN;
        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem DetailsMI;
        private BrowseNodeCtrl BrowseCTRL;
        private ConnectServerCtrl ServerCTRL;
        private System.Windows.Forms.Panel SearchPN;
        private System.Windows.Forms.CheckBox BrowseCK;
        private System.Windows.Forms.ColumnHeader ApplicationTypeCH;
    }
}
