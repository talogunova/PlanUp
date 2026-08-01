using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    /// <summary>
    /// Checks if the building volume exceeds the rasante (shadow envelope)
    /// defined by inclined planes rising from property boundaries.
    /// 
    /// HOW RASANTES WORK (OGUC Art. 2.6.3):
    /// 
    /// From each point along the property boundary, an imaginary inclined
    /// line rises at a defined angle (typically 70 degrees from horizontal).
    /// This creates an inclined plane along each boundary segment.
    /// The building must fit entirely below all these planes.
    /// 
    /// HOW THIS EXTRACTOR WORKS (Point Sampling - Option B):
    /// 
    ///   1. Get all property boundary segments
    ///   2. For each building element, get the highest points (top corners
    ///      of bounding boxes and sampled points along roofs/walls)
    ///   3. For each high point, calculate the maximum allowed height at
    ///      that horizontal position based on the rasante angle and the
    ///      distance to the nearest property boundary
    ///   4. If the point's actual height exceeds the allowed height,
    ///      that is a rasante violation
    /// 
    /// THE MATH:
    /// 
    /// At any point P inside the site, the maximum allowed height is:
    ///   max_height = base_height + (horizontal_distance_to_boundary × tan(angle))
    /// 
    /// For a 70 degree rasante with base height 0:
    ///   max_height = distance × tan(70°) = distance × 2.747
    /// 
    /// So a point 5 meters from the boundary can be at most 13.74 m high.
    /// A point 15 meters from the boundary can be at most 41.2 m high.
    /// </summary>
    public class RasanteExtractor
    {
        private const double FeetToMeters = 0.3048;
        private const double MetersToFeet = 1.0 / 0.3048;

        /// <summary>
        /// Categories of elements to sample for rasante violations.
        /// We focus on elements that define the building envelope.
        /// </summary>
        private static readonly BuiltInCategory[] EnvelopeCategories = new BuiltInCategory[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_GenericModel
        };

        /// <summary>
        /// Checks if any part of the building exceeds the rasante envelope.
        /// 
        /// Parameters:
        ///   doc           - the Revit document
        ///   angleDegrees  - the rasante angle from horizontal (typically 70)
        ///   baseHeightM   - height above ground where the rasante starts (typically 0)
        /// 
        /// Returns a RasanteResult with violation details.
        /// </summary>
        public static RasanteResult Extract(Document doc, double angleDegrees, double baseHeightM)
        {
            // Step 1: Get property boundary lines
            List<Curve> propertyBoundary = GetPropertyBoundary(doc);

            if (propertyBoundary.Count == 0)
            {
                return new RasanteResult
                {
                    ErrorMessage = "No property lines found in the model."
                };
            }

            // Convert angle to radians for math functions
            double angleRadians = angleDegrees * Math.PI / 180.0;
            double tanAngle = Math.Tan(angleRadians);

            // Convert base height to feet (Revit internal units)
            double baseHeightFeet = baseHeightM * MetersToFeet;

            // Get the ground level
            double groundLevel = GetGroundLevel(doc);

            // Step 2: Sample high points from building elements
            List<SampledPoint> allPoints = new List<SampledPoint>();

            foreach (BuiltInCategory category in EnvelopeCategories)
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                    if (bbox == null) continue;

                    // Sample the 4 top corners of the bounding box
                    // (the highest points of the element)
                    double topZ = bbox.Max.Z;

                    // Only check points above ground level
                    if (topZ <= groundLevel) continue;

                    // The 4 top corners of the bounding box
                    XYZ[] topCorners = new XYZ[]
                    {
                        new XYZ(bbox.Min.X, bbox.Min.Y, topZ),
                        new XYZ(bbox.Max.X, bbox.Min.Y, topZ),
                        new XYZ(bbox.Max.X, bbox.Max.Y, topZ),
                        new XYZ(bbox.Min.X, bbox.Max.Y, topZ)
                    };

                    // Also sample the midpoint of the top face
                    XYZ topCenter = new XYZ(
                        (bbox.Min.X + bbox.Max.X) / 2.0,
                        (bbox.Min.Y + bbox.Max.Y) / 2.0,
                        topZ);

                    List<XYZ> samplePoints = new List<XYZ>(topCorners);
                    samplePoints.Add(topCenter);

                    foreach (XYZ point in samplePoints)
                    {
                        allPoints.Add(new SampledPoint
                        {
                            Point = point,
                            ElementId = element.Id,
                            ElementName = element.Name,
                            Category = category.ToString()
                        });
                    }
                }
            }

            if (allPoints.Count == 0)
            {
                return new RasanteResult
                {
                    ErrorMessage = "No building elements found above ground level."
                };
            }

            // Step 3: For each sampled point, check against all boundary segments
            List<RasanteViolation> violations = new List<RasanteViolation>();
            int totalChecks = 0;

            foreach (SampledPoint sample in allPoints)
            {
                // Calculate the actual height of this point above ground
                double pointHeightFeet = sample.Point.Z - groundLevel;
                double pointHeightMeters = pointHeightFeet * FeetToMeters;

                // Find the minimum horizontal distance from this point to any boundary
                double minDistanceFeet = double.MaxValue;
                int closestBoundaryIndex = 0;

                for (int i = 0; i < propertyBoundary.Count; i++)
                {
                    Curve boundary = propertyBoundary[i];

                    // Project the point onto the XY plane for horizontal distance
                    XYZ pointXY = new XYZ(sample.Point.X, sample.Point.Y, 0);

                    // Get the closest point on the boundary to our sample point
                    // (ignoring Z, since rasante is about horizontal distance)
                    XYZ boundaryStart = new XYZ(boundary.GetEndPoint(0).X, boundary.GetEndPoint(0).Y, 0);
                    XYZ boundaryEnd = new XYZ(boundary.GetEndPoint(1).X, boundary.GetEndPoint(1).Y, 0);

                    double distance = PointToSegmentDistance(pointXY, boundaryStart, boundaryEnd);

                    if (distance < minDistanceFeet)
                    {
                        minDistanceFeet = distance;
                        closestBoundaryIndex = i;
                    }
                }

                // Calculate the maximum allowed height at this horizontal distance
                double maxAllowedHeightFeet = baseHeightFeet + (minDistanceFeet * tanAngle);
                double maxAllowedHeightMeters = maxAllowedHeightFeet * FeetToMeters;

                totalChecks++;

                // Check if the point exceeds the rasante
                double excessFeet = pointHeightFeet - maxAllowedHeightFeet;
                double excessMeters = excessFeet * FeetToMeters;

                if (excessMeters > 0.01) // tolerance of 1 cm
                {
                    double distanceMeters = minDistanceFeet * FeetToMeters;
                    string boundaryName = GetBoundaryName(propertyBoundary[closestBoundaryIndex]);

                    violations.Add(new RasanteViolation
                    {
                        ElementId = sample.ElementId,
                        ElementName = sample.ElementName,
                        PointHeightM = Math.Round(pointHeightMeters, 2),
                        MaxAllowedHeightM = Math.Round(maxAllowedHeightMeters, 2),
                        ExcessM = Math.Round(excessMeters, 2),
                        DistanceToBoundaryM = Math.Round(distanceMeters, 2),
                        BoundaryName = boundaryName,
                        BoundaryIndex = closestBoundaryIndex
                    });
                }
            }

            return new RasanteResult
            {
                Violations = violations,
                TotalPointsChecked = totalChecks,
                PropertyBoundarySegments = propertyBoundary.Count,
                AngleDegrees = angleDegrees,
                BaseHeightM = baseHeightM,
                ErrorMessage = ""
            };
        }

        /// <summary>
        /// Calculates the minimum distance from a point to a line segment.
        /// All coordinates are in the XY plane (Z = 0).
        /// 
        /// This is a standard computational geometry formula:
        /// 1. Project the point onto the infinite line containing the segment
        /// 2. If the projection falls within the segment, the distance is
        ///    the perpendicular distance
        /// 3. If the projection falls outside, the distance is to the
        ///    nearest endpoint
        /// </summary>
        private static double PointToSegmentDistance(XYZ point, XYZ segStart, XYZ segEnd)
        {
            XYZ seg = segEnd - segStart;
            double segLengthSq = seg.X * seg.X + seg.Y * seg.Y;

            if (segLengthSq < 1e-10)
            {
                // Degenerate segment (zero length)
                return point.DistanceTo(segStart);
            }

            // Parameter t of the projection of the point onto the line
            double t = ((point.X - segStart.X) * seg.X + (point.Y - segStart.Y) * seg.Y) / segLengthSq;

            // Clamp t to [0, 1] to stay within the segment
            t = Math.Max(0, Math.Min(1, t));

            // The closest point on the segment
            XYZ closest = new XYZ(
                segStart.X + t * seg.X,
                segStart.Y + t * seg.Y,
                0);

            return point.DistanceTo(closest);
        }

        /// <summary>
        /// Finds the ground level elevation (same logic as BuildingHeightExtractor).
        /// </summary>
        private static double GetGroundLevel(Document doc)
        {
            FilteredElementCollector levelCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level));

            double closestToZero = 0;
            double smallestDifference = double.MaxValue;

            foreach (Level level in levelCollector)
            {
                double difference = Math.Abs(level.Elevation);
                if (difference < smallestDifference)
                {
                    smallestDifference = difference;
                    closestToZero = level.Elevation;
                }
            }

            return closestToZero;
        }

        /// <summary>
        /// Gets property boundary curves (reuses same logic as SetbackExtractor).
        /// </summary>
        private static List<Curve> GetPropertyBoundary(Document doc)
        {
            List<Curve> curves = new List<Curve>();

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
                catch { }
            }

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
            }
            catch { }

            return curves;
        }

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
        /// Names a boundary segment based on its direction.
        /// </summary>
        private static string GetBoundaryName(Curve boundaryCurve)
        {
            XYZ start = boundaryCurve.GetEndPoint(0);
            XYZ end = boundaryCurve.GetEndPoint(1);
            XYZ midpoint = (start + end) / 2.0;
            XYZ direction = (end - start).Normalize();

            double absX = Math.Abs(direction.X);
            double absY = Math.Abs(direction.Y);

            if (absX > absY)
            {
                return midpoint.Y > 0 ? "north boundary" : "south boundary";
            }
            else
            {
                return midpoint.X > 0 ? "east boundary" : "west boundary";
            }
        }
    }

    /// <summary>
    /// A point sampled from a building element for rasante checking.
    /// </summary>
    public class SampledPoint
    {
        public XYZ Point { get; set; }
        public ElementId ElementId { get; set; }
        public string ElementName { get; set; } = "";
        public string Category { get; set; } = "";
    }

    /// <summary>
    /// A single rasante violation at a specific point.
    /// </summary>
    public class RasanteViolation
    {
        public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
        public string ElementName { get; set; } = "";
        public double PointHeightM { get; set; }
        public double MaxAllowedHeightM { get; set; }
        public double ExcessM { get; set; }
        public double DistanceToBoundaryM { get; set; }
        public string BoundaryName { get; set; } = "";
        public int BoundaryIndex { get; set; }
    }

    /// <summary>
    /// Complete result of a rasante check.
    /// </summary>
    public class RasanteResult
    {
        public List<RasanteViolation> Violations { get; set; } = new List<RasanteViolation>();
        public int TotalPointsChecked { get; set; }
        public int PropertyBoundarySegments { get; set; }
        public double AngleDegrees { get; set; }
        public double BaseHeightM { get; set; }
        public string ErrorMessage { get; set; } = "";

        public bool IsValid => string.IsNullOrEmpty(ErrorMessage);
        public bool HasViolations => Violations.Count > 0;

        /// <summary>
        /// Returns the worst violation (largest excess above rasante).
        /// </summary>
        public RasanteViolation GetWorstViolation()
        {
            if (Violations.Count == 0) return null;
            return Violations.OrderByDescending(v => v.ExcessM).First();
        }
    }
}
