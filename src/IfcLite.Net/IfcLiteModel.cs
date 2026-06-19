// MIT License. See LICENSE in the repository root.

using System.Collections.Generic;

namespace IfcLite.Net
{
    /// <summary>
    /// The fully parsed result of an IFC file: all meshes plus the metadata, statistics
    /// and placement transforms needed to position the geometry.
    /// </summary>
    public sealed class IfcLiteModel
    {
        /// <summary>
        /// Every mesh extracted from the file.
        /// </summary>
        public IReadOnlyList<IfcMesh> Meshes { get; internal init; } = new List<IfcMesh>();

        /// <summary>
        /// The coordinate space the mesh vertices are expressed in. One of
        /// <c>"site_local"</c>, <c>"raw_ifc"</c> or <c>"model_rtc"</c>, or <c>null</c>
        /// when the parser did not report one.
        /// </summary>
        /// <remarks>
        /// In practice the native layer normalizes <c>raw_ifc</c> geometry to
        /// <c>site_local</c> before returning, so callers most commonly see
        /// <c>"site_local"</c>.
        /// </remarks>
        public string? MeshCoordinateSpace { get; internal init; }

        /// <summary>
        /// The <c>IfcSite</c> placement as a column-major 4x4 matrix (16 values, in
        /// meters), or <c>null</c> when the file has none. Use it to relocate geometry
        /// between global and site-local coordinate systems.
        /// </summary>
        public double[]? SiteTransform { get; internal init; }

        /// <summary>
        /// The <c>IfcBuilding</c> placement as a column-major 4x4 matrix (16 values, in
        /// meters), or <c>null</c> when the file has none.
        /// </summary>
        public double[]? BuildingTransform { get; internal init; }

        /// <summary>
        /// High-level metadata about the model.
        /// </summary>
        public IfcModelMetadata Metadata { get; internal init; } = new IfcModelMetadata();

        /// <summary>
        /// Processing statistics for this parse.
        /// </summary>
        public IfcProcessingStats Stats { get; internal init; } = new IfcProcessingStats();

        /// <summary>
        /// Initializes a new, empty model. Properties are populated via their init setters
        /// during deserialization within the assembly.
        /// </summary>
        internal IfcLiteModel()
        {
        }
    }
}
