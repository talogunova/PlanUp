using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using PlanUp.UI;

namespace PlanUp
{
    /// <summary>
    /// Main entry point for the PlanUp plugin.
    /// Revit calls OnStartup when it loads the add-in, and OnShutdown when Revit closes.
    /// This class creates the PlanUp ribbon tab, registers buttons, and registers
    /// the dockable compliance panel.
    /// </summary>
    public class PlanUpApp : IExternalApplication
    {
        /// <summary>
        /// A unique GUID that identifies the PlanUp dockable pane.
        /// 
        /// WHY A GUID?
        /// Revit tracks dockable panes by a unique identifier, not by name.
        /// This prevents conflicts if two different plugins both create a pane
        /// called "Results". The GUID is permanent. Once you pick one, never change it,
        /// because Revit remembers pane positions and states using this ID.
        /// If you change it, users lose their layout preferences for this pane.
        /// </summary>
        public static readonly DockablePaneId CompliancePaneId =
            new DockablePaneId(new Guid("B1C2D3E4-F5A6-7890-BCDE-FA1234567890"));

        /// <summary>
        /// We keep a reference to the panel instance so that RunCheckCommand
        /// can access it to load results. Static so it is accessible from
        /// any command class without passing references around.
        /// </summary>
        public static CompliancePanel CompliancePanelInstance { get; private set; }

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
        /// Creates the ribbon tab, panel, buttons, and registers the dockable pane.
        /// </summary>
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // ---- RIBBON SETUP ----

                string tabName = "PlanUp";
                application.CreateRibbonTab(tabName);

                RibbonPanel compliancePanel = application.CreateRibbonPanel(tabName, "Compliance");

                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                PushButtonData runCheckData = new PushButtonData(
                    "PlanUp_RunCheck",
                    "Run\nCheck",
                    assemblyPath,
                    "PlanUp.Commands.RunCheckCommand"
                );
                runCheckData.ToolTip = "Run OGUC compliance checks against the current model";

                PushButton runCheckButton = compliancePanel.AddItem(runCheckData) as PushButton;

                // ---- DOCKABLE PANE REGISTRATION ----

                // Create the WPF panel instance.
                // This must happen during OnStartup because Revit only allows
                // dockable pane registration at startup, not later during commands.
                CompliancePanelInstance = new CompliancePanel();

                // Register the pane with Revit.
                // After this call, the pane exists in Revit's system but is not visible.
                // RunCheckCommand will call DockablePane.Show() to make it appear.
                application.RegisterDockablePane(
                    CompliancePaneId,                    // the unique GUID
                    "PlanUp Compliance",                 // the title shown on the pane tab
                    CompliancePanelInstance               // the WPF control to display
                );

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("PlanUp Error", $"Failed to initialize PlanUp:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Called by Revit when it shuts down.
        /// </summary>
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
