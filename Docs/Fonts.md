# Fonts

The client supports two font types:

- **TrueType** – TTF/OTF fonts rendered via FontStashSharp
- **SpriteFont** – precompiled XNA/MonoGame bitmap fonts (.xnb files)

Font configuration is done via `Fonts.ini` placed in your `Resources` directory.

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
3. Run `breakttc.exe yourfont.ttc` in a 32-bit Windows (e.g., Windows XP) to extract TTF files.

- See also: [Stack Overflow — Convert or extract TTC font to TTF](https://stackoverflow.com/questions/15455895/convert-or-extract-ttc-font-to-ttf-how-to)

## Troubleshooting

**Font doesn't load** — check `client.log` for `FontManager:` messages and verify the file path and that it's a valid TTF/OTF.

**Wrong font used** — remember the primary font is tried first, then fallbacks in order. Check no other `Fonts.ini` is being loaded from a different location.

**Characters render as `?`** — the character isn't in any of your fonts. Add a font that covers it to `[FallbackFonts]`.

**Performance issues** — disable `TextShaping` if not needed, reduce fallback font count, and lower `CacheSize` if memory is tight.
