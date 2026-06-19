// MIT License. See LICENSE in the repository root.

using System;

namespace IfcLite.Net
{
    /// <summary>
    /// Thrown when the native ifc-lite parser returns a non-zero error code, or when
    /// the data it returns cannot be interpreted.
    /// </summary>
    public class IfcLiteException : Exception
    {
        /// <summary>
        /// The raw error code returned by the native FFI layer.
        /// </summary>
        /// <remarks>
        /// Known values:
        /// <list type="bullet">
        ///   <item><description><c>1</c> — a null pointer was passed, or the path was not valid UTF-8.</description></item>
        ///   <item><description><c>2</c> — the IFC file could not be read.</description></item>
        ///   <item><description><c>3</c> — geometry processing panicked.</description></item>
        ///   <item><description><c>4</c> — the result could not be serialized to JSON.</description></item>
        /// </list>
        /// A value of <c>0</c> indicates the error did not originate from a native return
        /// code (for example, a JSON deserialization failure on the managed side).
        /// </remarks>
        public int ErrorCode { get; }

        /// <summary>
        /// Creates an exception from a native FFI error code, choosing a friendly message
        /// for the documented codes <c>1</c>–<c>4</c> and a generic fallback otherwise.
        /// </summary>
        /// <param name="errorCode">The native error code.</param>
        public IfcLiteException(int errorCode)
            : base(MessageForCode(errorCode))
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Creates an exception with an explicit message (and optional inner exception).
        /// <see cref="ErrorCode"/> is set to <c>0</c> to indicate a managed-side failure.
        /// </summary>
        /// <param name="message">A human-readable description of the failure.</param>
        /// <param name="innerException">The underlying exception, if any.</param>
        public IfcLiteException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = 0;
        }

        /// <summary>
        /// Maps a native FFI error code to a friendly, human-readable message.
        /// </summary>
        private static string MessageForCode(int errorCode) => errorCode switch
        {
            1 => "ifc-lite could not read the file path: a null pointer was supplied or the path was not valid UTF-8.",
            2 => "ifc-lite could not read the IFC file. Check that the path exists and is accessible.",
            3 => "ifc-lite failed while processing the model geometry (the native parser panicked). " +
                 "See ifc_lite_panic.log in your system temp directory for details.",
            4 => "ifc-lite produced a result that could not be serialized to JSON.",
            _ => $"ifc-lite failed with an unexpected error code ({errorCode}).",
        };
    }
}
