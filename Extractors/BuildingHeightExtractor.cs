using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    /// <summary>
    /// Extracts the maximum building height from a Revit model.
    /// 
    /// HOW IT WORKS:
    /// 
    /// Every element in Revit has a BoundingBox: the smallest rectangular
    /// box that fully contains that element. The BoundingBox has a Min point
    /// (lowest corner) and a Max point (highest corner), each with X, Y, Z
    /// coordinates.
    /// 
    /// To find the building height, we:
    ///   1. Collect all building elements (walls, floors, roofs, etc.)
    ///   2. Get each element's BoundingBox
    ///   3. Find the highest Z coordinate across all BoundingBoxes (the top of the building)
    ///   4. Find the ground level elevation (the base reference)
    ///   5. Subtract: height = highest point - ground level
    ///   6. Convert from Revit internal units (feet) to meters
    /// 
    /// WHY FEET?
    /// Revit stores all dimensions internally in decimal feet, regardless
    /// of what display units you have set in your project. A wall that
    /// shows as 3.0 meters in Revit is stored as 9.84252 feet internally.
    /// We always multiply by 0.3048 to convert to meters.
    /// </summary>
    public class BuildingHeightExtractor
    {
        // Conversion factor from Revit internal units (feet) to meters
        private const double FeetToMeters = 0.3048;

        /// <summary>
        /// The categories of elements we consider as "the building."
        /// 
        /// We include structural and architectural elements that define
        /// the building envelope. We exclude site elements, property lines,
        /// annotations, and other non-building objects because they would
        /// give us a false reading.
        /// 
        /// For example, if a property line marker is placed at ground level
        /// 50 meters away from the building, its bounding box should not
        /// affect our height measurement.
        /// </summary>
        private static readonly BuiltInCategory[] BuildingCategories = new BuiltInCategory[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRailing,
            BuiltInCategory.OST_GenericModel
        };

        /// <summary>
        /// Measures the building height in the given Revit document.
        /// 
        /// Returns a BuildingHeightResult containing:
        ///   - MaxElevation: the highest point of the building in meters
        ///   - GroundLevel: the ground reference elevation in meters
        ///   - Height: the difference (max - ground) in meters
        ///   - ElementCount: how many elements were analyzed
        ///   - HighestElementId: the Revit ElementId of the element at the top
        ///     (so the user can navigate to it and understand what defines the height)
        /// </summary>
        public static BuildingHeightResult Extract(Document doc)
        {
            double maxZ = double.MinValue;
            double minZ = double.MaxValue;
            ElementId highestElementId = ElementId.InvalidElementId;
            int elementCount = 0;

            // Loop through each building category and collect elements
            foreach (BuiltInCategory category in BuildingCategories)
            {
                // FilteredElementCollector is the Revit API's query tool.
                // Think of it as a search: "give me all elements in this
                // document that belong to this category."
                //
                // .WhereElementIsNotElementType() filters out the type
                // definitions (the templates) and keeps only the actual
                // placed instances in the model.
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    // Get the element's bounding box in the model's coordinate system
                    // The View parameter is null, meaning we want the 3D bounding box,
                    // not a view-specific one (some elements have different representations
                    // in different views).
                    BoundingBoxXYZ bbox = element.get_BoundingBox(null);

                    // Some elements do not have a bounding box (for example,
                    // analytical elements or elements that have been deleted
                    // but not purged). Skip them.
                    if (bbox == null) continue;

                    elementCount++;

                    // Check if this element's top is higher than our current maximum
                    if (bbox.Max.Z > maxZ)
                    {
                        maxZ = bbox.Max.Z;
                        highestElementId = element.Id;
                    }

                    // Track the lowest point too (useful for understanding
                    // if the building has underground levels)
                    if (bbox.Min.Z < minZ)
                    {
                        minZ = bbox.Min.Z;
                    }
                }
            }

            // If no elements were found, return a zero result
            if (elementCount == 0)
            {
                return new BuildingHeightResult
                {
                    MaxElevation = 0,
                    GroundLevel = 0,
                    Height = 0,
                    ElementCount = 0,
                    HighestElementId = ElementId.InvalidElementId,
                    ErrorMessage = "No building elements found in the model"
                };
            }

            // Determine the ground level.
            // Strategy: look for the level closest to elevation 0,
            // which is typically the ground floor in most Revit models.
            double groundLevel = GetGroundLevel(doc);

            // Calculate the building height
            double maxElevationMeters = maxZ * FeetToMeters;
            double groundLevelMeters = groundLevel * FeetToMeters;
            double heightMeters = (maxZ - groundLevel) * FeetToMeters;

            return new BuildingHeightResult
            {
                MaxElevation = Math.Round(maxElevationMeters, 2),
                GroundLevel = Math.Round(groundLevelMeters, 2),
                Height = Math.Round(heightMeters, 2),
                ElementCount = elementCount,
                HighestElementId = highestElementId,
                ErrorMessage = ""
            };
        }

        /// <summary>
        /// Finds the ground level elevation in the model.
        /// 
        /// Revit models have Levels (horizontal reference planes at specific
        /// elevations). We look for the level closest to elevation 0, which
        /// in most projects represents the ground floor / natural ground.
        /// 
        /// If no levels are found (unusual but possible), we fall back to 0.
        /// </summary>
        private static double GetGroundLevel(Document doc)
        {
            FilteredElementCollector levelCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level));

            double closestToZero = 0;
            double smallestDifference = double.MaxValue;

            foreach (Level level in levelCollector)
            {
                // Level.Elevation returns the height of the level
                // in Revit internal units (feet)
                double difference = Math.Abs(level.Elevation);

                if (difference < smallestDifference)
                {
                    smallestDifference = difference;
                    closestToZero = level.Elevation;
                }
            }

            return closestToZero;
        }
    }

    /// <summary>
    /// Contains the results of a building height extraction.
    /// 
    /// This is a simple data container (no Revit API dependencies except
    /// ElementId) that can be passed to the engine and the UI without
    /// carrying Revit context around.
    /// </summary>
    public class BuildingHeightResult
    {
        /// <summary>Highest point of the building, in meters above project origin.</summary>
        public double MaxElevation { get; set; }

        /// <summary>Ground level reference, in meters.</summary>
        public double GroundLevel { get; set; }

        /// <summary>Building height (max elevation minus ground level), in meters.</summary>
        public double Height { get; set; }

        /// <summary>Number of building elements that were analyzed.</summary>
        public int ElementCount { get; set; }

        /// <summary>ElementId of the element at the highest point. Can be used
        /// to zoom/navigate to it in Revit.</summary>
        public ElementId HighestElementId { get; set; } = ElementId.InvalidElementId;

        /// <summary>Error message if extraction failed. Empty string means success.</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>True if the extraction completed without errors.</summary>
        public bool IsValid => string.IsNullOrEmpty(ErrorMessage);
    }
}
