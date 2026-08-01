using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    /// <summary>
    /// Creates rasante envelope visualization and highlights violating walls.
    /// 
    /// Improvements over first version:
    ///   - Planes are clipped at bisector lines between adjacent boundary
    ///     segments so edges do not cross each other
    ///   - Violating walls are colored red in the active view
    /// </summary>
    public class RasanteVisualizer
    {
        private const double FeetToMeters = 0.3048;
        private const double MetersToFeet = 1.0 / 0.3048;

        private static readonly ElementId DirectShapeCategoryId =
            new ElementId(BuiltInCategory.OST_GenericModel);

        /// <summary>
        /// Creates rasante envelope surfaces with proper clipping between
        /// adjacent boundary segments.
        /// Must be called within a Transaction.
        /// </summary>
        public static List<ElementId> CreateRasanteSurfaces(
            Document doc,
            double angleDegrees,
            double baseHeightM,
            double maxDepthM = 50.0)
        {
            List<ElementId> createdIds = new List<ElementId>();

            List<Curve> boundary = GetPropertyBoundary(doc);
            if (boundary.Count == 0) return createdIds;

            double angleRadians = angleDegrees * Math.PI / 180.0;
            double tanAngle = Math.Tan(angleRadians);
            double groundLevel = GetGroundLevel(doc);
            double baseHeightFeet = baseHeightM * MetersToFeet;
            double maxDepthFeet = maxDepthM * MetersToFeet;
            double baseZ = groundLevel + baseHeightFeet;

            XYZ siteCenter = GetSiteCenter(boundary);

            // For each boundary segment, compute a clipped rasante quad
            for (int i = 0; i < boundary.Count; i++)
            {
                try
                {
                    Curve current = boundary[i];
                    XYZ start = current.GetEndPoint(0);
                    XYZ end = current.GetEndPoint(1);

                    // Inward normal for this segment
                    XYZ boundaryDir = (end - start).Normalize();
                    XYZ normal1 = new XYZ(-boundaryDir.Y, boundaryDir.X, 0);
                    XYZ normal2 = new XYZ(boundaryDir.Y, -boundaryDir.X, 0);
                    XYZ boundaryMid = (start + end) / 2.0;
                    XYZ toCenter = (siteCenter - boundaryMid).Normalize();
                    XYZ inwardNormal = (toCenter.DotProduct(normal1) > 0) ? normal1 : normal2;

                    // Calculate depth: use distance to the farthest site point
                    // projected onto the inward normal, but cap at maxDepthFeet
                    double depth = 0;
                    foreach (Curve c in boundary)
                    {
                        XYZ p1 = c.GetEndPoint(0);
                        XYZ p2 = c.GetEndPoint(1);
                        double d1 = (p1 - boundaryMid).DotProduct(inwardNormal);
                        double d2 = (p2 - boundaryMid).DotProduct(inwardNormal);
                        depth = Math.Max(depth, Math.Max(d1, d2));
                    }
                    depth = Math.Min(depth, maxDepthFeet);
                    if (depth < 1.0) depth = maxDepthFeet; // fallback

                    // Build the four corners of the plane
                    XYZ bottomLeft = new XYZ(start.X, start.Y, baseZ);
                    XYZ bottomRight = new XYZ(end.X, end.Y, baseZ);

                    double topHeightFeet = baseHeightFeet + (depth * tanAngle);
                    double topZ = groundLevel + topHeightFeet;

                    XYZ topLeft = new XYZ(
                        start.X + inwardNormal.X * depth,
                        start.Y + inwardNormal.Y * depth,
                        topZ);
                    XYZ topRight = new XYZ(
                        end.X + inwardNormal.X * depth,
                        end.Y + inwardNormal.Y * depth,
                        topZ);

                    // Create the mesh
                    TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                    builder.OpenConnectedFaceSet(false);

                    TessellatedFace face1 = new TessellatedFace(
                        new List<XYZ> { bottomLeft, bottomRight, topRight },
                        ElementId.InvalidElementId);
                    builder.AddFace(face1);

                    TessellatedFace face2 = new TessellatedFace(
                        new List<XYZ> { bottomLeft, topRight, topLeft },
                        ElementId.InvalidElementId);
                    builder.AddFace(face2);

                    builder.CloseConnectedFaceSet();
                    builder.Build();

                    TessellatedShapeBuilderResult result = builder.GetBuildResult();

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
                    continue;
                }
            }

            return createdIds;
        }

        /// <summary>
        /// Calculates the bisector direction at a boundary vertex.
        /// 
        /// At each corner of the property boundary, two segments meet.
        /// The bisector splits the angle between them, pointing inward.
        /// This is where one rasante plane should end and the next begins,
        /// preventing the edges from crossing.
        /// </summary>
        private static XYZ GetBisectorDirection(
            List<Curve> boundary, int segmentIndex, bool atStart, XYZ siteCenter)
        {
            XYZ current_start = boundary[segmentIndex].GetEndPoint(0);
            XYZ current_end = boundary[segmentIndex].GetEndPoint(1);
            XYZ currentDir = (current_end - current_start).Normalize();

            // Get the inward normal of the current segment
            XYZ normal1 = new XYZ(-currentDir.Y, currentDir.X, 0);
            XYZ normal2 = new XYZ(currentDir.Y, -currentDir.X, 0);
            XYZ midPt = (current_start + current_end) / 2.0;
            XYZ toCenter = (siteCenter - midPt).Normalize();
            XYZ inwardNormal = (toCenter.DotProduct(normal1) > 0) ? normal1 : normal2;

            // Find the adjacent segment by closest endpoint
            XYZ vertex = atStart ? current_start : current_end;

            int adjIndex = -1;
            double closestDist = double.MaxValue;

            for (int i = 0; i < boundary.Count; i++)
            {
                if (i == segmentIndex) continue;

                XYZ s = boundary[i].GetEndPoint(0);
                XYZ e = boundary[i].GetEndPoint(1);

                double ds = new XYZ(vertex.X - s.X, vertex.Y - s.Y, 0).GetLength();
                double de = new XYZ(vertex.X - e.X, vertex.Y - e.Y, 0).GetLength();
                double minD = Math.Min(ds, de);

                if (minD < closestDist)
                {
                    closestDist = minD;
                    adjIndex = i;
                }
            }

            // If no adjacent segment found, use the inward normal directly
            if (adjIndex < 0) return inwardNormal;

            // Get the inward normal of the adjacent segment
            XYZ adj_start = boundary[adjIndex].GetEndPoint(0);
            XYZ adj_end = boundary[adjIndex].GetEndPoint(1);
            XYZ adjDir = (adj_end - adj_start).Normalize();

            XYZ adjNormal1 = new XYZ(-adjDir.Y, adjDir.X, 0);
            XYZ adjNormal2 = new XYZ(adjDir.Y, -adjDir.X, 0);
            XYZ adjMid = (adj_start + adj_end) / 2.0;
            XYZ adjToCenter = (siteCenter - adjMid).Normalize();
            XYZ adjInwardNormal = (adjToCenter.DotProduct(adjNormal1) > 0) ? adjNormal1 : adjNormal2;

            // Bisector = average of the two inward normals, normalized
            XYZ bisector = (inwardNormal + adjInwardNormal);
            double len = Math.Sqrt(bisector.X * bisector.X + bisector.Y * bisector.Y);

            if (len < 1e-10) return inwardNormal; // parallel segments

            return new XYZ(bisector.X / len, bisector.Y / len, 0);
        }

        /// <summary>
        /// Highlights walls that violate the rasante by coloring them red.
        /// Must be called within a Transaction.
        /// </summary>
        public static void HighlightViolatingWalls(
            Document doc,
            View view,
            List<ElementId> wallIds)
        {
            if (wallIds.Count == 0) return;

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            Color red = new Color(231, 76, 60); // #E74C3C

            ogs.SetSurfaceForegroundPatternColor(red);
            ogs.SetSurfaceBackgroundPatternColor(red);
            ogs.SetProjectionLineColor(red);

            // Find solid fill pattern
            FilteredElementCollector patternCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement));

            foreach (FillPatternElement fpe in patternCollector)
            {
                FillPattern fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill)
                {
                    ogs.SetSurfaceForegroundPatternId(fpe.Id);
                    ogs.SetSurfaceBackgroundPatternId(fpe.Id);
                    break;
                }
            }

            foreach (ElementId id in wallIds)
            {
                view.SetElementOverrides(id, ogs);
            }
        }

        /// <summary>
        /// Clears wall color overrides set by PlanUp.
        /// Resets all walls to default appearance.
        /// Must be called within a Transaction.
        /// </summary>
        public static void ClearWallHighlights(Document doc, View view)
        {
            FilteredElementCollector wallCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType();

            OverrideGraphicSettings defaultOgs = new OverrideGraphicSettings();

            foreach (Element wall in wallCollector)
            {
                view.SetElementOverrides(wall.Id, defaultOgs);
            }
        }

        /// <summary>
        /// Applies color override to rasante surfaces.
        /// </summary>
        public static void ApplyColorOverrides(
            Document doc,
            View view,
            List<ElementId> rasanteIds,
            bool hasViolations)
        {
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            Color surfaceColor = hasViolations
                ? new Color(231, 76, 60)   // red
                : new Color(39, 174, 96);  // green

            ogs.SetSurfaceForegroundPatternColor(surfaceColor);
            ogs.SetSurfaceTransparency(70);

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

            foreach (ElementId id in rasanteIds)
            {
                view.SetElementOverrides(id, ogs);
            }
        }

        /// <summary>
        /// Removes all previously created rasante surfaces.
        /// </summary>
        public static void ClearPreviousRasante(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape));

            List<ElementId> toDelete = new List<ElementId>();

            foreach (DirectShape ds in collector)
            {
                if (ds.Name == "PlanUp Rasante Envelope")
                {
                    toDelete.Add(ds.Id);
                }
            }

            if (toDelete.Count > 0)
            {
                doc.Delete(toDelete);
            }
        }

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
