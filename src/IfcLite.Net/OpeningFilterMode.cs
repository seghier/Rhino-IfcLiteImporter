// MIT License. See LICENSE in the repository root.

namespace IfcLite.Net
{
    /// <summary>
    /// Controls how openings (windows and doors, i.e. <c>IfcWindow</c> / <c>IfcDoor</c>)
    /// are handled while geometry is generated.
    /// </summary>
    /// <remarks>
    /// The integer values match the native FFI contract exactly and MUST NOT change:
    /// they are passed straight through to <c>ifc_lite_parse_ex</c>.
    /// </remarks>
    public enum OpeningFilterMode
    {
        /// <summary>
        /// Export all openings and cut their voids out of the host walls.
        /// This is the default, geometrically faithful behaviour.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Skip all window and door meshes and do not cut any voids. Produces solid
        /// walls. Useful for fast, lightweight previews.
        /// </summary>
        IgnoreAll = 1,

        /// <summary>
        /// Skip only opaque (non-glazed) windows and doors; glazed openings are kept.
        /// </summary>
        IgnoreOpaque = 2,
    }
}
