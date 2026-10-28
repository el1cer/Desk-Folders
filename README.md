# Desk-Folders

A quality of life feature for Windows that allows you to make phone-like folders.

Each folder is an icon on your desktop that shows small previews of the apps and files inside it. Double-click it and the folder grows from its icon to the centre of the screen, showing its contents in a grid with pages.

<img width="256" height="256" alt="DeskFolders-7" src="https://github.com/user-attachments/assets/1e67d53b-ec4f-4156-b653-693cd30bcda1" />

## Features

- **Real desktop icons.** Every folder is a normal desktop item. Move it, align it or rename it (F2) like any other icon.
- **Live folder icon.** The desktop icon shows the first nine items inside, in the folder's colour or wallpaper.
- **Opening animation.** The folder expands from the icon you clicked and closes back into it. The rest of the screen dims while it is open.
- **Drag in, drag out.**
  - Drop files, shortcuts or folders onto a folder's desktop icon to add them. The folder does not open.
  - Drag an item out of an open folder to put it back on the desktop.
- **Pages.** Choose the number of rows and columns. When a page is full, a new one is created. Switch pages with the mouse wheel, the arrow keys or the page dots.
- **Rearranging.** Hold an item over a spot and a dashed placeholder shows where it will land. Hover over a page dot to move it to another page. The hold time is adjustable (Move delay).
- **Multi-select.** Drag a selection box from an empty spot, Ctrl+click to add or remove items, Ctrl+A to select everything. Selected items can be moved, opened, removed or deleted together.
- **Appearance.** Per-folder colour, background image and opacity slider.
- **Thumbnails.** Pictures (JPG, PNG, BMP, GIF, TIFF) show a preview. Videos and newer image formats show one when Windows provides it.
- **Recycle Bin.** Delete items from a folder straight to the Recycle Bin, with a confirmation.
- **Right-click → New → DeskFolder.** Create a folder from the desktop context menu.
- **Tray icon.** A small background part sits in the system tray. It keeps folders in sync with their storage and offers *New folder*, *Start with Windows* and *Exit*.
- **Proper uninstall.** DeskFolders appears in *Settings → Apps*. Uninstalling puts everything from your folders back on the desktop.

<img width="1323" height="791" alt="screenshot 1" src="https://github.com/user-attachments/assets/f7119d66-87b2-45b1-aec3-def2ce15f6d3" />

<img width="732" height="559" alt="screen shoot 2" src="https://github.com/user-attachments/assets/97665cde-83d0-4038-94e6-03b6fb0d1742" />

<img width="902" height="603" alt="3" src="https://github.com/user-attachments/assets/03bbaa2e-ac85-4ae2-b79a-b759bc711424" />


## Where your files go

When you add an item from the desktop, it is moved into a storage folder: by default `Documents\DeskFolders\<folder name>`. The original on the desktop goes to the Recycle Bin.

- Items added from anywhere else (for example a program in `Program Files`) are not moved. The folder only remembers where they are.
- You can choose a different storage location from the folder menu (**Storage location**).
- The storage folder and the desktop folder stay in sync:
  - Deleting a file from the storage folder removes it from the DeskFolder.
  - Copying a file in adds it.
  - Deleting the whole storage folder removes the DeskFolder from the desktop.
- Settings and generated icons are kept in `%AppData%\DeskFolders`.

## Requirements

- Windows 10 or 11.
- .NET Framework 4.x. It ships with Windows; no install is needed.

## Building

The repository contains two files:

| File | Purpose |
| --- | --- |
| `DeskFolders.cs` | The source code |
| `build_deskfolders.bat` | Build script, with the app icon embedded |

1. Put both files in the same folder. Keep the folder somewhere permanent, because the desktop icons point to the `.exe` inside it.
2. Double-click `build_deskfolders.bat`.

The script uses the C# compiler that comes with the .NET Framework (`csc.exe`). It extracts the icon, builds `DeskFolders.exe` and starts it. On first start it asks for the name of your first folder.

> The block at the end of `build_deskfolders.bat` that looks like a certificate is the app icon stored as text. `certutil` turns it back into `DeskFolders.ico` during the build. Nothing is signed or installed.

## Usage

| Action | How |
| --- | --- |
| New folder | Right-click desktop → **New** → **DeskFolder**, tray icon → **New folder...**, or open `DeskFolders.exe` |
| Open a folder | Double-click its desktop icon |
| Add items | Drop them onto the folder's desktop icon, or **Add apps or files...** in the folder menu |
| Open an item | Click it |
| Folder menu | The **⋯** button in the top-right corner of an open folder, or right-click an empty spot |
| Item menu | Right-click an item: *Open*, *Remove from folder*, *Delete (Recycle Bin)* |
| Close | Click outside the folder or press **Esc** |

## Uninstall

Use **Uninstall DeskFolders...** in the folder menu, or uninstall it from *Settings → Apps → Installed apps*. Everything inside your folders goes back to the desktop, the folder icons and settings are removed, and the `.exe` deletes itself.

Deleting `DeskFolders.exe` by hand skips this step. In that case your files are still in the storage folder, but the desktop icons stop working.

## Known limitations

- **New-menu placement.** A folder created from **New → DeskFolder** may appear in a different spot than where you right-clicked.
- **Image quality.** App icons come from Windows' 48 px icon set, so they can look soft on high-DPI screens.
- **Protected shortcuts.** Shortcuts installed for all users need administrator permission to be removed from the desktop. Windows asks for it.
