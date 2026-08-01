using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    /// <summary>
    /// Creates visible 3D geometry in the Revit model representing
    /// the rasante (shadow envelope) planes from property boundaries.
    /// 
    /// HOW IT WORKS:
    /// 
    /// For each property boundary segment, we create a triangulated surface
    /// (a DirectShape element) representing the inclined plane at the
    /// rasante angle. The surface rises from the boundary line at the
    /// specified angle and extends inward over the site.
    /// 
    /// WHAT IS A DIRECTSHAPE?
    /// 
    /// A DirectShape is a Revit element that displays arbitrary geometry
    /// (meshes, solids, curves) in the model without being tied to a
    /// family or type. It is perfect for visualization overlays because:
    ///   - It shows up in 3D views like any other element
    ///   - It can be made translucent with color overrides
    ///   - It can be deleted without affecting the model
    ///   - It persists across sessions (so the user can rotate the view
    ///     and inspect the envelope from different angles)
    /// 
    /// We place all rasante shapes in a subcategory called "PlanUp Rasante"
    /// so they can be toggled on/off in Visibility/Graphics.
    /// </summary>
    public class RasanteVisualizer
    {
        private const double FeetToMeters = 0.3048;
        private const double MetersToFeet = 1.0 / 0.3048;

        /// <summary>
        /// The name used for the DirectShape category.
        /// Using Generic Models as the parent category.
        /// </summary>
        private static readonly ElementId DirectShapeCategoryId =
            new ElementId(BuiltInCategory.OST_GenericModel);

        /// <summary>
        /// Creates rasante envelope surfaces in the model.
        /// Must be called within a Transaction.
        /// 
        /// Parameters:
        ///   doc          - the Revit document
        ///   angleDegrees - rasante angle (typically 70)
        ///   baseHeightM  - height where rasante starts (typically 0)
        ///   maxDepthM    - how far inward the plane extends from the boundary
        ///                  (set to site depth or a reasonable maximum)
        /// 
        /// Returns the list of created DirectShape ElementIds so they
        /// can be deleted later when the user runs a new check.
        /// </summary>
        public static List<ElementId> CreateRasanteSurfaces(
            Document doc,
            double angleDegrees,
            double baseHeightM,
            double maxDepthM = 50.0)
        {
            List<ElementId> createdIds = new List<ElementId>();

            // Get property boundary
            List<Curve> boundary = GetPropertyBoundary(doc);
            if (boundary.Count == 0) return createdIds;

            double angleRadians = angleDegrees * Math.PI / 180.0;
            double tanAngle = Math.Tan(angleRadians);
            double groundLevel = GetGroundLevel(doc);

            double baseHeightFeet = baseHeightM * MetersToFeet;
            double maxDepthFeet = maxDepthM * MetersToFeet;

            foreach (Curve boundaryCurve in boundary)
            {
                try
                {
                    XYZ start = boundaryCurve.GetEndPoint(0);
                    XYZ end = boundaryCurve.GetEndPoint(1);

                    // Calculate the inward normal direction
                    // (perpendicular to the boundary, pointing toward the site center)
                    XYZ boundaryDir = (end - start).Normalize();
                    // Rotate 90 degrees in XY plane to get the inward normal
                    // We try both directions and pick the one pointing toward site center
                    XYZ normal1 = new XYZ(-boundaryDir.Y, boundaryDir.X, 0);
                    XYZ normal2 = new XYZ(boundaryDir.Y, -boundaryDir.X, 0);

                    // Use the midpoint of all boundaries as an approximate site center
                    XYZ siteCenter = GetSiteCenter(boundary);
                    XYZ boundaryMid = (start + end) / 2.0;
                    XYZ toCenter = (siteCenter - boundaryMid).Normalize();

                    // Pick the normal that points toward the center
                    XYZ inwardNormal = (toCenter.DotProduct(normal1) > 0) ? normal1 : normal2;

                    // Build the rasante surface as a triangulated mesh
                    // The surface has 4 corners:
                    //   Bottom-left:  start of boundary at ground + base height
                    //   Bottom-right: end of boundary at ground + base height
                    //   Top-left:     start offset inward by maxDepth, at rasante height
                    //   Top-right:    end offset inward by maxDepth, at rasante height

                    double baseZ = groundLevel + baseHeightFeet;
                    double topHeight = baseHeightFeet + (maxDepthFeet * tanAngle);
                    double topZ = groundLevel + topHeight;

                    XYZ bottomLeft = new XYZ(start.X, start.Y, baseZ);
                    XYZ bottomRight = new XYZ(end.X, end.Y, baseZ);
                    XYZ topLeft = new XYZ(
                        start.X + inwardNormal.X * maxDepthFeet,
                        start.Y + inwardNormal.Y * maxDepthFeet,
                        topZ);
                    XYZ topRight = new XYZ(
                        end.X + inwardNormal.X * maxDepthFeet,
                        end.Y + inwardNormal.Y * maxDepthFeet,
                        topZ);

                    // Create a TessellatedShapeBuilder to make the surface
                    TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                    builder.OpenConnectedFaceSet(false);

                    // Add two triangles forming the quad surface
                    // Triangle 1: bottomLeft, bottomRight, topRight
                    TessellatedFace face1 = new TessellatedFace(
                        new List<XYZ> { bottomLeft, bottomRight, topRight }, ElementId.InvalidElementId);
                    builder.AddFace(face1);

                    // Triangle 2: bottomLeft, topRight, topLeft
                    TessellatedFace face2 = new TessellatedFace(
                        new List<XYZ> { bottomLeft, topRight, topLeft }, ElementId.InvalidElementId);
                    builder.AddFace(face2);

                    builder.CloseConnectedFaceSet();
                    builder.Build();

                    TessellatedShapeBuilderResult result = builder.GetBuildResult();

                    // Create the DirectShape element
                    DirectShape ds = DirectShape.CreateElement(doc, DirectShapeCategoryId);
                    ds.ApplicationId = "PlanUp";
                    ds.ApplicationDataId = "RasanteEnvelope";

                    IList<GeometryObject> geomObjects = result.GetGeometricalObjects();
                    if (geomObjects.Count > 0)
                    {
                        ds.SetShape(geomObjects);
                        ds.SetName("PlanUp Rasante Envelope");
                        createdIds.Add(ds.Id);
                    }
                }
                catch
                {
                    // Skip this boundary segment if geometry creation fails
                    continue;
                }
            }

            return createdIds;
        }

        /// <summary>
        /// Applies a translucent color override to the rasante surfaces
        /// so they appear as a visible but see-through envelope.
        /// 
        /// Green for compliant areas, red for violations.
        /// Must be called within a Transaction.
        /// </summary>
        public static void ApplyColorOverrides(
            Document doc,
            View view,
            List<ElementId> rasanteIds,
            bool hasViolations)
        {
            // Set up the override: translucent surface with color
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            Color surfaceColor;
            if (hasViolations)
            {
                surfaceColor = new Color(231, 76, 60); // red (#E74C3C)
            }
            else
            {
                surfaceColor = new Color(39, 174, 96); // green (#27AE60)
            }

            ogs.SetSurfaceForegroundPatternColor(surfaceColor);
            ogs.SetSurfaceTransparency(70); // 70% transparent

            // Try to set a solid fill pattern
            FilteredElementCollector patternCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement));

            foreach (FillPatternElement fpe in patternCollector)
            {
                FillPattern fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill)
                {
                    ogs.SetSurfaceForegroundPatternId(fpe.Id);
                    break;
                }
            }

            // Apply the override to each rasante surface
            foreach (ElementId id in rasanteIds)
            {
                view.SetElementOverrides(id, ogs);
            }
        }

        /// <summary>
        /// Removes all previously created rasante surfaces from the model.
        /// Must be called within a Transaction.
        /// </summary>
        public static void ClearPreviousRasante(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape));

            List<ElementId> toDelete = new List<ElementId>();

            foreach (DirectShape ds in collector)
            {
                if (ds.ApplicationId == "PlanUp" &&
                    ds.ApplicationDataId == "RasanteEnvelope")
                {
                    toDelete.Add(ds.Id);
                }
            }

            if (toDelete.Count > 0)
            {
                doc.Delete(toDelete);
            }
        }

        /// <summary>
        /// Calculates the approximate center of the site from boundary curves.
        /// Used to determine which direction is "inward" from each boundary.
        /// </summary>
        private static XYZ GetSiteCenter(List<Curve> boundary)
        {
            double sumX = 0, sumY = 0;
            int count = 0;

            foreach (Curve curve in boundary)
            {
                XYZ mid = (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2.0;
                sumX += mid.X;
                sumY += mid.Y;
                count++;
            }

            if (count == 0) return XYZ.Zero;
            return new XYZ(sumX / count, sumY / count, 0);
        }

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
    }
}
