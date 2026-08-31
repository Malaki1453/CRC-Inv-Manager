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
            lblBusinessName = new Label();
            txtBusinessName = new TextBox();
            lblAddress = new Label();
            txtAddress = new TextBox();
            lblPhone = new Label();
            txtPhone = new TextBox();
            lblEmail = new Label();
            txtEmail = new TextBox();
            lblEIN = new Label();
            txtEIN = new TextBox();
            lblPaymentTerms = new Label();
            txtPaymentTerms = new TextBox();
            lblFolder = new Label();
            txtFolderPath = new TextBox();
            btnChangeFolder = new Button();
            btnRollToNextTerm = new Button();
            SuspendLayout();

            lblTitle.Text = "Settings";
            lblBusinessName.Text = "BUSINESS NAME";
            txtBusinessName.Name = "txtBusinessName";
            lblAddress.Text = "ADDRESS";
            txtAddress.Name = "txtAddress";
            lblPhone.Text = "PHONE";
            txtPhone.Name = "txtPhone";
            lblEmail.Text = "EMAIL";
            txtEmail.Name = "txtEmail";
            lblEIN.Text = "EIN";
            txtEIN.Name = "txtEIN";
            lblPaymentTerms.Text = "PAYMENT TERMS";
            txtPaymentTerms.Name = "txtPaymentTerms";
            lblFolder.Text = "DATA FOLDER PATH";
            txtFolderPath.Name = "txtFolderPath";
            txtFolderPath.ReadOnly = true;
            btnChangeFolder.Name = "btnChangeFolder";
            btnChangeFolder.Text = "Change Folder…";
            btnChangeFolder.Click += btnChangeFolder_Click;
            btnRollToNextTerm.Name = "btnRollToNextTerm";
            btnRollToNextTerm.Text = "Roll to Next Term";
            btnRollToNextTerm.Click += btnRollToNextTerm_Click;

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 640);
            Name = "Settings";
            Text = "Settings";
            ResumeLayout(false);
        }

        #endregion

        private Label lblTitle;
        private Label lblBusinessName;
        private TextBox txtBusinessName;
        private Label lblAddress;
        private TextBox txtAddress;
        private Label lblPhone;
        private TextBox txtPhone;
        private Label lblEmail;
        private TextBox txtEmail;
        private Label lblEIN;
        private TextBox txtEIN;
        private Label lblPaymentTerms;
        private TextBox txtPaymentTerms;
        private Label lblFolder;
        private TextBox txtFolderPath;
        private Button btnChangeFolder;
        private Button btnRollToNextTerm;
    }
}
