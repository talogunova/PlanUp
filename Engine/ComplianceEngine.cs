using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PlanUp.Engine
{
    /// <summary>
    /// The brain of PlanUp. This class:
    /// 
    ///   1. Loads rule JSON files from the Rules folder
    ///   2. Validates that each rule has the required fields
    ///   3. (Step 4+) Calls geometry extractors based on what each rule needs
    ///   4. (Step 4+) Evaluates rules against extracted geometry
    ///   5. (Step 4+) Produces CheckResult objects for the UI
    /// 
    /// For Step 3, we only implement loading and validation.
    /// The evaluation logic comes in Steps 4 through 6.
    /// </summary>
    public class ComplianceEngine
    {
        /// <summary>
        /// All loaded rule definitions, keyed by rule_id.
        /// Using a Dictionary lets us look up a specific rule quickly
        /// without searching through a list.
        /// </summary>
        private Dictionary<string, RuleDefinition> _rules = new Dictionary<string, RuleDefinition>();

        /// <summary>
        /// Any errors encountered during loading, so we can report
        /// them to the user instead of failing silently.
        /// </summary>
        private List<string> _loadErrors = new List<string>();

        /// <summary>
        /// The folder path where rule JSON files are stored.
        /// </summary>
        private string _rulesFolder;

        /// <summary>
        /// Creates a new ComplianceEngine and immediately loads all
        /// rule files from the specified folder.
        /// 
        /// The constructor calls LoadRules automatically so that
        /// by the time you have an engine instance, the rules are
        /// already loaded and ready.
        /// </summary>
        /// <param name="rulesFolder">
        /// Full path to the folder containing rule JSON files.
        /// Typically this is the "Rules" subfolder next to the DLL.
        /// </param>
        public ComplianceEngine(string rulesFolder)
        {
            _rulesFolder = rulesFolder;
            LoadRules();
        }

        /// <summary>
        /// Returns how many rules were loaded successfully.
        /// </summary>
        public int RuleCount => _rules.Count;

        /// <summary>
        /// Returns a copy of the loaded rules for inspection.
        /// </summary>
        public IReadOnlyDictionary<string, RuleDefinition> Rules => _rules;

        /// <summary>
        /// Returns any errors that occurred during loading.
        /// Empty list means everything loaded cleanly.
        /// </summary>
        public IReadOnlyList<string> LoadErrors => _loadErrors;

        /// <summary>
        /// Returns true if all rules loaded without errors.
        /// </summary>
        public bool IsHealthy => _loadErrors.Count == 0 && _rules.Count > 0;

        /// <summary>
        /// Scans the rules folder for .json files and tries to load each one.
        /// 
        /// HOW IT WORKS:
        ///   1. Directory.GetFiles finds all .json files in the folder
        ///   2. For each file, File.ReadAllText reads the entire content as a string
        ///   3. JsonSerializer.Deserialize converts the JSON string into a RuleDefinition object
        ///   4. ValidateRule checks that the required fields are present
        ///   5. Valid rules go into the _rules dictionary, invalid ones generate error messages
        /// 
        /// If the folder does not exist, we report an error but do not crash.
        /// If a single file fails to parse, the other files still load.
        /// This resilience matters because a user might be editing rule files
        /// and save a file with a typo. The engine should still work with
        /// the valid rules rather than refusing to run entirely.
        /// </summary>
        private void LoadRules()
        {
            _rules.Clear();
            _loadErrors.Clear();

            // Check if the rules folder exists
            if (!Directory.Exists(_rulesFolder))
            {
                _loadErrors.Add($"Rules folder not found: {_rulesFolder}");
                return;
            }

            // Find all JSON files in the folder
            string[] ruleFiles = Directory.GetFiles(_rulesFolder, "*.json");

            if (ruleFiles.Length == 0)
            {
                _loadErrors.Add($"No rule files found in: {_rulesFolder}");
                return;
            }

            // Configure the JSON deserializer
            // PropertyNameCaseInsensitive means "rule_id" in JSON matches
            // "rule_id" in C# regardless of casing differences
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            foreach (string filePath in ruleFiles)
            {
                string fileName = Path.GetFileName(filePath);

                try
                {
                    // Read the file content
                    string jsonContent = File.ReadAllText(filePath);

                    // Deserialize JSON into a RuleDefinition object
                    RuleDefinition? rule = JsonSerializer.Deserialize<RuleDefinition>(jsonContent, options);

                    if (rule == null)
                    {
                        _loadErrors.Add($"{fileName}: failed to parse (returned null)");
                        continue;
                    }

                    // Validate the rule has required fields
                    List<string> validationErrors = ValidateRule(rule, fileName);

                    if (validationErrors.Count > 0)
                    {
                        _loadErrors.AddRange(validationErrors);
                        continue;
                    }

                    // Check for duplicate rule IDs
                    if (_rules.ContainsKey(rule.rule_id))
                    {
                        _loadErrors.Add($"{fileName}: duplicate rule_id '{rule.rule_id}' (already loaded from another file)");
                        continue;
                    }

                    // Rule is valid, add it to the dictionary
                    _rules[rule.rule_id] = rule;
                }
                catch (JsonException ex)
                {
                    // JSON syntax error (missing comma, unclosed bracket, etc.)
                    _loadErrors.Add($"{fileName}: JSON syntax error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Any other error (file permission, encoding, etc.)
                    _loadErrors.Add($"{fileName}: unexpected error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Checks that a parsed rule has all the fields the engine needs.
        /// Returns an empty list if valid, or a list of error messages if not.
        /// 
        /// This validation catches problems early (at load time) rather than
        /// during a compliance check when the user is waiting for results.
        /// </summary>
        private List<string> ValidateRule(RuleDefinition rule, string fileName)
        {
            List<string> errors = new List<string>();

            if (string.IsNullOrWhiteSpace(rule.rule_id))
                errors.Add($"{fileName}: missing 'rule_id'");

            if (string.IsNullOrWhiteSpace(rule.name))
                errors.Add($"{fileName}: missing 'name'");

            if (string.IsNullOrWhiteSpace(rule.article))
                errors.Add($"{fileName}: missing 'article'");

            if (rule.geometry_required == null || rule.geometry_required.Count == 0)
                errors.Add($"{fileName}: missing or empty 'geometry_required'");

            if (rule.evaluation == null || string.IsNullOrWhiteSpace(rule.evaluation.type))
                errors.Add($"{fileName}: missing 'evaluation.type'");

            return errors;
        }

        /// <summary>
        /// Returns a summary string describing what was loaded.
        /// Useful for showing in a TaskDialog during development.
        /// </summary>
        public string GetLoadSummary()
        {
            string summary = $"Loaded {_rules.Count} rule(s):\n";

            foreach (var rule in _rules.Values)
            {
                // Check how many parameters are still null (need user input)
                int missingParams = 0;
                int totalParams = 0;

                foreach (var param in rule.parameters.Values)
                {
                    totalParams++;
                    if (param.value == null) missingParams++;
                }

                string paramStatus = totalParams == 0
                    ? "no parameters needed"
                    : missingParams == 0
                        ? $"all {totalParams} parameter(s) set"
                        : $"{missingParams} of {totalParams} parameter(s) need input";

                summary += $"\n  [{rule.rule_id}]\n";
                summary += $"    {rule.name}\n";
                summary += $"    Article: {rule.article}\n";
                summary += $"    Evaluation type: {rule.evaluation.type}\n";
                summary += $"    Geometry needed: {string.Join(", ", rule.geometry_required)}\n";
                summary += $"    Parameters: {paramStatus}\n";
            }

            if (_loadErrors.Count > 0)
            {
                summary += $"\n{_loadErrors.Count} error(s):\n";
                foreach (string error in _loadErrors)
                {
                    summary += $"  - {error}\n";
                }
            }

            return summary;
        }
    }
}
