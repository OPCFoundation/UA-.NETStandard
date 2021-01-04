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
    partial class BrowseNodeCtrl
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
            this.BrowseCTRL = new Opc.Ua.Client.Controls.BrowseTreeViewCtrl();
            this.AttributesCTRL = new Opc.Ua.Client.Controls.AttributesListViewCtrl();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.AttributesCTRL);
            this.MainPN.Size = new System.Drawing.Size(1003, 569);
            this.MainPN.SplitterDistance = 387;
            this.MainPN.TabIndex = 11;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesControl = this.AttributesCTRL;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.Size = new System.Drawing.Size(387, 569);
            this.BrowseCTRL.TabIndex = 0;
            this.BrowseCTRL.View = null;
            // 
            // AttributesCTRL
            // 
            this.AttributesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesCTRL.Location = new System.Drawing.Point(0, 0);
            this.AttributesCTRL.Name = "AttributesCTRL";
            this.AttributesCTRL.Size = new System.Drawing.Size(612, 569);
            this.AttributesCTRL.TabIndex = 0;
            this.AttributesCTRL.View = null;
            // 
            // BrowseNodeCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.MainPN);
            this.Name = "BrowseNodeCtrl";
            this.Size = new System.Drawing.Size(1003, 569);
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer MainPN;

        /// <summary>
        /// The browse list control.
        /// </summary>
        public BrowseTreeViewCtrl BrowseCTRL;

        /// <summary>
        /// The attribute list control.
        /// </summary>
        public AttributesListViewCtrl AttributesCTRL;
    }
}
