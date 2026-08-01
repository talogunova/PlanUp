using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlanUp.Engine;
using PlanUp.Extractors;
using PlanUp.UI;

namespace PlanUp.Commands
{
    /// <summary>
    /// The command that runs when the user clicks "Run Check" on the ribbon.
    /// 
    /// Sequence:
    ///   1. Clear any previous rasante visualization (so it does not
    ///      interfere with geometry measurements)
    ///   2. Run all compliance checks against real model geometry
    ///   3. Create new rasante visualization based on results
    ///   4. Display results in the dockable panel
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunCheckCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // ---- SHOW THE DOCKABLE PANE ----

                DockablePane pane = uiApp.GetDockablePane(PlanUpApp.CompliancePaneId);

                if (pane != null && !pane.IsShown())
                {
                    pane.Show();
                }

                // ---- STEP 1: CLEAR PREVIOUS RASANTE VISUALIZATION ----
                // Must happen BEFORE running checks, otherwise the rasante
                // DirectShape surfaces get measured as building elements.

                using (Transaction cleanTx = new Transaction(doc, "PlanUp Clear Rasante"))
                {
                    cleanTx.Start();
                    RasanteVisualizer.ClearPreviousRasante(doc);
                    cleanTx.Commit();
                }

                // ---- STEP 2: LOAD RULES AND RUN CHECKS ----

                string assemblyDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? "";
                string rulesFolder = Path.Combine(assemblyDir, "Rules");

                ComplianceEngine engine = new ComplianceEngine(rulesFolder);

                if (!engine.IsHealthy)
                {
                    string errors = string.Join("\n", engine.LoadErrors);
                    TaskDialog.Show("PlanUp Warning",
                        $"Some rules could not be loaded:\n\n{errors}");
                }

                List<CheckResult> results = engine.RunChecks(doc);

                // ---- STEP 3: CREATE RASANTE VISUALIZATION ----
                // Only create visualization in 3D views.
                // In plan views, the inclined planes look confusing.

                bool is3DView = doc.ActiveView is View3D;
                bool isPlanView = doc.ActiveView is ViewPlan;
                bool canVisualize = is3DView || isPlanView;

                double rasanteAngle = 70.0;
                double rasanteBaseHeight = 0.0;
                bool rasanteHasViolations = false;

                foreach (var rule in engine.Rules.Values)
                {
                    if (rule.evaluation.type == "envelope_intersection")
                    {
                        if (!string.IsNullOrEmpty(rule.evaluation.angle_param) &&
                            rule.parameters.ContainsKey(rule.evaluation.angle_param))
                        {
                            double? val = rule.parameters[rule.evaluation.angle_param].value;
                            if (val != null) rasanteAngle = val.Value;
                        }

                        if (!string.IsNullOrEmpty(rule.evaluation.base_height_param) &&
                            rule.parameters.ContainsKey(rule.evaluation.base_height_param))
                        {
                            double? val = rule.parameters[rule.evaluation.base_height_param].value;
                            if (val != null) rasanteBaseHeight = val.Value;
                        }

                        CheckResult rasanteResult = results.FirstOrDefault(r => r.RuleId == rule.rule_id);
                        if (rasanteResult != null)
                        {
                            rasanteHasViolations = rasanteResult.Status == ComplianceStatus.Red;
                        }

                        break;
                    }
                }

                using (Transaction visTx = new Transaction(doc, "PlanUp Rasante Visualization"))
                {
                    visTx.Start();

                    // Clear previous wall highlights
                    if (doc.ActiveView is View3D || doc.ActiveView is ViewPlan)
                    {
                        RasanteVisualizer.ClearWallHighlights(doc, doc.ActiveView);
                    }

                    if (canVisualize)
                    {
                        // Get violating boundary indices from rasante check
                        HashSet<int> violatingBoundaries = new HashSet<int>();
                        if (rasanteHasViolations)
                        {
                            RasanteResult rasResult = RasanteExtractor.Extract(
                                doc, rasanteAngle, rasanteBaseHeight);
                            if (rasResult.IsValid && rasResult.HasViolations)
                            {
                                foreach (var v in rasResult.Violations)
                                {
                                    violatingBoundaries.Add(v.BoundaryIndex);
                                }

                                // Highlight violating walls
                                List<ElementId> violatingWallIds = rasResult.Violations
                                    .Select(v => v.ElementId)
                                    .Where(id => id != ElementId.InvalidElementId)
                                    .Distinct()
                                    .ToList();

                                RasanteVisualizer.HighlightViolatingWalls(
                                    doc, doc.ActiveView, violatingWallIds);
                            }
                        }

                        // Create rasante surfaces with per-plane coloring
                        List<RasanteVisualizer.PlaneInfo> planes =
                            RasanteVisualizer.CreateRasanteSurfacesWithIndex(
                                doc, rasanteAngle, rasanteBaseHeight);

                        if (planes.Count > 0)
                        {
                            RasanteVisualizer.ApplyPerPlaneColors(
                                doc, doc.ActiveView, planes, violatingBoundaries);
                        }
                    }

                    visTx.Commit();
                }

                // Force the view to refresh so the rasante planes appear immediately
                uidoc.RefreshActiveView();

                // ---- STEP 4: LOAD RESULTS INTO THE PANEL ----

                CompliancePanel panel = PlanUpApp.CompliancePanelInstance;
                if (panel != null)
                {
                    panel.LoadResults(results);
                }
                else
                {
                    TaskDialog.Show("PlanUp Error",
                        "The compliance panel could not be found. Try restarting Revit.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
