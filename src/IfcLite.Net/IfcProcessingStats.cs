// MIT License. See LICENSE in the repository root.

namespace IfcLite.Net
{
    /// <summary>
    /// Statistics reported by the native parser for a single parse operation.
    /// </summary>
    public sealed class IfcProcessingStats
    {
        /// <summary>
        /// The total number of meshes generated.
        /// </summary>
        public int TotalMeshes { get; internal init; }

        /// <summary>
        /// The total number of vertices across all meshes.
        /// </summary>
        public int TotalVertices { get; internal init; }

        /// <summary>
        /// The total number of triangles across all meshes.
        /// </summary>
        public int TotalTriangles { get; internal init; }

        /// <summary>
        /// The total wall-clock processing time, in milliseconds.
        /// </summary>
        public long TotalTimeMs { get; internal init; }

        /// <summary>
        /// Initializes a new, empty statistics instance. Properties are populated via
        /// their init setters during deserialization within the assembly.
        /// </summary>
        internal IfcProcessingStats()
        {
        }
    }
}
