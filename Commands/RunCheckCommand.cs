using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PlanUp.Engine;
using PlanUp.UI;

namespace PlanUp.Commands
{
    /// <summary>
    /// The command that runs when the user clicks "Run Check" on the ribbon.
    /// 
    /// Step 3: Now loads rule definitions from JSON files using ComplianceEngine,
    /// then shows a summary of what was loaded. Still uses dummy results in the
    /// panel because real geometry extractors come in Step 4.
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

                // ---- LOAD RULES FROM JSON ----

                // Find the Rules folder next to the DLL
                string assemblyDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? "";
                string rulesFolder = Path.Combine(assemblyDir, "Rules");

                // Create the engine, which loads all rule files automatically
                ComplianceEngine engine = new ComplianceEngine(rulesFolder);

                // Show what was loaded (temporary, for development)
                TaskDialog.Show("PlanUp Engine", engine.GetLoadSummary());

                // ---- CREATE DUMMY RESULTS ----

                // Still using dummy results until geometry extractors are built.
                // But now each dummy result uses metadata from the loaded rules
                // to ensure consistency between the JSON definitions and the UI.

                List<CheckResult> dummyResults = new List<CheckResult>();

                // Build dummy results from loaded rules
                foreach (var rule in engine.Rules.Values)
                {
                    CheckResult result;

                    switch (rule.evaluation.type)
                    {
                        case "max_threshold":
                            result = new CheckResult
                            {
                                RuleId = rule.rule_id,
                                ArticleReference = rule.article,
                                RuleName = rule.name,
                                MeasuredValue = 12.5,
                                AllowedValue = 15.0,
                                Unit = rule.evaluation.unit,
                                Status = ComplianceStatus.Green,
                                SourceUrl = rule.source_url,
                                StatusMessage = rule.messages.green
                                    .Replace("{measured}", "12.5")
                                    .Replace("{limit}", "15.0"),
                                DetailDescription = rule.description
                            };
                            break;

                        case "min_threshold_per_face":
                            result = new CheckResult
                            {
                                RuleId = rule.rule_id,
                                ArticleReference = rule.article,
                                RuleName = rule.name,
                                MeasuredValue = 2.1,
                                AllowedValue = 2.0,
                                Unit = rule.evaluation.unit,
                                Status = ComplianceStatus.Yellow,
                                SourceUrl = rule.source_url,
                                StatusMessage = rule.messages.yellow
                                    .Replace("{measured}", "2.1")
                                    .Replace("{limit}", "2.0")
                                    .Replace("{boundary_name}", "north boundary"),
                                DetailDescription = rule.description
                            };
                            break;

                        case "envelope_intersection":
                            result = new CheckResult
                            {
                                RuleId = rule.rule_id,
                                ArticleReference = rule.article,
                                RuleName = rule.name,
                                MeasuredValue = 72.0,
                                AllowedValue = 70.0,
                                Unit = "\u00B0",
                                Status = ComplianceStatus.Red,
                                SourceUrl = rule.source_url,
                                StatusMessage = rule.messages.red
                                    .Replace("{boundary_name}", "east boundary")
                                    .Replace("{distance}", "0.8")
                                    .Replace("{level}", "4"),
                                DetailDescription = rule.description
                            };
                            break;

                        default:
                            result = new CheckResult
                            {
                                RuleId = rule.rule_id,
                                ArticleReference = rule.article,
                                RuleName = rule.name,
                                Status = ComplianceStatus.Yellow,
                                SourceUrl = rule.source_url,
                                StatusMessage = $"Unknown evaluation type: {rule.evaluation.type}",
                                DetailDescription = rule.description
                            };
                            break;
                    }

                    dummyResults.Add(result);
                }

                // ---- LOAD RESULTS INTO THE PANEL ----

                CompliancePanel panel = PlanUpApp.CompliancePanelInstance;
                if (panel != null)
                {
                    panel.LoadResults(dummyResults);
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
