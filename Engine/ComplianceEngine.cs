using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using PlanUp.Extractors;

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

        /// <summary>
        /// Runs all loaded compliance checks against a Revit document.
        /// 
        /// This is the main method that connects everything:
        ///   1. Iterates through each loaded rule
        ///   2. Based on the evaluation type, calls the right geometry extractor
        ///   3. Compares the extracted measurements against the rule's thresholds
        ///   4. Produces a CheckResult for each rule
        /// 
        /// For Step 4, only "max_threshold" (altura) uses real geometry.
        /// Other evaluation types still produce dummy results.
        /// Steps 5 and 6 will add real extractors for distanciamiento and rasante.
        /// </summary>
        /// <param name="doc">The active Revit document to check.</param>
        /// <returns>A list of CheckResult objects, one per rule.</returns>
        public List<CheckResult> RunChecks(Document doc)
        {
            List<CheckResult> results = new List<CheckResult>();

            foreach (var rule in _rules.Values)
            {
                CheckResult result;

                switch (rule.evaluation.type)
                {
                    case "max_threshold":
                        result = RunMaxThresholdCheck(rule, doc);
                        break;

                    case "min_threshold_per_face":
                        result = RunSetbackCheck(rule, doc);
                        break;

                    case "envelope_intersection":
                        result = RunRasanteCheck(rule, doc);
                        break;

                    default:
                        result = CreateDummyResult(rule, ComplianceStatus.Yellow,
                            0, 0, "",
                            $"Unknown evaluation type: {rule.evaluation.type}");
                        break;
                }

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Runs the altura check: extracts the building height from the model
        /// and compares it against the maximum allowed by the rule.
        /// 
        /// This is the first check that uses REAL geometry from the Revit model.
        /// </summary>
        private CheckResult RunMaxThresholdCheck(RuleDefinition rule, Document doc)
        {
            // Step 1: Get the limit parameter value
            string limitParamName = rule.evaluation.limit_param;
            double? limitValue = null;

            if (rule.parameters.ContainsKey(limitParamName))
            {
                limitValue = rule.parameters[limitParamName].value;
            }

            // If the limit is not set, we cannot evaluate
            if (limitValue == null)
            {
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    SourceUrl = rule.source_url,
                    Status = ComplianceStatus.Yellow,
                    StatusMessage = $"Cannot evaluate: parameter '{limitParamName}' is not set. Check the rule definition.",
                    DetailDescription = rule.description
                };
            }

            // Step 2: Extract geometry from the model
            BuildingHeightResult heightResult = BuildingHeightExtractor.Extract(doc);

            if (!heightResult.IsValid)
            {
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    SourceUrl = rule.source_url,
                    Status = ComplianceStatus.Yellow,
                    StatusMessage = $"Could not extract geometry: {heightResult.ErrorMessage}",
                    DetailDescription = rule.description
                };
            }

            // Step 3: Compare measured height against the limit
            double measured = heightResult.Height;
            double limit = limitValue.Value;
            string unit = rule.evaluation.unit;

            ComplianceStatus status;
            string statusMessage;

            if (measured > limit)
            {
                status = ComplianceStatus.Red;
                statusMessage = rule.messages.red
                    .Replace("{measured}", measured.ToString("F1"))
                    .Replace("{limit}", limit.ToString("F1"));
            }
            else if (measured > limit - 1.0)
            {
                status = ComplianceStatus.Yellow;
                statusMessage = rule.messages.yellow
                    .Replace("{measured}", measured.ToString("F1"))
                    .Replace("{limit}", limit.ToString("F1"));
            }
            else
            {
                status = ComplianceStatus.Green;
                statusMessage = rule.messages.green
                    .Replace("{measured}", measured.ToString("F1"))
                    .Replace("{limit}", limit.ToString("F1"));
            }

            return new CheckResult
            {
                RuleId = rule.rule_id,
                ArticleReference = rule.article,
                RuleName = rule.name,
                MeasuredValue = measured,
                AllowedValue = limit,
                Unit = unit,
                Status = status,
                SourceUrl = rule.source_url,
                StatusMessage = statusMessage,
                DetailDescription = $"{rule.description}\n\nAnalyzed {heightResult.ElementCount} elements. "
                    + $"Highest point at {heightResult.MaxElevation} m, ground level at {heightResult.GroundLevel} m."
            };
        }

        /// <summary>
        /// Creates a dummy CheckResult for rules that do not have real
        /// extractors yet. Will be removed as Steps 5 and 6 are completed.
        /// </summary>
        private CheckResult CreateDummyResult(RuleDefinition rule,
            ComplianceStatus status, double measured, double allowed,
            string unit, string statusMessage)
        {
            return new CheckResult
            {
                RuleId = rule.rule_id,
                ArticleReference = rule.article,
                RuleName = rule.name,
                MeasuredValue = measured,
                AllowedValue = allowed,
                Unit = unit,
                Status = status,
                SourceUrl = rule.source_url,
                StatusMessage = statusMessage,
                DetailDescription = rule.description
            };
        }

        /// <summary>
        /// Runs the distanciamiento check: measures the distance from each
        /// wall to the nearest property boundary and compares against the
        /// minimum required by OGUC.
        /// 
        /// This check can produce multiple results if there are violations
        /// on different walls, but for the panel we report the most critical
        /// one (the wall closest to a property line).
        /// </summary>
        private CheckResult RunSetbackCheck(RuleDefinition rule, Document doc)
        {
            // Step 1: Get the limit parameters
            double? limitConVano = null;
            double? limitSinVano = null;

            string conVanoParam = rule.evaluation.limit_param_con_vano;
            string sinVanoParam = rule.evaluation.limit_param_sin_vano;

            if (!string.IsNullOrEmpty(conVanoParam) && rule.parameters.ContainsKey(conVanoParam))
                limitConVano = rule.parameters[conVanoParam].value;

            if (!string.IsNullOrEmpty(sinVanoParam) && rule.parameters.ContainsKey(sinVanoParam))
                limitSinVano = rule.parameters[sinVanoParam].value;

            if (limitConVano == null && limitSinVano == null)
            {
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    SourceUrl = rule.source_url,
                    Status = ComplianceStatus.Yellow,
                    StatusMessage = "Cannot evaluate: setback parameters are not set.",
                    DetailDescription = rule.description
                };
            }

            // Step 2: Extract geometry
            SetbackResult setbackResult = SetbackExtractor.Extract(doc);

            if (!setbackResult.IsValid)
            {
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    SourceUrl = rule.source_url,
                    Status = ComplianceStatus.Yellow,
                    StatusMessage = setbackResult.ErrorMessage,
                    DetailDescription = rule.description
                };
            }

            // Step 3: Find the most critical wall (smallest setback)
            // Check con vano and sin vano separately
            WallSetback criticalConVano = setbackResult.GetCriticalConVano();
            WallSetback criticalSinVano = setbackResult.GetCriticalSinVano();

            // Determine the overall worst case
            ComplianceStatus worstStatus = ComplianceStatus.Green;
            string worstMessage = "";
            double reportedMeasured = 0;
            double reportedLimit = 0;
            string detailLines = $"{rule.description}\n\nAnalyzed {setbackResult.WallSetbacks.Count} walls against {setbackResult.PropertyBoundarySegments} property boundary segments.\n";

            // Check con vano
            if (criticalConVano != null && limitConVano != null)
            {
                double measured = criticalConVano.DistanceToPropertyLine;
                double limit = limitConVano.Value;
                ComplianceStatus status = EvaluateSetback(measured, limit);

                detailLines += $"\nCritical wall with openings (con vano): {measured:F1} m to {criticalConVano.NearestBoundaryName} (min: {limit:F1} m)";

                if (status > worstStatus)
                {
                    worstStatus = status;
                    reportedMeasured = measured;
                    reportedLimit = limit;
                    worstMessage = GetSetbackMessage(rule, status, measured, limit, criticalConVano.NearestBoundaryName);
                }
            }

            // Check sin vano
            if (criticalSinVano != null && limitSinVano != null)
            {
                double measured = criticalSinVano.DistanceToPropertyLine;
                double limit = limitSinVano.Value;
                ComplianceStatus status = EvaluateSetback(measured, limit);

                detailLines += $"\nCritical solid wall (sin vano): {measured:F1} m to {criticalSinVano.NearestBoundaryName} (min: {limit:F1} m)";

                if (status > worstStatus)
                {
                    worstStatus = status;
                    reportedMeasured = measured;
                    reportedLimit = limit;
                    worstMessage = GetSetbackMessage(rule, status, measured, limit, criticalSinVano.NearestBoundaryName);
                }
            }

            // If everything passed and we have no worst message, set green
            if (worstStatus == ComplianceStatus.Green)
            {
                reportedMeasured = criticalConVano != null ? criticalConVano.DistanceToPropertyLine :
                    (criticalSinVano != null ? criticalSinVano.DistanceToPropertyLine : 0);
                reportedLimit = limitConVano ?? limitSinVano ?? 0;
                worstMessage = rule.messages.green
                    .Replace("{measured}", reportedMeasured.ToString("F1"))
                    .Replace("{limit}", reportedLimit.ToString("F1"))
                    .Replace("{boundary_name}", "all boundaries");
            }

            return new CheckResult
            {
                RuleId = rule.rule_id,
                ArticleReference = rule.article,
                RuleName = rule.name,
                MeasuredValue = reportedMeasured,
                AllowedValue = reportedLimit,
                Unit = rule.evaluation.unit,
                Status = worstStatus,
                SourceUrl = rule.source_url,
                StatusMessage = worstMessage,
                DetailDescription = detailLines
            };
        }

        /// <summary>
        /// Evaluates a single setback distance against its limit.
        /// Uses a 0.5 m buffer for the warning zone.
        /// </summary>
        private ComplianceStatus EvaluateSetback(double measured, double limit)
        {
            if (measured < limit)
                return ComplianceStatus.Red;
            else if (measured < limit + 0.5)
                return ComplianceStatus.Yellow;
            else
                return ComplianceStatus.Green;
        }

        /// <summary>
        /// Builds the status message for a setback check result.
        /// </summary>
        private string GetSetbackMessage(RuleDefinition rule, ComplianceStatus status,
            double measured, double limit, string boundaryName)
        {
            string template;
            switch (status)
            {
                case ComplianceStatus.Red:
                    template = rule.messages.red;
                    break;
                case ComplianceStatus.Yellow:
                    template = rule.messages.yellow;
                    break;
                default:
                    template = rule.messages.green;
                    break;
            }

            return template
                .Replace("{measured}", measured.ToString("F1"))
                .Replace("{limit}", limit.ToString("F1"))
                .Replace("{boundary_name}", boundaryName);
        }

        /// <summary>
        /// Runs the rasante check: samples high points of the building and
        /// checks if any exceed the inclined plane from property boundaries.
        /// </summary>
        private CheckResult RunRasanteCheck(RuleDefinition rule, Document doc)
        {
            // Get parameters from the rule
            double angleDegrees = 70.0;
            double baseHeightM = 0.0;

            string angleParam = rule.evaluation.angle_param;
            string baseParam = rule.evaluation.base_height_param;

            if (!string.IsNullOrEmpty(angleParam) && rule.parameters.ContainsKey(angleParam))
            {
                double? val = rule.parameters[angleParam].value;
                if (val != null) angleDegrees = val.Value;
            }

            if (!string.IsNullOrEmpty(baseParam) && rule.parameters.ContainsKey(baseParam))
            {
                double? val = rule.parameters[baseParam].value;
                if (val != null) baseHeightM = val.Value;
            }

            // Run the extractor
            RasanteResult rasanteResult = RasanteExtractor.Extract(doc, angleDegrees, baseHeightM);

            if (!rasanteResult.IsValid)
            {
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    SourceUrl = rule.source_url,
                    Status = ComplianceStatus.Yellow,
                    StatusMessage = rasanteResult.ErrorMessage,
                    DetailDescription = rule.description
                };
            }

            string detailLines = $"{rule.description}\n\nChecked {rasanteResult.TotalPointsChecked} points against {rasanteResult.PropertyBoundarySegments} boundary segments at {angleDegrees}\u00B0.";

            if (!rasanteResult.HasViolations)
            {
                // All clear
                return new CheckResult
                {
                    RuleId = rule.rule_id,
                    ArticleReference = rule.article,
                    RuleName = rule.name,
                    MeasuredValue = angleDegrees,
                    AllowedValue = angleDegrees,
                    Unit = "\u00B0",
                    Status = ComplianceStatus.Green,
                    SourceUrl = rule.source_url,
                    StatusMessage = rule.messages.green,
                    DetailDescription = detailLines
                };
            }

            // There are violations. Report the worst one.
            RasanteViolation worst = rasanteResult.GetWorstViolation();

            detailLines += $"\n\n{rasanteResult.Violations.Count} violation(s) found.";
            detailLines += $"\nWorst: {worst.ElementName} exceeds rasante by {worst.ExcessM:F2} m";
            detailLines += $" at {worst.DistanceToBoundaryM:F1} m from {worst.BoundaryName}.";
            detailLines += $"\nPoint height: {worst.PointHeightM:F1} m, max allowed: {worst.MaxAllowedHeightM:F1} m.";

            // Determine if it is a warning (close) or a fail
            ComplianceStatus status = worst.ExcessM > 0.5
                ? ComplianceStatus.Red
                : ComplianceStatus.Yellow;

            string statusMessage;
            if (status == ComplianceStatus.Red)
            {
                statusMessage = rule.messages.red
                    .Replace("{boundary_name}", worst.BoundaryName)
                    .Replace("{distance}", worst.ExcessM.ToString("F1"))
                    .Replace("{level}", $"{worst.PointHeightM:F1} m");
            }
            else
            {
                statusMessage = rule.messages.yellow
                    .Replace("{boundary_name}", worst.BoundaryName)
                    .Replace("{distance}", worst.ExcessM.ToString("F2"))
                    .Replace("{level}", $"{worst.PointHeightM:F1} m");
            }

            return new CheckResult
            {
                RuleId = rule.rule_id,
                ArticleReference = rule.article,
                RuleName = rule.name,
                MeasuredValue = worst.PointHeightM,
                AllowedValue = worst.MaxAllowedHeightM,
                Unit = "m",
                Status = status,
                SourceUrl = rule.source_url,
                StatusMessage = statusMessage,
                DetailDescription = detailLines
            };
        }
    }
}
