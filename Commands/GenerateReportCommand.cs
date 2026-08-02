using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using PlanUp.Engine;
using PlanUp.Reports;

namespace PlanUp.Commands
{
    /// <summary>
    /// Generates a PDF compliance report from the current model.
    /// Shows a save dialog so the user picks the location and page size.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateReportCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                Document doc = uiApp.ActiveUIDocument.Document;

                // Show save file dialog
                SaveFileDialog saveDialog = new SaveFileDialog();
                saveDialog.Title = "Save Compliance Report";
                saveDialog.Filter = "PDF A4|*.pdf|PDF Letter|*.pdf";
                saveDialog.FilterIndex = 1;
                saveDialog.FileName = $"PlanUp_Compliance_{DateTime.Now:yyyy-MM-dd}";
                saveDialog.DefaultExt = ".pdf";

                bool? dialogResult = saveDialog.ShowDialog();
                if (dialogResult != true) return Result.Cancelled;

                string filePath = saveDialog.FileName;
                bool isA4 = saveDialog.FilterIndex == 1;

                // Clear previous rasante shapes
                using (Transaction cleanTx = new Transaction(doc, "PlanUp Clear Rasante"))
                {
                    cleanTx.Start();
                    Extractors.RasanteVisualizer.ClearPreviousRasante(doc);
                    cleanTx.Commit();
                }

                // Load rules and run checks
                string assemblyDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? "";
                string rulesFolder = Path.Combine(assemblyDir, "Rules");

                ComplianceEngine engine = new ComplianceEngine(rulesFolder);
                List<CheckResult> results = engine.RunChecks(doc);

                // Generate the PDF
                string projectName = doc.Title ?? "Untitled Project";
                string comunaZone = "Providencia / EA12";

                ReportGenerator.Generate(results, projectName, comunaZone, filePath, isA4);

                // Open the PDF
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });

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
