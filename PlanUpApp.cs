using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace PlanUp
{
    /// <summary>
    /// Main entry point for the PlanUp plugin.
    /// Revit calls OnStartup when it loads the add-in, and OnShutdown when Revit closes.
    /// This class creates the PlanUp ribbon tab and registers buttons.
    /// </summary>
    public class PlanUpApp : IExternalApplication
    {
        // Store the path to the folder where this DLL lives.
        // We use this to find the icon files and rule JSON files later.
        private static string AssemblyDirectory
        {
            get
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                return Path.GetDirectoryName(assemblyPath) ?? "";
            }
        }

        /// <summary>
        /// Called by Revit when the add-in is loaded at startup.
        /// Creates the ribbon tab, panel, and buttons.
        /// </summary>
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Step 1: Create a new tab in the Revit ribbon called "PlanUp"
                string tabName = "PlanUp";
                application.CreateRibbonTab(tabName);

                // Step 2: Create a panel inside that tab called "Compliance"
                RibbonPanel compliancePanel = application.CreateRibbonPanel(tabName, "Compliance");

                // Step 3: Create the "Run Check" button
                // PushButtonData takes four arguments:
                //   - internalName: a unique ID Revit uses internally (not shown to user)
                //   - displayText: what the user sees on the button
                //   - assemblyPath: the full path to the DLL containing the command class
                //   - className: the full namespace.class of the IExternalCommand to run
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData runCheckData = new PushButtonData(
                    "PlanUp_RunCheck",                          // internal name
                    "Run\nCheck",                                // display text (\n puts "Check" on second line)
                    assemblyPath,                                // path to this DLL
                    "PlanUp.Commands.RunCheckCommand"            // the command class to execute
                );

                // Set the tooltip that appears when hovering over the button
                runCheckData.ToolTip = "Run OGUC compliance checks against the current model";

                // Add the button to the panel
                // The 'as PushButton' cast gives us the actual button object
                // so we could set an icon on it later
                PushButton runCheckButton = compliancePanel.AddItem(runCheckData) as PushButton;

                // Icon will be added later when we have one ready.
                // To add an icon, you would do:
                // runCheckButton.LargeImage = new BitmapImage(new Uri(Path.Combine(AssemblyDirectory, "planup-icon-32.png")));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // If anything goes wrong during startup, show the error
                // so we know what happened instead of the plugin silently failing
                TaskDialog.Show("PlanUp Error", $"Failed to initialize PlanUp:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Called by Revit when it shuts down.
        /// We do not need to clean up anything yet, but this method is required
        /// by the IExternalApplication interface.
        /// </summary>
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
