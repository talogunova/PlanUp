using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlanUp.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenSettingsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            TaskDialog.Show("PlanUp Settings",
                "Settings panel coming soon.\n\nHere you will configure:\n" +
                "- Project zone (comuna and zone code)\n" +
                "- PRC parameter overrides\n" +
                "- Rasante visualization options");
            return Result.Succeeded;
        }
    }
}
