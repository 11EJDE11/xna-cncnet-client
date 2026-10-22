# Designer Tools

The client has built-in tools for theme designers. Start it with the `-designer` parameter to enable them:

```
clientdx.exe -designer
```

Players never see them; without the parameter none of the keys below do anything.

## Hot reload

Press **F5** to reload without restarting:

- every cached image (PNG) that kept its size,
- the theme colors from `DTACnCNetClient.ini`,
- the INI layout of every open window.

The client also reloads by itself shortly after an `.ini` or `.png` file in `Resources` or the current theme folder is saved, so you can keep your INI files open in your editor and see the result as you save.

Reloading re-applies each window's INI to the controls that already exist. Controls added to `$ExtraControls`, `ExtraControls` or as `$CC` children are created; controls removed from an INI stay until the client restarts. Some changes still need a restart:

- Images whose size changed. Controls that read them from an INI key show the new image; images loaded by code keep the old one.
- `Items`, `ItemLabels`, `Icons`, `OptionN` and `ColumnN` keys, which add to lists, and `Checked`, `DefaultIndex` and `SelectedIndex`, which would reset what the player chose. These keys are skipped when reloading.
- Fonts (`Fonts.ini`).
- Layout that code calculates from INI values once, such as the player rows of the game lobbies.
- Values that code sets after a window has loaded may change back to the INI value when reloading, for example a button text that code changes depending on the game state.

## Inspector and editor

