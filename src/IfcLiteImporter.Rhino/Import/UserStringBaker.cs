// MIT License. See LICENSE in the repository root.

using System.Collections.Generic;
using System.Globalization;
using IfcLite.Net;
using Rhino.DocObjects;

namespace IfcLiteImporter.Rhino.Import
{
    /// <summary>
    /// Copies IFC metadata from an <see cref="IfcMesh"/> onto a Rhino object's
    /// user strings (key/value attribute pairs), so the data travels with the
    /// geometry and can be queried, filtered, or exported later.
    /// </summary>
    public static class UserStringBaker
    {
        // Well-known keys. Kept as constants so callers can read them back reliably.
        public const string KeyIfcType = "IfcType";
        public const string KeyExpressId = "IfcExpressId";
        public const string KeyGlobalId = "IfcGlobalId";
        public const string KeyName = "IfcName";
        public const string KeyPresentationLayer = "PresentationLayer";
        public const string KeyMaterialName = "MaterialName";

        /// <summary>
        /// Writes the IFC metadata of <paramref name="mesh"/> into the user-string
        /// dictionary of <paramref name="attr"/>. Null/empty values are skipped.
        /// </summary>
        /// <param name="attr">The attributes that will be assigned to the Rhino object.</param>
        /// <param name="mesh">The source mesh carrying the IFC metadata.</param>
        public static void Bake(ObjectAttributes attr, IfcMesh mesh)
        {
            if (attr is null || mesh is null)
                return;

            // Always-present identity fields.
            Set(attr, KeyIfcType, mesh.IfcType);
            Set(attr, KeyExpressId, mesh.ExpressId.ToString(CultureInfo.InvariantCulture));

            // Optional identity / classification fields.
            Set(attr, KeyGlobalId, mesh.GlobalId);
            Set(attr, KeyName, mesh.Name);
            Set(attr, KeyPresentationLayer, mesh.PresentationLayer);
            Set(attr, KeyMaterialName, mesh.MaterialName);

            // Every property set value the parser surfaced. We write them under
            // their original key; if a property happens to collide with one of the
            // well-known keys above, the property value wins (it is more specific).
            IReadOnlyDictionary<string, string>? props = mesh.Properties;
            if (props is not null)
            {
                foreach (KeyValuePair<string, string> kvp in props)
                    Set(attr, kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Sets a single user string, ignoring null/empty keys or values so we
        /// never write meaningless attributes.
        /// </summary>
        private static void Set(ObjectAttributes attr, string? key, string? value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return;

            attr.SetUserString(key, value);
        }
    }
}
