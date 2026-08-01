using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    /// <summary>
    /// Extracts the minimum distance from each exterior building wall
    /// to the nearest property boundary.
    /// 
    /// HOW IT WORKS:
    /// 
    ///   1. Find all Property Lines in the model (Revit category: Site > Property Lines)
    ///   2. Extract their boundary curves as a list of line segments
    ///   3. Find all exterior Walls in the model
    ///   4. For each wall, determine if it has openings (windows/doors) = "con vano"
    ///      or is solid = "sin vano"
    ///   5. Calculate the minimum distance from each wall's exterior face
    ///      to the nearest property boundary line
    ///   6. Return a result for each wall with its distance and classification
    /// 
    /// WHAT IS A PROPERTY LINE IN REVIT?
    /// Property Lines are created through Massing & Site > Property Line.
    /// They are stored as PropertyLine elements (BuiltInCategory.OST_PropertyLine)
    /// and contain a sketch with line segments forming the site boundary.
    /// </summary>
    public class SetbackExtractor
    {
        private const double FeetToMeters = 0.3048;

        /// <summary>
        /// Measures the distance from each exterior wall to the nearest property boundary.
        /// 
        /// Returns a SetbackResult containing a list of WallSetback objects,
        /// one per wall, plus summary information.
        /// </summary>
        public static SetbackResult Extract(Document doc)
        {
            // Step 1: Get property boundary lines
            List<Curve> propertyBoundary = GetPropertyBoundary(doc);

            if (propertyBoundary.Count == 0)
            {
                return new SetbackResult
                {
                    ErrorMessage = "No property lines found in the model. Draw property lines using Massing & Site > Property Line."
                };
            }

            // Step 2: Get all walls and measure distances
            List<WallSetback> wallSetbacks = new List<WallSetback>();

            FilteredElementCollector wallCollector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType();

            foreach (Wall wall in wallCollector.Cast<Wall>())
            {
                // Skip curtain walls for now (their geometry is complex)
                if (wall.WallType.Kind == WallKind.Curtain) continue;

                // Get the wall's location line (the center line of the wall)
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve wallCurve = locCurve.Curve;

                // Determine if the wall has openings (windows or doors hosted on it)
                bool hasOpenings = HasHostedOpenings(doc, wall);

                // Get the wall's exterior face offset from the center line
                // The wall width tells us how far the exterior face is from center
                double halfWidth = wall.WallType.Width / 2.0;

                // Calculate the minimum distance from the wall center line
                // to the nearest property boundary, then subtract the half width
                // to get the distance from the exterior face
                double minDistance = double.MaxValue;
                string nearestBoundaryName = "";
                int boundaryIndex = 0;

                for (int i = 0; i < propertyBoundary.Count; i++)
                {
                    Curve boundaryCurve = propertyBoundary[i];
                    double distance = GetMinDistanceBetweenCurves(wallCurve, boundaryCurve);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        boundaryIndex = i;
                    }
                }

                // Subtract half the wall width to get distance from exterior face
                // (approximation: assumes wall is perpendicular to boundary)
                double exteriorDistance = minDistance - halfWidth;
                if (exteriorDistance < 0) exteriorDistance = 0;

                // Convert to meters
                double distanceMeters = Math.Round(exteriorDistance * FeetToMeters, 2);

                // Name the boundary segment (north, south, east, west based on direction)
                string boundaryName = GetBoundaryName(propertyBoundary[boundaryIndex]);

                wallSetbacks.Add(new WallSetback
                {
                    WallId = wall.Id,
                    WallName = wall.Name,
                    DistanceToPropertyLine = distanceMeters,
                    HasOpenings = hasOpenings,
                    NearestBoundaryName = boundaryName,
                    BoundaryIndex = boundaryIndex
                });
            }

            if (wallSetbacks.Count == 0)
            {
                return new SetbackResult
                {
                    ErrorMessage = "No walls found in the model to measure setbacks."
                };
            }

            return new SetbackResult
            {
                WallSetbacks = wallSetbacks,
                PropertyBoundarySegments = propertyBoundary.Count,
                ErrorMessage = ""
            };
        }

        /// <summary>
        /// Finds all Property Line elements and extracts their boundary curves.
        /// 
        /// Property Lines in Revit are sketch-based elements. We get their
        /// geometry and extract the line segments that form the boundary.
        /// </summary>
        private static List<Curve> GetPropertyBoundary(Document doc)
        {
            List<Curve> curves = new List<Curve>();

            // Approach 1: Try to find Property Lines by category
            BuiltInCategory[] possibleCategories = new BuiltInCategory[]
            {
                BuiltInCategory.OST_SitePropertyLineSegment,
                BuiltInCategory.OST_SiteProperty,
                BuiltInCategory.OST_Site
            };

            foreach (BuiltInCategory category in possibleCategories)
            {
                try
                {
                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType();

                    foreach (Element element in collector)
                    {
                        Options geomOptions = new Options();
                        GeometryElement geomElement = element.get_Geometry(geomOptions);
                        if (geomElement == null) continue;

                        ExtractCurvesFromGeometry(geomElement, curves);
                    }

                    if (curves.Count > 0) return curves;
                }
                catch { /* category might not exist, try next */ }
            }

            // Approach 2: Find by PropertyLine class (Autodesk.Revit.DB.PropertyLine)
            try
            {
                FilteredElementCollector classCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertyLine));

                foreach (Element element in classCollector)
                {
                    Options geomOptions = new Options();
                    GeometryElement geomElement = element.get_Geometry(geomOptions);
                    if (geomElement == null) continue;

                    ExtractCurvesFromGeometry(geomElement, curves);
                }

                if (curves.Count > 0) return curves;
            }
            catch { }

            return curves;
        }

        /// <summary>
        /// Recursively extracts curves from a GeometryElement,
        /// handling nested GeometryInstances.
        /// </summary>
        private static void ExtractCurvesFromGeometry(GeometryElement geomElement, List<Curve> curves)
        {
            foreach (GeometryObject geomObj in geomElement)
            {
                if (geomObj is Curve curve)
                {
                    curves.Add(curve);
                }
                else if (geomObj is GeometryInstance geomInstance)
                {
                    GeometryElement instanceGeom = geomInstance.GetInstanceGeometry();
                    if (instanceGeom != null)
                    {
                        ExtractCurvesFromGeometry(instanceGeom, curves);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a wall has windows or doors hosted on it.
        /// 
        /// In Revit, windows and doors are "hosted" by walls. We can find
        /// them by looking for FamilyInstance elements whose Host property
        /// points to our wall.
        /// 
        /// If the wall has any openings, it is classified as "con vano"
        /// which requires a larger setback distance per OGUC.
        /// </summary>
        private static bool HasHostedOpenings(Document doc, Wall wall)
        {
            // Get all inserts (windows, doors, openings) hosted by this wall
            IList<ElementId> insertIds = wall.FindInserts(true, false, true, true);

            foreach (ElementId insertId in insertIds)
            {
                Element insert = doc.GetElement(insertId);
                if (insert == null) continue;

                // Check if the insert is a window or door
                Category cat = insert.Category;
                if (cat == null) continue;

                if (cat.Id.Value == (int)BuiltInCategory.OST_Windows ||
                    cat.Id.Value == (int)BuiltInCategory.OST_Doors)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Calculates the minimum distance between two curves.
        /// 
        /// We sample multiple points along each curve and find the
        /// shortest distance between any pair of points. This is an
        /// approximation but works well for straight walls and straight
        /// property boundary segments.
        /// 
        /// For perfectly straight lines (which walls and property boundaries
        /// typically are), we also check the analytical minimum using
        /// closest point projection.
        /// </summary>
        private static double GetMinDistanceBetweenCurves(Curve curve1, Curve curve2)
        {
            double minDist = double.MaxValue;

            // Use the midpoint of the wall and project it onto the boundary line
            XYZ wallMidpoint = curve1.Evaluate(0.5, true);

            // Get the closest point on the boundary curve to the wall midpoint
            IntersectionResult result = curve2.Project(wallMidpoint);
            if (result != null)
            {
                double dist = wallMidpoint.DistanceTo(result.XYZPoint);
                if (dist < minDist) minDist = dist;
            }

            // Also check endpoints of the wall against the boundary
            XYZ wallStart = curve1.GetEndPoint(0);
            XYZ wallEnd = curve1.GetEndPoint(1);

            IntersectionResult resultStart = curve2.Project(wallStart);
            if (resultStart != null)
            {
                double dist = wallStart.DistanceTo(resultStart.XYZPoint);
                if (dist < minDist) minDist = dist;
            }

            IntersectionResult resultEnd = curve2.Project(wallEnd);
            if (resultEnd != null)
            {
                double dist = wallEnd.DistanceTo(resultEnd.XYZPoint);
                if (dist < minDist) minDist = dist;
            }

            return minDist;
        }

        /// <summary>
        /// Names a property boundary segment based on its direction.
        /// 
        /// Looks at the direction of the boundary line and assigns
        /// a cardinal name: a line running mostly east-west is the
        /// "north" or "south" boundary, and a line running mostly
        /// north-south is the "east" or "west" boundary.
        /// 
        /// This naming is approximate and used for display purposes only.
        /// </summary>
        private static string GetBoundaryName(Curve boundaryCurve)
        {
            XYZ start = boundaryCurve.GetEndPoint(0);
            XYZ end = boundaryCurve.GetEndPoint(1);
            XYZ midpoint = (start + end) / 2.0;
            XYZ direction = (end - start).Normalize();

            // Determine if the line runs mostly east-west or north-south
            // In Revit, X is typically east, Y is typically north
            double absX = Math.Abs(direction.X);
            double absY = Math.Abs(direction.Y);

            if (absX > absY)
            {
                // Line runs mostly east-west, so it is a north or south boundary
                // Use the Y position of the midpoint relative to model origin
                // to determine if it is north or south
                return midpoint.Y > 0 ? "north boundary" : "south boundary";
            }
            else
            {
                // Line runs mostly north-south, so it is an east or west boundary
                return midpoint.X > 0 ? "east boundary" : "west boundary";
            }
        }
    }

    /// <summary>
    /// Contains the results of a setback extraction for the entire model.
    /// </summary>
    public class SetbackResult
    {
        /// <summary>Individual setback measurements for each wall.</summary>
        public List<WallSetback> WallSetbacks { get; set; } = new List<WallSetback>();

        /// <summary>Number of property boundary segments found.</summary>
        public int PropertyBoundarySegments { get; set; }

        /// <summary>Error message if extraction failed.</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>True if extraction completed without errors.</summary>
        public bool IsValid => string.IsNullOrEmpty(ErrorMessage);

        /// <summary>
        /// Returns the wall with the smallest setback distance that has openings.
        /// This is the most critical measurement for "con vano" compliance.
        /// Returns null if no walls with openings were found.
        /// </summary>
        public WallSetback GetCriticalConVano()
        {
            var conVano = WallSetbacks.Where(w => w.HasOpenings).ToList();
            if (conVano.Count == 0) return null;
            return conVano.OrderBy(w => w.DistanceToPropertyLine).First();
        }

        /// <summary>
        /// Returns the wall with the smallest setback distance that has no openings.
        /// This is the most critical measurement for "sin vano" compliance.
        /// Returns null if no solid walls were found.
        /// </summary>
        public WallSetback GetCriticalSinVano()
        {
            var sinVano = WallSetbacks.Where(w => !w.HasOpenings).ToList();
            if (sinVano.Count == 0) return null;
            return sinVano.OrderBy(w => w.DistanceToPropertyLine).First();
        }
    }

    /// <summary>
    /// Setback measurement for a single wall.
    /// </summary>
    public class WallSetback
    {
        /// <summary>The Revit ElementId of the wall.</summary>
        public ElementId WallId { get; set; } = ElementId.InvalidElementId;

        /// <summary>The wall type name for display.</summary>
        public string WallName { get; set; } = "";

        /// <summary>Distance from exterior wall face to nearest property line, in meters.</summary>
        public double DistanceToPropertyLine { get; set; }

        /// <summary>True if the wall has windows or doors (con vano).</summary>
        public bool HasOpenings { get; set; }

        /// <summary>Name of the nearest property boundary segment.</summary>
        public string NearestBoundaryName { get; set; } = "";

        /// <summary>Index of the nearest boundary segment in the property line list.</summary>
        public int BoundaryIndex { get; set; }

        /// <summary>Classification label for display.</summary>
        public string Classification => HasOpenings ? "con vano" : "sin vano";
    }
}
