using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using PlanUp.UI;

namespace PlanUp
{
    public class PlanUpApp : IExternalApplication
    {
        public static readonly DockablePaneId CompliancePaneId =
            new DockablePaneId(new Guid("B1C2D3E4-F5A6-7890-BCDE-FA1234567890"));

        public static CompliancePanel CompliancePanelInstance { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                string tabName = "PlanUp";
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                application.CreateRibbonTab(tabName);

                // ---- COMPLIANCE PANEL (main buttons) ----

                RibbonPanel compliancePanel = application.CreateRibbonPanel(tabName, "Compliance");

                // Run Check button
                PushButtonData runCheckData = new PushButtonData(
                    "PlanUp_RunCheck",
                    "Run\nCheck",
                    assemblyPath,
                    "PlanUp.Commands.RunCheckCommand");
                runCheckData.ToolTip = "Run OGUC compliance checks against the current model";
                runCheckData.LargeImage = LoadIcon("icon_run_check.png");
                compliancePanel.AddItem(runCheckData);

                // Report button
                PushButtonData reportData = new PushButtonData(
                    "PlanUp_Report",
                    "Report",
                    assemblyPath,
                    "PlanUp.Commands.GenerateReportCommand");
                reportData.ToolTip = "Generate a compliance report";
                reportData.LargeImage = LoadIcon("icon_report.png");
                compliancePanel.AddItem(reportData);

                // ---- TOOLS PANEL (settings and rules) ----

                RibbonPanel toolsPanel = application.CreateRibbonPanel(tabName, "Tools");

                // Settings button
                PushButtonData settingsData = new PushButtonData(
                    "PlanUp_Settings",
                    "Settings",
                    assemblyPath,
                    "PlanUp.Commands.OpenSettingsCommand");
                settingsData.ToolTip = "Configure project zone, PRC parameters, and visualization options";
                settingsData.LargeImage = LoadIcon("icon_settings.png");
                toolsPanel.AddItem(settingsData);

                // Rules button
                PushButtonData rulesData = new PushButtonData(
                    "PlanUp_Rules",
                    "Rules",
                    assemblyPath,
                    "PlanUp.Commands.ManageRulesCommand");
                rulesData.ToolTip = "Browse and manage compliance rule sets";
                rulesData.LargeImage = LoadIcon("icon_rules.png");
                toolsPanel.AddItem(rulesData);

                // ---- DOCKABLE PANE REGISTRATION ----

                CompliancePanelInstance = new CompliancePanel();

                application.RegisterDockablePane(
                    CompliancePaneId,
                    "PlanUp Compliance",
                    CompliancePanelInstance);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("PlanUp Error", $"Failed to initialize PlanUp:\n{ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Loads an icon from the Resources folder using a pack URI.
        /// The icon must be included as a Resource in the .csproj file.
        /// </summary>
        private static BitmapImage LoadIcon(string filename)
        {
            try
            {
                Uri uri = new Uri(
                    $"pack://application:,,,/PlanUp;component/Resources/{filename}");
                BitmapImage img = new BitmapImage(uri);
                return img;
            }
            catch
            {
                return null;
            }
        }
    }
}
