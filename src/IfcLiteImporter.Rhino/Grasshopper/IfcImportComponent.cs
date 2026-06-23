// MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using IfcLite.Net;
using IfcLiteImporter.Rhino.Import;
using Rhino;
using Rhino.Geometry;

namespace IfcLiteImporter.Rhino.Grasshopper
{
    /// <summary>
    /// A custom Grasshopper component that imports an IFC file, using dual-level caching
    /// to ensure rapid recalculations when input parameters are changed.
    /// </summary>
    public sealed class IfcImportComponent : GH_Component
    {
        // ---- Level 1 Cache: Parsed IFC file ----------------------------------
        private string? _cachedFilePath;
        private DateTime _cachedLastWriteTime;
        private IfcLiteModel? _cachedModel;

        // ---- Level 2 Cache: Built and coplanar-merged Rhino meshes ----------
        private List<BuiltMesh>? _cachedBuiltMeshes;
        private CoordinateMode? _cachedCoordMode;
        private bool? _cachedMergeCoplanar;

        public IfcImportComponent()
          : base("IFC Import", "IfcImport",
              "Imports an IFC file into Grasshopper using smart caching for fast parameter changes.",
              "IfcLite", "Import")
        {
        }

        public override Guid ComponentGuid => new Guid("e9b9cf9b-b6fb-40db-9080-60b777a83d7a");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "F", "Absolute path to the .ifc file", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Coordinate Mode", "M", "0 = Project (site-local), 1 = Shared (real-world site transform)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Join by Properties", "J", "When true, meshes that share identical properties are merged", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Merge Coplanar", "MC", "When true, adjacent coplanar faces are merged into single n-gons", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "The imported meshes grouped by IFC type", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "C", "The material color of each mesh", GH_ParamAccess.list);
            pManager.AddTextParameter("User Strings", "U", "Data tree of metadata for each mesh where each branch corresponds to a mesh index", GH_ParamAccess.tree);
            pManager.AddTextParameter("Types", "T", "The IFC Type of each mesh (e.g. IfcWall)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string ifcPath = string.Empty;
            if (!DA.GetData(0, ref ifcPath)) return;

            if (string.IsNullOrWhiteSpace(ifcPath) || !File.Exists(ifcPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {ifcPath}");
                ClearCache();
                return;
            }

            int coordModeInt = 0;
            DA.GetData(1, ref coordModeInt);
            CoordinateMode coordMode = coordModeInt == 1 ? CoordinateMode.Shared : CoordinateMode.Project;

            bool joinByProperties = true;
            DA.GetData(2, ref joinByProperties);

            bool mergeCoplanar = true;
            DA.GetData(3, ref mergeCoplanar);

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc is null) return;

            try
            {
                // ---- Level 1 Cache Check: Parse the file only if path or write time changed ----
                DateTime currentWriteTime = File.GetLastWriteTime(ifcPath);
                bool isFileCacheValid = _cachedModel != null
                                       && string.Equals(_cachedFilePath, ifcPath, StringComparison.OrdinalIgnoreCase)
                                       && _cachedLastWriteTime == currentWriteTime;

                if (!isFileCacheValid)
                {
                    _cachedModel = IfcLiteParser.Parse(ifcPath, OpeningFilterMode.Default);
                    _cachedFilePath = ifcPath;
                    _cachedLastWriteTime = currentWriteTime;

                    // The source model changed, so we must invalidate the subsequent built meshes cache
                    _cachedBuiltMeshes = null;
                }

                // ---- Level 2 Cache Check: Build and merge meshes only if coordinates or coplanar settings changed ----
                bool isGeometryCacheValid = _cachedBuiltMeshes != null
                                            && _cachedCoordMode == coordMode
                                            && _cachedMergeCoplanar == mergeCoplanar;

                List<BuiltMesh> builtMeshes;
                if (isGeometryCacheValid)
                {
                    builtMeshes = _cachedBuiltMeshes!;
                }
                else
                {
                    IReadOnlyList<IfcMesh> rawMeshes = _cachedModel!.Meshes;
                    double[]? siteTransform = _cachedModel.SiteTransform;

                    builtMeshes = new List<BuiltMesh>(rawMeshes.Count);
                    var meshBuilder = new RhinoMeshBuilder();
                    double absTol = doc.ModelAbsoluteTolerance;
                    double angleTol = doc.ModelAngleToleranceRadians;

                    foreach (IfcMesh src in rawMeshes)
                    {
                        Mesh? rhinoMesh = meshBuilder.Build(src, doc, coordMode, siteTransform);
                        if (rhinoMesh is not null && rhinoMesh.Faces.Count > 0)
                        {
                            if (mergeCoplanar)
                            {
                                // Repair flat triangle soup connectivity
                                rhinoMesh.Vertices.CombineIdentical(true, true);
                                rhinoMesh.Weld(angleTol);

                                // Populate face direction data explicitly
                                rhinoMesh.FaceNormals.ComputeFaceNormals();

                                // Merge coplanar faces
                                rhinoMesh.MergeAllCoplanarFaces(absTol, angleTol);

                                // Re-apply original vertex colors matching the new post-weld vertex count
                                float[] color = src.Color;
                                if (color is { Length: >= 3 })
                                {
                                    System.Drawing.Color c = FloatColorToArgb(color);
                                    rhinoMesh.VertexColors.Clear();
                                    for (int v = 0; v < rhinoMesh.Vertices.Count; v++)
                                    {
                                        rhinoMesh.VertexColors.Add(c);
                                    }
                                }
                            }

                            builtMeshes.Add(new BuiltMesh(rhinoMesh, src));
                        }
                    }

                    // Save to geometry cache
                    _cachedBuiltMeshes = builtMeshes;
                    _cachedCoordMode = coordMode;
                    _cachedMergeCoplanar = mergeCoplanar;
                }

                // ---- Stage 3: Group/Join (This lightweight stage runs if 'Join by Properties' is toggled) ----
                IReadOnlyList<JoinedObject> joined = PropertyJoiner.Join(builtMeshes, joinByProperties, CancellationToken.None);

                // Sort alphabetically by IfcType to replicate the order used in your layer tree
                var sortedObjects = joined
                    .OrderBy(o => o.IfcType, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(o => o.Representative.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Prepare Output Collections
                var outputMeshes = new List<Mesh>(sortedObjects.Count);
                var outputColors = new List<System.Drawing.Color>(sortedObjects.Count);
                var outputTypes = new List<string>(sortedObjects.Count);
                var outputUserstrings = new DataTree<string>();

                for (int i = 0; i < sortedObjects.Count; i++)
                {
                    var obj = sortedObjects[i];
                    outputMeshes.Add(obj.Mesh);
                    outputTypes.Add(obj.IfcType);

                    // Extract Color
                    System.Drawing.Color meshColor = System.Drawing.Color.LightGray; // fallback
                    if (obj.Representative.Color is { Length: >= 3 })
                    {
                        meshColor = FloatColorToArgb(obj.Representative.Color);
                    }
                    outputColors.Add(meshColor);

                    // Extract Metadata User Strings to DataTree branch
                    var path = new GH_Path(i);

                    // General classification attributes
                    AddUserString(outputUserstrings, path, "IfcType", obj.IfcType);
                    AddUserString(outputUserstrings, path, "IfcExpressId", obj.Representative.ExpressId.ToString(CultureInfo.InvariantCulture));
                    AddUserString(outputUserstrings, path, "IfcGlobalId", obj.Representative.GlobalId);
                    AddUserString(outputUserstrings, path, "IfcName", obj.Representative.Name);
                    AddUserString(outputUserstrings, path, "PresentationLayer", obj.Representative.PresentationLayer);
                    AddUserString(outputUserstrings, path, "MaterialName", obj.Representative.MaterialName);

                    // Custom Property Set values
                    if (obj.Representative.Properties is not null)
                    {
                        foreach (var kvp in obj.Representative.Properties)
                        {
                            AddUserString(outputUserstrings, path, kvp.Key, kvp.Value);
                        }
                    }
                }

                // Set Outputs
                DA.SetDataList(0, outputMeshes);
                DA.SetDataList(1, outputColors);
                DA.SetDataTree(2, outputUserstrings);
                DA.SetDataList(3, outputTypes);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"IFC Import failed: {ex.Message}");
                ClearCache();
            }
        }

        private void ClearCache()
        {
            _cachedFilePath = null;
            _cachedLastWriteTime = default;
            _cachedModel = null;
            _cachedBuiltMeshes = null;
            _cachedCoordMode = null;
            _cachedMergeCoplanar = null;
        }

        private static void AddUserString(DataTree<string> tree, GH_Path path, string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;
            tree.Add($"{key} = {value}", path);
        }

        private static System.Drawing.Color FloatColorToArgb(float[] rgba)
        {
            byte ToByte(float f) => (byte)Math.Round((f < 0f ? 0f : (f > 1f ? 1f : f)) * 255f);
            byte r = ToByte(rgba[0]);
            byte g = ToByte(rgba[1]);
            byte b = ToByte(rgba[2]);
            byte a = rgba.Length >= 4 ? ToByte(rgba[3]) : (byte)255;
            return System.Drawing.Color.FromArgb(a, r, g, b);
        }
    }
}