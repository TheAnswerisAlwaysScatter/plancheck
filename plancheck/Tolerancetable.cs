using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace plancheck
{
    public enum CheckStatus
    {
        Unknown = 0,   // no expected value configured, or value is N/A
        Pass,          // green
        Warn,          // yellow
        Fail           // red
    }

    /// <summary>One entry from the expected-values JSON.</summary>
    public sealed class Tolerance
    {
        public string CheckName { get; set; }
        public string Section { get; set; }
        public string Expected { get; set; }
        public double? Warn { get; set; }
        public double? Fail { get; set; }
        public string Unit { get; set; }
        public string Type { get; set; }

        public bool IsNumeric
        {
            get { return string.Equals(Type, "numeric", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public sealed class EvaluationResult
    {
        public CheckStatus Status { get; set; }
        public string Detail { get; set; }

        internal static EvaluationResult Make(CheckStatus s, string detail)
        {
            return new EvaluationResult { Status = s, Detail = detail };
        }
    }

    /// <summary>
    /// Flattened view of the expected-values JSON for a single protocol.
    /// Section names in the JSON are organisational only; lookup is by CheckName,
    /// which must therefore be unique across all sections for a given protocol.
    /// </summary>
    public sealed class ToleranceTable
    {
        private const double NumericEpsilon = 1e-9;

        private static readonly Regex LeadingNumber = new Regex(
            @"^\s*[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.Compiled);

        private readonly Dictionary<string, Tolerance> _byName;
        private readonly Dictionary<string, EvaluationResult> _memo =
            new Dictionary<string, EvaluationResult>(StringComparer.Ordinal);

        public string Protocol { get; private set; }

        public IEnumerable<string> ConfiguredCheckNames
        {
            get { return _byName.Keys; }
        }

        private ToleranceTable(string protocol, Dictionary<string, Tolerance> byName)
        {
            Protocol = protocol;
            _byName = byName;
        }

        // ── Loading ────────────────────────────────────────────────────────

        /// <summary>Protocol keys present in the file, for error messages and UI.</summary>
        public static IList<string> AvailableProtocols(string jsonPath)
        {
            var root = JObject.Parse(File.ReadAllText(jsonPath));
            var names = new List<string>();
            foreach (var section in root.Properties())
            {
                if (section.Name.StartsWith("_")) continue;
                var obj = section.Value as JObject;
                if (obj == null) continue;
                foreach (var proto in obj.Properties())
                    if (!names.Contains(proto.Name)) names.Add(proto.Name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public static ToleranceTable Load(string jsonPath, string protocol)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("Tolerance file path is empty. Set AppSettings[\"TolerancePath\"].");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("Tolerance file not found.", jsonPath);
            if (string.IsNullOrWhiteSpace(protocol))
                throw new ArgumentException(
                    "No protocol selected. Set AppSettings[\"Protocol\"] to one of: " +
                    string.Join(", ", AvailableProtocols(jsonPath)));

            var root = JObject.Parse(File.ReadAllText(jsonPath));
            var byName = new Dictionary<string, Tolerance>(StringComparer.Ordinal);
            var sectionsWithProtocol = 0;

            foreach (var sectionProp in root.Properties())
            {
                if (sectionProp.Name.StartsWith("_")) continue;   // "_comment" and friends

                var section = sectionProp.Value as JObject;
                if (section == null) continue;

                var protoNode = section[protocol] as JObject;
                if (protoNode == null) continue;                   // section not defined for this protocol
                sectionsWithProtocol++;

                foreach (var checkProp in protoNode.Properties())
                {
                    var tol = checkProp.Value.ToObject<Tolerance>();
                    tol.CheckName = checkProp.Name;
                    tol.Section = sectionProp.Name;

                    Validate(tol);

                    Tolerance existing;
                    if (byName.TryGetValue(tol.CheckName, out existing))
                        throw new InvalidDataException(string.Format(
                            "Duplicate check name '{0}' under protocol '{1}' (in sections '{2}' and '{3}'). " +
                            "Check names must be unique across sections.",
                            tol.CheckName, protocol, existing.Section, tol.Section));

                    byName.Add(tol.CheckName, tol);
                }
            }

            if (sectionsWithProtocol == 0)
                throw new InvalidDataException(string.Format(
                    "Protocol '{0}' not found in {1}. Available: {2}",
                    protocol, Path.GetFileName(jsonPath),
                    string.Join(", ", AvailableProtocols(jsonPath))));

            return new ToleranceTable(protocol, byName);
        }

        private static void Validate(Tolerance t)
        {
            if (string.IsNullOrWhiteSpace(t.Expected))
                throw new InvalidDataException(string.Format("'{0}' has no expected value.", t.CheckName));

            if (t.IsNumeric)
            {
                double e;
                if (!TryParseNumber(t.Expected, out e))
                    throw new InvalidDataException(string.Format(
                        "'{0}' is type numeric but expected value '{1}' is not a number.", t.CheckName, t.Expected));

                if (t.Warn.HasValue && t.Warn.Value < 0)
                    throw new InvalidDataException(string.Format("'{0}' has a negative warn band.", t.CheckName));
                if (t.Fail.HasValue && t.Fail.Value < 0)
                    throw new InvalidDataException(string.Format("'{0}' has a negative fail band.", t.CheckName));
                if (t.Warn.HasValue && t.Fail.HasValue && t.Warn.Value > t.Fail.Value)
                    throw new InvalidDataException(string.Format(
                        "'{0}' has warn ({1}) greater than fail ({2}); bands must widen outward.",
                        t.CheckName, t.Warn, t.Fail));
            }
            else if (string.Equals(t.Type, "string", StringComparison.OrdinalIgnoreCase))
            {
                if (t.Warn.HasValue || t.Fail.HasValue)
                    throw new InvalidDataException(string.Format(
                        "'{0}' is type string but defines warn/fail bands, which are meaningless for an exact match.",
                        t.CheckName));
            }
            else
            {
                throw new InvalidDataException(string.Format(
                    "'{0}' has unrecognised type '{1}'. Expected \"numeric\" or \"string\".", t.CheckName, t.Type));
            }
        }

        // ── Evaluation ─────────────────────────────────────────────────────

        /// <summary>
        /// Grade one observed value against its configured tolerance.
        /// Unknown = uncoloured: either no expected value exists, or the cell is a
        /// placeholder ("N/A" padding in the beams grid).
        /// </summary>
        public EvaluationResult Evaluate(string checkName, string actual)
        {
            if (string.IsNullOrEmpty(checkName))
                return EvaluationResult.Make(CheckStatus.Unknown, null);

            var memoKey = checkName + "\u0001" + (actual ?? "");
            EvaluationResult cached;
            if (_memo.TryGetValue(memoKey, out cached)) return cached;

            var result = EvaluateCore(checkName, actual);
            _memo[memoKey] = result;
            return result;
        }

        private EvaluationResult EvaluateCore(string checkName, string actual)
        {
            Tolerance t;
            if (!_byName.TryGetValue(checkName, out t))
                return EvaluationResult.Make(CheckStatus.Unknown, "No expected value configured for this check.");

            if (actual != null && actual.Trim().Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return EvaluationResult.Make(CheckStatus.Unknown, "No value produced for this beam.");

            if (string.IsNullOrWhiteSpace(actual))
                return EvaluationResult.Make(CheckStatus.Fail,
                    string.Format("Expected {0}{1}, but no value was produced.", t.Expected, t.Unit));

            if (!t.IsNumeric)
            {
                var match = string.Equals(actual.Trim(), t.Expected.Trim(), StringComparison.OrdinalIgnoreCase);
                return EvaluationResult.Make(
                    match ? CheckStatus.Pass : CheckStatus.Fail,
                    match
                        ? string.Format("Matches expected value '{0}'.", t.Expected)
                        : string.Format("Expected '{0}', found '{1}'.", t.Expected, actual.Trim()));
            }

            double a;
            if (!TryParseNumber(actual, out a))
                return EvaluationResult.Make(CheckStatus.Fail,
                    string.Format("Expected a number near {0}{1}, but '{2}' could not be parsed.",
                        t.Expected, t.Unit, actual.Trim()));

            double e;
            TryParseNumber(t.Expected, out e);   // already validated at load
            var delta = Math.Abs(a - e);

            CheckStatus status;
            if (!t.Warn.HasValue && !t.Fail.HasValue)
                status = delta <= NumericEpsilon ? CheckStatus.Pass : CheckStatus.Fail;
            else if (t.Warn.HasValue && !t.Fail.HasValue)
                status = delta <= t.Warn.Value ? CheckStatus.Pass : CheckStatus.Fail;
            else if (!t.Warn.HasValue)
                status = delta <= t.Fail.Value ? CheckStatus.Pass : CheckStatus.Fail;
            else if (delta <= t.Warn.Value)
                status = CheckStatus.Pass;
            else if (delta <= t.Fail.Value)
                status = CheckStatus.Warn;
            else
                status = CheckStatus.Fail;

            var detail = string.Format(
                "Expected {0}{1}, found {2}{1} (Δ {3:0.####}{1}{4}).",
                t.Expected, t.Unit, a.ToString("0.####", CultureInfo.InvariantCulture), delta,
                DescribeBands(t));

            return EvaluationResult.Make(status, detail);
        }

        private static string DescribeBands(Tolerance t)
        {
            if (!t.Warn.HasValue && !t.Fail.HasValue) return "; exact match required";
            if (t.Warn.HasValue && t.Fail.HasValue)
                return string.Format("; warn > {0}, fail > {1}", t.Warn.Value, t.Fail.Value);
            if (t.Warn.HasValue) return string.Format("; fail > {0}", t.Warn.Value);
            return string.Format("; fail > {0}", t.Fail.Value);
        }

        /// <summary>
        /// Strips trailing units, so "-0.5cm" parses as -0.5 and "45.0°" as 45.0.
        /// Always invariant culture: the plan files use '.' as the decimal separator
        /// regardless of the machine's regional settings.
        /// </summary>
        public static bool TryParseNumber(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var m = LeadingNumber.Match(s);
            if (!m.Success) return false;

            return double.TryParse(m.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // ── Coverage ───────────────────────────────────────────────────────

        /// <summary>
        /// Checks configured for this protocol that the script never produced.
        /// A check that silently fails to run is the most dangerous outcome for a
        /// plan-check tool, so surface these as explicit failures.
        /// </summary>
        public IList<string> MissingChecks(IEnumerable<string> observedCheckNames)
        {
            var observed = new HashSet<string>(observedCheckNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return _byName.Keys.Where(k => !observed.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        public Tolerance Lookup(string checkName)
        {
            Tolerance t;
            return _byName.TryGetValue(checkName, out t) ? t : null;
        }
    }
}