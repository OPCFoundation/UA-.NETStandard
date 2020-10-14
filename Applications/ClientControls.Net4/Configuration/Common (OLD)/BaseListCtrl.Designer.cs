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
    partial class BaseListCtrl
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
            this.ItemsLV = new System.Windows.Forms.ListView();
            this.SuspendLayout();
            // 
            // ItemsLV
            // 
            this.ItemsLV.AllowDrop = true;
            this.ItemsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ItemsLV.FullRowSelect = true;
            this.ItemsLV.Location = new System.Drawing.Point(0, 0);
            this.ItemsLV.Name = "ItemsLV";
            this.ItemsLV.Size = new System.Drawing.Size(541, 412);
            this.ItemsLV.TabIndex = 0;
            this.ItemsLV.UseCompatibleStateImageBehavior = false;
            this.ItemsLV.View = System.Windows.Forms.View.Details;
            this.ItemsLV.DragEnter += new System.Windows.Forms.DragEventHandler(this.ItemsLV_DragEnter);
            this.ItemsLV.DragDrop += new System.Windows.Forms.DragEventHandler(this.ItemsLV_DragDrop);
            this.ItemsLV.DoubleClick += new System.EventHandler(this.ItemsLV_DoubleClick);
            this.ItemsLV.SelectedIndexChanged += new System.EventHandler(this.ItemsLV_SelectedIndexChanged);
            this.ItemsLV.MouseUp += new System.Windows.Forms.MouseEventHandler(this.ItemsLV_MouseUp);
            this.ItemsLV.MouseMove += new System.Windows.Forms.MouseEventHandler(this.ItemsLV_MouseMove);
            this.ItemsLV.MouseDown += new System.Windows.Forms.MouseEventHandler(this.ItemsLV_MouseDown);
            // 
            // BaseListCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ItemsLV);
            this.Name = "BaseListCtrl";
            this.Size = new System.Drawing.Size(541, 412);
            this.ResumeLayout(false);

        }

        #endregion
    }
}
