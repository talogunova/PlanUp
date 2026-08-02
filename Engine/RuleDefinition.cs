using System.Collections.Generic;

namespace PlanUp.Engine
{
    /// <summary>
    /// C# representation of a rule JSON file.
    /// 
    /// When the engine reads a JSON file using System.Text.Json, it maps
    /// each JSON field to a property in this class. The property names
    /// use C# naming convention (PascalCase) and the JsonPropertyName
    /// attribute maps them to the JSON field names (snake_case).
    /// 
    /// This class IS the schema. If a JSON file has a field that does not
    /// exist here, it gets ignored. If a JSON file is missing a required
    /// field, it gets set to null or its default value, and the engine
    /// can detect that during validation.
    /// </summary>
    public class RuleDefinition
    {
        /// <summary>
        /// Unique identifier for this rule.
        /// Example: "OGUC-1.1.2-altura"
        /// </summary>
        public string rule_id { get; set; } = "";

        /// <summary>
        /// Which country/regulation system this rule belongs to.
        /// Example: "CL-OGUC" for Chile's OGUC
        /// This is how the marketplace knows which jurisdiction a rule covers.
        /// </summary>
        public string jurisdiction { get; set; } = "";

        /// <summary>
        /// The article reference for display and linking.
        /// Example: "Art. 2.6.3"
        /// </summary>
        public string article { get; set; } = "";

        /// <summary>
        /// URL to the official source text of the regulation.
        /// </summary>
        public string source_url { get; set; } = "";

        /// <summary>
        /// Human readable name of the check.
        /// Example: "Altura maxima de edificacion"
        /// </summary>
        public string name { get; set; } = "";

        /// <summary>
        /// Longer description of what this rule checks and why.
        /// </summary>
        public string description { get; set; } = "";

        /// <summary>
        /// Version identifier for tracking rule updates.
        /// When a regulation is amended, the rule file gets a new version.
        /// </summary>
        public string version { get; set; } = "";

        /// <summary>
        /// Whether this check is enabled. Disabled checks are skipped.
        /// Default true. User can toggle off checks not relevant to their project.
        /// </summary>
        public bool enabled { get; set; } = true;

        /// <summary>
        /// Warning buffer in the same unit as the evaluation.
        /// Controls when yellow (warning) status triggers.
        /// Default 1.0 for heights (meters), 0.5 for setbacks (meters).
        /// </summary>
        public double warning_buffer { get; set; } = 1.0;

        /// <summary>
        /// Firm safety margin as a percentage (0 to 100).
        /// Reduces the effective limit by this percentage.
        /// Example: 10% on a 42m limit makes the effective limit 37.8m.
        /// Default 0 (no margin).
        /// </summary>
        public double safety_margin_percent { get; set; } = 0.0;

        /// <summary>
        /// Free text notes from the firm. Institutional knowledge, DOM quirks,
        /// client requirements. Appears in the compliance report.
        /// </summary>
        public string notes { get; set; } = "";

        /// <summary>
        /// List of geometry extractor names that this rule needs.
        /// Each string maps to a specific extractor function in the engine.
        /// Example: ["building_max_height", "natural_ground_level"]
        /// </summary>
        public List<string> geometry_required { get; set; } = new List<string>();

        /// <summary>
        /// Parameters that come from external sources (PRC, user input, etc).
        /// The key is the parameter name, the value contains its current value,
        /// source description, and human readable description.
        /// </summary>
        public Dictionary<string, RuleParameter> parameters { get; set; } = new Dictionary<string, RuleParameter>();

        /// <summary>
        /// Defines how the engine evaluates this rule: what type of comparison
        /// to perform, which measurement to use, and which parameter is the limit.
        /// </summary>
        public RuleEvaluation evaluation { get; set; } = new RuleEvaluation();

        /// <summary>
        /// Defines the thresholds for green, yellow, and red status.
        /// These are human readable expressions (not executed as code).
        /// The engine uses the evaluation type to determine the actual logic.
        /// </summary>
        public TrafficLightThresholds traffic_light { get; set; } = new TrafficLightThresholds();

        /// <summary>
        /// Message templates for each status. The engine replaces
        /// placeholders like {measured} and {limit} with actual values.
        /// </summary>
        public StatusMessages messages { get; set; } = new StatusMessages();
    }

    /// <summary>
    /// A parameter value that comes from an external source.
    /// 
    /// Parameters are values the engine cannot extract from the Revit model.
    /// For example, the maximum building height comes from the Plan Regulador
    /// Comunal, not from the model itself. The user must provide this value
    /// (or eventually the platform looks it up automatically).
    /// 
    /// When value is null, the engine knows this parameter has not been set
    /// and flags the check as "needs input" rather than failing silently.
    /// </summary>
    public class RuleParameter
    {
        /// <summary>
        /// The parameter value. Null means "not yet provided."
        /// Uses double? (nullable double) so the engine can distinguish
        /// between "value is 0" and "value was never set."
        /// </summary>
        public double? value { get; set; }

        /// <summary>
        /// Where this value comes from.
        /// Example: "Plan Regulador Comunal"
        /// </summary>
        public string source { get; set; } = "";

        /// <summary>
        /// Human readable explanation of what this parameter is.
        /// </summary>
        public string description { get; set; } = "";
    }

    /// <summary>
    /// Defines how the engine evaluates the rule.
    /// 
    /// The "type" field determines which evaluation logic the engine uses:
    ///   "max_threshold"         = measured must be <= limit (altura)
    ///   "min_threshold_per_face" = measured must be >= limit for each face (distanciamiento)
    ///   "envelope_intersection"  = building must not intersect envelope (rasante)
    /// </summary>
    public class RuleEvaluation
    {
        public string type { get; set; } = "";
        public string measured { get; set; } = "";
        public string baseline { get; set; } = "";
        public string limit_param { get; set; } = "";
        public string classification { get; set; } = "";
        public string limit_param_con_vano { get; set; } = "";
        public string limit_param_sin_vano { get; set; } = "";
        public string envelope_source { get; set; } = "";
        public string building_geometry { get; set; } = "";
        public string angle_param { get; set; } = "";
        public string base_height_param { get; set; } = "";
        public string unit { get; set; } = "m";
    }

    /// <summary>
    /// Human readable expressions defining the traffic light thresholds.
    /// These serve as documentation within the JSON file.
    /// The actual comparison logic lives in the engine code, not here.
    /// </summary>
    public class TrafficLightThresholds
    {
        public string green { get; set; } = "";
        public string yellow { get; set; } = "";
        public string red { get; set; } = "";
    }

    /// <summary>
    /// Message templates shown to the user for each status.
    /// Placeholders in curly braces get replaced with actual values.
    /// </summary>
    public class StatusMessages
    {
        public string green { get; set; } = "";
        public string yellow { get; set; } = "";
        public string red { get; set; } = "";
    }
}
