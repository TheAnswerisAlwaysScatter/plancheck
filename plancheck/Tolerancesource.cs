using System;
using System.Windows.Forms;

namespace plancheck
{
    /// <summary>
    /// Holds the currently selected ToleranceTable and notifies listeners when it
    /// changes. Grids bind to this once; swapping protocol swaps the table underneath
    /// them and raises Changed, rather than re-subscribing event handlers (which would
    /// silently stack up one duplicate handler per protocol change).
    ///
    /// When no protocol is selected, every cell grades as Unknown, so nothing is
    /// coloured green before the user has said what they are checking against.
    /// </summary>
    public sealed class ToleranceSource
    {
        private static readonly EvaluationResult NoProtocol =
            EvaluationResult.Make(CheckStatus.Unknown, "No protocol selected.");

        private static readonly EvaluationResult LoadFailed =
            EvaluationResult.Make(CheckStatus.Unknown, "Tolerance file could not be loaded.");

        public ToleranceTable Table { get; private set; }
        public string Error { get; private set; }

        public bool HasProtocol { get { return Table != null; } }

        public string ProtocolName { get { return Table == null ? null : Table.Protocol; } }

        public event EventHandler Changed;

        public void Set(ToleranceTable table)
        {
            Table = table;
            Error = null;
            Raise();
        }

        public void SetError(string message)
        {
            Table = null;
            Error = message;
            Raise();
        }

        public void Clear()
        {
            Table = null;
            Error = null;
            Raise();
        }

        public EvaluationResult Evaluate(string checkName, string actual)
        {
            if (Table != null) return Table.Evaluate(checkName, actual);
            return Error == null ? NoProtocol : LoadFailed;
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}