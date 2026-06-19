// MIT License. See LICENSE in the repository root.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using IfcLite.Net;
using IfcLiteImporter.Rhino.Import;
using Rhino;

namespace IfcLiteImporter.Rhino.UI
{
    /// <summary>
    /// The interactive import dialog, built with Eto.Forms (the cross-platform
    /// toolkit Rhino ships on both Windows and macOS).
    /// </summary>
    /// <remarks>
    /// <para><b>Threading.</b> Eto widgets may only be touched on the UI thread.
    /// When the user clicks <em>Import</em> we start the heavy work on a background
    /// <see cref="Task"/> so the dialog stays responsive, then route progress and
    /// completion back to the UI thread with
    /// <see cref="Application.AsyncInvoke(Action)"/>.</para>
    /// <para>The import itself (<see cref="IfcImportService"/>) takes care of the
    /// second half of the threading story: it does the parse + mesh conversion on
    /// our background thread but marshals the actual RhinoDoc writes onto Rhino's
    /// own UI thread. See that class for details.</para>
    /// </remarks>
    public sealed class ImportWindow : Form
    {
        // The embedded resource holding the header logo. Loaded defensively; the
        // dialog works fine if the PNG has not been added to the project yet.
        private const string LogoResourceName = "IfcLiteImporter.Rhino.Resources.link-logo.png";

        // ---- Controls -----------------------------------------------------------
        private readonly TextBox _filePathBox;
        private readonly Button _browseButton;
        private readonly RadioButtonList _coordinateChoice;
        private readonly CheckBox _ignoreOpeningsCheck;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly Button _importButton;
        private readonly Button _closeButton;

        // ---- Import state -------------------------------------------------------
        private CancellationTokenSource? _cts;
        private bool _isImporting;

        public ImportWindow()
        {
            Title = "IfcLite Importer";
            Resizable = false;
            Minimizable = false;
            Maximizable = false;
            // Fix the width; let the vertical layout drive the height. A width-only
            // ClientSize (height -1) tells Eto to auto-size the height to content.
            ClientSize = new Size(460, -1);
            MinimumSize = new Size(460, 0);

            // ---- Header: LINK Arkitektur logo ----------------------------------
            var logoView = new ImageView();
            Bitmap? logo = TryLoadLogo();
            if (logo is not null)
            {
                logoView.Image = logo;
                logoView.Size = new Size(logo.Width, logo.Height);
            }

            // ---- File row ------------------------------------------------------
            _filePathBox = new TextBox { ReadOnly = true, PlaceholderText = "Choose an .ifc file…" };
            _browseButton = new Button { Text = "Browse…" };
            _browseButton.Click += (_, _) => OnBrowse();

            // ---- Coordinate choice ---------------------------------------------
            _coordinateChoice = new RadioButtonList
            {
                Orientation = Orientation.Vertical,
                // Index 0 = Project (default), index 1 = Shared.
                Items = { "Project coordinates", "Shared coordinates" },
                SelectedIndex = 0,
                ToolTip =
                    "Project: geometry near the Rhino origin (site-local).\n" +
                    "Shared: geometry at real-world site coordinates " +
                    "(applies the IfcSite placement transform).",
            };

            // ---- Options -------------------------------------------------------
            _ignoreOpeningsCheck = new CheckBox
            {
                Text = "Ignore openings (doors/windows voids)",
                Checked = false,
                ToolTip = "When checked, door and window openings are not cut from host walls.",
            };

            // ---- Progress + status ---------------------------------------------
            _progressBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0 };
            _statusLabel = new Label { Text = "Ready." };

            // ---- Buttons -------------------------------------------------------
            _importButton = new Button { Text = "Import" };
            _importButton.Click += (_, _) => OnImport();

            _closeButton = new Button { Text = "Close" };
            _closeButton.Click += (_, _) => Close();

            // A plain Form (unlike Dialog) has no DefaultButton/AbortButton, so we
            // wire the convenience keys ourselves: Enter starts the import, Esc
            // closes (or cancels a running import via the Closing handler below).
            KeyDown += (_, e) =>
            {
                if (e.Key == Keys.Enter)
                {
                    OnImport();
                    e.Handled = true;
                }
                else if (e.Key == Keys.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };

            // ---- Layout --------------------------------------------------------
            var fileRow = new TableLayout
            {
                Spacing = new Size(6, 0),
                Rows =
                {
                    new TableRow(
                        new TableCell(_filePathBox, scaleWidth: true),
                        new TableCell(_browseButton)),
                },
            };

            var buttonRow = new TableLayout
            {
                Spacing = new Size(6, 0),
                Rows =
                {
                    new TableRow(
                        null, // spacer pushes buttons to the right
                        new TableCell(_importButton),
                        new TableCell(_closeButton)),
                },
            };

            var layout = new DynamicLayout
            {
                Padding = new Padding(12),
                DefaultSpacing = new Size(8, 8),
            };

            if (logo is not null)
            {
                layout.AddCentered(logoView);
            }
            layout.AddRow(new Label { Text = "IFC file" });
            layout.AddRow(fileRow);
            layout.AddRow(new Label { Text = "Coordinates" });
            layout.AddRow(_coordinateChoice);
            layout.AddRow(_ignoreOpeningsCheck);
            layout.AddRow(_progressBar);
            layout.AddRow(buttonRow);

            // The status label sits in its own bottom "status bar" strip.
            var root = new DynamicLayout();
            root.Add(layout, yscale: true);
            root.Add(new Panel
            {
                Padding = new Padding(12, 6),
                Content = _statusLabel,
            });

            Content = root;

            // (Cancellation on close is handled by the OnClosing override below,
            // which also covers closing via the title bar.)
        }