Press **F6** to open the inspector. While it is open, clicks go to the inspector instead of the client. **F1** (or **?** in the panel's title bar) lists every shortcut.

- **Hover** outlines the control under the cursor with its name and type.
- **Click** selects a control. The panel shows where it is (window > panel > control; click a part to select it), the INI file its window was loaded from, the section the control reads and what creates it (code, an `ExtraControls` entry or a `$CC` key), and its position. Below that it lists every key of the section with its value, marked by where it was defined: green in the current theme folder, grey inherited from another file (through `BasedOn` or `$BaseSection`), yellow not saved yet. Selecting a key shows the exact file and section it came from.
- A click on a part of a composite control selects the control: the columns and headers of a multi-column list box such as the map list, scroll bars, and a scroll panel's content. **Shift+click** selects the part itself.
- **Ctrl+click** adds a control to the selection or removes it. **PageDown** (or *Inside*) selects every control inside the selected one, for example all the controls of a panel. The panel shows the keys of the control clicked last, the other selected controls, and the files a change of the selection is saved to.
- A toolbar next to the selection adds a control to it, duplicates it, selects its parent or deletes it.
- **Drag** a selected control to move it together with the rest of the selection. The handle at the bottom right corner resizes a single control; with several selected it scales the whole group from its top left corner, positions and sizes alike. **Arrow keys** move the selection by 1 pixel, 10 with **Shift**; **Ctrl+arrow keys** resize each selected control.
- While dragging, the selection's edges and center snap to the edges and centers of neighboring controls and of the parent when they come within 6 pixels, and a guide line shows what they snapped to. Hold **Alt** to drag without snapping. An axis only changes once the drag has moved more than 3 pixels along it, so resizing or moving in one direction leaves the other alone; use the arrow keys for moves smaller than that.
- While the inspector is open, every control with unsaved changes is outlined in yellow with a marker in its top right corner.
- When the edit box holds a key that takes a color, a color picker opens: click a swatch or drag the red, green, blue and alpha sliders. The change is previewed when the swatch is clicked or the slider released.
- When it holds a key that takes an image or a sound (`IdleTexture`, `BackgroundTexture`, `ClickSoundEffect` and so on), the files the client can load are listed: the translation folder, the theme folder with its subfolders, `Resources` and the game folder, each name once, from the folder the client would load it from. Typing after the `=` filters the list. Selecting an image shows it with its size, selecting a sound plays it, and clicking a file applies it. **Up**/**Down** in the edit box move through the list and **Enter** applies.
- Type `Key=Value` in the edit box and press **Enter** to change or add any key. Clicking a key in the list puts it in the edit box, ready to be edited. **Double-clicking** a key that is `true` or `false` (or `yes`/`no`) flips it; double-clicking any other key selects its value, so typing replaces it.
- The **x** at the end of a key's row removes the key. Only keys defined in the theme folder can be removed (green and yellow ones); a grey key comes from a stock file or another section and can only be given another value. If the removed key was saved before and no other file sets it, the control keeps showing the old value until a restart, as the window has no earlier value to go back to, and the panel says so.
- Below the keys, *Positioned from* lists the controls the selected one's `$X`, `$Y`, `$Width` or `$Height` reads, as in `$X=getX(MapPreviewBox)`, and *Followed by* lists the controls that read it, so they move or resize with it. On screen they are outlined and joined to it by a line: green for the controls it reads, purple for those that follow it. Double-click a row to select that control. *Unlink* replaces those keys with their current value as a plain number, for the selected row or, with no row selected, for all of them, so the controls stay where they are but no longer follow.
- While typing a key, the keys the selected control can read are suggested, with their current values; prefix matches come first. **Up**/**Down** choose one, **Tab** or **Enter** (or a click) puts it in the edit box with its current value, **Esc** hides the list. The line above the edit box says what kind of value the key expects. The suggestions come from a list in `DesignerKeyCatalog.cs`, which needs updating when a control learns a new INI key.
- **PageUp** selects the parent of the selected control; **Esc** deselects.
- **F9** (or *Outlines*) outlines every control, which shows where invisible panels and empty labels are.
- *Open INI* opens the file the selected key comes from, or the window's INI file, in the program the system opens INI files with. The client reloads when it is saved there.

The panel can be moved out of the way: drag its title bar, or any empty part of it, to move it sideways; near a side of the screen it docks there. **F7** (or the arrow in the title bar) docks it to the other side, **_** collapses it to its title bar, which can then be dragged anywhere, and **F8** or a **middle click** anywhere hides it completely while the inspector keeps working.

The **Controls** tab lists the controls of the selected control's window as a tree, or of every open window when nothing is selected, including hidden ones. Click one to select it, double-click to inspect it. Typing in the edit box (**Ctrl+F** from anywhere) lists the controls whose name contains the text; **Enter** inspects the first one.

Every change is previewed immediately by reloading the window through the normal INI loading, so what you see is what the INI files will do once saved. If the preview puts a control somewhere other than where you dropped it, the keys that decide the wrong edges are corrected by what is missing, as part of the same change. That happens when a key is calculated from something else that moved, for example a `$Width` that reads the control's own position, or a `FillWidth` in a stock file that is read before the new `X` key of the theme's file. If correcting doesn't help, code or a later key (such as an `IdleTexture` that sets a button's size) decides where the control goes, and the panel says so. A value the window can't read, like an invalid color, is rejected and nothing changes.

Changes to several controls are one undo step. They are planned in the order the INI reads the controls, and a control positioned relative to another selected one (`$Y=getBottom(chkShortGame) + 6`) is planned from where that one moved it, so chained positions don't shift twice. Controls that are not selected but positioned relative to selected ones follow them, as they would when the client loads the saved INI.

Moves and resizes keep how the control is positioned:

| The section uses | A move or resize changes |
|---|---|
| `$X=expression` | the expression's trailing offset, e.g. `$X=LOBBY_EMPTY_SPACE_SIDES` becomes `$X=LOBBY_EMPTY_SPACE_SIDES + 40` |
| `DistanceFromRightBorder` / `DistanceFromBottomBorder` | the distance, so the control stays anchored to that border, calculated from the parent's new size when the parent is scaled too |
| `FillWidth` / `FillHeight` | the fill, so the far edge stays where it was when moving |
| `Location`, `Size`, `X`, `Y`, `Width`, `Height` | that value |
| nothing (positioned by code) | a new `X`/`Y`/`Width`/`Height` key |

