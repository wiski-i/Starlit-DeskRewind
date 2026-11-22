<p align="center">
  <img src="assets/logo.svg" width="88" height="88" alt="Starlit DeskRewind logo">
</p>

# Starlit DeskRewind

Starlit DeskRewind is a Windows tray utility for switching between saved workspaces and rewinding recent window arrangements.

## Run

Build DeskRewind from source, then open `bin\DeskRewind.exe` on Windows 10 or 11 with .NET Framework 4.8. DeskRewind opens your saved layouts and stays in the notification area when you close the window.

1. Move or resize a few windows, pausing briefly between arrangements.
2. Press **Ctrl+Alt+Z**. The filmstrip opens with the previous arrangement selected.
3. Hover over a frame or press **←** and **→** to preview positions on your desktop. Use **Home** or **End** to jump to the oldest or newest frame.
4. Press **Enter** or click **Restore** to apply the selection. Press **Esc** to close the filmstrip.

If **Ctrl+Alt+Z** is already in use, DeskRewind tries **Ctrl+Alt+Shift+Z**. The tray menu shows the registered shortcut and can also open the filmstrip or exit the app.

## History and restoration

DeskRewind keeps up to 32 recent layouts in memory while it runs, including the current layout. It records positions, sizes, and minimized or maximized states. Thumbnails and transparent outlines are drawn from window geometry. When restoring an older layout, DeskRewind can move windows that are still open on your current Windows desktop. If a monitor is disconnected, it fits windows from that monitor into the nearest available work area. Administrator or other protected windows may refuse repositioning.

## Save a workspace

1. Position your application windows directly on the Windows desktop. Open **Live desktop** to see the windows DeskRewind can capture, their display, and whether they are minimized or maximized.
2. Press **Ctrl+Alt+S** or choose **Capture layout** from DeskRewind or its tray menu. DeskRewind saves the open windows and their positions across your displays and selects the new layout. Each capture gets a timestamped name you can rename later.
3. Select a saved layout and click **Restore** or press **Enter**. Set **Open closed apps** to choose whether that layout starts closed applications. DeskRewind remembers this choice for each layout, including when you restore it from the tray.

Select a row in **Saved windows** to highlight its saved position on the monitor map. Each row also shows whether Windows reports the window at that position, open elsewhere, closed, or unmatched. Hover over a title to read it in full.

If the capture shortcut is in use, DeskRewind tries **Ctrl+Alt+Shift+S**. The registered combination appears in the app and the tray menu.

DeskRewind saves workspaces in `%LOCALAPPDATA%\DeskRewind\workspaces.json`. If the file is missing or damaged, DeskRewind can load the previous version from `workspaces.json.bak`. Your next save preserves the damaged file separately.

DeskRewind records minimized windows and skips its own windows. New captures include display identities when Windows exposes them. When multiple windows of the same app could match a saved position, DeskRewind leaves ambiguous matches alone. Apps handle their own tabs and documents. The restore result reports how many windows were placed.

## Build from source

From the project folder, run this in Windows PowerShell. The build uses the C# compiler supplied by .NET Framework and writes `bin\DeskRewind.exe`.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```
