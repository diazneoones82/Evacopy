# Evacopy

Download **Evacopy.exe** from the [latest GitHub release](https://github.com/diazneoones82/Evacopy/releases/latest). Local builds default to `dist\release\Evacopy.exe`; versioned builds also appear under `dist\v0.1.x` when an older EXE is running. Close earlier versions before opening Evacopy so they release their global shortcuts. One portable executable contains the UI, service and worker entry points, theme, and icon. Windows Robocopy and .NET Framework 4.8 are required (included in modern Windows). No installer, administrator access or package downloads are needed.

## Multiple copy jobs

Click source **Browse…**, then choose **Folder…** or **Files…** from the menu underneath. Use destination **Browse…** to select the destination. Both pickers open at the currently displayed path; file selections start in the selected file location. If a destination path does not yet exist, the picker opens at its closest existing parent folder. A new job also remembers the previous source browsing location. Missing destination folders are created. For folder selections, **Keep source folder name** is enabled by default: selecting `C:\Source\Photos` and destination `D:\Backup` copies into `D:\Backup\Photos`, creating that folder when needed. If the selected destination is already named `Photos`, it is reused, avoiding `Photos\Photos`. Uncheck this option to copy only the source folder's contents directly into the destination. The option is disabled for individual-file selections. Both modes retain nested folders and empty folders. Existing destination files may be overwritten; destination-only files are retained.

Choose 8–128 Robocopy threads and Normal, AboveNormal or High CPU priority independently for each job. Choose **Action → Copy** to keep the source, or **Action → Move** to copy and CRC-verify the data before removing source files and empty folders. Click **Start copy job** or **Start move job**. Move uses verified copy-and-cleanup for both same-drive and cross-drive transfers; it does not use an instant same-drive rename. The **Transfers** tab opens to show the job list. Click **+ ⚙** to choose the next source on the **New job** tab; choose its destination and settings, then start another job. Select a job to see its status, percentage and speed or use **Cancel selected**. Jobs with overlapping destinations, conflicting source/destination folders, or source-deletion conflicts are rejected.

Copy percentages and average speeds are estimates from destination file lengths and timestamps. Existing files, preallocation and cached writes can affect them. Verification progress reflects verified bytes. Worker and Robocopy process IDs are shown in the job list. Hover over a job for details or its error. Robocopy output is saved with the job under `%LOCALAPPDATA%\Evacopy\Jobs`.

## Process architecture

Evacopy uses a small **per-user supervisor service**, running as `Evacopy.exe --service`, with one independent `Evacopy.exe --worker <job-id>` process per job and a separate Robocopy child for actual copying. This is a user-mode supervisor, not a kernel driver or an installed Windows Service Control Manager service. It runs under your user account without elevation.

The UI communicates asynchronously over a named pipe restricted to your Windows account. Copying, CRC reads and filesystem scanning happen in workers. Closing or minimizing the window hides it to the tray without cancelling jobs. Choosing **Exit** from the tray cancels active jobs and stops all workers, Robocopy children and the supervisor before closing the UI. Shutdown first requests cancellation and then terminates workers that do not stop within the grace period. Reopening the UI reconnects to the service and restores the job list. Job manifests and atomic progress snapshots are stored locally. The service exits after an idle interval without active workers. A service restart can reconnect to surviving workers using their process identity. A Windows job object terminates the Robocopy child if its worker crashes. Failed or interrupted jobs are not automatically resumed.

High priority is requested for each worker and its Robocopy process; Windows may deny it. Priority and thread count do not guarantee full CPU usage. Storage throughput and concurrent jobs affect performance.

## Verification and source cleanup

Normal copy jobs use fast mode by default: **Verify CRC-32** is unchecked, no file data is read for checksums, and completion uses Robocopy status plus destination file-size checks. Standard copy mode replaces restartable `/Z` copying, unchanged files are not forced to recopy, and selected files in the same directory are batched into multithreaded Robocopy calls. These changes remove extra work; actual throughput depends on your disks, network, files and thread count.

Enable **Verify CRC-32** when content verification is needed. This records source size and CRC-32 before copying, then verifies both source and destination. **Action → Move** automatically requires CRC verification even if the optional checkbox was off. Switching back to Copy restores your previous optional CRC choice. Only after every recorded file passes can optional permanent source deletion begin. Copy is the default action; Move requires confirmation. Each source and destination is checked again under file locks immediately before source deletion.

For folder selections, recorded empty source directories are removed from the bottom up, including the selected root, after verified deletion. Non-empty directories and drive roots are retained. When selecting individual files, their parent folders remain. Cancellation or failure during deletion can leave some source files already deleted. Partial destination files may remain after cancellation. CRC-32 detects accidental corruption and is not a cryptographic signature.

Overlapping source/destination folders, linked files, junctions, reparse directories and duplicate destination filenames are rejected. Use stable source folders: Evacopy does not create filesystem snapshots or track new files added after scanning. It copies data, attributes and timestamps, not ACLs. Long names with spaces and nested paths longer than 260 characters are supported through the long-path-aware manifest and .NET path handling. Copy and Move are integration-tested with these paths. Windows permissions and filesystem limits still apply, including the usual 255-character limit for each individual name. Selected files may be moved into a subfolder of their current parent without moving unselected files.

## Desktop integration and appearance

The opening window follows the Mouse Without Borders Settings default landscape dimensions: a 571 × 391 content area, approximately 577 × 420 including the Windows frame at 100% scaling. Dimensions scale with Windows display settings. Separate New job and Transfers tabs keep the controls usable at this size. Reference: read-only inspection of the installed MouseWithoutBorders.frmMatrix.InitializeComponent method (ClientSize 571 × 391; DPI design baseline 96 × 96). Live window screenshots were unavailable during verification.

The interface uses the requested six-color palette: background `#051125`, cards `#0A2342`, cyan accent `#00F0FF`, blue borders `#1A9CFF`, secondary controls `#0A507A`, and white text `#FFFFFF`. Checkbox labels have an explicit white foreground and a custom template. The image from the supplied Gemini share is embedded as a multiresolution EXE/window/tray icon and header image.

**Start with Windows** starts Evacopy hidden in the tray at sign-in. Put the EXE in a permanent location before enabling it. The checkbox removes the startup entry when disabled. Evacopy uses its own startup setting; disable the old SuperRobocopy startup option if you enabled that earlier.

The global shortcut tries **Win+C**, then **Ctrl+Alt+C** if Windows reserves Win+C. The checkbox displays the active shortcut. If both are unavailable, use the tray icon. Shortcuts work while the UI process is running. Close/minimize hide it to the tray; double-click the tray icon to reopen. The tray menu's **Exit** stops active jobs and all Evacopy background processes before quitting.

## Build and validation

```powershell
.\build.ps1
.\test.ps1
```

Build checks cover the CRC known vector, path boundaries, compact layout, checkbox contrast and embedded icon. Integration checks launch real supervisor, worker and Robocopy processes and cover simultaneous jobs, independent PIDs, overlap rejection, supervisor restart recovery, copy/CRC success, fast-mode jobs with zero CRC bytes read, optional verification, batched file selections, Unicode paths, paths over 260 characters, long folder names with spaces, Copy/Move actions, empty directories, source preservation, verified move cleanup, cancellation, and complete shutdown while idle and during an active copy. Picker checks cover current folders, file parent folders, nonexistent destination paths and the native Explorer starting location. Test reports are saved under test-results. Test fixtures use an isolated service/profile and do not alter startup settings.

The EXE is unsigned. Try disposable data before enabling permanent deletion for important files.

## License

Copyright (C) 2026 diazneoones82.

Evacopy is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version (**GPL-3.0-or-later**).

Evacopy is distributed without any warranty, including the implied warranties of merchantability or fitness for a particular purpose. See [LICENSE](LICENSE) for the complete license terms.
