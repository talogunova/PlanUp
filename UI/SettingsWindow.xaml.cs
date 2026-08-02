using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PlanUp.Engine;

namespace PlanUp.UI
{
    /// <summary>
    /// Settings dialog where the architect picks their project's comuna and zone.
    /// When a zone is selected, the PRC parameters auto-fill and can be applied
    /// to the rule JSON files so the next Run Check uses the correct limits.
    /// 
    /// HOW IT WORKS:
    /// 
    ///   1. The dialog has a dictionary of zone data (hardcoded for prototype)
    ///   2. When the user picks a comuna, the zone dropdown populates
    ///   3. When the user picks a zone, the parameter fields fill automatically
    ///   4. Clicking "Apply" writes the values to the rule JSON files
    ///   5. The dialog stays open so the architect can review before closing
    /// 
    /// For the marketplace version, step 1 would read from a database or API
    /// instead of a hardcoded dictionary.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>
        /// Path to the Rules folder where JSON files are stored.
        /// Passed in from the command that opens this window.
        /// </summary>
        private string _rulesFolder;

        /// <summary>
        /// Zone parameter data, organized by comuna then zone code.
        /// Each zone has: max height, setback con vano, setback sin vano, rasante angle.
        /// 
        /// For the prototype, this is hardcoded for Providencia.
        /// For the marketplace, this becomes a database lookup.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, ZoneParams>> ComunaData =
            new Dictionary<string, Dictionary<string, ZoneParams>>
            {
                {
                    "Providencia", new Dictionary<string, ZoneParams>
                    {
                        {
                            "EA12", new ZoneParams
                            {
                                MaxHeightM = 42.0,
                                SetbackConVanoM = 10.5,
                                SetbackSinVanoM = 5.25,
                                RasanteAngle = 70.0,
                                Description = "Edificacion Aislada, max 12 pisos"
                            }
                        },
                        {
                            "EA7", new ZoneParams
                            {
                                MaxHeightM = 24.5,
                                SetbackConVanoM = 6.0,
                                SetbackSinVanoM = 3.0,
                                RasanteAngle = 70.0,
                                Description = "Edificacion Aislada, max 7 pisos"
                            }
                        },
                        {
                            "EA5", new ZoneParams
                            {
                                MaxHeightM = 17.5,
                                SetbackConVanoM = 4.5,
                                SetbackSinVanoM = 2.25,
                                RasanteAngle = 70.0,
                                Description = "Edificacion Aislada, max 5 pisos"
                            }
                        }
                    }
                }
            };

        /// <summary>
        /// The currently selected zone parameters.
        /// Null until a zone is selected.
        /// </summary>
        private ZoneParams _selectedParams = null;

        public SettingsWindow(string rulesFolder)
        {
            InitializeComponent();
            _rulesFolder = rulesFolder;

            // Populate the comuna dropdown
            foreach (string comuna in ComunaData.Keys)
            {
                ComunaCombo.Items.Add(comuna);
            }
        }

        /// <summary>
        /// When a comuna is selected, populate the zone dropdown
        /// with the available zones for that comuna.
        /// </summary>
        private void ComunaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ZoneCombo.Items.Clear();
            _selectedParams = null;
            ApplyButton.IsEnabled = false;
            ClearParameterDisplay();

            string comuna = ComunaCombo.SelectedItem as string;
            if (comuna == null) return;

            if (ComunaData.ContainsKey(comuna))
            {
                foreach (string zone in ComunaData[comuna].Keys)
                {
                    ZoneCombo.Items.Add(zone);
                }
            }
        }

        /// <summary>
        /// When a zone is selected, fill in the parameter values.
        /// </summary>
        private void ZoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedParams = null;
            ApplyButton.IsEnabled = false;
            ClearParameterDisplay();
            StatusText.Text = "";

            string comuna = ComunaCombo.SelectedItem as string;
            string zone = ZoneCombo.SelectedItem as string;
            if (comuna == null || zone == null) return;

            if (ComunaData.ContainsKey(comuna) && ComunaData[comuna].ContainsKey(zone))
            {
                _selectedParams = ComunaData[comuna][zone];

                HeightValue.Text = _selectedParams.MaxHeightM.ToString("F1");
                ConVanoValue.Text = _selectedParams.SetbackConVanoM.ToString("F1");
                SinVanoValue.Text = _selectedParams.SetbackSinVanoM.ToString("F1");
                RasanteValue.Text = _selectedParams.RasanteAngle.ToString("F0");

                ApplyButton.IsEnabled = true;
                StatusText.Text = _selectedParams.Description;
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(127, 140, 141)); // grey
            }
        }

        /// <summary>
        /// Writes the selected zone parameters to the rule JSON files.
        /// 
        /// HOW THIS WORKS:
        /// 
        /// Each rule JSON file has a "parameters" section with values
        /// that can be null or filled in. This method:
        ///   1. Reads each JSON file
        ///   2. Deserializes it into a RuleDefinition object
        ///   3. Updates the parameter values from the selected zone
        ///   4. Serializes it back to JSON
        ///   5. Writes the file
        /// 
        /// The mapping between zone parameters and rule parameters:
        ///   - max_height_m -> altura rule
        ///   - min_distance_con_vano_m -> distanciamiento rule
        ///   - min_distance_sin_vano_m -> distanciamiento rule
        ///   - rasante_angle_degrees -> rasante rule
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedParams == null) return;

            try
            {
                JsonSerializerOptions readOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                JsonSerializerOptions writeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string[] ruleFiles = Directory.GetFiles(_rulesFolder, "*.json");

                foreach (string filePath in ruleFiles)
                {
                    string json = File.ReadAllText(filePath);
                    RuleDefinition rule = JsonSerializer.Deserialize<RuleDefinition>(json, readOptions);
                    if (rule == null) continue;

                    bool changed = false;

                    // Update parameters based on what this rule needs
                    if (rule.parameters.ContainsKey("max_height_m"))
                    {
                        rule.parameters["max_height_m"].value = _selectedParams.MaxHeightM;
                        changed = true;
                    }
                    if (rule.parameters.ContainsKey("min_distance_con_vano_m"))
                    {
                        rule.parameters["min_distance_con_vano_m"].value = _selectedParams.SetbackConVanoM;
                        changed = true;
                    }
                    if (rule.parameters.ContainsKey("min_distance_sin_vano_m"))
                    {
                        rule.parameters["min_distance_sin_vano_m"].value = _selectedParams.SetbackSinVanoM;
                        changed = true;
                    }
                    if (rule.parameters.ContainsKey("rasante_angle_degrees"))
                    {
                        rule.parameters["rasante_angle_degrees"].value = _selectedParams.RasanteAngle;
                        changed = true;
                    }

                    if (changed)
                    {
                        string updatedJson = JsonSerializer.Serialize(rule, writeOptions);
                        File.WriteAllText(filePath, updatedJson);
                    }
                }

                StatusText.Text = "Parameters saved. Click Run Check to apply.";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(39, 174, 96)); // green
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error saving: {ex.Message}";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(231, 76, 60)); // red
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearParameterDisplay()
        {
            HeightValue.Text = "--";
            ConVanoValue.Text = "--";
            SinVanoValue.Text = "--";
            RasanteValue.Text = "--";
        }
    }

    /// <summary>
    /// Holds the PRC parameters for a specific zone.
    /// </summary>
    public class ZoneParams
    {
        public double MaxHeightM { get; set; }
        public double SetbackConVanoM { get; set; }
        public double SetbackSinVanoM { get; set; }
        public double RasanteAngle { get; set; }
        public string Description { get; set; } = "";
    }
}
