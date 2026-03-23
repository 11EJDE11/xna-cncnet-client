# Fonts

The client supports two font types:

- **TrueType** – TTF/OTF fonts rendered via FontStashSharp
- **SpriteFont** – precompiled XNA/MonoGame bitmap fonts (.xnb files)

Font configuration is done via `Fonts.ini` placed in your `Resources` directory.

**Warning:** The TTF/OTF font file might need to be preprocessed for an optimal rendering experience. Please explicitly follow the instructions in "Preprocess font files" section to determine if your font files need preprocessing. Do not just copy existing TTF/OTF files!

## Fonts.ini location

The client searches for `Fonts.ini` in this order, loading the first one found:

1. Translation+Theme folder (e.g. `Translations/Korean/Allied/Fonts.ini`)
2. Theme folder (e.g. `Resources/Allied/Fonts.ini`)
3. Translation folder (e.g. `Translations/Korean/Fonts.ini`)
4. Base resources folder (`Resources/Fonts.ini`)

This lets translations supply their own fonts without touching the base configuration.

## Configuration

```ini
[TextShaping]
; HarfBuzz text shaping. Required for complex scripts (Arabic, Hebrew) and ZWJ emoji.
; Disable for simple Latin-only text (English, Spanish, French) for better performance.
Enabled=true
EnableBiDi=true       ; Bidirectional text support (mixed LTR/RTL)
CacheSize=1000        ; Shaped text cache entries. Use 1000+ for CJK languages.

[FallbackFonts]
; Fonts tried in order when the primary font is missing a character.
; Optional.
Count=2
Fallback0=NotoSans-Regular.ttf
Fallback1=AnotherFont.ttf

[Fonts]
Count=4   ; Number of font indexes to define

[Font0]
; Type: "TrueType" or "SpriteFont"
Type=TrueType
Path=myfont.ttf   ; Path relative to the directory containing Fonts.ini
Size=14           ; Size in pixels (TrueType only; ignored for SpriteFont)

[Font1]
Type=TrueType
Path=myfont.ttf
Size=16

[Font2]
Type=TrueType
Path=myfont.ttf
Size=18

[Font3]
Type=TrueType
Path=myfont.ttf
Size=20
```

Font paths are relative to the directory containing `Fonts.ini`. Both `/` and `\` are accepted.

## Character fallback

When rendering a character at FontIndex=1:

1. Try the primary font defined in `[Font1]`
2. If not found, try `Fallback0`, then `Fallback1`, etc.
3. If still not found, renders as `?`

All fonts in the fallback chain render at the size specified in `[Font1]`.

## Font indexes

UI controls reference fonts by index, matching `[Font0]`, `[Font1]`, etc.:

```ini
[MyLabel]
FontIndex=1
```

```csharp
myLabel.FontIndex = 1;
```

## Examples

### English only

```ini
[TextShaping]
Enabled=false
EnableBiDi=false
CacheSize=100

[Fonts]
Count=4

[Font0]
Type=TrueType
Path=myfont.ttf
Size=14

[Font1]
Type=TrueType
Path=myfont.ttf
Size=16

[Font2]
Type=TrueType
Path=myfont.ttf
Size=18

[Font3]
Type=TrueType
Path=myfont.ttf
Size=20
```

### Korean translation with Chinese fallback

Korean font as primary, Chinese font as fallback for any characters the Korean font is missing.

```ini
[TextShaping]
Enabled=true
EnableBiDi=false
CacheSize=1000

[FallbackFonts]
Count=1
Fallback0=NotoSansSC-Regular.ttf

[Fonts]
Count=4

[Font0]
Type=TrueType
Path=NotoSansKR-Regular.ttf
Size=14

[Font1]
Type=TrueType
Path=NotoSansKR-Regular.ttf
Size=16

[Font2]
Type=TrueType
Path=NotoSansKR-Regular.ttf
Size=18

[Font3]
Type=TrueType
Path=NotoSansKR-Bold.ttf
Size=20
```

### English with CJK fallback

English font as primary, CJK font as fallback. Characters not in the English font (e.g. Chinese) automatically use the fallback.

```ini
[TextShaping]
Enabled=false
EnableBiDi=false
CacheSize=100

[FallbackFonts]
Count=1
Fallback0=NotoSansSC-Regular.ttf

