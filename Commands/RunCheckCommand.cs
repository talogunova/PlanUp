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
    /// Step 4: Now uses ComplianceEngine.RunChecks to produce real results
    /// for the altura check (measured from actual model geometry).
    /// Distanciamiento and rasante still show dummy data until Steps 5 and 6.
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

                // ---- LOAD RULES AND RUN CHECKS ----

                // Find the Rules folder next to the DLL
                string assemblyDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? "";
                string rulesFolder = Path.Combine(assemblyDir, "Rules");

                // Create the engine and load rules
                ComplianceEngine engine = new ComplianceEngine(rulesFolder);

                // Check if rules loaded successfully
                if (!engine.IsHealthy)
                {
                    string errors = string.Join("\n", engine.LoadErrors);
                    TaskDialog.Show("PlanUp Warning",
                        $"Some rules could not be loaded:\n\n{errors}");
                }

                // Run all checks against the current model
                // The engine uses real geometry for altura and dummy data
                // for distanciamiento and rasante (Steps 5 and 6)
                List<CheckResult> results = engine.RunChecks(doc);

                // ---- LOAD RESULTS INTO THE PANEL ----

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
