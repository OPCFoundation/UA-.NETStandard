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

namespace Opc.Ua.Client.Controls
{
    partial class GuiUtils
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GuiUtils));
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.SuspendLayout();
            // 
            // ImageList
            // 
            this.ImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("ImageList.ImageStream")));
            this.ImageList.TransparentColor = System.Drawing.Color.Transparent;
            this.ImageList.Images.SetKeyName(0, "SimpleItem");
            this.ImageList.Images.SetKeyName(1, "Object");
            this.ImageList.Images.SetKeyName(2, "FolderOld");
            this.ImageList.Images.SetKeyName(3, "Area");
            this.ImageList.Images.SetKeyName(4, "Variable");
            this.ImageList.Images.SetKeyName(5, "Property");
            this.ImageList.Images.SetKeyName(6, "Method");
            this.ImageList.Images.SetKeyName(7, "ReferenceType");
            this.ImageList.Images.SetKeyName(8, "DataType");
            this.ImageList.Images.SetKeyName(9, "View");
            this.ImageList.Images.SetKeyName(10, "ExpandPlus");
            this.ImageList.Images.SetKeyName(11, "ExpandMinus");
            this.ImageList.Images.SetKeyName(12, "VariableType");
            this.ImageList.Images.SetKeyName(13, "ObjectType");
            this.ImageList.Images.SetKeyName(14, "Info");
            this.ImageList.Images.SetKeyName(15, "Server");
            this.ImageList.Images.SetKeyName(16, "ServerStopped");
            this.ImageList.Images.SetKeyName(17, "Computer");
            this.ImageList.Images.SetKeyName(18, "Network");
            this.ImageList.Images.SetKeyName(19, "Folder");
            this.ImageList.Images.SetKeyName(20, "SelectedFolder");
            this.ImageList.Images.SetKeyName(21, "Process");
            this.ImageList.Images.SetKeyName(22, "Certificate");
            this.ImageList.Images.SetKeyName(23, "CertificateStore");
            this.ImageList.Images.SetKeyName(24, "Users");
            this.ImageList.Images.SetKeyName(25, "Service");
            this.ImageList.Images.SetKeyName(26, "InvalidCertificate");
            this.ImageList.Images.SetKeyName(27, "Drive");
            this.ImageList.Images.SetKeyName(28, "ServiceGroup");
            this.ImageList.Images.SetKeyName(29, "Desktop");
            this.ImageList.Images.SetKeyName(30, "SingleUser");
            this.ImageList.Images.SetKeyName(31, "UserGroup");
            this.ImageList.Images.SetKeyName(32, "RedCross");
            this.ImageList.Images.SetKeyName(33, "GreenCheck");
            this.ImageList.Images.SetKeyName(34, "UsersRedCross");
            // 
            // GuiUtils
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Name = "GuiUtils";
            this.ResumeLayout(false);

        }

        #endregion
    }
}