[Fonts]
Count=4

[Font0]
Type=TrueType
Path=myfont.ttf
Size=14

[Font1]
Type=TrueType
Path=myfont.ttf
Size=16

[Font2]
Type=TrueType
Path=myfont.ttf
Size=18

[Font3]
Type=TrueType
Path=myfont.ttf
Size=20
```

### SpriteFont (legacy)

```ini
[Fonts]
Count=4

[Font0]
Type=SpriteFont
Path=SpriteFont0

[Font1]
Type=SpriteFont
Path=SpriteFont1

[Font2]
Type=SpriteFont
Path=SpriteFont2

[Font3]
Type=SpriteFont
Path=SpriteFont3
```

Files must be `SpriteFont0.xnb`, `SpriteFont1.xnb`, etc. in the Resources folder.

## Preprocess font files

This step is required for a proportion of TTF/OTF fonts to render ideally. 

Please follow the guideline below. We assume you have a Windows 10/11 PC, but similar steps should also work on Linux/Mac.

1. What's the extension of your font file?
   - If it's `.ttc`, follow the instructions in the "TTC fonts" section to extract a TTF file first.
   - If it's `.ttf` or `.otf`, continue to Step 2.

2. Download and install FontForge from https://fontforge.org/. Open your font file in FontForge. If FontForge shows a warning about "Bad Font Name", you can ignore this dialog.

3. If FontForge shows a "Load Bitmap Font" dialog, this means your TTF/OTF file must be preprocessed. If you don't see this dialog, your font file should work without preprocessing. You can skip the rest of the steps and use the original TTF/OTF file directly.

The dialog should look like this:

![FontForge Load Bitmap Font dialog](Images/ttf-fontforge-preprocess-bitmap-font-dialog.png)

4. Select a font size (e.g. 14 px) and click "OK". Remember the size you chose.

5. File -> Generate Fonts. Select "No Outline Font" and "BDF" as the font type, and save the file as `myfont.bdf`. Use 96 DPI as the BDF resolution, if being asked.

![FontForge Generate Fonts dialog](Images/ttf-fontforge-preprocess-generate-font.png)

6. Open `myfont.bdf` in a text editor supporting opening large files, e.g., VSCode and EmEditor. Check if the first line is `STARTFONT 2.1` or `STARTFONT 2.2`.

7. If the first line is `STARTFONT 2.2`, you need to use https://github.com/SadPencil/BdfToolSP to downgrade the BDF file to version 2.1. Even not, you can still use BdfToolSP to clean up the BDF file by running the downgrade command. Besides, it's strongly recommended to run the downgrade command with `--font-name` parameter, so you can set up a new font name, e.g., "My Font 14px", to avoid conflicts with the original TTF/OTF file.

8. Check if `ENCODING 65` (character 'A') exists in the BDF file. This means whether the extracted bitmap font contains basic ASCII characters.

9. If `ENCODING 65` exists, jump to Step 13. Otherwise, `ENCODING 65` does not exist, you need to select a basic font to provide the missing ASCII characters, say `micross.ttf`. Open it with FontForge.

10. Element -> Bitmap Strikes Available. If "Pixel Sizes" is empty or does not contain the size you chose previously, you need to manually fill the pixel size and click "OK". 

![FontForge Bitmap Strikes Available dialog](Images/ttf-fontforge-preprocess-secondary-font-bitmap-available.png)

![FontForge Bitmap Strikes Available dialog with pixel size filled](Images/ttf-fontforge-preprocess-secondary-font-create-bitmap.png)

11. Save the font as `secondary.bdf` as in Step 5.

12. Use https://github.com/SadPencil/BdfToolSP to merge `myfont.bdf` and `secondary.bdf` into `merged.bdf`. Rename `merged.bdf` to `myfont.bdf`. Again, it's strongly recommended to run the merge command with `--font-name` parameter to set up a unique font name, to avoid conflicts.

13. Download and install the latest LTS version of Eclipse Temurin (previously named AdoptOpenJDK). You can skip this step if you already have JRE installed.

14. Download `BitsNPicas.jar` from https://github.com/kreativekorp/bitsnpicas/releases/. Generally speaking, you should download the latest version, but if you encounter issues, try v2.2. Backup link [here](https://web.archive.org/web/20260323030634/https://release-assets.githubusercontent.com/github-production-release-asset/43279520/e23e2667-b049-4bc6-9bd7-d368f2ff1efa?sp=r&sv=2018-11-09&sr=b&spr=https&se=2026-03-23T03%3A48%3A12Z&rscd=attachment%3B+filename%3DBitsNPicas.jar&rsct=application%2Foctet-stream&skoid=96c2d410-5711-43a1-aedd-ab1947aa7ab0&sktid=398a6654-997b-47e9-b12b-9515b896b4de&skt=2026-03-23T02%3A47%3A48Z&ske=2026-03-23T03%3A48%3A12Z&sks=b&skv=2018-11-09&sig=jk3XNrzfJ2Cqvu5h4KaUKDSihNHRsq2BjzfLApVbEoQ%3D&jwt=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmVsZWFzZS1hc3NldHMuZ2l0aHVidXNlcmNvbnRlbnQuY29tIiwia2V5Ijoia2V5MSIsImV4cCI6MTc3NDIzNTQ3NiwibmJmIjoxNzc0MjM1MTc2LCJwYXRoIjoicmVsZWFzZWFzc2V0cHJvZHVjdGlvbi5ibG9iLmNvcmUud2luZG93cy5uZXQifQ.IOoejI6Q2tKBi6IZz8rsv-MoCkh81NKtPYfycflzzbg&response-content-disposition=attachment%3B%20filename%3DBitsNPicas.jar&response-content-type=application%2Foctet-stream).

15. Create the preprocessed TTF file by running `java -jar BitsNPicas.jar convertbitmap -f ttf -o myfont.ttf myfont.bdf`.

16. Double-click the TTF file. The sample text should display correctly. You might feel the font looks worse if the font size does not match the size you chose in Step 4, but this means you have successfully preprocessed the font file. You can now use this TTF file in your `Fonts.ini`, with the font size you chose in Step 4.

## TTC fonts

TTC (TrueType Collection) files bundle multiple fonts in one file. Only TTF/OTF files are supported — you need to extract the font you want from a TTC first.

Tools to extract TTF from TTC:

- Online: [everythingfonts.com/ttc-to-ttf](https://everythingfonts.com/ttc-to-ttf) or [transfonter.org/ttc-unpack](https://transfonter.org/ttc-unpack)

- Extract locally using Python and fonttools: 
1. Install the latest Python 3. 
2. Run `python3 -m venv venv` and `venv\Scripts\activate` (Windows) or `source venv/bin/activate` (Linux/Mac) to create and activate a virtual environment. 
3. Run `pip install fonttools` (`pip install fonttools==4.62.1` if the latest version causes issues). 
4. Create `extract_ttc.py` with the following content ([source](https://github.com/fonttools/fonttools/discussions/2647#discussioncomment-3093867)):
```python
from fontTools.ttLib.ttCollection import TTCollection
import os
import sys

