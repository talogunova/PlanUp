using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PlanUp.Commands
{
    /// <summary>
    /// The command that runs when the user clicks "Run Check" on the ribbon.
    /// 
    /// The [Transaction(TransactionMode.Manual)] attribute tells Revit that
    /// this command will manage its own transactions. We need this because
    /// later steps will modify the model (for example, adding color overrides
    /// to highlight violations). For now, we are only reading, so no
    /// transaction is started yet.
    /// 
    /// The [Regeneration(RegenerationOption.Manual)] attribute tells Revit
    /// we will call doc.Regenerate() ourselves if needed, rather than having
    /// Revit do it automatically after every change.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunCheckCommand : IExternalCommand
    {
        /// <summary>
        /// Execute is called by Revit when the button is clicked.
        /// 
        /// Parameters:
        ///   commandData - gives us access to the Revit application and document
        ///   message     - if we return Failed, this message is shown to the user
        ///   elements    - if we return Failed, these elements are highlighted
        /// 
        /// Returns:
        ///   Result.Succeeded - command completed normally
        ///   Result.Failed    - command encountered an error
        ///   Result.Cancelled - user cancelled the operation
        /// </summary>
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // Get the active document (the currently open Revit project)
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                // For Step 1, just show a dialog confirming the plugin is working.
                // This will be replaced by the actual compliance engine in Step 4.
                TaskDialog dialog = new TaskDialog("PlanUp Compliance Engine");
                dialog.MainInstruction = "PlanUp is running";
                dialog.MainContent = $"Project: {doc.Title}\n"
                    + $"Active view: {doc.ActiveView.Name}\n"
                    + "\nThe compliance engine will be connected in the next step.";
                dialog.CommonButtons = TaskDialogCommonButtons.Ok;
                dialog.Show();

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
