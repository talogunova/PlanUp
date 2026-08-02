using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PlanUp.Extractors
{
    public class RasanteVisualizer
    {
        private const double FeetToMeters = 0.3048;
        private const double MetersToFeet = 1.0 / 0.3048;
        private static readonly ElementId DirectShapeCategoryId = new ElementId(BuiltInCategory.OST_GenericModel);

        public class PlaneInfo
        {
            public ElementId Id { get; set; }
            public int BoundaryIndex { get; set; }
        }

        public static List<PlaneInfo> CreateRasanteSurfacesWithIndex(Document doc, double angleDegrees, double baseHeightM, double maxDepthM = 50.0)
        {
            List<PlaneInfo> planeInfos = new List<PlaneInfo>();
            List<Curve> boundary = GetPropertyBoundary(doc);
            if (boundary.Count == 0) return planeInfos;

            double angleRad = angleDegrees * Math.PI / 180.0;
            double tanAngle = Math.Tan(angleRad);
            double groundLevel = GetGroundLevel(doc);
            double baseHFeet = baseHeightM * MetersToFeet;
            double maxDepthFeet = maxDepthM * MetersToFeet;
            double baseZ = groundLevel + baseHFeet;
            XYZ siteCenter = GetSiteCenter(boundary);

            for (int i = 0; i < boundary.Count; i++)
            {
                try
                {
                    Curve current = boundary[i];
                    XYZ start = current.GetEndPoint(0);
                    XYZ end = current.GetEndPoint(1);

                    XYZ boundaryDir = (end - start).Normalize();
                    XYZ n1 = new XYZ(-boundaryDir.Y, boundaryDir.X, 0);
                    XYZ n2 = new XYZ(boundaryDir.Y, -boundaryDir.X, 0);
                    XYZ mid = (start + end) / 2.0;
                    XYZ toCenter = (siteCenter - mid).Normalize();
                    XYZ inward = (toCenter.DotProduct(n1) > 0) ? n1 : n2;

                    // Limit depth to half the distance across the site
                    double depth = 0;
                    foreach (Curve c in boundary)
                    {
                        double d1 = (c.GetEndPoint(0) - mid).DotProduct(inward);
                        double d2 = (c.GetEndPoint(1) - mid).DotProduct(inward);
                        depth = Math.Max(depth, Math.Max(d1, d2));
                    }
                    depth = Math.Min(depth * 0.5, maxDepthFeet);
                    if (depth < 1.0) depth = maxDepthFeet * 0.3;

                    double topH = baseHFeet + (depth * tanAngle);
                    double topZ = groundLevel + topH;

                    XYZ bottomLeft = new XYZ(start.X, start.Y, baseZ);
                    XYZ bottomRight = new XYZ(end.X, end.Y, baseZ);
                    XYZ topLeft = new XYZ(start.X + inward.X * depth, start.Y + inward.Y * depth, topZ);
                    XYZ topRight = new XYZ(end.X + inward.X * depth, end.Y + inward.Y * depth, topZ);

                    TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                    builder.OpenConnectedFaceSet(false);
                    builder.AddFace(new TessellatedFace(new List<XYZ> { bottomLeft, bottomRight, topRight }, ElementId.InvalidElementId));
                    builder.AddFace(new TessellatedFace(new List<XYZ> { bottomLeft, topRight, topLeft }, ElementId.InvalidElementId));
                    builder.CloseConnectedFaceSet();
                    builder.Build();

                    TessellatedShapeBuilderResult result = builder.GetBuildResult();
                    IList<GeometryObject> geomObjects = result.GetGeometricalObjects();

                    if (geomObjects.Count > 0)
                    {
                        DirectShape ds = DirectShape.CreateElement(doc, DirectShapeCategoryId);
                        ds.ApplicationId = "PlanUp";
                        ds.ApplicationDataId = "RasanteEnvelope";
                        ds.SetShape(geomObjects);
                        ds.SetName("PlanUp Rasante Envelope");
                        planeInfos.Add(new PlaneInfo { Id = ds.Id, BoundaryIndex = i });
                    }
                }
                catch { continue; }
            }

            return planeInfos;
        }

        public static void ApplyPerPlaneColors(Document doc, View view, List<PlaneInfo> planes, HashSet<int> violatingBoundaryIndices)
        {
            ElementId solidPatternId = ElementId.InvalidElementId;
            foreach (FillPatternElement fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
            {
                FillPattern fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill) { solidPatternId = fpe.Id; break; }
            }

            foreach (PlaneInfo plane in planes)
            {
                bool isViolating = violatingBoundaryIndices.Contains(plane.BoundaryIndex);
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                Color color = isViolating ? new Color(231, 76, 60) : new Color(39, 174, 96);
                ogs.SetSurfaceForegroundPatternColor(color);
                ogs.SetSurfaceTransparency(70);
                if (solidPatternId != ElementId.InvalidElementId) ogs.SetSurfaceForegroundPatternId(solidPatternId);
                view.SetElementOverrides(plane.Id, ogs);
            }
        }

        public static void HighlightViolatingWalls(Document doc, View view, List<ElementId> wallIds)
        {
            if (wallIds.Count == 0) return;
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            Color red = new Color(231, 76, 60);
            ogs.SetSurfaceForegroundPatternColor(red);
            ogs.SetSurfaceBackgroundPatternColor(red);
            ogs.SetProjectionLineColor(red);
            foreach (FillPatternElement fpe in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)))
            {
                FillPattern fp = fpe.GetFillPattern();
                if (fp != null && fp.IsSolidFill) { ogs.SetSurfaceForegroundPatternId(fpe.Id); ogs.SetSurfaceBackgroundPatternId(fpe.Id); break; }
            }
            foreach (ElementId id in wallIds) view.SetElementOverrides(id, ogs);
        }

        public static void ClearWallHighlights(Document doc, View view)
        {
            var wc = new FilteredElementCollector(doc, view.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType();
            OverrideGraphicSettings def = new OverrideGraphicSettings();
            foreach (Element w in wc) view.SetElementOverrides(w.Id, def);
        }

        public static void ClearPreviousRasante(Document doc)
        {
            List<ElementId> toDelete = new List<ElementId>();
            foreach (DirectShape ds in new FilteredElementCollector(doc).OfClass(typeof(DirectShape)))
            {
                if (ds.Name == "PlanUp Rasante Envelope") toDelete.Add(ds.Id);
            }
            if (toDelete.Count > 0) doc.Delete(toDelete);
        }

        private static XYZ GetSiteCenter(List<Curve> boundary)
        {
            double sx = 0, sy = 0; int n = 0;
            foreach (Curve c in boundary) { XYZ m = (c.GetEndPoint(0) + c.GetEndPoint(1)) / 2.0; sx += m.X; sy += m.Y; n++; }
            return n == 0 ? XYZ.Zero : new XYZ(sx / n, sy / n, 0);
        }

        private static double GetGroundLevel(Document doc)
        {
            double closest = 0; double smallest = double.MaxValue;
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)))
            { double d = Math.Abs(l.Elevation); if (d < smallest) { smallest = d; closest = l.Elevation; } }
            return closest;
        }

        private static List<Curve> GetPropertyBoundary(Document doc)
        {
            List<Curve> curves = new List<Curve>();
            BuiltInCategory[] cats = { BuiltInCategory.OST_SitePropertyLineSegment, BuiltInCategory.OST_SiteProperty, BuiltInCategory.OST_Site };
            foreach (BuiltInCategory cat in cats)
            {
                try
                {
                    foreach (Element e in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
                    { Options o = new Options(); GeometryElement ge = e.get_Geometry(o); if (ge != null) ExtractCurves(ge, curves); }
                    if (curves.Count > 0) return curves;
                }
                catch { }
            }
            try
            {
                foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(PropertyLine)))
                { Options o = new Options(); GeometryElement ge = e.get_Geometry(o); if (ge != null) ExtractCurves(ge, curves); }
            }
            catch { }
            return curves;
        }

        private static void ExtractCurves(GeometryElement ge, List<Curve> curves)
        {
            foreach (GeometryObject go in ge)
            {
                if (go is Curve c) curves.Add(c);
                else if (go is GeometryInstance gi) { GeometryElement ig = gi.GetInstanceGeometry(); if (ig != null) ExtractCurves(ig, curves); }
            }
        }
    }
}
