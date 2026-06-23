// MIT License. See LICENSE in the repository root.

using System;
using IfcLite.Net;
using Rhino;
using Rhino.Geometry;

namespace IfcLiteImporter.Rhino.Import
{
    /// <summary>
    /// Converts an <see cref="IfcMesh"/> (flat float arrays, in metres) into a
    /// <see cref="Rhino.Geometry.Mesh"/> in the active document's unit system.
    /// </summary>
    /// <remarks>
    /// This class performs no document mutation and is safe to call from a
    /// background thread. (Reading <see cref="RhinoDoc.ModelUnitSystem"/> is a
    /// cheap, thread-safe property read.)
    /// </remarks>
    public sealed class RhinoMeshBuilder
    {
        /// <summary>
        /// Builds a Rhino mesh from a single IFC mesh.
        /// </summary>
        /// <param name="src">The source mesh (positions/normals in metres, xyz triplets).</param>
        /// <param name="doc">The target document, used only to read the model unit system.</param>
        /// <param name="mode">Project (site-local) or Shared (real-world) coordinates.</param>
        /// <param name="siteTransform">
        /// The column-major 4x4 <c>site_transform</c> (16 doubles, metres). Required for
        /// <see cref="CoordinateMode.Shared"/>; ignored for <see cref="CoordinateMode.Project"/>.
        /// </param>
        /// <returns>The converted mesh, or <c>null</c> if the source has no usable geometry.</returns>
        public Mesh? Build(IfcMesh src, RhinoDoc doc, CoordinateMode mode, double[]? siteTransform)
        {
            if (src is null) throw new ArgumentNullException(nameof(src));

            float[] positions = src.Positions;
            uint[] indices = src.Indices;

            // Positions come as xyz triplets; anything else is malformed/empty.
            if (positions is null || positions.Length < 9 || positions.Length % 3 != 0)
                return null;
            if (indices is null || indices.Length < 3)
                return null;

            int vertexCount = positions.Length / 3;

            // The parser emits geometry in metres. Convert to the document's units.
            double unitScale = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);

            // For Shared coordinates we first lift each vertex into the real-world
            // site frame, THEN apply the unit scale. (Project coordinates skip the
            // site transform entirely.)
            bool applySite = mode == CoordinateMode.Shared
                             && siteTransform is { Length: 16 };
            Transform site = applySite
                ? TransformFromColumnMajor(siteTransform!)
                : Transform.Identity;

            var mesh = new Mesh();

            // ---- Vertices -------------------------------------------------------
            for (int v = 0; v < vertexCount; v++)
            {
                int o = v * 3;
                double x = positions[o];
                double y = positions[o + 1];
                double z = positions[o + 2];

                if (applySite)
                {
                    // The site transform is expressed in metres, matching the raw
                    // positions, so it must be applied before unit scaling.
                    var p = new Point3d(x, y, z);
                    p.Transform(site);
                    x = p.X; y = p.Y; z = p.Z;
                }

                mesh.Vertices.Add(x * unitScale, y * unitScale, z * unitScale);
            }

            // ---- Faces ----------------------------------------------------------
            // Indices describe triangles (three indices per face).
            int triangleCount = indices.Length / 3;
            for (int t = 0; t < triangleCount; t++)
            {
                int i = t * 3;
                int a = (int)indices[i];
                int b = (int)indices[i + 1];
                int c = (int)indices[i + 2];

                // Skip out-of-range indices defensively rather than throwing.
                if (a < 0 || b < 0 || c < 0 ||
                    a >= vertexCount || b >= vertexCount || c >= vertexCount)
                    continue;

                mesh.Faces.AddFace(a, b, c);
            }

            // ---- Normals (optional) --------------------------------------------
            // Use the supplied normals when they line up 1:1 with the vertices;
            // otherwise let Rhino compute them so shading still looks right.
            float[] normals = src.Normals;
            if (normals is { Length: > 0 } && normals.Length == positions.Length)
            {
                mesh.Normals.Clear();
                for (int v = 0; v < vertexCount; v++)
                {
                    int o = v * 3;
                    mesh.Normals.Add(normals[o], normals[o + 1], normals[o + 2]);
                }
            }
            else
            {
                mesh.Normals.ComputeNormals();
            }

            // ---- Vertex colours (optional) -------------------------------------
            // IfcMesh.Color is an RGBA float quad in [0,1]. Paint every vertex so
            // the object shows its material colour in shaded views.
            float[] color = src.Color;
            if (color is { Length: >= 3 })
            {
                System.Drawing.Color c = FloatColorToArgb(color);
                mesh.VertexColors.Clear();
                for (int v = 0; v < vertexCount; v++)
                    mesh.VertexColors.Add(c);
            }

            // Tidy up any degenerate data the source may have contained.
            mesh.Compact();
            return mesh;
        }

        /// <summary>
        /// Builds a Rhino <see cref="Transform"/> from a column-major 4x4 matrix
        /// stored as 16 doubles (the layout used by <c>site_transform</c> /
        /// <c>building_transform</c>).
        /// </summary>
        /// <remarks>
        /// Column-major means the array is [col0(4), col1(4), col2(4), col3(4)], so
        /// element <c>m[row + 4*col]</c>. Rhino's <see cref="Transform"/> indexer is
        /// <c>[row, col]</c>.
        /// </remarks>
        public static Transform TransformFromColumnMajor(double[] m)
        {
            if (m is null) throw new ArgumentNullException(nameof(m));
            if (m.Length != 16) throw new ArgumentException("Expected 16 doubles (4x4).", nameof(m));

            var t = new Transform();
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    t[row, col] = m[row + 4 * col];
            return t;
        }

        /// <summary>
        /// Converts an RGB(A) float quad in [0,1] to a <see cref="System.Drawing.Color"/>.
        /// </summary>
        private static System.Drawing.Color FloatColorToArgb(float[] rgba)
        {
            // Replaced Math.Clamp to ensure standard .NET Framework compatibility
            byte ToByte(float f) => (byte)Math.Round((f < 0f ? 0f : (f > 1f ? 1f : f)) * 255f);
            byte r = ToByte(rgba[0]);
            byte g = ToByte(rgba[1]);
            byte b = ToByte(rgba[2]);
            byte a = rgba.Length >= 4 ? ToByte(rgba[3]) : (byte)255;
            return System.Drawing.Color.FromArgb(a, r, g, b);
        }
    }
}