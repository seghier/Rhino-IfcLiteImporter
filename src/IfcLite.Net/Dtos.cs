// MIT License. See LICENSE in the repository root.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IfcLite.Net
{
    // ---------------------------------------------------------------------------
    // Internal data-transfer objects that mirror the JSON the native ifc-lite layer
    // emits (serde with snake_case keys). They are intentionally separate from the
    // public, immutable types: we deserialize into these mutable DTOs and then map
    // them across. net7.0 has no built-in snake_case naming policy, so every member
    // carries an explicit [JsonPropertyName] attribute. Unknown JSON fields (uvs,
    // texture, georeferencing, symbolic_data, the extra timing counters, ...) are
    // simply ignored by System.Text.Json.
    // ---------------------------------------------------------------------------

    /// <summary>Top-level response returned by <c>ifc_lite_parse</c> / <c>ifc_lite_parse_ex</c>.</summary>
    internal sealed class ParseResponseDto
    {
        [JsonPropertyName("cache_key")]
        public string? CacheKey { get; set; }

        [JsonPropertyName("meshes")]
        public List<MeshDataDto>? Meshes { get; set; }

        [JsonPropertyName("mesh_coordinate_space")]
        public string? MeshCoordinateSpace { get; set; }

        [JsonPropertyName("site_transform")]
        public double[]? SiteTransform { get; set; }

        [JsonPropertyName("building_transform")]
        public double[]? BuildingTransform { get; set; }

        [JsonPropertyName("metadata")]
        public ModelMetadataDto? Metadata { get; set; }

        [JsonPropertyName("stats")]
        public ProcessingStatsDto? Stats { get; set; }

        // `symbolic_data` is deliberately not mapped.
    }

    /// <summary>One mesh entry from the <c>meshes</c> array.</summary>
    internal sealed class MeshDataDto
    {
        [JsonPropertyName("express_id")]
        public uint ExpressId { get; set; }

        [JsonPropertyName("ifc_type")]
        public string? IfcType { get; set; }

        [JsonPropertyName("global_id")]
        public string? GlobalId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("presentation_layer")]
        public string? PresentationLayer { get; set; }

        [JsonPropertyName("positions")]
        public float[]? Positions { get; set; }

        [JsonPropertyName("normals")]
        public float[]? Normals { get; set; }

        [JsonPropertyName("indices")]
        public uint[]? Indices { get; set; }

        [JsonPropertyName("color")]
        public float[]? Color { get; set; }

        [JsonPropertyName("material_name")]
        public string? MaterialName { get; set; }

        [JsonPropertyName("geometry_item_id")]
        public uint? GeometryItemId { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, string>? Properties { get; set; }
    }

    /// <summary>The <c>metadata</c> object.</summary>
    internal sealed class ModelMetadataDto
    {
        [JsonPropertyName("schema_version")]
        public string? SchemaVersion { get; set; }

        [JsonPropertyName("entity_count")]
        public int EntityCount { get; set; }

        [JsonPropertyName("geometry_entity_count")]
        public int GeometryEntityCount { get; set; }

        [JsonPropertyName("coordinate_info")]
        public CoordinateInfoDto? CoordinateInfo { get; set; }

        [JsonPropertyName("length_unit_scale")]
        public double? LengthUnitScale { get; set; }

        // `georeferencing` is deliberately not mapped.
    }

    /// <summary>The nested <c>coordinate_info</c> object.</summary>
    internal sealed class CoordinateInfoDto
    {
        [JsonPropertyName("origin_shift")]
        public double[]? OriginShift { get; set; }

        [JsonPropertyName("is_geo_referenced")]
        public bool IsGeoReferenced { get; set; }
    }

    /// <summary>The <c>stats</c> object.</summary>
    internal sealed class ProcessingStatsDto
    {
        [JsonPropertyName("total_meshes")]
        public int TotalMeshes { get; set; }

        [JsonPropertyName("total_vertices")]
        public int TotalVertices { get; set; }

        [JsonPropertyName("total_triangles")]
        public int TotalTriangles { get; set; }

        [JsonPropertyName("parse_time_ms")]
        public long ParseTimeMs { get; set; }

        [JsonPropertyName("geometry_time_ms")]
        public long GeometryTimeMs { get; set; }

        [JsonPropertyName("total_time_ms")]
        public long TotalTimeMs { get; set; }
    }
}
