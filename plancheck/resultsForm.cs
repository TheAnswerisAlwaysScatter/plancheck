using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace plancheck
{
    public partial class resultsForm : Form
    {
        private const string NoSelection = "\u2014 select protocol \u2014";

        private readonly ToleranceSource _tolerances = new ToleranceSource();

        private readonly List<string[]> _gridRows;
        private readonly List<string[]> _exceptionRows;
        private readonly List<string[]> _structureRows;
        private readonly List<string[]> _beamDataRows = new List<string[]>();
        private readonly List<string[]> _DVHRows;

        private readonly string _tolerancePath;

        // Optional designer controls, resolved by name. Absent is fine.
        private Label _summaryLabel;
        private Control _legendHost;

        private readonly ToolTip _comboTip = new ToolTip();

        public resultsForm(
            List<string[]> gridRows,
            List<string[]> ExceptionGridRows,
            List<string[]> structuresGridRows,
            List<string[]> BeamsGridRows,
            List<string[]> DVHGridRows,
            string tolerancePath = null)
        {
            InitializeComponent();  // called once only

            _gridRows = gridRows ?? new List<string[]>();
            _exceptionRows = ExceptionGridRows ?? new List<string[]>();
            _structureRows = structuresGridRows ?? new List<string[]>();
            _DVHRows = DVHGridRows ?? new List<string[]>();
            _tolerancePath = ResolveTolerancePath(tolerancePath);

            _summaryLabel = FindControl<Label>("summaryLabel");
            _legendHost = FindControl<Control>("legendPanel");

            BuildTwoColumnGrid(resultsDataGridView, _gridRows);
            BuildTwoColumnGrid(structureDataGridView, _structureRows);
            BuildTwoColumnGrid(DVHdataGridView, _DVHRows);
            BuildExceptionsGridColumns();
            BuildBeamsGrid(BeamsGridRows ?? new List<string[]>());

            GridColouring.Attach(resultsDataGridView, _tolerances);
            GridColouring.Attach(structureDataGridView, _tolerances);
            GridColouring.Attach(beamsDataGridView, _tolerances);
            GridColouring.Attach(DVHdataGridView, _tolerances);

            PopulateLegend();

            // Wire the listener BEFORE anything can raise an error, or the reason for a
            // failed load never reaches the exceptions grid and the combo just greys out.
            _tolerances.Changed += (s, e) => { RebuildExceptionsGrid(); UpdateSummary(); };

            InitialiseProtocolCombo();

            RebuildExceptionsGrid();
            UpdateSummary();
        }

        /// <summary>
        /// AppSettings["TolerancePath"] wins; otherwise look beside the executable.
        /// A missing config key should not be the difference between a working tool
        /// and a disabled dropdown with no explanation.
        /// </summary>
        private static string ResolveTolerancePath(string supplied)
        {
            if (!string.IsNullOrWhiteSpace(supplied)) return supplied;

            var configured = ConfigurationManager.AppSettings["TolerancePath"];
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tolerances.json");
        }

        private T FindControl<T>(string name) where T : Control
        {
            var matches = Controls.Find(name, true);
            return matches.Length == 0 ? null : matches[0] as T;
        }

        // ── Protocol selection ─────────────────────────────────────────────

        private void InitialiseProtocolCombo()
        {
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;   // no free text
            comboBox1.Items.Clear();
            comboBox1.Items.Add(NoSelection);

            try
            {
                if (!File.Exists(_tolerancePath))
                    throw new FileNotFoundException("Tolerance file not found.", _tolerancePath);

                foreach (var protocol in ToleranceTable.AvailableProtocols(_tolerancePath))
                    comboBox1.Items.Add(protocol);

                comboBox1.Enabled = true;
                _comboTip.SetToolTip(comboBox1, "Expected values from " + _tolerancePath);
            }
            catch (Exception ex)
            {
                comboBox1.Enabled = false;
                _comboTip.SetToolTip(comboBox1, ex.Message);
                _tolerances.SetError(string.Format(
                    "Could not read protocols from '{0}': {1}", _tolerancePath, ex.Message));
            }

            comboBox1.SelectedIndex = 0;   // deliberately unselected: nothing is graded yet
            comboBox1.SelectedIndexChanged += (s, e) => ApplyProtocol(SelectedProtocol());
        }

        private string SelectedProtocol()
        {
            var text = Convert.ToString(comboBox1.SelectedItem);
            return text == NoSelection ? null : text;
        }

        private void ApplyProtocol(string protocol)
        {
            if (protocol == null)
            {
                _tolerances.Clear();
                return;
            }

            try
            {
                _tolerances.Set(ToleranceTable.Load(_tolerancePath, protocol));
            }
            catch (Exception ex)
            {
                _tolerances.SetError(ex.Message);
                MessageBox.Show(
                    string.Format("Could not load protocol '{0}':\n\n{1}", protocol, ex.Message),
                    "Tolerance file error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Grid construction ──────────────────────────────────────────────

        private static void BuildTwoColumnGrid(DataGridView grid, IEnumerable<string[]> rows)
        {
            grid.Columns.Clear();
            grid.Rows.Clear();
            grid.Columns.Add("CheckName", "Check");
            grid.Columns.Add("Value", "Value");
            grid.Columns["CheckName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns["Value"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            foreach (var row in rows)
                grid.Rows.Add(row.Length > 0 ? row[0] : "", row.Length > 1 ? row[1] : "");
        }

        private void BuildExceptionsGridColumns()
        {
            exceptionsDataGridView.Columns.Clear();
            exceptionsDataGridView.Rows.Clear();
            exceptionsDataGridView.Columns.Add("CheckName", "Check");
            exceptionsDataGridView.Columns.Add("Value", "Detail");
            exceptionsDataGridView.Columns["CheckName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            exceptionsDataGridView.Columns["Value"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        /// <summary>
        /// Exceptions raised during collection, plus any check the selected protocol
        /// expects that the script never produced. Coverage depends on the protocol,
        /// so this is rebuilt whenever the protocol changes.
        /// </summary>
        private void RebuildExceptionsGrid()
        {
            if (exceptionsDataGridView.Columns.Count == 0) return;
            exceptionsDataGridView.Rows.Clear();

            foreach (var row in _exceptionRows)
                exceptionsDataGridView.Rows.Add(row.Length > 0 ? row[0] : "", row.Length > 1 ? row[1] : "");

            if (_tolerances.Error != null)
                AddFailRow("Tolerances", _tolerances.Error);

            if (!_tolerances.HasProtocol) return;

            var observed = _gridRows
                .Concat(_structureRows)
                .Concat(_beamDataRows)
                .Where(r => r.Length > 0)
                .Select(r => r[0]);

            foreach (var missing in _tolerances.Table.MissingChecks(observed))
                AddFailRow(missing, "NOT EVALUATED \u2013 expected by protocol but the script produced no value");
        }

        private void AddFailRow(string check, string detail)
        {
            var index = exceptionsDataGridView.Rows.Add(check, detail);
            var row = exceptionsDataGridView.Rows[index];
            row.DefaultCellStyle.BackColor = GridColouring.FailBack;
            row.DefaultCellStyle.ForeColor = GridColouring.FailFore;
            row.DefaultCellStyle.SelectionBackColor = ControlPaint.Dark(GridColouring.FailBack, 0.08f);
            row.DefaultCellStyle.SelectionForeColor = GridColouring.FailFore;
        }

        /// <summary>
        /// The header row is detected rather than assumed. If Program.cs adds the
        /// ["Check Name", "Beam 1", ...] header to the wrong list, treating row 0 as a
        /// header would title the columns after beam values AND silently drop the first
        /// check from the report. Synthesising headers is the safe fallback.
        /// </summary>
        private void BuildBeamsGrid(List<string[]> beamRows)
        {
            beamsDataGridView.Columns.Clear();
            beamsDataGridView.Rows.Clear();
            _beamDataRows.Clear();

            if (beamRows.Count == 0) return;

            var hasHeader = beamRows[0].Length > 0
                && string.Equals(beamRows[0][0], "Check Name", StringComparison.OrdinalIgnoreCase);

            _beamDataRows.AddRange(hasHeader ? beamRows.Skip(1) : beamRows);
            if (_beamDataRows.Count == 0) return;

            var beamCount = hasHeader
                ? beamRows[0].Length - 1
                : _beamDataRows.Max(r => r.Length) - 1;

            beamsDataGridView.Columns.Add("CheckName", "Check");
            beamsDataGridView.Columns["CheckName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;

            for (int i = 1; i <= beamCount; i++)
            {
                var key = "Beam" + i;
                var caption = hasHeader ? beamRows[0][i] : "Beam " + i;
                beamsDataGridView.Columns.Add(key, caption);
                beamsDataGridView.Columns[key].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            foreach (var row in _beamDataRows)
                beamsDataGridView.Rows.Add(row.Cast<object>().ToArray());
        }

        // ── Status readout ─────────────────────────────────────────────────

        private void PopulateLegend()
        {
            if (_legendHost == null) return;

            var legend = GridColouring.BuildLegend();
            legend.Dock = DockStyle.Fill;
            _legendHost.Controls.Clear();
            _legendHost.Controls.Add(legend);
        }

        private void UpdateSummary()
        {
            if (_summaryLabel == null) return;

            if (!_tolerances.HasProtocol)
            {
                _summaryLabel.Text = _tolerances.Error != null
                    ? "Tolerances unavailable \u2014 values shown unchecked"
                    : "No protocol selected \u2014 values shown unchecked";
                _summaryLabel.ForeColor = SystemColors.GrayText;
                return;
            }

            var counts = GridColouring.Summarise(_tolerances, _gridRows, _structureRows, _beamDataRows);
            _summaryLabel.Text = _tolerances.ProtocolName + ": " + GridColouring.SummaryText(counts);
            _summaryLabel.ForeColor = counts[CheckStatus.Fail] > 0
                ? GridColouring.FailFore
                : SystemColors.ControlText;
        }

        // ── Designer-wired handlers ────────────────────────────────────────

        private void resultsDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // reserved for future cell click handling
        }

        private void exceptionDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // reserved for future cell click handling
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}