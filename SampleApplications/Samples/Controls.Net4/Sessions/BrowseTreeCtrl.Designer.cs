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
    partial class BrowseTreeCtrl
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
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.BrowseOptionsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ShowReferencesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator01 = new System.Windows.Forms.ToolStripSeparator();
            this.BrowseMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewAttributesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ReadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HistoryReadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WriteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HistoryUpdateMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EncodingsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscribeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscribeNewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.CallMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator02 = new System.Windows.Forms.ToolStripSeparator();
            this.SelectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SelectItemMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SelectChildrenMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SelectSeparatorMI = new System.Windows.Forms.ToolStripSeparator();
            this.BrowseRefreshMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PopupMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // NodesTV
            // 
            this.NodesTV.ContextMenuStrip = this.PopupMenu;
            this.NodesTV.LineColor = System.Drawing.Color.Black;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.BrowseOptionsMI,
            this.ShowReferencesMI,
            this.Separator01,
            this.BrowseMI,
            this.ViewAttributesMI,
            this.ReadMI,
            this.HistoryReadMI,
            this.WriteMI,
            this.HistoryUpdateMI,
            this.EncodingsMI,
            this.SubscribeMI,
            this.CallMI,
            this.Separator02,
            this.SelectMI,
            this.SelectSeparatorMI,
            this.BrowseRefreshMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(162, 330);
            // 
            // BrowseOptionsMI
            // 
            this.BrowseOptionsMI.Name = "BrowseOptionsMI";
            this.BrowseOptionsMI.Size = new System.Drawing.Size(161, 22);
            this.BrowseOptionsMI.Text = "Browse Options...";
            this.BrowseOptionsMI.Click += new System.EventHandler(this.BrowseOptionsMI_Click);
            // 
            // ShowReferencesMI
            // 
            this.ShowReferencesMI.Checked = true;
            this.ShowReferencesMI.CheckOnClick = true;
            this.ShowReferencesMI.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ShowReferencesMI.Name = "ShowReferencesMI";
            this.ShowReferencesMI.Size = new System.Drawing.Size(161, 22);
            this.ShowReferencesMI.Text = "Show References";
            this.ShowReferencesMI.CheckedChanged += new System.EventHandler(this.ShowReferencesMI_CheckedChanged);
            // 
            // Separator01
            // 
            this.Separator01.Name = "Separator01";
            this.Separator01.Size = new System.Drawing.Size(158, 6);
            // 
            // BrowseMI
            // 
            this.BrowseMI.Name = "BrowseMI";
            this.BrowseMI.Size = new System.Drawing.Size(161, 22);
            this.BrowseMI.Text = "Browse...";
            this.BrowseMI.Click += new System.EventHandler(this.BrowseMI_Click);
            // 
            // ViewAttributesMI
            // 
            this.ViewAttributesMI.Name = "ViewAttributesMI";
            this.ViewAttributesMI.Size = new System.Drawing.Size(161, 22);
            this.ViewAttributesMI.Text = "View Attributes...";
            this.ViewAttributesMI.Click += new System.EventHandler(this.ViewAttributesMI_Click);
            // 
            // ReadMI
            // 
            this.ReadMI.Name = "ReadMI";
            this.ReadMI.Size = new System.Drawing.Size(161, 22);
            this.ReadMI.Text = "Read..";
            this.ReadMI.Click += new System.EventHandler(this.ReadMI_Click);
            // 
            // HistoryReadMI
            // 
            this.HistoryReadMI.Name = "HistoryReadMI";
            this.HistoryReadMI.Size = new System.Drawing.Size(161, 22);
            this.HistoryReadMI.Text = "History Read...";
            this.HistoryReadMI.Click += new System.EventHandler(this.HistoryReadMI_Click);
            // 
            // WriteMI
            // 
            this.WriteMI.Name = "WriteMI";
            this.WriteMI.Size = new System.Drawing.Size(161, 22);
            this.WriteMI.Text = "Write...";
            this.WriteMI.Click += new System.EventHandler(this.WriteMI_Click);
            // 
            // HistoryUpdateMI
            // 
            this.HistoryUpdateMI.Name = "HistoryUpdateMI";
            this.HistoryUpdateMI.Size = new System.Drawing.Size(161, 22);
            this.HistoryUpdateMI.Text = "History Update...";
            // 
            // EncodingsMI
            // 
            this.EncodingsMI.Name = "EncodingsMI";
            this.EncodingsMI.Size = new System.Drawing.Size(161, 22);
            this.EncodingsMI.Text = "View Encodings...";
            this.EncodingsMI.Click += new System.EventHandler(this.EncodingsMI_Click);
            // 
            // SubscribeMI
            // 
            this.SubscribeMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SubscribeNewMI});
            this.SubscribeMI.Name = "SubscribeMI";
            this.SubscribeMI.Size = new System.Drawing.Size(161, 22);
            this.SubscribeMI.Text = "Subscribe";
            // 
            // SubscribeNewMI
            // 
            this.SubscribeNewMI.Name = "SubscribeNewMI";
            this.SubscribeNewMI.Size = new System.Drawing.Size(152, 22);
            this.SubscribeNewMI.Text = "New...";
            this.SubscribeNewMI.Click += new System.EventHandler(this.SubscribeNewMI_Click);
            // 
            // CallMI
            // 
            this.CallMI.Name = "CallMI";
            this.CallMI.Size = new System.Drawing.Size(161, 22);
            this.CallMI.Text = "Call...";
            this.CallMI.Click += new System.EventHandler(this.CallMI_Click);
            // 
            // Separator02
            // 
            this.Separator02.Name = "Separator02";
            this.Separator02.Size = new System.Drawing.Size(158, 6);
            // 
            // SelectMI
            // 
            this.SelectMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SelectItemMI,
            this.SelectChildrenMI});
            this.SelectMI.Name = "SelectMI";
            this.SelectMI.Size = new System.Drawing.Size(161, 22);
            this.SelectMI.Text = "Select";
            // 
            // SelectItemMI
            // 
            this.SelectItemMI.Name = "SelectItemMI";
            this.SelectItemMI.Size = new System.Drawing.Size(145, 22);
            this.SelectItemMI.Text = "Select Item";
            this.SelectItemMI.Click += new System.EventHandler(this.SelectItemMI_Click);
            // 
            // SelectChildrenMI
            // 
            this.SelectChildrenMI.Name = "SelectChildrenMI";
            this.SelectChildrenMI.Size = new System.Drawing.Size(145, 22);
            this.SelectChildrenMI.Text = "Select Children";
            this.SelectChildrenMI.Click += new System.EventHandler(this.SelectChildrenMI_Click);
            // 
            // SelectSeparatorMI
            // 
            this.SelectSeparatorMI.Name = "SelectSeparatorMI";
            this.SelectSeparatorMI.Size = new System.Drawing.Size(158, 6);
            // 
            // BrowseRefreshMI
            // 
            this.BrowseRefreshMI.Name = "BrowseRefreshMI";
            this.BrowseRefreshMI.Size = new System.Drawing.Size(161, 22);
            this.BrowseRefreshMI.Text = "Refresh";
            this.BrowseRefreshMI.Click += new System.EventHandler(this.BrowseRefreshMI_Click);
            // 
            // BrowseTreeCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Name = "BrowseTreeCtrl";
            this.Controls.SetChildIndex(this.NodesTV, 0);
            this.PopupMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem BrowseOptionsMI;
        private System.Windows.Forms.ToolStripSeparator Separator02;
        private System.Windows.Forms.ToolStripMenuItem BrowseRefreshMI;
        private System.Windows.Forms.ToolStripMenuItem SelectMI;
        private System.Windows.Forms.ToolStripMenuItem ShowReferencesMI;
        private System.Windows.Forms.ToolStripMenuItem ViewAttributesMI;
        private System.Windows.Forms.ToolStripSeparator Separator01;
        private System.Windows.Forms.ToolStripSeparator SelectSeparatorMI;
        private System.Windows.Forms.ToolStripMenuItem CallMI;
        private System.Windows.Forms.ToolStripMenuItem SubscribeMI;
        private System.Windows.Forms.ToolStripMenuItem SelectItemMI;
        private System.Windows.Forms.ToolStripMenuItem SelectChildrenMI;
        private System.Windows.Forms.ToolStripMenuItem WriteMI;
        private System.Windows.Forms.ToolStripMenuItem ReadMI;
        private System.Windows.Forms.ToolStripMenuItem SubscribeNewMI;
        private System.Windows.Forms.ToolStripMenuItem EncodingsMI;
        private System.Windows.Forms.ToolStripMenuItem HistoryReadMI;
        private System.Windows.Forms.ToolStripMenuItem HistoryUpdateMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseMI;
    }
}
