// MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Threading;
using IfcLite.Net;
using IfcLiteImporter.Rhino.Import;
using Rhino;
using Rhino.Commands;

namespace IfcLiteImporter.Rhino.Commands
{
    /// <summary>
    /// The headless command. Running <c>IfcLiteImport</c> prompts for an
    /// <c>.ifc</c> file and imports it synchronously with default options
    /// (project coordinates, join-by-properties), writing progress to the command
    /// line. Handy for scripting and macros where no dialog is wanted.
    /// </summary>
    public sealed class IfcLiteImportCommand : Command
    {
        /// <summary>The name typed at the Rhino command line.</summary>
        public override string EnglishName => "IfcLiteImport";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Ask the user to pick a file. ShowOpenDialog returns false on cancel.
            var dialog = new global::Rhino.UI.OpenFileDialog
            {
                Title = "Select an IFC file",
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
            };

            if (!dialog.ShowOpenDialog())
                return Result.Cancel;

            string path = dialog.FileName;
            if (string.IsNullOrEmpty(path))
                return Result.Cancel;

            if (!File.Exists(path))
            {
                RhinoApp.WriteLine($"IfcLiteImport: file not found: {path}");
                return Result.Failure;
            }

            // Default options match the dialog's defaults.
            var options = new ImportOptions
            {
                CoordinateMode = CoordinateMode.Project,
                OpeningFilterMode = OpeningFilterMode.Default,
                JoinByProperties = true,
            };

            // Mirror progress to the command line. Progress<T> would post to a
            // captured synchronization context; here we run synchronously and want
            // each update printed immediately, so use a direct sink instead.
            var progress = new ConsoleProgress();

            try
            {
                RhinoApp.WriteLine($"IfcLiteImport: importing {Path.GetFileName(path)}…");

                var service = new IfcImportService();
                // This command runs on Rhino's UI thread, so the service's
                // RhinoApp.InvokeOnUiThread call simply executes in place.
                ImportResult result = service.Import(doc, path, options, progress, CancellationToken.None);

                RhinoApp.WriteLine(
                    $"IfcLiteImport: added {result.ObjectCount} objects from {result.MeshCount} meshes " +
                    $"({result.SchemaVersion}) in {result.ElapsedMs} ms.");

                doc.Views.Redraw();
                return Result.Success;
            }
            catch (OperationCanceledException)
            {
                RhinoApp.WriteLine("IfcLiteImport: cancelled.");
                return Result.Cancel;
            }
            catch (IfcLiteException ex)
            {
                RhinoApp.WriteLine($"IfcLiteImport failed: {ex.Message} (code {ex.ErrorCode})");
                return Result.Failure;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IfcLiteImport failed: {ex.Message}");
                return Result.Failure;
            }
        }

        /// <summary>
        /// A trivial <see cref="IProgress{T}"/> that prints each update straight to
        /// the Rhino command line. Used by the synchronous command path.
        /// </summary>
        private sealed class ConsoleProgress : IProgress<ImportProgress>
        {
            private int _lastPercent = -1;

            public void Report(ImportProgress value)
            {
                // Avoid spamming identical percentages.
                if (value.Percent == _lastPercent)
                    return;
                _lastPercent = value.Percent;
                RhinoApp.WriteLine($"  [{value.Percent,3}%] {value.Status}");
            }
        }
    }
}
