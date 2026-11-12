# Evacopy

Run **dist\release\Evacopy.exe**. This is the current release; EXEs elsewhere in dist are earlier builds. Close earlier versions before opening Evacopy so they release their global shortcuts. One portable executable contains the UI, service and worker entry points, theme, and icon. Windows Robocopy and .NET Framework 4.8 are required (included in modern Windows). No installer, administrator access or package downloads are needed.

## Multiple copy jobs

Choose a source folder or multiple files and a destination. Missing destination folders are created. Folder selections copy their contents recursively, including empty folders. Existing destination files may be overwritten; destination-only files are retained.

Choose 8–128 Robocopy threads and Normal, AboveNormal or High CPU priority independently for each job. Click **Start copy job**. The **Transfers** tab opens to show the job list. Click **+ ⚙** to choose the next source on the **New job** tab; choose its destination and settings, then start another job. Select a job to see its status, percentage and speed or use **Cancel selected**. Jobs with overlapping destinations, conflicting source/destination folders, or source-deletion conflicts are rejected.

Copy percentages and average speeds are estimates from destination file lengths and timestamps. Existing files, preallocation and cached writes can affect them. Verification progress reflects verified bytes. Worker and Robocopy process IDs are shown in the job list. Hover over a job for details or its error. Robocopy output is saved with the job under `%LOCALAPPDATA%\Evacopy\Jobs`.

## Process architecture

Evacopy uses a small **per-user supervisor service**, running as `Evacopy.exe --service`, with one independent `Evacopy.exe --worker <job-id>` process per job and a separate Robocopy child for actual copying. This is a user-mode supervisor, not a kernel driver or an installed Windows Service Control Manager service. It runs under your user account without elevation.

The UI communicates asynchronously over a named pipe restricted to your Windows account. Copying, CRC reads and filesystem scanning happen in workers. Closing or exiting the UI does not cancel jobs. Reopening the UI reconnects to the service and restores the job list. Job manifests and atomic progress snapshots are stored locally. The service exits after an idle interval without active workers. A service restart can reconnect to surviving workers using their process identity. A Windows job object terminates the Robocopy child if its worker crashes. Failed or interrupted jobs are not automatically resumed.

High priority is requested for each worker and its Robocopy process; Windows may deny it. Priority and thread count do not guarantee full CPU usage. Storage throughput and concurrent jobs affect performance.

## Verification and source cleanup

Source size and CRC-32 are recorded before copying, then both source and destination are verified. Only after every recorded file passes can optional permanent source deletion begin. Deletion defaults off and requires confirmation. Each source and destination is checked again under file locks immediately before source deletion.

For folder selections, recorded empty source directories are removed from the bottom up, including the selected root, after verified deletion. Non-empty directories and drive roots are retained. When selecting individual files, their parent folders remain. Cancellation or failure during deletion can leave some source files already deleted. Partial destination files may remain after cancellation. CRC-32 detects accidental corruption and is not a cryptographic signature.

Overlapping source/destination folders, linked files, junctions, reparse directories and duplicate destination filenames are rejected. Use stable source folders: Evacopy does not create filesystem snapshots or track new files added after scanning. It copies data, attributes and timestamps, not ACLs. Windows permissions and path-length limits apply.

## Desktop integration and appearance

The opening window follows the Mouse Without Borders Settings default landscape dimensions: a 571 × 391 content area, approximately 577 × 420 including the Windows frame at 100% scaling. Dimensions scale with Windows display settings. Separate New job and Transfers tabs keep the controls usable at this size. Reference: read-only inspection of the installed MouseWithoutBorders.frmMatrix.InitializeComponent method (ClientSize 571 × 391; DPI design baseline 96 × 96). Live window screenshots were unavailable during verification.

The interface uses the requested six-color palette: background `#051125`, cards `#0A2342`, cyan accent `#00F0FF`, blue borders `#1A9CFF`, secondary controls `#0A507A`, and white text `#FFFFFF`. Checkbox labels have an explicit white foreground and a custom template. The image from the supplied Gemini share is embedded as a multiresolution EXE/window/tray icon and header image.

**Start with Windows** starts Evacopy hidden in the tray at sign-in. Put the EXE in a permanent location before enabling it. The checkbox removes the startup entry when disabled. Evacopy uses its own startup setting; disable the old SuperRobocopy startup option if you enabled that earlier.

The global shortcut tries **Win+C**, then **Ctrl+Alt+C** if Windows reserves Win+C. The checkbox displays the active shortcut. If both are unavailable, use the tray icon. Shortcuts work while the UI process is running. Close/minimize hide it to the tray; double-click the tray icon to reopen. The tray menu's **Exit** quits the UI while jobs continue in the background.

## Build and validation

```powershell
.\build.ps1
.\test.ps1
```

Build checks cover the CRC known vector, path boundaries, compact layout, checkbox contrast and embedded icon. Integration checks launch real supervisor, worker and Robocopy processes and cover simultaneous jobs, independent PIDs, overlap rejection, supervisor restart recovery, copy/CRC success, Unicode paths, empty directories, source preservation, verified move cleanup and cancellation. Test reports are saved under test-results. Test fixtures use an isolated service/profile and do not alter startup settings.

The EXE is unsigned. Try disposable data before enabling permanent deletion for important files.

