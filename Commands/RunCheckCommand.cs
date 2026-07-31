using System;
using System.Collections.Generic;
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
    /// For Step 2, this creates three hardcoded dummy results (one green, one yellow,
    /// one red) and sends them to the dockable panel. This lets us verify the full
    /// UI pipeline works before connecting real geometry extractors.
    /// 
    /// In Step 4, the dummy data will be replaced by actual ComplianceEngine output.
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

                // Get a reference to the dockable pane using the GUID we registered.
                // DockablePane is a Revit class that lets us show, hide, and check
                // the state of our panel.
                DockablePane pane = uiApp.GetDockablePane(PlanUpApp.CompliancePaneId);

                // If the pane is not visible, show it.
                // The user can also show/hide it from View > User Interface in Revit.
                if (pane != null && !pane.IsShown())
                {
                    pane.Show();
                }

                // ---- CREATE DUMMY RESULTS ----

                // These three results demonstrate each traffic light state.
                // The values are realistic OGUC numbers so the UI looks
                // like the real thing when demoing.

                List<CheckResult> dummyResults = new List<CheckResult>
                {
                    // GREEN: building height is well within limits
                    new CheckResult
                    {
                        RuleId = "OGUC-2.6.3-altura",
                        ArticleReference = "Art. 1.1.2 / PRC",
                        RuleName = "Altura maxima de edificacion",
                        MeasuredValue = 12.5,
                        AllowedValue = 15.0,
                        Unit = "m",
                        Status = ComplianceStatus.Green,
                        SourceUrl = "https://modulor.cl/oguc-disposiciones-generales-normas-de-competencia-definiciones-y-plazos/",
                        StatusMessage = "Building height (12.5 m) is within the allowed maximum (15.0 m)",
                        DetailDescription = "Measured from natural ground level to the highest point of the building. Limit set by Plan Regulador Comunal zone Z-3."
                    },

                    // YELLOW: setback is close to the minimum (within 5%)
                    new CheckResult
                    {
                        RuleId = "OGUC-2.6.3-distanciamiento",
                        ArticleReference = "Art. 2.6.3 / 2.6.4",
                        RuleName = "Distanciamiento a deslinde norte",
                        MeasuredValue = 2.1,
                        AllowedValue = 2.0,
                        Unit = "m",
                        Status = ComplianceStatus.Yellow,
                        SourceUrl = "https://www.bcn.cl/leychile/Navegar?idNorma=8201&idParte=100008867",
                        StatusMessage = "Setback distance (2.1 m) complies but is close to the minimum (2.0 m). Verify tolerances.",
                        DetailDescription = "Facade without openings. Distance measured from exterior wall face to nearest property boundary."

                    },

                    // RED: building pierces the rasante envelope
                    new CheckResult
                    {
                        RuleId = "OGUC-2.6.3-rasante",
                        ArticleReference = "Art. 2.6.3",
                        RuleName = "Rasante deslinde oriente",
                        MeasuredValue = 72.0,
                        AllowedValue = 70.0,
                        Unit = "\u00B0",  // degree symbol
                        Status = ComplianceStatus.Red,
                        SourceUrl = "https://www.bcn.cl/leychile/Navegar?idNorma=8201&idParte=100008867",
                        StatusMessage = "Building volume exceeds the rasante envelope on the east property boundary. Volumetric modification required.",
                        DetailDescription = "Rasante is measured from the property boundary line at a 70 degree angle. The building volume intersects the rasante plane at level 4."
                    }
                };

                // ---- LOAD RESULTS INTO THE PANEL ----

                // Get the panel instance we stored in PlanUpApp during startup
                // and send the results to it.
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
