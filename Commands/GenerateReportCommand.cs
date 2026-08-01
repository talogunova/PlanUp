using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlanUp.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateReportCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            TaskDialog.Show("PlanUp Report",
                "Report generation coming soon.\n\nThis will export:\n" +
                "- Compliance summary PDF\n" +
                "- Detailed check results\n" +
                "- Violation screenshots");
            return Result.Succeeded;
        }
    }
}
