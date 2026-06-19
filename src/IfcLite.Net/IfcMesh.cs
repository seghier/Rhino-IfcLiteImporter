// MIT License. See LICENSE in the repository root.

using System.Collections.Generic;

namespace IfcLite.Net
{
    /// <summary>
    /// A single triangulated mesh extracted from one IFC element, together with the
    /// element metadata needed to identify and style it.
    /// </summary>
    /// <remarks>
    /// All geometry is expressed in meters. The coordinate space the vertices live in
    /// is reported once per model by <see cref="IfcLiteModel.MeshCoordinateSpace"/>.
    /// </remarks>
    public sealed class IfcMesh
    {
        /// <summary>
        /// The IFC EXPRESS id (STEP line number) of the source element.
        /// </summary>
        public uint ExpressId { get; internal init; }

        /// <summary>
        /// The IFC type name of the source element, e.g. <c>"IfcWall"</c>.
        /// </summary>
        public string IfcType { get; internal init; } = string.Empty;

        /// <summary>
        /// The IFC <c>GlobalId</c> of the element, or <c>null</c> when unavailable.
        /// </summary>
        public string? GlobalId { get; internal init; }

        /// <summary>
        /// The IFC <c>Name</c> of the element, or <c>null</c> when unavailable.
        /// </summary>
        public string? Name { get; internal init; }

        /// <summary>
        /// The name of the IFC presentation layer the element is assigned to, or
        /// <c>null</c> when it has no layer assignment.
        /// </summary>
        public string? PresentationLayer { get; internal init; }

        /// <summary>
        /// Flat array of vertex positions as <c>(x, y, z)</c> triplets, in meters.
        /// Length is always a multiple of three.
        /// </summary>
        public float[] Positions { get; internal init; } = System.Array.Empty<float>();

        /// <summary>
        /// Flat array of vertex normals as <c>(x, y, z)</c> triplets. Parallel to
        /// <see cref="Positions"/> (same vertex count).
        /// </summary>
        public float[] Normals { get; internal init; } = System.Array.Empty<float>();

        /// <summary>
        /// Triangle indices into the vertex arrays. Length is always a multiple of three;
        /// each consecutive triple defines one triangle.
        /// </summary>
        public uint[] Indices { get; internal init; } = System.Array.Empty<uint>();

        /// <summary>
        /// The element's display color as <c>[r, g, b, a]</c>, each component in the
        /// range <c>0..1</c>.
        /// </summary>
        public float[] Color { get; internal init; } = new float[] { 0.8f, 0.8f, 0.8f, 1.0f };

        /// <summary>
        /// The resolved material/style name, or <c>null</c> when the element has no
        /// per-item styling.
        /// </summary>
        public string? MaterialName { get; internal init; }

        /// <summary>
        /// Optional IFC property-set values keyed by property name. Primarily populated
        /// for spaces and zones (<c>IfcSpace</c> / <c>IfcZone</c>). <c>null</c> when the
        /// element carries no forwarded properties.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Properties { get; internal init; }

        /// <summary>
        /// The number of vertices in this mesh (<c>Positions.Length / 3</c>).
        /// </summary>
        public int VertexCount => Positions.Length / 3;

        /// <summary>
        /// The number of triangles in this mesh (<c>Indices.Length / 3</c>).
        /// </summary>
        public int TriangleCount => Indices.Length / 3;

        /// <summary>
        /// Initializes a new, empty mesh. Properties are populated via their init setters
        /// during deserialization within the assembly.
        /// </summary>
        internal IfcMesh()
        {
        }
    }
}
