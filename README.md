# Scribo ✍️

[![.NET](https://img.shields.io/badge/.NET-9.0--windows-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Download Release](https://img.shields.io/github/v/release/0xPyAI/scribo?label=Download%20.EXE&style=flat-square&color=2ea44f&logo=windows)](https://github.com/0xPyAI/scribo/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4?style=flat-square&logo=windows)](https://microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-WPF%20%2F%20Win32-blue?style=flat-square)](#architecture)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](#license)

**Scribo** is a high-performance, 100% native Windows desktop screen annotation, whiteboard, and presentation suite built in **C# (.NET 9) & WPF**.

---

## ⚡ Quick Download

You don't need to install build tools or compile anything to use Scribo:

* 📦 **[Download Full Working Package (Zip)](https://github.com/0xPyAI/scribo/releases/download/v1.0.0/Scribo-v1.0.0-win-x64-Full-Package.zip)** *(Recommended — 58 MB)*  
  Complete self-contained folder with all runtime libraries, WPF dependencies, `icon/` assets, and `run.bat` launcher. **Works out-of-the-box on any Windows 10/11 PC with zero prerequisites.** Just extract and launch!
  For machines with [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) already installed.
* 🏷️ **[View All Releases on GitHub](https://github.com/0xPyAI/scribo/releases/latest)**

---

Designed specifically for **software demos, live coding walkthroughs, educators, and technical presenters**, Scribo combines fluid hardware-accelerated screen drawing, split-screen paper notebooks, live keystroke visualization, and focus tools into a single, cohesive utility—with zero web browser or Electron overhead.

---

## 🌟 Key Highlights

* **100% Native & Hardware-Accelerated:** Powered directly by Windows Presentation Foundation (`InkCanvas`) and Win32 Desktop Window Manager (DWM). Sub-millisecond latency with smooth Bézier curve interpolation.
* **Modern Neo-Pop Design:** Eye-catching 2D Neo-Pop cartoon aesthetic featuring high-contrast borders, springy micro-animations, clear vector glyphs, and collapsible tool groups.
* **Dual Dock Orientation:** Seamlessly switch between a **Horizontal Toolbar** and a sleek **Vertical Dock** with a single click.
* **True Desktop Pass-Through:** Transparent overlay lets you draw on screen, then click directly through to apps underneath using native Win32 `WS_EX_TRANSPARENT`.
* **Built-in Presentation Suite:** Includes an integrated live **Keystroke Visualizer**, **Spotlight**, **Magnifier**, **Laser Pointer**, and **Numbered Step Badges**—eliminating the need to run 3+ separate utilities.
* **Interactive Whiteboard & Paper Notebooks:** 6 background templates (Whiteboard, Graph Paper, Ruled Notebook, Dot Matrix, Greenboard, Frosted Glass) plus **Split-Screen layouts** (`◧` / `◨`) and multi-page notebooks.

---

## 🎨 Feature Overview

### 1. ✏️ Drawing & Inking Tools
* **Pen (`P`):** Fluid, pressure-sensitive freehand vector pen.
* **Highlighter (`H`):** Semi-transparent ink designed for highlighting code, terminal logs, and text without obscuring content.
* **Laser Pointer (`L`):** Self-fading pointer trail that automatically evaporates after ~1.3 seconds, keeping your screen uncluttered.
* **Stroke Eraser (`E`):** Instant, single-click stroke deletion. Click again to return directly to cursor mode.
* **Select & Transform (`S`):** Lasso or click elements to move, recolor, resize, or delete them.
  * **Hold-to-Select Gesture:** Hold a modifier key (e.g. `Alt`) to temporarily enter select mode, drag an item, and release to instantly resume drawing.

### 2. 📐 Geometric Shapes (Split Button)
* **Shapes Library:** Rectangle, Circle / Ellipse, Single Arrow (`A`), Double Arrow, and Straight Line.
* **Semi-Transparent Shape Fill:** Toggle fill to draw highlighted boxes and shaded callouts.
* **Auto-Revert Workflow:** Automatically switches back to the Cursor mode as soon as a shape is drawn, preventing accidental duplicate shapes.
* **Dropdown Flyout:** Click the chevron arrow or right-click the Shape icon to select your preferred geometry.

### 3. 🔤 Typography & Text Tool (Split Button)
* **In-Place Text Notes (`T`):** Click anywhere on screen to type sharp, scalable vector annotations.
* **Font Family Dropdown:** Curated list of popular fonts with WYSIWYG previews:
  * *Segoe UI*, *Arial*, *Calibri*, *Consolas (Code)*, *Comic Sans MS*, *Impact*, *Times New Roman*, *Georgia*, *Trebuchet MS*, *Verdana*.
* **Font Size & Steppers:** Sizes from `14px` to `96px` with quick **`A-`** and **`A+`** stepper buttons.
* **Style Toggles:** **Bold (`B`)**, *Italic (`I`)*, and <u>Underline (`U`)</u> toggles.
* **Live In-Editor Hotkeys:** Press `Ctrl + B`, `Ctrl + I`, or `Ctrl + U` while typing to toggle styles in real time.
* **Double-Click Re-Edit:** Double-click any existing text on screen to edit its text, font, or styling.

### 4. 🔢 Numbered Step Badges (`N`)
* Click anywhere on screen to stamp circular numbered sequence badges (**①, ②, ③...**).
* Sequence automatically increments with each stamp.
* **Right-click** the badge tool at any time to reset the sequence back to **1**.

### 5. 📋 Whiteboards & Split-Screen Notebooks
Switch between Desktop drawing and full-featured digital boards with independent stroke memory:
* **6 Paper & Canvas Styles:**
  * **Desktop (Transparent):** Annotate directly over your active applications, IDE, or browser.
  * **Plain Whiteboard:** Clean, solid white canvas.
  * **Grid / Graph Paper:** Crisp Slate (`#CBD5E1`) engineering grid lines.
  * **Ruled Notebook:** Notebook-blue (`#93C5FD`) horizontal ruled lines (36px ruling).
  * **Dot Matrix Grid:** Modern bullet-journal dot matrix (`#94A3B8`).
  * **Classroom Greenboard:** Chalkboard green (`#1B4D3E`) with **Smart Pen Auto-Contrast** (automatically turns dark pen white).
  * **Frosted Glass:** Translucent tinted overlay (`#DCF8FAFC`).
* **Screen Layout Modes:**
  * **Full:** Fullscreen board canvas.
  * **Split-Left (`◧`):** Left-half board; right-half desktop pass-through.
  * **Split-Right (`◨`):** Right-half board; left-half desktop pass-through.
* **Multi-Page Notebook:**
  * Dedicated `Prev`, `Next`, and `+ Blank Page` buttons with live page indicator (`1/1`, `1/2`...).
  * Each page retains its own independent drawing canvas.
  * Desktop annotations and board notes are isolated—switching between them never erases your work.

### 6. 🔦 Focus & Presentation FX
* **Spotlight (`F`):** Dims the screen with a dark vignette while leaving a circular spotlight following your cursor to direct attention.
* **Live Magnifier (`M`):** Floating 2× optical zoom loupe with crosshair reticle.
* **Eye Icon (Hide/Reveal):** Temporarily toggle the visibility of all drawings with a single click without clearing the canvas.
* **Keystroke Visualizer:** Displays live keyboard shortcuts (e.g. `Ctrl + Shift + P`) on screen using animated floating pill badges. Default disabled, easily toggled in settings.

### 7. 🎨 Color Palette & Stroke Width
* **12 Curated Neo-Pop Swatches:** High-contrast palette designed for dark and light backgrounds with bouncy spring hover animations.
* **4-Step Thickness Slider:** Adjust stroke and shape line weight with a quick slider and numerical indicator.

### 8. 📸 Screen Capture & Export
* **1-Click Screenshot:** Captures your screen together with all annotations directly to the Windows Clipboard.
* Integrated Windows System Tray balloon notification confirms when the screenshot is ready to paste.

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **`Alt + S`** / **`F8`** | Summon / Toggle Scribo from System Tray (Configurable) |
| **`P`** | Pen Tool |
| **`H`** | Highlighter Tool |
| **`L`** | Laser Pointer (auto-fading trail) |
| **`T`** | Text Tool (Click on screen to type) |
| **`N`** | Numbered Step Badge (①, ②, ③...) |
| **`E`** | Stroke Eraser |
| **`S`** | Select / Transform Tool |
| **`B`** | Toggle Board Mode (Desktop ⇄ Whiteboard) |
| **`F`** | Spotlight Focus Mode |
| **`M`** | Magnifier / 2× Zoom Loupe |
| **`Ctrl + Z`** | Undo last stroke / action |
| **`Ctrl + Y`** | Redo last action |
| **`Ctrl + K`** | Clear active canvas |
| **`Ctrl + B`** *(in text)* | Toggle Bold |
| **`Ctrl + I`** *(in text)* | Toggle Italic |
| **`Ctrl + U`** *(in text)* | Toggle Underline |
| **`Esc`** | Exit current tool / return to Cursor pass-through mode |

---

## ⚙️ Settings & Customization

Click the **Settings (⚙️)** icon on the toolbar to configure:
* **Summon Hotkey:** Choose between `Alt + S (Default)`, `F8`, `Ctrl + Shift + S`, or `Ctrl + Alt + D`.
* **Hold-to-Select Key:** Choose between `Alt (Default)`, `Ctrl`, `Shift`, `Space`, or `Disabled`.
* **Keystroke Visualizer:** Enable or disable live shortcut overlay display.
* **Default Toolbar Dock:** Choose between `Horizontal` or `Vertical`.
* **Launch on Startup:** Toggle automatic startup with Windows.

---

## 🚀 Getting Started

### Prerequisites
* **Operating System:** Windows 10 (1809+) or Windows 11
* **Runtime:** [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) to build)

### Running from Source
1. Clone the repository:
   ```powershell
   git clone https://github.com/0xPyAI/scribo.git
   cd scribo
   ```
2. Build and run:
   ```powershell
   dotnet run
   ```
3. Or double-click `run.bat` in the project root (it automatically launches the compiled `.exe` if available, or compiles via `dotnet run`).

### Building Binaries & Output Paths

| Build Type | Command | Executable Output Path |
| :--- | :--- | :--- |
| **Debug Build** | `dotnet build` | `bin\Debug\net9.0-windows\Scribo.exe` |
| **Release Build** | `dotnet build -c Release` | `bin\Release\net9.0-windows\Scribo.exe` |
| **Self-Contained Publish** | `dotnet publish -c Release -r win-x64 --self-contained true` | `bin\Release\net9.0-windows\win-x64\publish\Scribo.exe` |
| **Single-File Publish** | `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` | `bin\Release\net9.0-windows\win-x64\publish\Scribo.exe` |

> [!NOTE]
> When downloading the **Full Release ZIP**, `Scribo.exe` is located **directly in the root of the extracted folder** (`.\Scribo.exe`).

---

## 🏗️ Project Architecture

```
scribo/
├── App.xaml / App.xaml.cs          # Application entry, single-instance mutex & crash guards
├── AppSettings.cs                  # Persistent JSON settings engine (%AppData% & portable)
├── Logger.cs                       # Production thread-safe rolling log manager
├── MainWindow.xaml / .cs           # Neo-Pop floating toolbar, dock logic & controls
├── OverlayWindow.xaml / .cs        # Fullscreen transparent DWM inking canvas & presentation tools
├── SettingsWindow.xaml / .cs       # Unified hotkeys section, key capture & presentation toggles
├── NativeMethods.cs                # Win32 user32/gdi32 P/Invoke & low-level hooks
├── ScreenCaptureHelper.cs          # Leak-proof screen & annotation capture to clipboard/file
├── ShapeHelper.cs                  # Geometric vector calculation for arrows & lines
├── TrayHelper.cs                   # Native Windows notification area system tray integration
├── Scribo.csproj                   # .NET 9.0 Windows desktop project definition with metadata
├── app.manifest                    # PerMonitorV2 High-DPI, Win10/11 compatibility manifest
├── app.ico                         # Multi-resolution embedded application & tray icon
├── icon/                           # Custom vector-styled toolbar PNG icons
└── run.bat                         # Quick launch batch script
```

---

## 📄 License

This project is open-source and available under the [MIT License](LICENSE).
