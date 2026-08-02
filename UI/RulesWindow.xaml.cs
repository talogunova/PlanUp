using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using PlanUp.Engine;

namespace PlanUp.UI
{
    /// <summary>
    /// Rules dialog showing loaded compliance rules with editable
    /// user preferences: enable/disable, warning buffer, safety margin, notes.
    /// 
    /// Rule metadata (name, article, jurisdiction, version) is read-only.
    /// The four user-configurable fields persist to the JSON files.
    /// </summary>
    public partial class RulesWindow : Window
    {
        private string _rulesFolder;
        private List<RuleViewModel> _viewModels = new List<RuleViewModel>();

        public RulesWindow(string rulesFolder)
        {
            InitializeComponent();
            _rulesFolder = rulesFolder;
            LoadRules();
        }

        private void LoadRules()
        {
            _viewModels.Clear();

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            string[] ruleFiles = Directory.GetFiles(_rulesFolder, "*.json");

            foreach (string filePath in ruleFiles)
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    RuleDefinition rule = JsonSerializer.Deserialize<RuleDefinition>(json, options);
                    if (rule == null) continue;

                    _viewModels.Add(new RuleViewModel
                    {
                        FilePath = filePath,
                        RuleId = rule.rule_id,
                        Name = rule.name,
                        Article = rule.article,
                        Jurisdiction = rule.jurisdiction,
                        Version = rule.version,
                        Unit = rule.evaluation?.unit ?? "m",
                        Enabled = rule.enabled,
                        WarningBuffer = rule.warning_buffer,
                        SafetyMargin = rule.safety_margin_percent,
                        Notes = rule.notes
                    });
                }
                catch { continue; }
            }

            RulesList.ItemsSource = _viewModels;
        }

        /// <summary>
        /// Saves the user-configurable fields back to each rule JSON file.
        /// Only modifies: enabled, warning_buffer, safety_margin_percent, notes.
        /// All other fields are preserved as-is.
        /// </summary>
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
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

                foreach (RuleViewModel vm in _viewModels)
                {
                    string json = File.ReadAllText(vm.FilePath);
                    RuleDefinition rule = JsonSerializer.Deserialize<RuleDefinition>(json, readOptions);
                    if (rule == null) continue;

                    rule.enabled = vm.Enabled;
                    rule.warning_buffer = vm.WarningBuffer;
                    rule.safety_margin_percent = vm.SafetyMargin;
                    rule.notes = vm.Notes;

                    string updatedJson = JsonSerializer.Serialize(rule, writeOptions);
                    File.WriteAllText(vm.FilePath, updatedJson);
                }

                MessageBox.Show("Rule preferences saved. Click Run Check to apply.",
                    "PlanUp Rules", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}",
                    "PlanUp Rules", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// View model for a single rule in the Rules dialog.
    /// Separates display logic from the RuleDefinition data class.
    /// 
    /// INotifyPropertyChanged makes WPF update the UI automatically
    /// when a property value changes (for example, when the user
    /// types in a text box).
    /// </summary>
    public class RuleViewModel : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";
        public string RuleId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Article { get; set; } = "";
        public string Jurisdiction { get; set; } = "";
        public string Version { get; set; } = "";
        public string Unit { get; set; } = "m";

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
        }

        private double _warningBuffer = 1.0;
        public double WarningBuffer
        {
            get => _warningBuffer;
            set { _warningBuffer = value; OnPropertyChanged(nameof(WarningBuffer)); }
        }

        private double _safetyMargin = 0.0;
        public double SafetyMargin
        {
            get => _safetyMargin;
            set { _safetyMargin = value; OnPropertyChanged(nameof(SafetyMargin)); }
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(nameof(Notes)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
