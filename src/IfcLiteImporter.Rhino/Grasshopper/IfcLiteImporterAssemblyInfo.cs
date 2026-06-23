// MIT License. See LICENSE in the repository root.

using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace IfcLiteImporter.Rhino.Grasshopper
{
    /// <summary>
    /// Registers the assembly with Grasshopper and exposes metadata.
    /// </summary>
    public sealed class IfcLiteImporterAssemblyInfo : GH_AssemblyInfo
    {
        public override string Name => "IfcLiteImporter";

        // Return a custom 24x24 pixel icon Bitmap here to represent your plug-in 
        // in the Grasshopper tab manager (e.g. from embedded resource properties).
        public override Bitmap Icon => null;

        public override string Description => "Grasshopper components for importing IFC files using IfcLite.";

        public override Guid Id => new Guid("A8585824-F9AD-4FDA-BBEF-E271B175F4EA");

        public override string AuthorName => "Seghier Mohamed Abdelaziz";

        public override string AuthorContact => "";
    }
}