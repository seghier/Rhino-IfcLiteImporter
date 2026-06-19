// MIT License. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace IfcLite.Net
{
    /// <summary>
    /// The entry point of the library: parses an IFC file via the native ifc-lite engine
    /// and returns a typed <see cref="IfcLiteModel"/>.
    /// </summary>
    public static class IfcLiteParser
    {
        /// <summary>
        /// Shared, thread-safe deserialization options. <see cref="JsonSerializerOptions"/>
        /// is safe to reuse concurrently once configured.
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            // We annotate every DTO member with [JsonPropertyName("snake_case")] explicitly
            // (net7.0 has no snake_case naming policy). Case-insensitive matching is enabled
            // purely as a defensive safety net.
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Parses an IFC file and returns the extracted meshes and metadata.
        /// </summary>
        /// <param name="ifcPath">Absolute or relative path to the <c>.ifc</c> file.</param>
        /// <param name="mode">How openings (windows/doors) should be handled.</param>
        /// <returns>The parsed model.</returns>
        /// <exception cref="ArgumentException"><paramref name="ifcPath"/> is null or empty.</exception>
        /// <exception cref="IfcLiteException">
        /// The native parser returned an error, or its output could not be deserialized.
        /// </exception>
        public static IfcLiteModel Parse(string ifcPath, OpeningFilterMode mode = OpeningFilterMode.Default)
        {
            if (string.IsNullOrEmpty(ifcPath))
            {
                throw new ArgumentException("The IFC path must not be null or empty.", nameof(ifcPath));
            }

            byte[] jsonBytes = InvokeNativeParse(ifcPath, mode);
            return Deserialize(jsonBytes);
        }

        /// <summary>
        /// Calls into the native FFI, copies the returned JSON bytes out, and frees the
        /// native buffer. Always frees the buffer (via <c>try/finally</c>) on success.
        /// </summary>
        private static unsafe byte[] InvokeNativeParse(string ifcPath, OpeningFilterMode mode)
        {
            // The native side expects a UTF-8 encoded path (not NUL-terminated; an explicit
            // length is passed alongside).
            byte[] pathBytes = Encoding.UTF8.GetBytes(ifcPath);

            byte* outPtr = null;
            nuint outLen = 0;
            int code;

            // Pin the path bytes for the duration of the call so the GC cannot move them.
            fixed (byte* pathPtr = pathBytes)
            {
                code = NativeMethods.ParseEx(
                    pathPtr,
                    (nuint)pathBytes.Length,
                    (int)mode,
                    &outPtr,
                    &outLen);
            }

            if (code != 0)
            {
                // On a non-zero return the native layer guarantees it allocated nothing,
                // so there is no buffer to free here.
                throw new IfcLiteException(code);
            }

            try
            {
                int length = checked((int)outLen);
                byte[] managed = new byte[length];
                if (length > 0)
                {
                    Marshal.Copy((IntPtr)outPtr, managed, 0, length);
                }

                return managed;
            }
            finally
            {
                // Free the native buffer regardless of what happened while copying it.
                if (outPtr != null)
                {
                    NativeMethods.Free(outPtr, outLen);
                }
            }
        }

        /// <summary>
        /// Deserializes the UTF-8 JSON returned by the native layer into the public model.
        /// </summary>
        private static IfcLiteModel Deserialize(byte[] jsonBytes)
        {
            ParseResponseDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ParseResponseDto>(jsonBytes, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new IfcLiteException("Failed to deserialize the ifc-lite parse response.", ex);
            }

            if (dto is null)
            {
                throw new IfcLiteException("The ifc-lite parse response was empty.");
            }

            return MapModel(dto);
        }

        // -----------------------------------------------------------------------
        // Mapping from the mutable DTOs to the public immutable types.
        // -----------------------------------------------------------------------

        private static IfcLiteModel MapModel(ParseResponseDto dto)
        {
            List<IfcMesh> meshes;
            if (dto.Meshes is { Count: > 0 })
            {
                meshes = new List<IfcMesh>(dto.Meshes.Count);
                foreach (MeshDataDto meshDto in dto.Meshes)
                {
                    meshes.Add(MapMesh(meshDto));
                }
            }
            else
            {
                meshes = new List<IfcMesh>();
            }

            return new IfcLiteModel
            {
                Meshes = meshes,
                MeshCoordinateSpace = dto.MeshCoordinateSpace,
                SiteTransform = dto.SiteTransform,
                BuildingTransform = dto.BuildingTransform,
                Metadata = MapMetadata(dto.Metadata),
                Stats = MapStats(dto.Stats),
            };
        }

        private static IfcMesh MapMesh(MeshDataDto dto)
        {
            IReadOnlyDictionary<string, string>? properties = dto.Properties;

            return new IfcMesh
            {
                ExpressId = dto.ExpressId,
                IfcType = dto.IfcType ?? string.Empty,
                GlobalId = dto.GlobalId,
                Name = dto.Name,
                PresentationLayer = dto.PresentationLayer,
                Positions = dto.Positions ?? Array.Empty<float>(),
                Normals = dto.Normals ?? Array.Empty<float>(),
                Indices = dto.Indices ?? Array.Empty<uint>(),
                Color = dto.Color ?? new float[] { 0.8f, 0.8f, 0.8f, 1.0f },
                MaterialName = dto.MaterialName,
                Properties = properties,
            };
        }

        private static IfcModelMetadata MapMetadata(ModelMetadataDto? dto)
        {
            if (dto is null)
            {
                return new IfcModelMetadata();
            }

            return new IfcModelMetadata
            {
                SchemaVersion = dto.SchemaVersion ?? string.Empty,
                EntityCount = dto.EntityCount,
                GeometryEntityCount = dto.GeometryEntityCount,
                LengthUnitScale = dto.LengthUnitScale,
                IsGeoReferenced = dto.CoordinateInfo?.IsGeoReferenced ?? false,
            };
        }

        private static IfcProcessingStats MapStats(ProcessingStatsDto? dto)
        {
            if (dto is null)
            {
                return new IfcProcessingStats();
            }

            return new IfcProcessingStats
            {
                TotalMeshes = dto.TotalMeshes,
                TotalVertices = dto.TotalVertices,
                TotalTriangles = dto.TotalTriangles,
                TotalTimeMs = dto.TotalTimeMs,
            };
        }
    }
}
