# TeraCopy investigation and Evacopy performance changes

Investigation date: 2026-11-14. Implementation: Evacopy v0.1.6.

## Targets and method

The supplied `teracopy.exe` was a TeraCopy 4.0.5 installer, SHA-256 `22f050b51c80a344cbdaf368c7f6a9266efca4d95b5109d606466906a421ff7e`. The user subsequently selected the installed application under `C:\Program Files\TeraCopy`.

The installed `TeraCopy.exe` is a 6,115,648-byte native x64 PE image, SHA-256 `1f0cca0be4e6a22e856c3b0de6655a1af8059a06cc66133398307de335098c6f`. REA static inspection and Ghidra decompilation did not launch or modify TeraCopy. REA 6.8 CLI was used initially; the installed REA 6.9 CLI then decompiled the same entry point with Ghidra 12.1.4 and temporary process-local engine/JDK settings. The REA 6.9 connector retained the resulting Evidence and performed independent PE-import and entropy inspection. No global REA configuration was changed.

## Findings and coverage

| Question | Evidence | Finding and limits |
| --- | --- | --- |
| Is this managed .NET code? | `ev_da1b3a980384b8599729c0dde4998d56e1c4a6b27430ce83116a88103d81618c` | The installed PE has no CLI metadata directory. A C# decompiler cannot recover its engine. |
| Which APIs are statically imported? | `ev_e734f55d6df98dee072bb3ae287026839ac6b5e516dd871d4275201baa94f171` | Four named imports across four DLLs are present in the inspected tables. Runtime-resolved imports remain unknown. |
| What did native analysis discover? | `ev_211e08cbda779c77e85aab4767d71243bd361f18fdc0a4eb771b9cb5ba4864f6` | Ghidra's default analysis defined seven procedures. This is discovery coverage, not the application's actual function count. |
| Could the copy engine be decompiled? | REA 6.9 / Ghidra: `ev_fe6f7feb190003da3248543921ab03db2eb67ecee528d8bb5bb208aeba782f00` | Entry point `0xf6ba48` yielded bootstrap pseudocode with 28 warnings, including overlapping instructions and invalid control flow. The readable copy engine, buffering policy, I/O loop and scheduling algorithm remain unresolved. A packed/protected bootstrap is an inference; no particular protector is identified. |
| Does entropy identify the packer? | `ev_fb12e79cfeb548858bd9f223c0f0769a117b7be7e577e53560c10cb1adbb948b` | File entropy is 7.5082 bits/byte. High entropy alone cannot distinguish compression from encryption or prove a packer. |
| Which features are documented by the shipped product? | Installed `Readme.txt` and `Whatsnew.txt`, read as ordinary files | They describe asynchronous/buffered copying, multithreading, configurable buffers, same-drive move optimization, queueing, pause/resume and recovery. These are documented features, not observations of the runtime I/O algorithm. |

The implementation below is independently written GPL code. No TeraCopy executable, proprietary pseudocode, resources or decompiled function bodies are incorporated into Evacopy or committed to this repository. Detailed private analysis bundles remain outside tracked source. Queueing, pause/resume, LocalSend and Explorer replacement are outside this release.

## Implemented improvements

1. **Bounded progress monitoring.** The old worker statted every destination file every 350 ms. The new worker keeps a rolling estimate and samples at most 128 files per update, with a cooperative 20 ms budget. One blocking network metadata call can exceed that time budget. Final file-size/CRC verification remains complete.
2. **Less worker bookkeeping.** Preparation validates each destination directory once per phase. Cancellation-file polling is throttled to approximately 100 ms during ordinary work; destructive operations still force a fresh cancellation check. Enumeration now checks cancellation too.
3. **Faster optional CRC.** An independent IEEE CRC-32 slicing-by-8 implementation replaces the worker's bytewise loop and reuses one 1 MiB read buffer. The polynomial remains `0xEDB88320`, with the same initialization/final XOR. The old bytewise implementation remains the independent test reference. CRC remains optional and off by default for Copy and Move.
4. **Same-volume Move fast path.** A single folder or single selected file with CRC off can be renamed when its destination does not exist and Windows volume GUIDs match. This avoids copying its file data. Existing-target merges, multiple selected files, different/unknown volumes, CRC-on moves and failed rename attempts use the existing Robocopy path. Reparse/overlap checks remain in effect.
5. **Large-file I/O option.** `I/O → Large files` selects Robocopy `/J` unbuffered I/O. `Standard` is the default. This is a workload option, not a universal speed improvement or a reproduction of TeraCopy's internal buffer sizing. Windows Robocopy itself is unchanged.

## Validation

Build and integration checks passed for:

- bounded probe counts and rolling estimates without double counting;
- accelerated CRC against the independent bytewise reference over multiple lengths, offsets and chunk boundaries;
- same-volume file/folder rename and existing-target merge fallback;
- large-file `/J` operation with correct destination content;
- both optional CRC modes, source cleanup, long/spaced paths and selected-file transfers;
- concurrent workers, service recovery, cancellation and full tray shutdown.

## Local benchmark

Three runs per case, warmed service processes, Windows 10 build 19045, four logical processors, 32 Robocopy threads and Normal priority. No system cache clearing was performed. Fixture creation and independent content/hash validation were outside timed regions. Evacopy timings include job submission, worker startup, scanning, transfer and final checks. Direct Robocopy has no Evacopy job bookkeeping. The CRC case is CPU-only, processing the same 32 MiB array eight times.

| Workload | v0.1.5 median | v0.1.6 median | Other reference |
| --- | ---: | ---: | ---: |
| Copy 6,000 files × 4 KiB | 4,962 ms | 3,698 ms | Direct Robocopy: 3,150 ms |
| Same-volume Move, 128 MiB | 294 ms | 130 ms | v0.1.6 used filesystem rename |
| CRC, 32 MiB × 8 | Bytewise: 686 ms | Slicing-by-8: 375 ms | Equal CRC results |

The copy median fell about 25% and the move median about 56%. Copy results varied substantially: candidate times were 2,597, 3,698 and 6,012 ms; the third candidate run was slower than its v0.1.5 reference. These are local, warm-cache observations, not guaranteed gains on other storage, networks or workloads. TeraCopy throughput was not benchmarked.

[Raw runs and executable hashes](performance-v0.1.6.json) accompany this report. Candidate hash: `f56c956ef25dc59201044dbf300d62edabb36df6f2398f1436197833ee816b1b`. It was captured immediately after the benchmark; the candidate EXE was not rebuilt during or after that run.

## Reproduce

With published baseline/candidate EXEs in the shown directories:

```powershell
New-Item -ItemType Directory -Force .tools | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /out:.tools\PerformanceBenchmark.exe /reference:System.Web.Extensions.dll tools\PerformanceBenchmark.cs src\FastCrc32.cs
.\.tools\PerformanceBenchmark.exe dist\v0.1.5\Evacopy.exe dist\v0.1.6\Evacopy.exe .tools\performance\reproduce 6000
```

The harness creates disposable fixtures beneath the selected evidence directory, uses isolated service profiles, verifies results and stops its service processes.
