// MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <remarks>
    /// <para><b>Threading model.</b> This service is designed to be called from a
    /// background thread (so the UI stays responsive), but Rhino document mutation
    /// is NOT thread-safe and must happen on Rhino's main UI thread.</para>
    /// <para>To satisfy both constraints, the expensive, thread-safe work — parsing
    /// the file and converting meshes — runs inline on whatever thread calls
    /// <see cref="Import"/>. The only document mutation (creating layers and adding
    /// objects) is wrapped in a single delegate and marshalled onto the UI thread
    /// via <see cref="RhinoApp.InvokeOnUiThread(Delegate, object[])"/>. We block
    /// until that delegate finishes so the returned <see cref="ImportResult"/> is
    /// accurate and any document errors surface to the caller.</para>
    /// <para>If the caller is already on the UI thread (e.g. the synchronous
    /// <c>IfcLiteImport</c> command), <see cref="RhinoApp.InvokeOnUiThread"/> simply
    /// runs the delegate immediately, so the same code path is safe in both cases.</para>
    /// </remarks>
    public sealed class IfcImportService
    {
        /// <summary>
        /// Runs the import end to end.
        /// </summary>
        /// <param name="doc">The target Rhino document.</param>
        /// <param name="ifcPath">Absolute path to the <c>.ifc</c> file.</param>
        /// <param name="options">User options (coordinate mode, opening filter, join).</param>
        /// <param name="progress">Optional progress sink; may be <c>null</c>.</param>
        /// <param name="ct">Cancellation token, honoured between stages and per mesh.</param>
        /// <returns>A summary of what was imported.</returns>
        /// <exception cref="OperationCanceledException">If <paramref name="ct"/> is cancelled.</exception>
        /// <exception cref="IfcLiteException">If the native parser fails.</exception>
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

            // For "Shared" coordinates we apply the IfcSite placement so geometry
            // lands at its real-world position. The model already returns meshes in
            // site-local space, so "Project" needs no transform.
            double[]? siteTransform = model.SiteTransform;

            // ---- Stage 2: build Rhino meshes (background-safe) -------------------
            // We pair each built mesh with its source IfcMesh so the join/bake steps
            // can read the original metadata.
            var built = new List<BuiltMesh>(meshes.Count);
            var meshBuilder = new RhinoMeshBuilder();

            for (int i = 0; i < meshes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                IfcMesh src = meshes[i];

                Mesh? rhinoMesh = meshBuilder.Build(src, doc, options.CoordinateMode, siteTransform);
                if (rhinoMesh is not null && rhinoMesh.Faces.Count > 0)
                    built.Add(new BuiltMesh(rhinoMesh, src));

                // Reserve the 5..85 band for geometry conversion progress.
                if (meshes.Count > 0)
                {
                    int pct = 5 + (int)(80L * (i + 1) / meshes.Count);
                    // Only report occasionally to avoid flooding the UI thread.
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

            int objectCount = 0;
            Exception? docError = null;

            // This delegate is the ONLY code that touches the RhinoDoc. We marshal
            // it onto Rhino's UI thread; see the class remarks for the rationale.
            void AddToDocument()
            {
                try
                {
                    objectCount = AddJoinedObjects(doc, joined);
                }
                catch (Exception ex)
                {
                    // Capture and rethrow on the calling thread so the caller's
                    // try/catch (and the dialog's error reporting) still works.
                    docError = ex;
                }
            }

            // InvokeOnUiThread blocks until the delegate completes. If we are already
            // on the UI thread it executes synchronously in place.
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
        /// layer, baking the IFC metadata as user strings. Runs on the UI thread.
        /// </summary>
        private static int AddJoinedObjects(RhinoDoc doc, IReadOnlyList<JoinedObject> joined)
        {
            int count = 0;

            // Cache layer lookups so we only create each IfcType layer once.
            var layerCache = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (JoinedObject obj in joined)
            {
                if (obj.Mesh is null || obj.Mesh.Faces.Count == 0)
                    continue;

                // Resolve (creating if needed) the layer for this object's IfcType.
                if (!layerCache.TryGetValue(obj.IfcType, out int layerIndex))
                {
                    layerIndex = LayerHelper.GetOrCreateIfcTypeLayer(doc, obj.IfcType);
                    layerCache[obj.IfcType] = layerIndex;
                }

                var attr = new ObjectAttributes { LayerIndex = layerIndex };

                // Give the object a friendly name when one is available.
                if (!string.IsNullOrEmpty(obj.Representative.Name))
                    attr.Name = obj.Representative.Name;

                // Bake IFC metadata (type, ids, properties, …) as user strings so it
                // round-trips with the geometry and is queryable in Rhino.
                UserStringBaker.Bake(attr, obj.Representative);

                Guid id = doc.Objects.AddMesh(obj.Mesh, attr);
                if (id != Guid.Empty)
                    count++;
            }

            return count;
        }
    }

    /// <summary>
    /// A converted Rhino mesh paired with the source <see cref="IfcMesh"/> it came
    /// from, so later stages can read the original IFC metadata.
    /// </summary>
    internal readonly struct BuiltMesh
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