A new position or size key goes before the section's first `DistanceFrom*` or `Fill*` key, in the preview as in the saved file, because those are calculated from it. When the anchor comes from the file the theme's file is based on, it is still read first, and the correction above makes up for it.

## Adding, duplicating and deleting controls

Select the window or panel to add to (or a control next to where the new one should go) and open the **Add** tab, or press *+ Add* in the selection toolbar. Pick a type (text, button, link, panel, image, check box, drop-down, text box or list), type a name or keep the suggested one, and press **Enter**. The control appears in the middle of the container, ready to be dragged into place and given keys.

The control is declared the way the window reads extra controls: an `ExtraControls` entry (with the container as its parent when it isn't the window itself) for windows such as the CnCNet lobby, or a `$CC` key in the container's section for INI-configured windows such as the game lobbies. In the latter, a container that is created by code rather than by the INI may never have its section read, so the control is added to the window instead. Its own section gets a position, a size where the type needs one, and starting keys such as `Text`. Undo or Discard removes the control again.

**Ctrl+D** (or *Duplicate*) copies the selected control: it is created the same way, with the keys of its section, 12 pixels below and to the right of it, named after it with the next free number (`btnOptions` becomes `btnOptions2`). Images and a size that code gave the control are written into the copy's section. Controls inside it aren't copied.

**Del** (or *Delete*) deletes the selection. A control that the theme's own INI creates loses its `ExtraControls` entry or `$CC` key and its section, together with the sections of the controls inside it. A control that code creates, or that a file outside the theme folder creates, can't be removed from an INI file, so it gets `Visible=false` and `Enabled=false` instead, and the panel says why. Windows can't be deleted. Like any other change, a deletion is previewed (the control is hidden), can be undone and is saved after review; the control is gone from the client once it restarts.

## Theme-wide files

The **Files** tab edits the files that apply to more than one window: the theme's `DTACnCNetClient.ini` (colors and settings), `GlobalThemeSettings.ini`, `GenericWindow.ini`, `Fonts.ini` and `ClientDefinitions.ini`. The copy in the theme folder is used when there is one. Click a file, then a section, then a key to edit it in the edit box, or type a new `Key=Value`. Changes to the first three are previewed immediately, including the theme colors; fonts and most client settings need a restart. Changes are saved after review like any other.

## Saving

Changes are not written until you save. **Ctrl+S** (or *Review & save*) lists every change by file, with old and new values: green for new keys, yellow for changed ones, red for removed keys and sections. *Write files* saves them, *Back* returns. **Ctrl+Z** undoes the last change, **Ctrl+Y** (or **Ctrl+Shift+Z**) redoes it, and *Discard* drops all unsaved changes.

Undoing or discarding puts moved and resized controls back where they were, including controls positioned by code, and shows hidden or deleted controls again. A key that a control's section didn't have before, other than a position, size, `Visible` or `Enabled` key, can't be unset on the live control: the panel names those keys, and they show as before after a restart.

Where a change is saved:

- A key defined in the current theme folder is changed where it is defined.
- Anything else goes to the window's own INI file in the current theme folder: the stock files in `Resources` and files shared with other themes are not changed. If the window has no INI file of its own yet, it gets one, based on what it used until then. A window that used `GenericWindow.ini` gets its own file based on it, for example `Resources\MyTheme\GameLoadingWindow.ini` with `BasedOn=..\GenericWindow.ini`, so the change doesn't apply to every other window that uses `GenericWindow.ini`.

Files are edited as text: comments, blank lines, key order and the file's line endings are kept, and only the lines of changed keys are touched. Removing a section removes it with the comments inside it. New keys are added to the end of their section, except position and size keys, which are added before the section's first `DistanceFrom*` or `Fill*` key because those are calculated from them.
