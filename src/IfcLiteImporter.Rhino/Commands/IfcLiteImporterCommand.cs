// MIT License. See LICENSE in the repository root.

using System;
using Eto.Forms;
using IfcLiteImporter.Rhino.UI;
using Rhino;
using Rhino.Commands;

namespace IfcLiteImporter.Rhino.Commands
{
    /// <summary>
    /// The interactive command. Running <c>IfcLiteImporter</c> opens the Eto
    /// import dialog. A single dialog instance is reused — running the command
    /// again just brings the existing window to the front.
    /// </summary>
    public sealed class IfcLiteImporterCommand : global::Rhino.Commands.Command
    {
        // Keep one window alive so the command is idempotent.
        private static ImportWindow? _window;

        /// <summary>The name typed at the Rhino command line.</summary>
        public override string EnglishName => "IfcLiteImporter";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            try
            {
                // Reuse the existing window if it is still open; otherwise create one.
                if (_window is null || !_window.Loaded)
                {
                    _window = new ImportWindow();

                    // Forget our reference once the window closes, so the next run
                    // creates a fresh dialog.
                    _window.Closed += (_, _) => _window = null;

                    // Show the dialog as a modeless form owned by Rhino's main
                    // window. Owning it keeps the dialog above Rhino and parents it
                    // correctly. We prefer the per-document main window because the
                    // process-wide RhinoEtoApp.MainWindow does not behave correctly
                    // on macOS; we fall back to it only if the per-document API is
                    // unavailable.
                    _window.Owner = ResolveOwnerWindow(doc);
                    _window.Show();
                }
                else
                {
                    // Already open — surface it.
                    _window.BringToFront();
                }

                return Result.Success;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IfcLiteImporter failed to open: {ex.Message}");
                return Result.Failure;
            }
        }

        /// <summary>
        /// Resolves the Eto window that should own the dialog. Prefers the
        /// per-document main window (correct on Windows and macOS); falls back to
        /// the process-wide main window if needed.
        /// </summary>
        private static Window? ResolveOwnerWindow(RhinoDoc doc)
        {
            try
            {
                if (doc is not null)
                {
                    Window? perDoc = global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc);
                    if (perDoc is not null)
                        return perDoc;
                }
            }
            catch
            {
                // Older RhinoCommon or unexpected state — fall through to the
                // process-wide window below.
            }

            return global::Rhino.UI.RhinoEtoApp.MainWindow;
        }
    }
}
