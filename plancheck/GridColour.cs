using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace plancheck
{
    /// <summary>
    /// Applies pass/warn/fail colouring to a DataGridView whose first column holds
    /// the CheckName and whose remaining columns hold observed values.
    ///
    /// Works for both shapes the script produces:
    ///   two-column grids  -> ["Check Name", "Value"]
    ///   the beams grid    -> ["Check Name", "Beam 1", "Beam 2", ... "Beam N"]
    /// Each value cell is graded independently, so a dual-arc plan can have one
    /// green and one red column on the same row.
    /// </summary>
    public static class GridColouring
    {
        // Palette borrowed from Excel's conditional-formatting defaults: it survives
        // projector washout and most common colour-vision deficiencies better than
        // saturated RGB, because the foreground colours differ in luminance too.
        public static readonly Color PassBack = Color.FromArgb(198, 239, 206);
        public static readonly Color PassFore = Color.FromArgb(0, 97, 0);
        public static readonly Color WarnBack = Color.FromArgb(255, 235, 156);
        public static readonly Color WarnFore = Color.FromArgb(156, 87, 0);
        public static readonly Color FailBack = Color.FromArgb(255, 199, 206);
        public static readonly Color FailFore = Color.FromArgb(156, 0, 6);
        public static readonly Color UnknownBack = Color.FromArgb(242, 242, 242);
        public static readonly Color UnknownFore = Color.FromArgb(110, 110, 110);

        /// <summary>
        /// Call once per grid. The grid repaints itself whenever the source's
        /// protocol changes; do not call this again on protocol change.
        /// </summary>
        public static void Attach(DataGridView grid, ToleranceSource source, int checkNameColumn = 0)
        {
            if (grid == null) throw new ArgumentNullException("grid");
            if (source == null) throw new ArgumentNullException("source");

            grid.ShowCellToolTips = true;

            grid.CellFormatting += (sender, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex == checkNameColumn) return;

                var result = Grade(grid, source, e.RowIndex, e.ColumnIndex, checkNameColumn);
                if (result == null) return;

                Color back, fore;
                Palette(result.Status, out back, out fore);

                e.CellStyle.BackColor = back;
                e.CellStyle.ForeColor = fore;

                // Without this the colour vanishes the moment a physicist clicks the row.
                e.CellStyle.SelectionBackColor = ControlPaint.Dark(back, 0.08f);
                e.CellStyle.SelectionForeColor = fore;
            };

            grid.CellToolTipTextNeeded += (sender, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex == checkNameColumn) return;

                var result = Grade(grid, source, e.RowIndex, e.ColumnIndex, checkNameColumn);
                if (result != null && !string.IsNullOrEmpty(result.Detail))
                    e.ToolTipText = result.Status + ": " + result.Detail;
            };

            EventHandler onChanged = (sender, e) =>
            {
                if (!grid.IsDisposed) grid.Refresh();
            };
            source.Changed += onChanged;
            grid.Disposed += (sender, e) => source.Changed -= onChanged;
        }

        private static EvaluationResult Grade(
            DataGridView grid, ToleranceSource source, int rowIndex, int colIndex, int checkNameColumn)
        {
            if (rowIndex >= grid.Rows.Count) return null;
            var row = grid.Rows[rowIndex];
            if (row.IsNewRow) return null;
            if (checkNameColumn >= row.Cells.Count || colIndex >= row.Cells.Count) return null;

            var nameCell = row.Cells[checkNameColumn].Value;
            var valueCell = row.Cells[colIndex].Value;
            if (nameCell == null) return null;

            return source.Evaluate(
                Convert.ToString(nameCell),
                valueCell == null ? null : Convert.ToString(valueCell));
        }

        private static void Palette(CheckStatus status, out Color back, out Color fore)
        {
            switch (status)
            {
                case CheckStatus.Pass: back = PassBack; fore = PassFore; break;
                case CheckStatus.Warn: back = WarnBack; fore = WarnFore; break;
                case CheckStatus.Fail: back = FailBack; fore = FailFore; break;
                default: back = UnknownBack; fore = UnknownFore; break;
            }
        }

        // ── Summary and legend ─────────────────────────────────────────────

        /// <summary>Counts every value cell across the supplied row sets.</summary>
        public static Dictionary<CheckStatus, int> Summarise(
            ToleranceSource source, params IEnumerable<string[]>[] rowSets)
        {
            var counts = new Dictionary<CheckStatus, int>
            {
                { CheckStatus.Pass, 0 }, { CheckStatus.Warn, 0 },
                { CheckStatus.Fail, 0 }, { CheckStatus.Unknown, 0 }
            };

            foreach (var rows in rowSets.Where(r => r != null))
                foreach (var row in rows)
                    for (int c = 1; c < row.Length; c++)
                        counts[source.Evaluate(row[0], row[c]).Status]++;

            return counts;
        }

        public static string SummaryText(Dictionary<CheckStatus, int> counts)
        {
            return string.Format("{0} pass \u00B7 {1} warn \u00B7 {2} fail \u00B7 {3} unchecked",
                counts[CheckStatus.Pass], counts[CheckStatus.Warn],
                counts[CheckStatus.Fail], counts[CheckStatus.Unknown]);
        }

        /// <summary>A legend strip. Text labels matter: never rely on hue alone.</summary>
        public static FlowLayoutPanel BuildLegend()
        {
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            Action<string, Color, Color> add = (text, back, fore) =>
                panel.Controls.Add(new Label
                {
                    Text = text,
                    BackColor = back,
                    ForeColor = fore,
                    AutoSize = true,
                    Padding = new Padding(8, 4, 8, 4),
                    Margin = new Padding(0, 3, 6, 3),
                    BorderStyle = BorderStyle.FixedSingle
                });

            add("Pass", PassBack, PassFore);
            add("Warn", WarnBack, WarnFore);
            add("Fail", FailBack, FailFore);
            add("Not checked", UnknownBack, UnknownFore);

            return panel;
        }
    }
}