        /// <summary>
        /// Reads the file path currently chosen by the user, if any.
        /// </summary>
        private string? SelectedPath => string.IsNullOrWhiteSpace(_filePathBox.Text) ? null : _filePathBox.Text;

        // ----------------------------------------------------------------------
        // Event handlers
        // ----------------------------------------------------------------------

        private void OnBrowse()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select an IFC file",
                MultiSelect = false,
                Filters =
                {
                    new FileFilter("IFC files", ".ifc"),
                    new FileFilter("All files", ".*"),
                },
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok && !string.IsNullOrEmpty(dialog.FileName))
            {
                _filePathBox.Text = dialog.FileName;
                SetStatus($"Selected {Path.GetFileName(dialog.FileName)}.");
            }
        }

        private void OnImport()
        {
            if (_isImporting)
                return;

            string? path = SelectedPath;
            if (path is null || !File.Exists(path))
            {
                SetStatus("Please choose an existing .ifc file first.");
                return;
            }

            // Snapshot the user's choices into an options object on the UI thread.
            var options = new ImportOptions
            {
                CoordinateMode = _coordinateChoice.SelectedIndex == 1
                    ? CoordinateMode.Shared
                    : CoordinateMode.Project,
                OpeningFilterMode = _ignoreOpeningsCheck.Checked == true
                    ? OpeningFilterMode.IgnoreAll
                    : OpeningFilterMode.Default,
                JoinByProperties = true,
            };

            RhinoDoc? doc = RhinoDoc.ActiveDoc;
            if (doc is null)
            {
                SetStatus("No active Rhino document.");
                return;
            }

            BeginImportingUiState();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            // Progress is reported from the background thread; marshal each update
            // onto the UI thread before touching the widgets.
            var progress = new Progress<ImportProgress>(p => Application.Instance.AsyncInvoke(() =>
            {
                _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
                _statusLabel.Text = p.Status;
            }));

            // Run the heavy work off the UI thread.
            Task.Run(() =>
            {
                var service = new IfcImportService();
                return service.Import(doc, path, options, progress, ct);
            })
            .ContinueWith(task => Application.Instance.AsyncInvoke(() => OnImportCompleted(task)),
                          TaskScheduler.Default);
        }

        /// <summary>
        /// Runs on the UI thread once the background import task finishes (whether
        /// it succeeded, was cancelled, or threw).
        /// </summary>
        private void OnImportCompleted(Task<ImportResult> task)
        {
            EndImportingUiState();

            if (task.IsCanceled || (_cts?.IsCancellationRequested ?? false))
            {
                SetStatus("Import cancelled.");
            }
            else if (task.IsFaulted)
            {
                // Unwrap the AggregateException to show the real cause.
                Exception ex = task.Exception?.GetBaseException() ?? new Exception("Unknown error.");
                string detail = ex is IfcLiteException ifc
                    ? $"{ifc.Message} (code {ifc.ErrorCode})"
                    : ex.Message;
                SetStatus($"Import failed: {detail}");
                RhinoApp.WriteLine($"IfcLite import failed: {detail}");
            }
            else
            {
                ImportResult r = task.Result;
                _progressBar.Value = 100;
                SetStatus($"Imported {r.ObjectCount} objects from {r.MeshCount} meshes " +
                          $"({r.SchemaVersion}) in {r.ElapsedMs} ms.");
                // Make the new geometry visible right away.
                RhinoDoc.ActiveDoc?.Views.Redraw();
            }

            _cts?.Dispose();
            _cts = null;
        }

        // ----------------------------------------------------------------------
        // UI state helpers
        // ----------------------------------------------------------------------

        private void BeginImportingUiState()
        {
            _isImporting = true;
            _progressBar.Value = 0;
            SetInputsEnabled(false);
            // While importing, the "Close" button doubles as a cancel button.
            _closeButton.Text = "Cancel";
        }

        private void EndImportingUiState()
        {
            _isImporting = false;
            SetInputsEnabled(true);
            _closeButton.Text = "Close";
        }

        private void SetInputsEnabled(bool enabled)
        {
            _filePathBox.Enabled = enabled;
            _browseButton.Enabled = enabled;
            _coordinateChoice.Enabled = enabled;
            _ignoreOpeningsCheck.Enabled = enabled;
            _importButton.Enabled = enabled;
            // _closeButton stays enabled so the user can cancel/close at any time.
        }

        private void SetStatus(string text) => _statusLabel.Text = text;

        // ----------------------------------------------------------------------
        // Resource loading
        // ----------------------------------------------------------------------

        /// <summary>
        /// Loads the header logo from the embedded resource, returning <c>null</c>
        /// if the resource is missing or cannot be decoded. The dialog must never
        /// fail to open just because the (optional) logo is absent.
        /// </summary>
        private static Bitmap? TryLoadLogo()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using Stream? stream = asm.GetManifestResourceStream(LogoResourceName);
                if (stream is null)
                    return null;

                return new Bitmap(stream);
            }
            catch
            {
                // Any failure (missing resource, unsupported format, …) → no logo.
                return null;
            }
        }

        // ----------------------------------------------------------------------
        // Closing override — make sure a running import is cancelled.
        // ----------------------------------------------------------------------

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // "Close" while importing should cancel the work rather than leaving an
            // orphaned task; the window still closes.
            _cts?.Cancel();
            base.OnClosing(e);
        }
    }
}
