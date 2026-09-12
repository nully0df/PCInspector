#nullable enable

namespace PCInspector;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private Button refreshButton = null!;
    private Label statusLabel = null!;
    private DataGridView summaryGrid = null!;
    private DataGridView disksGrid = null!;
    private TextBox warningsBox = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var layout = new TableLayoutPanel();
        var header = new TableLayoutPanel();
        var title = new Label();
        var disksTitle = new Label();
        refreshButton = new Button();
        statusLabel = new Label();
        summaryGrid = new DataGridView();
        disksGrid = new DataGridView();
        warningsBox = new TextBox();
        SuspendLayout();

        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(20);
        layout.ColumnCount = 1;
        layout.RowCount = 6;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        header.Dock = DockStyle.Fill;
        header.ColumnCount = 2;
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        title.Text = "PCInspector";
        title.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        refreshButton.Text = "Refresh";
        refreshButton.Dock = DockStyle.Fill;
        refreshButton.AccessibleName = "Refresh system information";
        refreshButton.Click += RefreshButton_Click;
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(refreshButton, 1, 0);

        ConfigureGrid(summaryGrid);
        summaryGrid.Columns.Add("Property", "Property");
        summaryGrid.Columns.Add("Value", "Value");
        summaryGrid.Columns[0].FillWeight = 30;
        summaryGrid.Columns[1].FillWeight = 70;
        summaryGrid.AccessibleName = "System information";

        disksTitle.Text = "Local drives and volumes";
        disksTitle.Dock = DockStyle.Fill;
        disksTitle.TextAlign = ContentAlignment.MiddleLeft;
        ConfigureGrid(disksGrid);
        disksGrid.Columns.Add("Drive", "Drive");
        disksGrid.Columns.Add("Type", "Type");
        disksGrid.Columns.Add("Total", "Total");
        disksGrid.Columns.Add("Free", "Free");
        disksGrid.Columns.Add("Status", "Status");
        disksGrid.AccessibleName = "Local drives and volumes";

        warningsBox.Multiline = true;
        warningsBox.ReadOnly = true;
        warningsBox.ScrollBars = ScrollBars.Vertical;
        warningsBox.Dock = DockStyle.Fill;
        warningsBox.Height = 65;
        warningsBox.Visible = false;
        warningsBox.AccessibleName = "Collection warnings";
        statusLabel.Text = "Ready";
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(summaryGrid, 0, 1);
        layout.Controls.Add(disksTitle, 0, 2);
        layout.Controls.Add(disksGrid, 0, 3);
        layout.Controls.Add(warningsBox, 0, 4);
        layout.Controls.Add(statusLabel, 0, 5);
        Controls.Add(layout);

        AutoScaleDimensions = new SizeF(7, 15);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(940, 650);
        MinimumSize = new Size(760, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PCInspector — System information";
        Shown += MainForm_Shown;
        ResumeLayout(false);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.DefaultCellStyle.Padding = new Padding(5);
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.FixedSingle;
    }
}
