using System;

namespace PlanUp.Engine
{
    /// <summary>
    /// The three possible outcomes of a compliance check.
    /// 
    /// Green  = fully compliant, no issues
    /// Yellow = warning, close to the limit or missing information
    /// Red    = violation, the model exceeds the allowed threshold
    /// </summary>
    public enum ComplianceStatus
    {
        Green,
        Yellow,
        Red
    }

    /// <summary>
    /// Holds the result of one compliance check.
    /// 
    /// This is the core data object that travels through the entire system:
    ///   1. The ComplianceEngine creates it after evaluating a rule
    ///   2. The RunCheckCommand passes it to the UI
    ///   3. The CompliancePanel displays it with a traffic light color
    /// 
    /// Every field is deliberately simple (strings, doubles, an enum).
    /// No Revit API types here, so this class can be tested independently.
    /// </summary>
    public class CheckResult
    {
        /// <summary>
        /// Unique identifier matching the rule definition JSON file.
        /// Example: "OGUC-2.6.3-altura"
        /// </summary>
        public string RuleId { get; set; } = "";

        /// <summary>
        /// The OGUC article number for display.
        /// Example: "Art. 2.6.3"
        /// </summary>
        public string ArticleReference { get; set; } = "";

        /// <summary>
        /// Human readable name of the check.
        /// Example: "Altura maxima de edificacion"
        /// </summary>
        public string RuleName { get; set; } = "";

        /// <summary>
        /// What the engine measured in the model.
        /// Example: 12.5 (meters)
        /// </summary>
        public double MeasuredValue { get; set; }

        /// <summary>
        /// The maximum (or minimum) allowed by the regulation.
        /// Example: 15.0 (meters)
        /// </summary>
        public double AllowedValue { get; set; }

        /// <summary>
        /// The unit of measurement for display purposes.
        /// Example: "m" for meters, "°" for degrees
        /// </summary>
        public string Unit { get; set; } = "m";

        /// <summary>
        /// The traffic light result: Green, Yellow, or Red.
        /// </summary>
        public ComplianceStatus Status { get; set; }

        /// <summary>
        /// A human readable explanation of the result in Spanish.
        /// Example: "La altura del edificio (12.5 m) no supera el maximo permitido (15.0 m)"
        /// </summary>
        public string StatusMessage { get; set; } = "";

        /// <summary>
        /// Optional: a more detailed description of what was checked and why.
        /// Shown when the user expands the result item in the panel.
        /// </summary>
        public string DetailDescription { get; set; } = "";

        /// <summary>
        /// Returns a formatted string showing measured vs allowed.
        /// Used in the UI to display "12.5 m / 15.0 m"
        /// </summary>
        public string ValueSummary
        {
            get
            {
                return $"{MeasuredValue:F1} {Unit} / {AllowedValue:F1} {Unit}";
            }
        }

        /// <summary>
        /// Returns the status as a display string.
        /// </summary>
        public string StatusLabel
        {
            get
            {
                switch (Status)
                {
                    case ComplianceStatus.Green: return "PASS";
                    case ComplianceStatus.Yellow: return "WARNING";
                    case ComplianceStatus.Red: return "FAIL";
                    default: return "UNKNOWN";
                }
            }
        }
    }
}
