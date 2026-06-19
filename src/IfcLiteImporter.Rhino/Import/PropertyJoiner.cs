// MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using IfcLite.Net;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace IfcLiteImporter.Rhino.Import
{
    /// <summary>
    /// One Rhino object ready to be added to the document: the (possibly merged)
    /// mesh, plus a representative <see cref="IfcMesh"/> whose metadata is baked
    /// onto the object as user strings.
    /// </summary>
    public sealed class JoinedObject
    {
        public JoinedObject(Mesh mesh, IfcMesh representative)
        {
            Mesh = mesh;
            Representative = representative;
        }

        /// <summary>The geometry for this object.</summary>
        public Mesh Mesh { get; }

        /// <summary>
        /// The IFC mesh whose metadata represents the whole group. When several
        /// meshes are joined they share identical join-relevant properties, so any
        /// member is a valid representative.
        /// </summary>
        public IfcMesh Representative { get; }

        /// <summary>Convenience accessor for the representative's IFC type.</summary>
        public string IfcType => string.IsNullOrEmpty(Representative.IfcType) ? "IfcProduct" : Representative.IfcType;
    }

    /// <summary>
    /// Groups built meshes that share the same properties and merges each group
    /// into a single Rhino object. This keeps the document light when an IFC file
    /// contains thousands of near-identical elements.
    /// </summary>
    public static class PropertyJoiner
    {
        /// <summary>
        /// Joins meshes by a property signature.
        /// </summary>
        /// <param name="built">The converted meshes paired with their source metadata.</param>
        /// <param name="joinByProperties">
        /// When <c>true</c>, meshes sharing a signature are merged into one object.
        /// When <c>false</c>, every mesh becomes its own object.
        /// </param>
        /// <param name="ct">Cancellation token, checked while merging.</param>
        /// <returns>The list of objects to add to the document.</returns>
        internal static IReadOnlyList<JoinedObject> Join(
            IReadOnlyList<BuiltMesh> built,
            bool joinByProperties,
            CancellationToken ct)
        {
            if (built is null || built.Count == 0)
                return Array.Empty<JoinedObject>();

            // Fast path: one object per mesh, no grouping.
            if (!joinByProperties)
            {
                var single = new List<JoinedObject>(built.Count);
                foreach (BuiltMesh bm in built)
                    single.Add(new JoinedObject(bm.Mesh, bm.Source));
                return single;
            }

            // Group by a deterministic signature built from the join-relevant
            // properties. Preserve first-seen order so the result is stable.
            var groups = new Dictionary<string, List<BuiltMesh>>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (BuiltMesh bm in built)
            {
                ct.ThrowIfCancellationRequested();
                string sig = BuildSignature(bm.Source);
                if (!groups.TryGetValue(sig, out List<BuiltMesh>? list))
                {
                    list = new List<BuiltMesh>();
                    groups[sig] = list;
                    order.Add(sig);
                }
                list.Add(bm);
            }

            var result = new List<JoinedObject>(order.Count);
            foreach (string sig in order)
            {
                ct.ThrowIfCancellationRequested();
                List<BuiltMesh> members = groups[sig];

                if (members.Count == 1)
                {
                    result.Add(new JoinedObject(members[0].Mesh, members[0].Source));
                    continue;
                }

                // Merge all member meshes into one. Mesh.Append re-bases vertex
                // indices for us, so colours and normals are carried over.
                var merged = new Mesh();
                foreach (BuiltMesh m in members)
                    merged.Append(m.Mesh);
                merged.Compact();

                // Any member is a valid representative — they share the signature.
                result.Add(new JoinedObject(merged, members[0].Source));
            }

            return result;
        }

        /// <summary>
        /// Builds the signature that decides whether two meshes may be joined.
        /// Meshes with identical signatures are considered "the same kind of thing".
        /// </summary>
        /// <remarks>
        /// The signature intentionally combines: IFC type, material name,
        /// presentation layer, the (rounded) colour, and the full sorted property
        /// set. Rounding the colour avoids spurious mismatches from tiny float
        /// differences; sorting the properties makes the signature order-independent.
        /// </remarks>
        private static string BuildSignature(IfcMesh mesh)
        {
            var sb = new StringBuilder(128);

            sb.Append(mesh.IfcType ?? string.Empty).Append('|');
            sb.Append(mesh.MaterialName ?? string.Empty).Append('|');
            sb.Append(mesh.PresentationLayer ?? string.Empty).Append('|');
            sb.Append(ColorKey(mesh.Color)).Append('|');

            if (mesh.Properties is { Count: > 0 })
            {
                // Sort by key so equal property sets always hash identically.
                foreach (KeyValuePair<string, string> kvp in mesh.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append(';');
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Produces a stable, rounded string key for an RGBA colour so that
        /// imperceptible float differences do not split a group.
        /// </summary>
        private static string ColorKey(float[]? color)
        {
            if (color is null || color.Length == 0)
                return "-";

            var sb = new StringBuilder(24);
            for (int i = 0; i < color.Length; i++)
            {
                // Round to 3 decimals — well below 1/255 perceptual resolution.
                sb.Append(Math.Round(color[i], 3).ToString("0.###", CultureInfo.InvariantCulture));
                if (i < color.Length - 1) sb.Append(',');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Helpers for placing imported objects onto a tidy, per-type layer structure:
    /// a single root "IFC" layer with one child layer per IFC type.
    /// </summary>
    public static class LayerHelper
    {
        /// <summary>The name of the parent layer all imported IFC layers live under.</summary>
        public const string RootLayerName = "IFC";

        /// <summary>
        /// Returns the index of the layer for <paramref name="ifcType"/> (e.g.
        /// "IfcWall"), creating it — and the parent "IFC" layer — if necessary.
        /// </summary>
        /// <remarks>Must be called on the Rhino UI thread (it mutates the layer table).</remarks>
        public static int GetOrCreateIfcTypeLayer(RhinoDoc doc, string ifcType)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(ifcType))
                ifcType = "IfcProduct";

            int rootIndex = GetOrCreateRootLayer(doc);

            // Look for an existing child layer with this name under the root.
            Layer? parent = doc.Layers.FindIndex(rootIndex);
            if (parent is not null)
            {
                foreach (Layer child in doc.Layers)
                {
                    if (!child.IsDeleted &&
                        child.ParentLayerId == parent.Id &&
                        string.Equals(child.Name, ifcType, StringComparison.Ordinal))
                    {
                        return child.Index;
                    }
                }
            }

            // Not found — create it nested under the root layer.
            var layer = new Layer
            {
                Name = ifcType,
                ParentLayerId = parent?.Id ?? Guid.Empty,
            };
            int index = doc.Layers.Add(layer);

            // Layers.Add returns -1 on failure; fall back to the root so the object
            // still gets placed somewhere sensible.
            return index >= 0 ? index : rootIndex;
        }

        /// <summary>
        /// Returns the index of the root "IFC" layer, creating it if needed.
        /// </summary>
        private static int GetOrCreateRootLayer(RhinoDoc doc)
        {
            // A top-level layer has no parent (Guid.Empty).
            foreach (Layer layer in doc.Layers)
            {
                if (!layer.IsDeleted &&
                    layer.ParentLayerId == Guid.Empty &&
                    string.Equals(layer.Name, RootLayerName, StringComparison.Ordinal))
                {
                    return layer.Index;
                }
            }

            var root = new Layer { Name = RootLayerName };
            int index = doc.Layers.Add(root);
            return index >= 0 ? index : doc.Layers.CurrentLayerIndex;
        }
    }
}
