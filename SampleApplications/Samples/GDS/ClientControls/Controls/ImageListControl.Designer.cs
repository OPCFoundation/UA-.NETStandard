namespace Opc.Ua.Gds.Client.Controls
{
    partial class ImageListControl
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ImageListControl));
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.SuspendLayout();
            // 
            // ImageList
            // 
            this.ImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("ImageList.ImageStream")));
            this.ImageList.TransparentColor = System.Drawing.Color.Transparent;
            this.ImageList.Images.SetKeyName(0, "information.png");
            this.ImageList.Images.SetKeyName(1, "sign_warning.png");
            this.ImageList.Images.SetKeyName(2, "error.png");
            this.ImageList.Images.SetKeyName(3, "nav_right_green.png");
            this.ImageList.Images.SetKeyName(4, "nav_left_green.png");
            this.ImageList.Images.SetKeyName(5, "application_server.png");
            this.ImageList.Images.SetKeyName(6, "node.png");
            this.ImageList.Images.SetKeyName(7, "earth_network.png");
            this.ImageList.Images.SetKeyName(8, "package.png");
            this.ImageList.Images.SetKeyName(9, "lock_ok.png");
            this.ImageList.Images.SetKeyName(10, "lock_delete.png");
            this.ImageList.Images.SetKeyName(11, "registry.png");
            this.ImageList.Images.SetKeyName(12, "tag.png");
            this.ImageList.Images.SetKeyName(13, "price_sticker_blue.png");
            this.ImageList.Images.SetKeyName(14, "gear_run_small.png");
            this.ImageList.Images.SetKeyName(15, "symbol_hash.png");
            this.ImageList.Images.SetKeyName(16, "text.png");
            this.ImageList.Images.SetKeyName(17, "text_binary.png");
            this.ImageList.Images.SetKeyName(18, "components.png");
            this.ImageList.Images.SetKeyName(19, "table_row.png");
            this.ImageList.Images.SetKeyName(20, "folder_small.png");
            this.ImageList.Images.SetKeyName(21, "add.png");
            this.ImageList.Images.SetKeyName(22, "workstation.png");
            this.ImageList.Images.SetKeyName(23, "ok.png");
            this.ImageList.Images.SetKeyName(24, "price_sticker_green.png");
            this.ImageList.Images.SetKeyName(25, "price_sticker_yellow.png");
            // 
            // ImageListControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Name = "ImageListControl";
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.ImageList ImageList;



    }
}
