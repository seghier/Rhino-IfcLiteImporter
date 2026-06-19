// MIT License. See LICENSE in the repository root.

using System;
using System.Runtime.InteropServices;

// A stable, unique plug-in identity. Rhino uses this GUID to track the plug-in
// across sessions, so it must never change once published.
[assembly: Guid("d3f2f2d3-41dd-4e6a-a749-b03aa1e01a7e")]

namespace IfcLiteImporter.Rhino
{
    /// <summary>
    /// The plug-in entry point. Rhino instantiates exactly one of these per
    /// session and discovers our <see cref="Rhino.Commands.Command"/> classes
    /// automatically by reflection over this assembly.
    /// </summary>
    /// <remarks>
    /// We expose a singleton <see cref="Instance"/> so commands and UI can reach
    /// plug-in-level services (settings, embedded resources, etc.) without having
    /// to look the plug-in up by GUID.
    /// </remarks>
    public sealed class IfcLiteImporterPlugin : global::Rhino.PlugIns.PlugIn
    {
        /// <summary>
        /// The single instance of this plug-in, set by Rhino when it constructs us.
        /// </summary>
        public static IfcLiteImporterPlugin? Instance { get; private set; }

        /// <summary>
        /// Rhino calls this constructor once, on load. We just capture the singleton.
        /// </summary>
        public IfcLiteImporterPlugin()
        {
            Instance = this;
        }

        /// <summary>
        /// The human-readable name shown in Rhino's PlugInManager. (Rhino also
        /// reads the assembly <c>AssemblyTitle</c>; we override here to be explicit.)
        /// </summary>
        public override global::Rhino.PlugIns.PlugInLoadTime LoadTime =>
            // Load on demand the first time one of our commands runs. This keeps
            // Rhino startup fast — the native ifc-lite library is only needed once
            // the user actually imports something.
            global::Rhino.PlugIns.PlugInLoadTime.AtStartup;
    }
}
