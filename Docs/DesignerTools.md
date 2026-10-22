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

Press **F6** to open the inspector. While it is open, clicks go to the inspector instead of the client.

- **Hover** outlines the control under the cursor with its name and type.
- **Click** selects a control. The panel shows the window it belongs to, the INI file that window was loaded from, the section the control reads, and its position. Below that it lists every key of the section with its value, colored by where it was defined: green in the current theme folder, grey inherited from another file (through `BasedOn` or `$BaseSection`), yellow not saved yet. Selecting a key shows the exact file and section it came from.
- **Ctrl+click** adds a control to the selection or removes it. **PageDown** selects every control inside the selected one, for example all the controls of a panel. The panel shows the keys of the control clicked last, the other selected controls, and the files a change of the selection is saved to.
- **Drag** a selected control to move it together with the rest of the selection. The handle at the bottom right corner resizes a single control; with several selected it scales the whole group from its top left corner, positions and sizes alike. **Arrow keys** move the selection by 1 pixel, 10 with **Shift**; **Ctrl+arrow keys** resize each selected control.
- While dragging, the selection's edges and center snap to the edges and centers of neighboring controls and of the parent when they come within 6 pixels, and a guide line shows what they snapped to. Hold **Alt** to drag without snapping. An axis only changes once the drag has moved more than 3 pixels along it, so resizing or moving in one direction leaves the other alone; use the arrow keys for moves smaller than that.
- While the inspector is open, every control with unsaved changes is outlined in yellow with a marker in its top right corner.
- When the edit box holds a key that takes a color, a color picker opens: click a swatch or drag the red, green, blue and alpha sliders. The change is previewed when the swatch is clicked or the slider released.
- Type `Key=Value` in the edit box and press **Enter** to change or add any key. Selecting a key in the list puts it in the edit box.
- While typing a key, the keys the selected control can read are suggested, with their current values; prefix matches come first. **Up**/**Down** choose one, **Tab** or **Enter** (or a click) puts it in the edit box with its current value, **Esc** hides the list. The line above the edit box says what kind of value the key expects. The suggestions come from a list in `DesignerKeyCatalog.cs`, which needs updating when a control learns a new INI key.
- **PageUp** selects the parent of the selected control; **Esc** deselects; **F7** moves the panel to the other side of the screen.

Every change is previewed immediately by reloading the window through the normal INI loading, so what you see is what the INI files will do once saved. If the preview puts a control somewhere else than where you dropped it, a later key in the section (for example an anchor, or an `IdleTexture` that sets a button's size) or code overrides it, and the panel says so. A value the window can't read, like an invalid color, is rejected and nothing changes.

Changes to several controls are one undo step. They are planned in the order the INI reads the controls, and a control positioned relative to another selected one (`$Y=getBottom(chkShortGame) + 6`) is planned from where that one moved it, so chained positions don't shift twice. Controls that are not selected but positioned relative to selected ones follow them, as they would when the client loads the saved INI.

Moves and resizes keep how the control is positioned:

| The section uses | A move or resize changes |
|---|---|
| `$X=expression` | the expression's trailing offset, e.g. `$X=LOBBY_EMPTY_SPACE_SIDES` becomes `$X=LOBBY_EMPTY_SPACE_SIDES + 40` |
| `DistanceFromRightBorder` / `DistanceFromBottomBorder` | the distance, so the control stays anchored to that border, calculated from the parent's new size when the parent is scaled too |
| `FillWidth` / `FillHeight` | the fill, so the far edge stays where it was when moving |
| `Location`, `Size`, `X`, `Y`, `Width`, `Height` | that value |
| nothing (positioned by code) | a new `X`/`Y`/`Width`/`Height` key |

## Adding controls

Select the window or panel to add to (or a control next to where the new one should go) and open the **Add control** tab. Pick a type (text, button, link, panel, image, check box, drop-down, text box or list), type a name or keep the suggested one, and press **Enter**. The control appears in the middle of the container, ready to be dragged into place and given keys.

The control is declared the way the window reads extra controls: an `ExtraControls` entry (with the container as its parent when it isn't the window itself) for windows such as the CnCNet lobby, or a `$CC` key in the container's section for INI-configured windows such as the game lobbies. In the latter, a container that is created by code rather than by the INI may never have its section read, so the control is added to the window instead. Its own section gets a position, a size where the type needs one, and starting keys such as `Text`. Undo or Discard removes the control again.

## Theme-wide files

The **Files** tab edits the files that apply to more than one window: the theme's `DTACnCNetClient.ini` (colors and settings), `GlobalThemeSettings.ini`, `GenericWindow.ini`, `Fonts.ini` and `ClientDefinitions.ini`. The copy in the theme folder is used when there is one. Click a file, then a section, then a key to edit it in the edit box, or type a new `Key=Value`. Changes to the first three are previewed immediately, including the theme colors; fonts and most client settings need a restart. Changes are saved after review like any other.

## Saving

Changes are not written until you save. **Ctrl+S** (or *Review & save*) lists every change by file, with old and new values; *Write files* saves them, *Back* returns. **Ctrl+Z** undoes the last change and *Discard* drops all unsaved changes.

Undoing or discarding puts moved and resized controls back where they were, including controls positioned by code. A key that a control's section didn't have before, other than a position or size key, can't be unset on the live control: the panel names those keys, and they show as before after a restart.

Where a change is saved:

- A key defined in the current theme folder is changed where it is defined.
- Anything else goes to the window's own INI file in the current theme folder: the stock files in `Resources` and files shared with other themes are not changed. If the window has no INI file of its own yet, it gets one, based on what it used until then. A window that used `GenericWindow.ini` gets its own file based on it, for example `Resources\MyTheme\GameLoadingWindow.ini` with `BasedOn=..\GenericWindow.ini`, so the change doesn't apply to every other window that uses `GenericWindow.ini`.

Files are edited as text: comments, blank lines, key order and the file's line endings are kept, and only the lines of changed keys are touched. New keys are added to the end of their section, except position and size keys, which are added before the section's first `DistanceFrom*` or `Fill*` key because those are calculated from them.
