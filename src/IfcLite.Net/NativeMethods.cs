// MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IfcLite.Net
{
    /// <summary>
    /// Low-level P/Invoke declarations for the native <c>ifc_lite_ffi</c> library and
    /// the custom resolver that loads it.
    /// </summary>
    /// <remarks>
    /// The native library is named per-platform:
    /// <list type="bullet">
    ///   <item><description>Windows: <c>ifc_lite_ffi.dll</c></description></item>
    ///   <item><description>Linux:   <c>libifc_lite_ffi.so</c></description></item>
    ///   <item><description>macOS:   <c>libifc_lite_ffi.dylib</c></description></item>
    /// </list>
    /// The <see cref="DllImportAttribute"/> declarations all reference the logical name
    /// <c>"ifc_lite_ffi"</c>; the resolver registered in the module initializer maps that
    /// logical name to the correct platform file located next to the managed assembly.
    /// </remarks>
    internal static unsafe class NativeMethods
    {
        /// <summary>
        /// The logical library name used by every <see cref="DllImportAttribute"/> below
        /// and recognised by <see cref="Resolve"/>.
        /// </summary>
        internal const string LibraryName = "ifc_lite_ffi";

        /// <summary>
        /// Parse an IFC file with the default opening filter and return JSON bytes.
        /// </summary>
        /// <param name="pathPtr">Pointer to the UTF-8 encoded file path.</param>
        /// <param name="pathLen">Length, in bytes, of the UTF-8 path.</param>
        /// <param name="outPtr">Receives a pointer to the allocated UTF-8 JSON buffer.</param>
        /// <param name="outLen">Receives the length, in bytes, of the JSON buffer.</param>
        /// <returns>0 on success; otherwise one of the documented FFI error codes.</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ifc_lite_parse")]
        internal static extern int Parse(byte* pathPtr, nuint pathLen, byte** outPtr, nuint* outLen);

        /// <summary>
        /// Parse an IFC file with a configurable opening filter and return JSON bytes.
        /// </summary>
        /// <param name="pathPtr">Pointer to the UTF-8 encoded file path.</param>
        /// <param name="pathLen">Length, in bytes, of the UTF-8 path.</param>
        /// <param name="openingFilterMode">0 = Default, 1 = IgnoreAll, 2 = IgnoreOpaque.</param>
        /// <param name="outPtr">Receives a pointer to the allocated UTF-8 JSON buffer.</param>
        /// <param name="outLen">Receives the length, in bytes, of the JSON buffer.</param>
        /// <returns>0 on success; otherwise one of the documented FFI error codes.</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ifc_lite_parse_ex")]
        internal static extern int ParseEx(byte* pathPtr, nuint pathLen, int openingFilterMode, byte** outPtr, nuint* outLen);

        /// <summary>
        /// Free a buffer previously returned by <see cref="Parse"/> or <see cref="ParseEx"/>.
        /// </summary>
        /// <param name="ptr">The pointer returned via the <c>outPtr</c> out-parameter.</param>
        /// <param name="len">The length returned via the <c>outLen</c> out-parameter.</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ifc_lite_free")]
        internal static extern void Free(byte* ptr, nuint len);

        /// <summary>
        /// Registers <see cref="Resolve"/> as the import resolver for this assembly. Runs
        /// automatically before any code in the assembly executes.
        /// </summary>
        [ModuleInitializer]
        internal static void Initialize()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);
        }

        /// <summary>
        /// Custom <see cref="DllImportResolver"/>. For the <c>ifc_lite_ffi</c> logical name
        /// it probes <see cref="AppContext.BaseDirectory"/> for the platform-specific file
        /// and loads it explicitly; for anything else (or if the file is not found) it
        /// returns <see cref="IntPtr.Zero"/> so the default .NET resolution still applies.
        /// </summary>
        private static IntPtr Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
            {
                // Not our library — let the runtime handle it.
                return IntPtr.Zero;
            }

            string fileName = GetPlatformFileName();
            string candidate = Path.Combine(AppContext.BaseDirectory, fileName);

            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }

            // Fall back to the default resolution strategy (e.g. system search paths).
            return IntPtr.Zero;
        }

        /// <summary>
        /// Returns the expected native library file name for the current operating system.
        /// </summary>
        private static string GetPlatformFileName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "ifc_lite_ffi.dll";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "libifc_lite_ffi.dylib";
            }

            // Default to the Linux/Unix shared-object naming convention.
            return "libifc_lite_ffi.so";
        }
    }
}
