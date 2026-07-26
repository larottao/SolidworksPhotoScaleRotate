namespace PhotoScaleRotate
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.ToolStrip toolStripMain;
        private System.Windows.Forms.ToolStripButton buttonOpen;
        private System.Windows.Forms.ToolStripButton buttonSaveAs;
        private System.Windows.Forms.ToolStripLabel labelMode;
        private System.Windows.Forms.ToolStripComboBox comboMode;
        private System.Windows.Forms.ToolStripLabel labelXmm;
        private System.Windows.Forms.ToolStripTextBox textXmm;
        private System.Windows.Forms.ToolStripLabel labelYmm;
        private System.Windows.Forms.ToolStripTextBox textYmm;
        private System.Windows.Forms.ToolStripButton buttonProcess;
        private System.Windows.Forms.ToolStripButton buttonShowResult;
        private System.Windows.Forms.ToolStripButton buttonClearMarks;
        private System.Windows.Forms.ToolStripButton buttonFit;

        private System.Windows.Forms.StatusStrip statusStripMain;
        private System.Windows.Forms.ToolStripStatusLabel statusMessage;
        private System.Windows.Forms.ToolStripStatusLabel statusCursor;
        private System.Windows.Forms.ToolStripStatusLabel statusZoom;

        private ImageCanvas canvas;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.toolStripMain = new System.Windows.Forms.ToolStrip();
            this.buttonOpen = new System.Windows.Forms.ToolStripButton();
            this.buttonSaveAs = new System.Windows.Forms.ToolStripButton();
            this.labelMode = new System.Windows.Forms.ToolStripLabel();
            this.comboMode = new System.Windows.Forms.ToolStripComboBox();
            this.labelXmm = new System.Windows.Forms.ToolStripLabel();
            this.textXmm = new System.Windows.Forms.ToolStripTextBox();
            this.labelYmm = new System.Windows.Forms.ToolStripLabel();
            this.textYmm = new System.Windows.Forms.ToolStripTextBox();
            this.buttonProcess = new System.Windows.Forms.ToolStripButton();
            this.buttonShowResult = new System.Windows.Forms.ToolStripButton();
            this.buttonClearMarks = new System.Windows.Forms.ToolStripButton();
            this.buttonFit = new System.Windows.Forms.ToolStripButton();

            this.statusStripMain = new System.Windows.Forms.StatusStrip();
            this.statusMessage = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusCursor = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusZoom = new System.Windows.Forms.ToolStripStatusLabel();

            this.canvas = new ImageCanvas();

            // toolStripMain
            this.toolStripMain.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStripMain.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolStripMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.buttonOpen,
                this.buttonSaveAs,
                new System.Windows.Forms.ToolStripSeparator(),
                this.labelMode,
                this.comboMode,
                new System.Windows.Forms.ToolStripSeparator(),
                this.labelXmm,
                this.textXmm,
                this.labelYmm,
                this.textYmm,
                new System.Windows.Forms.ToolStripSeparator(),
                this.buttonProcess,
                this.buttonShowResult,
                new System.Windows.Forms.ToolStripSeparator(),
                this.buttonClearMarks,
                this.buttonFit
            });

            this.buttonOpen.Text = "Open...";
            this.buttonOpen.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;

            this.buttonSaveAs.Text = "Save As...";
            this.buttonSaveAs.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonSaveAs.Enabled = false;

            this.labelMode.Text = "Click mode:";

            this.comboMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboMode.Items.AddRange(new object[] { "X axis", "Y axis", "Ruler" });
            this.comboMode.Width = 80;

            this.labelXmm.Text = "X mm:";
            this.textXmm.Width = 70;
            this.textXmm.Text = "";

            this.labelYmm.Text = "Y mm:";
            this.textYmm.Width = 70;
            this.textYmm.Text = "";

            this.buttonProcess.Text = "Process + Save";
            this.buttonProcess.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonProcess.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.buttonProcess.ToolTipText =
                "One click: rotates and calibrates at the photo's native resolution (no detail loss),\r\n" +
                "saves the result next to the original, and shows the values to enter in SolidWorks.";

            this.buttonShowResult.Text = "Result view";
            this.buttonShowResult.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonShowResult.CheckOnClick = true;
            this.buttonShowResult.Enabled = false;

            this.buttonClearMarks.Text = "Clear marks";
            this.buttonClearMarks.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;

            this.buttonFit.Text = "Fit";
            this.buttonFit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonFit.ToolTipText = "Zoom to fit the image in the window";

            // statusStripMain
            this.statusMessage.Spring = true;
            this.statusMessage.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.statusCursor.AutoSize = true;
            this.statusZoom.AutoSize = true;
            this.statusStripMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.statusMessage,
                this.statusCursor,
                this.statusZoom
            });

            // canvas
            this.canvas.Dock = System.Windows.Forms.DockStyle.Fill;

            // Form
            this.Controls.Add(this.canvas);
            this.Controls.Add(this.statusStripMain);
            this.Controls.Add(this.toolStripMain);
            this.Text = "Photo Scale & Rotate for SolidWorks";
            this.ClientSize = new System.Drawing.Size(1280, 800);
            this.KeyPreview = true;
        }
    }
}
