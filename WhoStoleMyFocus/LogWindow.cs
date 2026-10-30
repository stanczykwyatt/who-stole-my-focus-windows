using WhoStoleMyFocus.Core;

namespace WhoStoleMyFocus;

/// <summary>The log: steals or everything, newest first, with the top offenders above.</summary>
internal sealed class LogWindow : Form
{
    readonly FocusModel model;
    readonly DataGridView offenders = Grid();
    readonly DataGridView events = Grid();
    readonly Label summary = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly RadioButton stealsOnly = Segment("Steals");
    readonly RadioButton everything = Segment("Everything");
    readonly ContextMenuStrip rowMenu = new();
    List<FocusEvent> shown = [];

    public LogWindow(FocusModel model)
    {
        this.model = model;
        Text = "Who Stole My Focus";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Size = new Size(980, 640);
        Padding = new Padding(16);

        stealsOnly.Checked = true;
        stealsOnly.CheckedChanged += (_, _) => Reload();
        var copy = Button("Copy as CSV", CopyCsv);
        var clear = Button("Clear Log…", ClearLog);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 8) };
        bar.Controls.AddRange([stealsOnly, everything, Spacer(), copy, clear]);

        offenders.Columns.Add("app", "Top offenders");
        offenders.Columns.Add("count", "Steals");
        offenders.Columns.Add("victim", "Usually took focus from");
        offenders.Columns.Add("last", "Last");
        offenders.Dock = DockStyle.Top;
        offenders.Height = 150;

        foreach (var (name, title) in new[] { ("time", "Time"), ("app", "App"), ("what", "What happened"), ("why", "Why"), ("then", "Then") })
        {
            events.Columns.Add(name, title);
        }
        events.Columns["why"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        events.Dock = DockStyle.Fill;
        events.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) events.Rows[e.RowIndex].Selected = true;
        };
        events.ContextMenuStrip = rowMenu;
        rowMenu.Opening += (_, e) => BuildRowMenu(e);

        var gap = new Panel { Dock = DockStyle.Top, Height = 12 };
        Controls.Add(events);
        Controls.Add(gap);
        Controls.Add(offenders);
        Controls.Add(bar);
        Controls.Add(summary);
        summary.Dock = DockStyle.Bottom;

        model.Changed += OnChanged;
        FormClosed += (_, _) => model.Changed -= OnChanged;
        ApplyTheme();
        Reload();
    }

    void OnChanged()
    {
        if (IsHandleCreated) BeginInvoke(Reload);
    }

    void Reload()
    {
        var now = DateTime.UtcNow;
        shown = stealsOnly.Checked ? model.Steals() : model.Log.Events.Reverse().ToList();
        events.Rows.Clear();
        var theme = Theme.Current;
        foreach (var e in shown.Take(2_000))
        {
            var row = events.Rows.Add(
                Describe.CompactTime(e.Date, now), Describe.Name(e.App), Describe.VerdictName(e.Verdict),
                Describe.ShortReason(e), Describe.Aftermath(e, model.IsCurrent(e)));
            if (e.Verdict == Verdict.Stole && !stealsOnly.Checked) events.Rows[row].Cells["what"].Style.ForeColor = theme.Steal;
            events.Rows[row].Tag = e;
        }
        offenders.Rows.Clear();
        foreach (var o in model.Offenders().Take(10))
        {
            offenders.Rows.Add(Describe.Name(o.App), o.Count, o.UsualVictim is { } v ? Describe.Name(v) : "", Describe.CompactTime(o.LastDate, now));
        }
        var today = model.Steals(now.AddDays(-1)).Count;
        summary.Text = today switch
        {
            0 => "No steals in the last 24 hours.",
            1 => "1 steal in the last 24 hours.",
            _ => $"{today} steals in the last 24 hours.",
        } + " Right-click a row to copy its details or ignore the app.";
        model.MarkSeen();
    }

    void BuildRowMenu(System.ComponentModel.CancelEventArgs args)
    {
        rowMenu.Items.Clear();
        if (events.SelectedRows.Count == 0 || events.SelectedRows[0].Tag is not FocusEvent e)
        {
            args.Cancel = true;
            return;
        }
        rowMenu.Items.Add("Copy Details", null, (_, _) => Copy(Describe.Details(
            e, Describe.Relative(e.Date, DateTime.UtcNow), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), model.IsCurrent(e))));
        rowMenu.Items.Add(model.IsIgnored(e.App) ? $"Stop Ignoring {Describe.Name(e.App)}" : $"Ignore {Describe.Name(e.App)}", null, (_, _) =>
        {
            if (model.IsIgnored(e.App)) model.StopIgnoring(e.App.Key);
            else model.Ignore(e.App);
            Reload();
        });
    }

    void CopyCsv()
    {
        var csv = CsvExport.Csv(Enumerable.Reverse(shown), home: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Copy(csv);
    }

    /// <summary>Another app can be holding the clipboard for a moment; then it says so instead of failing.</summary>
    void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "Another app is using the clipboard. Try again in a moment.", "Who Stole My Focus", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    void ClearLog()
    {
        var answer = MessageBox.Show(this, "Clear every event from the log? This can't be undone.", "Who Stole My Focus",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK) model.ClearLog();
    }

    void ApplyTheme()
    {
        var theme = Theme.Current;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        summary.ForeColor = theme.SecondaryText;
        foreach (var grid in new[] { offenders, events })
        {
            grid.BackgroundColor = theme.Background;
            grid.GridColor = theme.Border;
            grid.DefaultCellStyle.BackColor = theme.Raised;
            grid.DefaultCellStyle.ForeColor = theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = theme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = theme.Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = theme.Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = theme.SecondaryText;
            grid.EnableHeadersVisualStyles = false;
        }
        foreach (var segment in new[] { stealsOnly, everything })
        {
            segment.FlatAppearance.CheckedBackColor = theme.Selection;
            segment.ForeColor = theme.Text;
        }
        if (theme.IsDark)
        {
            HandleCreated += (_, _) =>
            {
                var on = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            };
        }
    }

    static DataGridView Grid() => new()
    {
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
    };

    static RadioButton Segment(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        FlatStyle = FlatStyle.Flat,
        AutoSize = true,
        Padding = new Padding(10, 2, 10, 2),
    };

    static Button Button(string text, Action click)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.System };
        button.Click += (_, _) => click();
        return button;
    }

    static Control Spacer() => new Panel { Width = 24, Height = 1 };
}
