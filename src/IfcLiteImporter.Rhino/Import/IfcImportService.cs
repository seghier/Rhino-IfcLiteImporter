// MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using IfcLite.Net;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace IfcLiteImporter.Rhino.Import
{
    /// <summary>
    /// Which coordinate frame the imported geometry should be placed in.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>
        /// "Project coordinates": geometry is placed near the Rhino origin, in the
        /// site-local frame that the parser already returns. This is the usual,
        /// comfortable-to-model choice.
        /// </summary>
        Project,

        /// <summary>
        /// "Shared coordinates": geometry is placed at its real-world position by
        /// applying the <c>IfcSite</c> placement transform. Useful for coordinating
        /// with surveyed / geo-referenced data.
        /// </summary>
        Shared,
    }

    /// <summary>
    /// User-selectable options for a single import. Defaults mirror the headless
    /// <c>IfcLiteImport</c> command: project coordinates, no opening filtering, and
    /// joining objects that share the same properties.
    /// </summary>
    public sealed class ImportOptions
    {
        /// <summary>Coordinate frame for the imported geometry.</summary>
        public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.Project;

        /// <summary>How door/window opening voids are handled by the parser.</summary>
        public OpeningFilterMode OpeningFilterMode { get; set; } = OpeningFilterMode.Default;

        /// <summary>
        /// When <c>true</c> (default), meshes that share identical properties are
        /// merged into a single Rhino object to keep the document tidy and light.
        /// </summary>
        public bool JoinByProperties { get; set; } = true;

        /// <summary>
        /// When <c>true</c> (default), adjacent coplanar faces are merged into single 
        /// n-gons using the document's absolute and angle tolerances.
        /// </summary>
        public bool MergeCoplanarFaces { get; set; } = true;
    }

    /// <summary>
    /// A progress update emitted during an import. Reported through
    /// <see cref="IProgress{T}"/> so callers (the Eto dialog, or the console
    /// command) can render it however they like.
    /// </summary>
    /// <param name="Percent">Completion in the range [0, 100].</param>
    /// <param name="Status">A short human-readable status line.</param>
    public readonly record struct ImportProgress(int Percent, string Status);

    /// <summary>
    /// Summary of a completed import, returned to the caller.
    /// </summary>
    public sealed class ImportResult
    {
        /// <summary>Number of Rhino objects added to the document.</summary>
        public int ObjectCount { get; init; }

        /// <summary>Number of source IFC meshes processed.</summary>
        public int MeshCount { get; init; }

        /// <summary>The IFC schema version reported by the parser (e.g. "IFC4").</summary>
        public string SchemaVersion { get; init; } = string.Empty;

        /// <summary>Total wall-clock time for the whole import, in milliseconds.</summary>
        public long ElapsedMs { get; init; }
    }

    /// <summary>
    /// Orchestrates a full IFC import: parse → build Rhino meshes → group/join →
    /// add to the document.
    /// </summary>
    public sealed class IfcImportService
    {
        /// <summary>
        /// Runs the import end to end.
        /// </summary>
        public ImportResult Import(
            RhinoDoc doc,
            string ifcPath,
            ImportOptions options,
            IProgress<ImportProgress>? progress,
            CancellationToken ct)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(ifcPath)) throw new ArgumentException("Path is required.", nameof(ifcPath));
            options ??= new ImportOptions();

            var stopwatch = Stopwatch.StartNew();

            // ---- Stage 1: parse (background-safe, no Rhino doc access) -----------
            progress?.Report(new ImportProgress(0, "Parsing IFC file…"));
            ct.ThrowIfCancellationRequested();

            IfcLiteModel model = IfcLiteParser.Parse(ifcPath, options.OpeningFilterMode);
            string schema = model.Metadata?.SchemaVersion ?? "unknown";
            IReadOnlyList<IfcMesh> meshes = model.Meshes;

            progress?.Report(new ImportProgress(
                5, $"Parsed {meshes.Count} meshes ({schema}). Building geometry…"));

            double[]? siteTransform = model.SiteTransform;

            // ---- Stage 2: build Rhino meshes (background-safe) -------------------
            var built = new List<BuiltMesh>(meshes.Count);
            var meshBuilder = new RhinoMeshBuilder();

            // Read the tolerances needed for the coplanar merge operation
            double absTol = doc.ModelAbsoluteTolerance;
            double angleTol = doc.ModelAngleToleranceRadians;

            for (int i = 0; i < meshes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                IfcMesh src = meshes[i];

                Mesh? rhinoMesh = meshBuilder.Build(src, doc, options.CoordinateMode, siteTransform);
                if (rhinoMesh is not null && rhinoMesh.Faces.Count > 0)
                {
                    if (options.MergeCoplanarFaces)
                    {
                        // Fix Bug 1: Weld coincident vertices of the triangle soup 
                        // to establish shared topological edges within the individual component mesh.
                        rhinoMesh.Vertices.CombineIdentical(true, true);
                        rhinoMesh.Weld(angleTol);

                        // Fix Bug 2: Explicitly populate the FaceNormals collection 
                        // so MergeAllCoplanarFaces can verify coplanarity.
                        rhinoMesh.FaceNormals.ComputeFaceNormals();

                        // Run the coplanar merge on the isolated component
                        rhinoMesh.MergeAllCoplanarFaces(absTol, angleTol);

                        // Fix Color Loss: Re-apply the vertex colors based on the new, reduced vertex count.
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

                    built.Add(new BuiltMesh(rhinoMesh, src));
                }

                // Reserve the 5..85 band for geometry conversion progress.
                if (meshes.Count > 0)
                {
                    int pct = 5 + (int)(80L * (i + 1) / meshes.Count);
                    if (i % 25 == 0 || i == meshes.Count - 1)
                        progress?.Report(new ImportProgress(pct, $"Building geometry {i + 1}/{meshes.Count}…"));
                }
            }

            // ---- Stage 3: group / join by shared properties (background-safe) ----
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ImportProgress(88, "Grouping objects…"));
            IReadOnlyList<JoinedObject> joined = PropertyJoiner.Join(built, options.JoinByProperties, ct);

            // ---- Stage 4: write to the document (MUST run on the UI thread) ------
            progress?.Report(new ImportProgress(92, $"Adding {joined.Count} objects to the document…"));
            ct.ThrowIfCancellationRequested();

            string rootLayerName = Path.GetFileNameWithoutExtension(ifcPath);
            if (string.IsNullOrWhiteSpace(rootLayerName))
                rootLayerName = LayerHelper.DefaultRootLayerName;

            int objectCount = 0;
            Exception? docError = null;

            void AddToDocument()
            {
                try
                {
                    objectCount = AddJoinedObjects(doc, joined, rootLayerName);
                }
                catch (Exception ex)
                {
                    docError = ex;
                }
            }

            RhinoApp.InvokeOnUiThread((Action)AddToDocument);

            if (docError is not null)
                throw docError;

            stopwatch.Stop();
            progress?.Report(new ImportProgress(100, $"Done. Added {objectCount} objects in {stopwatch.ElapsedMilliseconds} ms."));

            return new ImportResult
            {
                ObjectCount = objectCount,
                MeshCount = meshes.Count,
                SchemaVersion = schema,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }

        /// <summary>
        /// Adds every joined object to the document on the correct per-IfcType
        /// layer, nesting them under the file's root layer and baking the IFC metadata.
        /// Runs on the UI thread.
        /// </summary>
        private static int AddJoinedObjects(RhinoDoc doc, IReadOnlyList<JoinedObject> joined, string rootLayerName)
        {
            int count = 0;

            var layerCache = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (JoinedObject obj in joined)
            {
                if (obj.Mesh is null || obj.Mesh.Faces.Count == 0)
                    continue;

                if (!layerCache.TryGetValue(obj.IfcType, out int layerIndex))
                {
                    layerIndex = LayerHelper.GetOrCreateIfcTypeLayer(doc, obj.IfcType, rootLayerName);
                    layerCache[obj.IfcType] = layerIndex;
                }

                var attr = new ObjectAttributes { LayerIndex = layerIndex };

                if (!string.IsNullOrEmpty(obj.Representative.Name))
                    attr.Name = obj.Representative.Name;

                UserStringBaker.Bake(attr, obj.Representative);

                Guid id = doc.Objects.AddMesh(obj.Mesh, attr);
                if (id != Guid.Empty)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Converts an RGB(A) float quad in [0,1] to a <see cref="System.Drawing.Color"/>.
        /// </summary>
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

    /// <summary>
    /// A converted Rhino mesh paired with the source <see cref="IfcMesh"/> it came
    /// from, so later stages can read the original IFC metadata.
    /// </summary>
    public readonly struct BuiltMesh
    {
        public BuiltMesh(Mesh mesh, IfcMesh source)
        {
            Mesh = mesh;
            Source = source;
        }

        public Mesh Mesh { get; }
        public IfcMesh Source { get; }
    }
}