filename = sys.argv[1]
ttc = TTCollection(filename)
basename = os.path.basename(filename)
for i, font in enumerate(ttc):
    font.save(f"{basename}#{i}.ttf")
```
5. Run `python extract_ttc.py yourfont.ttc` to extract TTF files.

- Extract locally using BREAKTTC:
1. Download Microsoft TrueType SDK from https://archive.org/details/microsoft-truetype-sdk
2. Extract `TTC\breakttc.exe` from the SDK.
3. Download and install DosBox from https://www.dosbox.com/
4. Run `breakttc.exe yourfont.ttc` in DosBox to extract TTF files.

- See also: [Stack Overflow — Convert or extract TTC font to TTF](https://stackoverflow.com/questions/15455895/convert-or-extract-ttc-font-to-ttf-how-to)

## Troubleshooting

**Font doesn't load** — check `client.log` for `FontManager:` messages and verify the file path and that it's a valid TTF/OTF.

**Wrong font used** — remember the primary font is tried first, then fallbacks in order. Check no other `Fonts.ini` is being loaded from a different location.

**Characters render as `?`** — the character isn't in any of your fonts. Add a font that covers it to `[FallbackFonts]`.

**Performance issues** — disable `TextShaping` if not needed, reduce fallback font count, and lower `CacheSize` if memory is tight.
