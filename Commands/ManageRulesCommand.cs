using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlanUp.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ManageRulesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            TaskDialog.Show("PlanUp Rules",
                "Rule manager coming soon.\n\nThis will allow:\n" +
                "- Browse available rule sets\n" +
                "- Import rules for new jurisdictions\n" +
                "- Edit PRC parameters per zone");
            return Result.Succeeded;
        }
    }
}
