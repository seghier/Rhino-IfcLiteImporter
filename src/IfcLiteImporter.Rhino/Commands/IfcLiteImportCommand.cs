// MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks; // Added
using IfcLite.Net;
using IfcLiteImporter.Rhino.Import;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom; // Added for GetCancel

namespace IfcLiteImporter.Rhino.Commands
{
    /// <summary>
    /// The headless command. Running <c>IfcLiteImport</c> prompts for an
    /// <c>.ifc</c> file and imports it asynchronously using Rhino's GetCancel,
    /// showing progress in the native status bar and keeping viewports completely
    /// responsive for navigation and rotation.
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

            var options = new ImportOptions
            {
                CoordinateMode = CoordinateMode.Project,
                OpeningFilterMode = OpeningFilterMode.Default,
                JoinByProperties = true,
                MergeCoplanarFaces = true,
            };

            // Instantiate Rhino's GetCancel to manage the non-blocking wait loop
            var gc = new GetCancel();
            gc.SetCommandPrompt("Importing IFC file... Press ESC to cancel");

            bool progressMeterVisible = false;

            // Progress<T> automatically captures the SynchronizationContext of the UI thread,
            // making UI-bound progress meter updates completely safe.
            var progress = new Progress<ImportProgress>(value =>
            {
                if (!progressMeterVisible)
                {
                    // Initialize the built-in progress bar
                    global::Rhino.UI.StatusBar.ShowProgressMeter(0, 100, "Importing IFC...", true, true);
                    progressMeterVisible = true;
                }
                global::Rhino.UI.StatusBar.UpdateProgressMeter(value.Percent, true);
                RhinoApp.SetCommandPrompt($"Importing IFC: {value.Percent}% - {value.Status}");
            });

            try
            {
                RhinoApp.WriteLine($"IfcLiteImport: importing {Path.GetFileName(path)}…");

                var service = new IfcImportService();

                // Run parsing, conversion, grouping, and merging on a background thread.
                // Pass gc.Token so pressing ESC cancels operations instantly.
                Task<ImportResult> importTask = Task.Run(() =>
                    service.Import(doc, path, options, progress, gc.Token)
                );

                // Wait for completion. This pumps messages, keeping viewports, 
                // mouse navigation, and redraws fully active.
                Result waitResult = gc.Wait(importTask, doc);

                // Clean up the status bar progress meter
                if (progressMeterVisible)
                {
                    global::Rhino.UI.StatusBar.HideProgressMeter();
                    progressMeterVisible = false;
                }

                if (waitResult == Result.Cancel)
                {
                    RhinoApp.WriteLine("IfcLiteImport: cancelled.");
                    return Result.Cancel;
                }

                if (waitResult == Result.Success)
                {
                    // Retrieve result safely once task is complete
                    ImportResult result = importTask.Result;

                    RhinoApp.WriteLine(
                        $"IfcLiteImport: added {result.ObjectCount} objects from {result.MeshCount} meshes " +
                        $"({result.SchemaVersion}) in {result.ElapsedMs} ms.");

                    doc.Views.Redraw();
                    return Result.Success;
                }

                // If waitResult is Failure and the task faulted, propagate the exception 
                // to be parsed and printed by the try-catch block.
                if (waitResult == Result.Failure && importTask.IsFaulted && importTask.Exception is not null)
                {
                    throw importTask.Exception;
                }

                return waitResult;
            }
            catch (AggregateException aggEx)
            {
                Exception inner = aggEx.InnerException ?? aggEx;
                if (inner is OperationCanceledException)
                {
                    RhinoApp.WriteLine("IfcLiteImport: cancelled.");
                    return Result.Cancel;
                }
                if (inner is IfcLiteException ex)
                {
                    RhinoApp.WriteLine($"IfcLiteImport failed: {ex.Message} (code {ex.ErrorCode})");
                    return Result.Failure;
                }
                RhinoApp.WriteLine($"IfcLiteImport failed: {inner.Message}");
                return Result.Failure;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"IfcLiteImport failed: {ex.Message}");
                return Result.Failure;
            }
            finally
            {
                if (progressMeterVisible)
                {
                    global::Rhino.UI.StatusBar.HideProgressMeter();
                }
            }
        }
    }
}