using Dicom;
using Dicom.Imaging;
using System.Configuration;
using Elekta.MonacoScripting.API;
using Elekta.MonacoScripting.DataType;
using Elekta.MonacoScripting.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace plancheck
{

    internal class Program

    {

        private static string Installation = ConfigurationManager.AppSettings["Installation"];
        private static string Clinic = ConfigurationManager.AppSettings["Clinic"];
        private static string BasePath = ConfigurationManager.AppSettings["BasePath"];


        static void Main(string[] args)
        {
            var gridRows = new List<string[]>();
            var structuresGridRows = new List<string[]>();
            var prescriptionGridRows = new List<string[]>();
            var BeamsGridRows = new List<string[]>();
            var IMRTGridRows = new List<string[]>();
            var ExceptionGridRows = new List<string[]>();
            var DVHGridRows = new List<string[]>();
            var catalogue = new Dictionary<string, List<string>>();

            MonacoApplication app = MonacoApplication.Instance;

            // ── Plan path ──────────────────────────────────────────────
            var planID = app.GetActivePlan();
            var dataPath = app.GetCurrentPatient().Path;
            string planPath = dataPath + "\\plan\\" + planID + "\\plan";
            Logger.Instance.Info($"Plan path: {planPath}");

            string[] planLines = File.ReadAllLines(planPath);

            int lineCount = 0;
            bool planFound = false;
            string imageName = string.Empty;
            string imagePath = string.Empty;
            var scanRefLines = new List<string>();
            int lastScanLine = 0;

            for (int i = 0; i < planLines.Length; i++)
            {
                if (planLines[i].Equals(planID)) planFound = true;
                if (planFound) lineCount++;
                if (lineCount == 5) imageName = planLines[i];

                var parts = planLines[i].Split(',');
                if (parts.Length == 3)
                {
                    scanRefLines.Add(planLines[i]);
                    lastScanLine = i;
                }
            }

            // ── Image set ──────────────────────────────────────────────
            foreach (string dir in Directory.GetDirectories(dataPath))
            {
                if (dir.Contains(imageName))
                {
                    imagePath = dir + "\\info";
                    Logger.Instance.Info($"Image path: {imagePath}");
                }
            }

            // ── Image orientation ──────────────────────────────────────────────
            try
            {
                string[] ctImageInfo = File.ReadAllLines(imagePath);
                gridRows.Add(new string[] { "Image Orientation", ctImageInfo[14] });
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Image Orientation", $"ERROR: {ex.Message}" }); }

            // ── Scan reference ─────────────────────────────────────────────────
            try
            {
                var scanRefCoords = scanRefLines.Last().Split(',');
                double xScan = Math.Round(Convert.ToDouble(scanRefCoords[0]) / 10, 2);
                double yScan = Math.Round(Convert.ToDouble(scanRefCoords[1]) / 10, 2);
                double zScan = Math.Round(Convert.ToDouble(scanRefCoords[2]) / 10, 2);
                gridRows.Add(new string[] { "Scan Ref X", $"{xScan}cm" });
                gridRows.Add(new string[] { "Scan Ref Y", $"{yScan}cm" });
                gridRows.Add(new string[] { "Scan Ref Z", $"{zScan}cm" });
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Scan Reference", $"ERROR: {ex.Message}" }); }

            // ── Couch ──────────────────────────────────────────────────────────
            try
            {
                string couchIndex = planLines[lastScanLine - 7];
                string couchOffset = Convert.ToString(Convert.ToDouble(planLines[lastScanLine - 6]) / 10);
                gridRows.Add(new string[] { "Couch Index", couchIndex });
                gridRows.Add(new string[] { "Couch Offset", $"{couchOffset}cm" });
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Couch", $"ERROR: {ex.Message}" }); }

            // ── DICOM pixel data ───────────────────────────────────────────────
            try
            {
                var redactimagepath = imagePath.Remove(imagePath.Length - 5);
                Logger.Instance.Info($"Redacted image path: {redactimagepath}");

                var folderPath = Path.Combine(redactimagepath, "DCMData");

                var dcmPath = Directory
                    .EnumerateFiles(folderPath, "*.dcm", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                if (dcmPath == null)
                {
                    throw new FileNotFoundException("No .dcm files found in the directory.");
                }

                var dicomFile = DicomFile.Open(dcmPath);
                var pixelData = DicomPixelData.Create(dicomFile.Dataset);
                gridRows.Add(new string[] { "DICOM Width", pixelData.Width.ToString() });
                gridRows.Add(new string[] { "DICOM Height", pixelData.Height.ToString() });
                gridRows.Add(new string[] { "DICOM Frames", pixelData.NumberOfFrames.ToString() });
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "DICOM", $"ERROR: {ex.Message}" }); }

            // ── Structures ─────────────────────────────────────────────────────
            try
            {
                var structure = app.GetStructuresSpreadsheet();
                var structAll = structure.GetStructuresProperties();
                var structProps = structAll.ContouredProperties;
                foreach (var p in structProps)
                {
                    structuresGridRows.Add(new string[] { $"Structure: {p.Name} - Type", p.Type.ToString() });
                    structuresGridRows.Add(new string[] { $"Structure: {p.Name} - FillED", p.FillED.ToString() });
                    structuresGridRows.Add(new string[] { $"Structure: {p.Name} - ForceED", p.ForceED.ToString() });
                    structuresGridRows.Add(new string[] { $"Structure: {p.Name} - RelativeED", p.RelativeED.ToString() });
                }

                var couchProps = structAll.CouchProperties;
                foreach (var p in couchProps)
                {
                    structuresGridRows.Add(new string[] { $"Couch: {p.Name} - Type", p.Type.ToString() });
                    structuresGridRows.Add(new string[] { $"Couch: {p.Name} - Generation", p.Generation.ToString() });
                    structuresGridRows.Add(new string[] { $"Couch: {p.Name} - RelativeED", p.RelativeED.ToString() });
                }

                var adaptProps = structAll.AdaptSetupProperties;
                foreach (var p in adaptProps)
                {
                    structuresGridRows.Add(new string[] { $"Adapt: {p.Name} - Generation Method", p.AnatomyGenerationMethod.ToString() });
                    structuresGridRows.Add(new string[] { $"Adapt: {p.Name} - ForceED on CT", p.ForceEDonCT.ToString() });
                    structuresGridRows.Add(new string[] { $"Adapt: {p.Name} - FillED on CT", p.FillEDonCT.ToString() });
                    structuresGridRows.Add(new string[] { $"Adapt: {p.Name} - Mean ED", p.MeanED.ToString() });
                    structuresGridRows.Add(new string[] { $"Adapt: {p.Name} - Layering Index", p.LayeringIndex.ToString() });
                }
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Structures", $"ERROR: {ex.Message}" }); }

            // ── Calculation properties ─────────────────────────────────────────
            try
            {
                var calcProps = app.GetCalculationPropertiesSettings();
                var calcProp = calcProps.GetCalculationProperty();
                gridRows.Add(new string[] { "Grid Spacing", calcProp.GridSpacing.ToString() });
                gridRows.Add(new string[] { "Uncertainty Per Beam", calcProp.UncentaintyPerBeam.ToString() });
                gridRows.Add(new string[] { "Uncertainty Per Spot", calcProp.UncentaintyPerSpot.ToString() });
                gridRows.Add(new string[] { "Dose Deposition", calcProp.DoseDeposition.ToString() });
                calcProps.ClickCancel();
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Calculation Properties", $"ERROR: {ex.Message}" }); }

            // ── Beams ──────────────────────────────────────────────────────────
            try
            {
                var beams = app.GetBeamsSpreadsheet();

;
                int beamCount = 0;

                void EnsureKey(string key)
                {
                    if (!catalogue.ContainsKey(key))
                        catalogue[key] = new List<string>();
                }

                // --- Treatment Aids ---
                var treatAids = beams.GetTreatmentAidsProperties().ToList();
                beamCount = Math.Max(beamCount, treatAids.Count);

                foreach (var beam in treatAids)
                        {
                            var treatFields = new Dictionary<string, string>
                {
                    { "Treatment Aids - Couch",  beam.Couch.ToString() },
                    { "Treatment Aids - Bolus",  beam.Bolus.ToString() }
                };

                    foreach (var kvp in treatFields)
                    {
                        EnsureKey(kvp.Key);
                        catalogue[kvp.Key].Add(kvp.Value);
                    }
                }

                // --- General Properties ---
                var beamGen = beams.GetBeamGeneralProperties().ToList();
                beamCount = Math.Max(beamCount, beamGen.Count);

                foreach (var beam in beamGen)
                {
                    var beamIso = $"{beam.BeamIso.IsoX},{beam.BeamIso.IsoY},{beam.BeamIso.IsoZ}";

                    var genFields = new Dictionary<string, string>
        {
            { "General - Field ID",         beam.FieldID.ToString() },
            { "General - Description",      beam.BeamDescription.ToString() },
            { "General - Algorithm",        beam.Algorithm.ToString() },
            { "General - Energy",           beam.Energy.ToString() },
            { "General - Delivery",         beam.Delivery.ToString() },
            { "General - SSD",              beam.SSD.ToString() },
            { "General - Modality",         beam.Modality.ToString() },
            { "General - MU",               beam.MUFx.ToString() },
            { "General - Beam Iso (x,y,z)", beamIso }
        };

                    foreach (var kvp in genFields)
                    {
                        EnsureKey(kvp.Key);
                        catalogue[kvp.Key].Add(kvp.Value);
                    }
                }

                // --- Geometry Properties ---
                var beamGeom = beams.GetBeamGeometryProperties().ToList();
                beamCount = Math.Max(beamCount, beamGeom.Count);

                foreach (var beam in beamGeom)
                {
                    var geomFields = new Dictionary<string, string>
        {
            { "Geometry - Gantry Angle",  beam.Gantry.ToString() },
            { "Geometry - Arc",           beam.Arc.ToString() },
            { "Geometry - Beam ID",       beam.BeamID.ToString() },
            { "Geometry - Collimator",    beam.Collimator.ToString() },
            { "Geometry - Field",         beam.Field.ToString() },
            { "Geometry - Couch",         beam.Couch.ToString() },
            { "Geometry - Field Type",    beam.FieldType.ToString() },
            { "Geometry - Gantry Start",  beam.GantryStart.ToString() },
            { "Geometry - Inc",           beam.Inc.ToString() },
            { "Geometry - IsAsym",        beam.IsAsym.ToString() },
            { "Geometry - Length1",       beam.Length1.ToString() },
            { "Geometry - Length2",       beam.Length2.ToString() },
            { "Geometry - Width1",        beam.Width1.ToString() },
            { "Geometry - Width2",        beam.Width2.ToString() }
        };

                    foreach (var kvp in geomFields)
                    {
                        EnsureKey(kvp.Key);
                        catalogue[kvp.Key].Add(kvp.Value);
                    }
                }

                // --- Build gridRows: ["Check Name", "Beam 1", "Beam 2", ..., "Beam N"] ---
                var header = new List<string> { "Check Name" };
                for (int i = 1; i <= beamCount; i++)
                    header.Add($"Beam {i}");
                gridRows.Add(header.ToArray());

                foreach (var kvp in catalogue)
                {
                    var row = new List<string> { kvp.Key };
                    for (int i = 0; i < beamCount; i++)
                        row.Add(i < kvp.Value.Count ? kvp.Value[i] : "N/A");
                    BeamsGridRows.Add(row.ToArray());
                }
            }
            catch (Exception ex)
            {
                ExceptionGridRows.Add(new string[] { "Calculation Properties", $"ERROR: {ex.Message}" });
            }

            // ── Prescription ───────────────────────────────────────────────────
            try
            {
               

                var rx = app.GetPrescription();
                var rxInfo = rx.getAllPrescriptionInfo();
                var rxInfoList = rxInfo.PrescriptionUIpropertyList;
                foreach (var i in rxInfoList)
                {
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Site", i.RxSite.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Dose", i.RxDose.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Total MU", i.TotalMU.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Weight By MU", i.WeightByMU.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Prescribe To", i.PrescribeToType.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Actual Dose", i.ActualDose.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Fractional Dose", i.FractionalDose.ToString() });
                    gridRows.Add(new string[] { $"Rx: {i.RxID} - Fraction Number", i.FractionNumber.ToString() });
                }
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Prescription", $"ERROR: {ex.Message}" }); }

            // ── IMRT constraints ───────────────────────────────────────────────
            try
            {
                var imrtConst = app.GetIMRTConstraintsSpreadsheet();
                var imrtProps = imrtConst.GetIMRTConstraintsProperties();
                var imrtList = imrtProps.IMRTConstrainedPropertyList;
                foreach (var i in imrtList)
                    foreach (var j in i.CostFunctionsOfStructure)
                    {
                        gridRows.Add(new string[] { $"IMRT: {i.StructureName} - Weighting", j.Weight.ToString() });
                        gridRows.Add(new string[] { $"IMRT: {i.StructureName} - Is Manual", j.IsManual.ToString() });
                        gridRows.Add(new string[] { $"IMRT: {i.StructureName} - Is Multi-Criterial", j.IsMultiCriterial.ToString() });
                    }
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "IMRT Constraints", $"ERROR: {ex.Message}" }); }

            // ── DVH statistics ─────────────────────────────────────────────────
            try
            {
                var dvhStats = app.GetDVHStatisticsSpreadsheet();
                var dvhProps = dvhStats.GetDVHStatisticsProperties();
                foreach (var i in dvhProps.StatisticsOfStructureList)
                    DVHGridRows.Add(new string[] { $"DVH: {i.StructureName} - Max Dose", i.MaxDose.ToString() });
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "DVH Statistics", $"ERROR: {ex.Message}" }); }

            // ── Dosimetric criteria ────────────────────────────────────────────
            try
            {
                var dvhStats = app.GetDVHStatisticsSpreadsheet();
                var doseCrit = dvhStats.GetDosimetricCriteriaInfo();
                var doseCritList = doseCrit.DosimetricCriteriaPropList;
                foreach (var q in doseCritList)
                    foreach (var i in q.DoseCriteriaList)
                    {
                        DVHGridRows.Add(new string[] { $"Criteria: {q.StructureName} - {i.DosimetricCriterion}", i.ActualValue.ToString() });
                        DVHGridRows.Add(new string[] { $"Criteria: {q.StructureName} - {i.DosimetricCriterion} Goal", i.DoseGoalResult.ToString() });
                    }
            }
            catch (Exception ex) { ExceptionGridRows.Add(new string[] { "Dosimetric Criteria", $"ERROR: {ex.Message}" }); }


            // ── Debug ──────────────────────────────────────────────────────────
            Logger.Instance.Info($"Total rows collected: {structuresGridRows.Count}");
            Logger.Instance.Info("About to show results form...");

            var debugText = structuresGridRows.Count == 0
                ? "gridRows is EMPTY"
                : string.Join("\n", structuresGridRows.Select(r => $"{r[0]}: {r[1]}"));

            var exceptionText = ExceptionGridRows.Count == 0
                ? "No exceptions"
                : string.Join("\n", ExceptionGridRows.Select(r => $"{r[0]}: {r[1]}"));

            //MessageBox.Show($"=== GRID ROWS ({gridRows.Count}) ===\n{debugText}\n\n=== EXCEPTIONS ({ExceptionGridRows.Count}) ===\n{exceptionText}",
                //"Debug Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            // ── Show results ───────────────────────────────────────────────────
            Application.EnableVisualStyles();
            new resultsForm(gridRows, ExceptionGridRows, structuresGridRows, BeamsGridRows, DVHGridRows).ShowDialog();

            Logger.Instance.Info("Results form closed.");

        }

    }
}