# QuickNotch & ShiftConvert: Engineering Constitution

Unified development guidelines based on **Superpowers**, **Karpathy Principles**, **Ponytail (Lazy Senior)**, **Pocock Engineering**, **Addy Osmani Production Standards**, and **ECC**.

---

## 1. Prime Directive: Think First, Code Second
- **Strict User Command Rule:** NEVER change code without the user's explicit command ("не спеши менять код, никогда больше без моей команды не меняй"). Discuss, plan, explain, and align first.
- **Root Cause First:** Understand the technical reality (e.g. video encoding vs AI super-resolution) before attempting changes. Never guess.

---

## 2. Simplicity & Native Architecture (Ponytail & Karpathy)
- **The best code is the code never written.** Maximum functionality with minimum new lines.
- **Native OS & Platform Power:** Use Windows.Media, WinRT, Direct2D, and native WPF controls instead of pulling in giant external packages (ffmpeg, bulky wrappers).
- **Surgical Edits:** Touch only what must be touched. Zero collateral damage to existing features.

---

## 3. Production Standards (Addy Osmani)
- **0% CPU in Idle:** Dynamic notch and background monitors must sleep efficiently (0% CPU).
- **Responsive 60 FPS UI:** Never run video transcoding, compression, or file I/O on the UI thread.
- **Resource Cleanup:** Dispose streams, cancel operations cleanly via `CancellationToken`, and `.Freeze()` bitmaps/brushes.
- **Single Exe Distribution:** Keep outputs as clean, standalone, single-file executables (`QuickNotch.exe`, `ShiftConvert.exe`).

---

## 4. Verification Gate
- Always verify builds with `dotnet build -c Release` before declaring done.
- Maintain clean git repositories with clear atomic commits.
- Clickable markdown links (`file:///...`) for all files.
