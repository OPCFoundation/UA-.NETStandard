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
    partial class ContentFilterElementListCtrl
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
            this.SetOperatorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SelectNodeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.CreateElementMI = new System.Windows.Forms.ToolStripMenuItem();
            this.CreateElementAndMI = new System.Windows.Forms.ToolStripMenuItem();
            this.CreateElementOrMI = new System.Windows.Forms.ToolStripMenuItem();
            this.CreateElementNotMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EditValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PopupMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // ItemsLV
            // 
            this.ItemsLV.ContextMenuStrip = this.PopupMenu;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SetOperatorMI,
            this.SelectNodeMI,
            this.EditValueMI,
            this.CreateElementMI,
            this.DeleteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(162, 136);
            // 
            // SetOperatorMI
            // 
            this.SetOperatorMI.Name = "SetOperatorMI";
            this.SetOperatorMI.Size = new System.Drawing.Size(161, 22);
            this.SetOperatorMI.Text = "Set Operator...";
            this.SetOperatorMI.Click += new System.EventHandler(this.SetOperatorMI_Click);
            // 
            // SelectEventTypeMI
            // 
            this.SelectNodeMI.Name = "SelectEventTypeMI";
            this.SelectNodeMI.Size = new System.Drawing.Size(161, 22);
            this.SelectNodeMI.Text = "Select Node...";
            this.SelectNodeMI.Click += new System.EventHandler(this.SelectNodeMI_Click);
            // 
            // CreateElementMI
            // 
            this.CreateElementMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CreateElementAndMI,
            this.CreateElementOrMI,
            this.CreateElementNotMI});
            this.CreateElementMI.Name = "CreateElementMI";
            this.CreateElementMI.Size = new System.Drawing.Size(161, 22);
            this.CreateElementMI.Text = "Combine Elements";
            // 
            // CreateElementAndMI
            // 
            this.CreateElementAndMI.Name = "CreateElementAndMI";
            this.CreateElementAndMI.Size = new System.Drawing.Size(93, 22);
            this.CreateElementAndMI.Text = "And";
            this.CreateElementAndMI.Click += new System.EventHandler(this.CreateElementAndMI_Click);
            // 
            // CreateElementOrMI
            // 
            this.CreateElementOrMI.Name = "CreateElementOrMI";
            this.CreateElementOrMI.Size = new System.Drawing.Size(93, 22);
            this.CreateElementOrMI.Text = "Or";
            this.CreateElementOrMI.Click += new System.EventHandler(this.CreateElementOrMI_Click);
            // 
            // CreateElementNotMI
            // 
            this.CreateElementNotMI.Name = "CreateElementNotMI";
            this.CreateElementNotMI.Size = new System.Drawing.Size(93, 22);
            this.CreateElementNotMI.Text = "Not";
            this.CreateElementNotMI.Click += new System.EventHandler(this.CreateElementNotMI_Click);
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(161, 22);
            this.DeleteMI.Text = "Delete";
            this.DeleteMI.Click += new System.EventHandler(this.DeleteMI_Click);
            // 
            // EditValueMI
            // 
            this.EditValueMI.Name = "EditValueMI";
            this.EditValueMI.Size = new System.Drawing.Size(161, 22);
            this.EditValueMI.Text = "Edit Value";
            this.EditValueMI.Click += new System.EventHandler(this.EditValueMI_Click);
            // 
            // ContentFilterElementListCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Name = "ContentFilterElementListCtrl";
            this.Controls.SetChildIndex(this.ItemsLV, 0);
            this.PopupMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.ToolStripMenuItem SetOperatorMI;
        private System.Windows.Forms.ToolStripMenuItem SelectNodeMI;
        private System.Windows.Forms.ToolStripMenuItem CreateElementMI;
        private System.Windows.Forms.ToolStripMenuItem CreateElementAndMI;
        private System.Windows.Forms.ToolStripMenuItem CreateElementOrMI;
        private System.Windows.Forms.ToolStripMenuItem CreateElementNotMI;
        private System.Windows.Forms.ToolStripMenuItem EditValueMI;
    }
}
