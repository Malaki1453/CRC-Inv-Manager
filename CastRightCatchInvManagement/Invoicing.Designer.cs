namespace CastRightCatchInvManagement
{
    partial class Invoicing
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
            btnUpload = new Button();
            dataGridView1 = new DataGridView();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            SuspendLayout();
            lblTitle.Name = "lblTitle";
            lblTitle.Text = "Invoicing";
            btnUpload.Name = "btnUpload";
            btnUpload.Text = "Upload";
            btnUpload.Click += btnUpload_Click;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.Name = "dataGridView1";
            dataGridView1.ReadOnly = true;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 640);
            Controls.Add(dataGridView1);
            Controls.Add(btnUpload);
            Controls.Add(lblTitle);
            Name = "Invoicing";
            Text = "Invoicing";
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Label lblTitle;
        private Button btnUpload;
        private DataGridView dataGridView1;
    }
}
