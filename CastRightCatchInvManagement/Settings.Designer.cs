namespace CastRightCatchInvManagement
{
    partial class Settings
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblTitle = new Label();
            lblFolder = new Label();
            txtFolderPath = new TextBox();
            btnChangeFolder = new Button();
            SuspendLayout();

            lblTitle.Text = "Settings";
            lblFolder.Text = "DATA FOLDER PATH";
            txtFolderPath.Name = "txtFolderPath";
            txtFolderPath.ReadOnly = true;
            btnChangeFolder.Name = "btnChangeFolder";
            btnChangeFolder.Text = "Change Folder…";
            btnChangeFolder.Click += btnChangeFolder_Click;

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 640);
            Name = "Settings";
            Text = "Settings";
            ResumeLayout(false);
        }

        #endregion

        private Label lblTitle;
        private Label lblFolder;
        private TextBox txtFolderPath;
        private Button btnChangeFolder;
    }
}
