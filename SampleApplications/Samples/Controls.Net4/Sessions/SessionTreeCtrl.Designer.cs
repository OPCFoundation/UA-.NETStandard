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
    partial class SessionTreeCtrl
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
            this.NewSessionMI = new System.Windows.Forms.ToolStripMenuItem();
            this.NewWindowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SessionSaveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SessionLoadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator01 = new System.Windows.Forms.ToolStripSeparator();
            this.BrowseMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseAllMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseObjectsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseServerViewsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseObjectTypesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseEventTypesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseVariableTypesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseDataTypesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BrowseReferenceTypesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionCreateMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionMonitorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionEnabledPublishingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ReadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WriteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetLocaleMI = new System.Windows.Forms.ToolStripMenuItem();
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
            this.NewSessionMI,
            this.NewWindowMI,
            this.SetLocaleMI,
            this.SessionSaveMI,
            this.SessionLoadMI,
            this.DeleteMI,
            this.Separator01,
            this.BrowseMI,
            this.SubscriptionMI,
            this.ReadMI,
            this.WriteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(184, 252);
            // 
            // NewSessionMI
            // 
            this.NewSessionMI.Name = "NewSessionMI";
            this.NewSessionMI.Size = new System.Drawing.Size(183, 22);
            this.NewSessionMI.Text = "New Session...";
            this.NewSessionMI.Click += new System.EventHandler(this.NewSessionMI_Click);
            // 
            // NewWindowMI
            // 
            this.NewWindowMI.Name = "NewWindowMI";
            this.NewWindowMI.Size = new System.Drawing.Size(183, 22);
            this.NewWindowMI.Text = "New Window...";
            this.NewWindowMI.Click += new System.EventHandler(this.NewWindowMI_Click);
            // 
            // SessionSaveMI
            // 
            this.SessionSaveMI.Name = "SessionSaveMI";
            this.SessionSaveMI.Size = new System.Drawing.Size(183, 22);
            this.SessionSaveMI.Text = "Save Subscriptions...";
            this.SessionSaveMI.Click += new System.EventHandler(this.SessionSaveMI_Click);
            // 
            // SessionLoadMI
            // 
            this.SessionLoadMI.Name = "SessionLoadMI";
            this.SessionLoadMI.Size = new System.Drawing.Size(183, 22);
            this.SessionLoadMI.Text = "Load Subscriptions...";
            this.SessionLoadMI.Click += new System.EventHandler(this.SessionLoadMI_Click);
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(183, 22);
            this.DeleteMI.Text = "Delete";
            this.DeleteMI.Click += new System.EventHandler(this.DeleteMI_Click);
            // 
            // Separator01
            // 
            this.Separator01.Name = "Separator01";
            this.Separator01.Size = new System.Drawing.Size(180, 6);
            // 
            // BrowseMI
            // 
            this.BrowseMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.BrowseAllMI,
            this.BrowseObjectsMI,
            this.BrowseServerViewsMI,
            this.BrowseObjectTypesMI,
            this.BrowseEventTypesMI,
            this.BrowseVariableTypesMI,
            this.BrowseDataTypesMI,
            this.BrowseReferenceTypesMI});
            this.BrowseMI.Name = "BrowseMI";
            this.BrowseMI.Size = new System.Drawing.Size(183, 22);
            this.BrowseMI.Text = "Browse";
            // 
            // BrowseAllMI
            // 
            this.BrowseAllMI.Name = "BrowseAllMI";
            this.BrowseAllMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseAllMI.Text = "All";
            this.BrowseAllMI.Click += new System.EventHandler(this.BrowseAllMI_Click);
            // 
            // BrowseObjectsMI
            // 
            this.BrowseObjectsMI.Name = "BrowseObjectsMI";
            this.BrowseObjectsMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseObjectsMI.Text = "Objects";
            this.BrowseObjectsMI.Click += new System.EventHandler(this.BrowseObjectsMI_Click);
            // 
            // BrowseServerViewsMI
            // 
            this.BrowseServerViewsMI.Name = "BrowseServerViewsMI";
            this.BrowseServerViewsMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseServerViewsMI.Text = "Server Defined View";
            this.BrowseServerViewsMI.DropDownOpening += new System.EventHandler(this.BrowseServerViewsMI_DropDownOpening);
            // 
            // BrowseObjectTypesMI
            // 
            this.BrowseObjectTypesMI.Name = "BrowseObjectTypesMI";
            this.BrowseObjectTypesMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseObjectTypesMI.Text = "Object Types...";
            this.BrowseObjectTypesMI.Click += new System.EventHandler(this.BrowseObjectTypesMI_Click);
            // 
            // BrowseEventTypesMI
            // 
            this.BrowseEventTypesMI.Name = "BrowseEventTypesMI";
            this.BrowseEventTypesMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseEventTypesMI.Text = "Event Types...";
            this.BrowseEventTypesMI.Click += new System.EventHandler(this.BrowseEventTypesMI_Click);
            // 
            // BrowseVariableTypesMI
            // 
            this.BrowseVariableTypesMI.Name = "BrowseVariableTypesMI";
            this.BrowseVariableTypesMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseVariableTypesMI.Text = "Variable Types...";
            this.BrowseVariableTypesMI.Click += new System.EventHandler(this.BrowseVariableTypesMI_Click);
            // 
            // BrowseDataTypesMI
            // 
            this.BrowseDataTypesMI.Name = "BrowseDataTypesMI";
            this.BrowseDataTypesMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseDataTypesMI.Text = "DataTypes...";
            this.BrowseDataTypesMI.Click += new System.EventHandler(this.BrowseDataTypesMI_Click);
            // 
            // BrowseReferenceTypesMI
            // 
            this.BrowseReferenceTypesMI.Name = "BrowseReferenceTypesMI";
            this.BrowseReferenceTypesMI.Size = new System.Drawing.Size(178, 22);
            this.BrowseReferenceTypesMI.Text = "ReferenceTypes...";
            this.BrowseReferenceTypesMI.Click += new System.EventHandler(this.BrowseReferenceTypesMI_Click);
            // 
            // SubscriptionMI
            // 
            this.SubscriptionMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SubscriptionCreateMI,
            this.SubscriptionMonitorMI,
            this.SubscriptionEnabledPublishingMI});
            this.SubscriptionMI.Name = "SubscriptionMI";
            this.SubscriptionMI.Size = new System.Drawing.Size(183, 22);
            this.SubscriptionMI.Text = "Subscription";
            // 
            // SubscriptionCreateMI
            // 
            this.SubscriptionCreateMI.Name = "SubscriptionCreateMI";
            this.SubscriptionCreateMI.Size = new System.Drawing.Size(168, 22);
            this.SubscriptionCreateMI.Text = "New...";
            this.SubscriptionCreateMI.Click += new System.EventHandler(this.SubscriptionCreateMI_Click);
            // 
            // SubscriptionMonitorMI
            // 
            this.SubscriptionMonitorMI.Name = "SubscriptionMonitorMI";
            this.SubscriptionMonitorMI.Size = new System.Drawing.Size(168, 22);
            this.SubscriptionMonitorMI.Text = "Monitor...";
            this.SubscriptionMonitorMI.Click += new System.EventHandler(this.SubscriptionMonitorMI_Click);
            // 
            // SubscriptionEnabledPublishingMI
            // 
            this.SubscriptionEnabledPublishingMI.CheckOnClick = true;
            this.SubscriptionEnabledPublishingMI.Name = "SubscriptionEnabledPublishingMI";
            this.SubscriptionEnabledPublishingMI.Size = new System.Drawing.Size(168, 22);
            this.SubscriptionEnabledPublishingMI.Text = "Enable Publishing";
            this.SubscriptionEnabledPublishingMI.Click += new System.EventHandler(this.SubscriptionEnabledPublishingMI_Click);
            // 
            // ReadMI
            // 
            this.ReadMI.Name = "ReadMI";
            this.ReadMI.Size = new System.Drawing.Size(183, 22);
            this.ReadMI.Text = "Read...";
            this.ReadMI.Click += new System.EventHandler(this.ReadMI_Click);
            // 
            // WriteMI
            // 
            this.WriteMI.Name = "WriteMI";
            this.WriteMI.Size = new System.Drawing.Size(183, 22);
            this.WriteMI.Text = "Write...";
            this.WriteMI.Click += new System.EventHandler(this.WriteMI_Click);
            // 
            // SetLocaleMI
            // 
            this.SetLocaleMI.Name = "SetLocaleMI";
            this.SetLocaleMI.Size = new System.Drawing.Size(183, 22);
            this.SetLocaleMI.Text = "Set Locale...";
            this.SetLocaleMI.Click += new System.EventHandler(this.SetLocaleMI_Click);
            // 
            // SessionTreeCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Name = "SessionTreeCtrl";
            this.Controls.SetChildIndex(this.NodesTV, 0);
            this.PopupMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripSeparator Separator01;
        private System.Windows.Forms.ToolStripMenuItem NewSessionMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseAllMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseObjectsMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseServerViewsMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionCreateMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionEnabledPublishingMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseEventTypesMI;
        private System.Windows.Forms.ToolStripMenuItem ReadMI;
        private System.Windows.Forms.ToolStripMenuItem WriteMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionMonitorMI;
        private System.Windows.Forms.ToolStripMenuItem SessionSaveMI;
        private System.Windows.Forms.ToolStripMenuItem SessionLoadMI;
        private System.Windows.Forms.ToolStripMenuItem NewWindowMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseObjectTypesMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseVariableTypesMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseDataTypesMI;
        private System.Windows.Forms.ToolStripMenuItem BrowseReferenceTypesMI;
        private System.Windows.Forms.ToolStripMenuItem SetLocaleMI;
    }
}
