// MIT License. See LICENSE in the repository root.

namespace IfcLite.Net
{
    /// <summary>
    /// High-level metadata describing the parsed IFC model.
    /// </summary>
    public sealed class IfcModelMetadata
    {
        /// <summary>
        /// The IFC schema version of the file, e.g. <c>"IFC2X3"</c>, <c>"IFC4"</c> or
        /// <c>"IFC4X3"</c>.
        /// </summary>
        public string SchemaVersion { get; internal init; } = string.Empty;

        /// <summary>
        /// The total number of entities in the file.
        /// </summary>
        public int EntityCount { get; internal init; }

        /// <summary>
        /// The number of geometry-bearing entities in the file.
        /// </summary>
        public int GeometryEntityCount { get; internal init; }

        /// <summary>
        /// The factor that converts model length values to meters (e.g. <c>0.001</c> for
        /// millimeters). <c>null</c> when the file did not specify one; consumers should
        /// then treat it as <c>1.0</c>. Note that mesh geometry is already returned in
        /// meters regardless of this value.
        /// </summary>
        public double? LengthUnitScale { get; internal init; }

        /// <summary>
        /// Whether the model is geo-referenced (carries map-conversion data).
        /// </summary>
        public bool IsGeoReferenced { get; internal init; }

        /// <summary>
        /// Initializes a new, empty metadata instance. Properties are populated via their
        /// init setters during deserialization within the assembly.
        /// </summary>
        internal IfcModelMetadata()
        {
        }
    }
}
