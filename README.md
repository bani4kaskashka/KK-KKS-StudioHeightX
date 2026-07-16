# StudioHeightX

Shows the height of whatever character you have selected in CharaStudio. That's it, that's the plugin.

I made this because HeightBar only works in the maker, and Studio is exactly the place where you resize characters and immediately lose track of how tall anyone actually is. So this shows both numbers:

```
Molly - Base 140.0 cm | Scaled 210.0 cm (150%)
```

If the character isn't scaled you just get the plain height. It updates live while you drag sliders, bones or the scale gizmo, and disappears when nothing is selected.

Works with Koikatsu / Koikatsu Party and Koikatsu Sunshine.

## Install

You need BepInEx 5.x and KKAPI (KKSAPI for Sunshine). If you run a repack you already have both.

Grab the right DLL and drop it in `BepInEx/plugins`:

- `KK_StudioHeight.dll` for Koikatsu / Party
- `KKS_StudioHeight.dll` for Sunshine

## Using it

- Click a character in the Workspace, the text shows up
- Drag the text with left mouse to move it wherever, it remembers the spot
- F1 (ConfigurationManager) for the rest: on/off, toggle hotkey, cm or feet/inches, font size

## Where the numbers come from

The cm conversion is lifted straight from [ManlyMarco's HeightBar Plugin](https://github.com/ManlyMarco/Illusion_HeightBar): distance from the character's root to the measure point, times 103.09278. The difference is that HeightBarX makes you line a bar up with the head by hand, while this uses the game's own head-top accessory attach point (`a_n_headtop`), so it's automatic and hair doesn't count.

Base height is measured in the character's local space, which is why Studio scaling doesn't touch it. Scaled height is the same thing times the actual world scale.

## Stuff to know

- Same warning HeightBar gives: ABMX edits that scale or move the feet/legs throw the root origin off, so the number will be wrong for those characters
- Height is measured in the current pose. Sitting characters read shorter, obviously. Use a standing pose if you want the real number
- With multiple characters selected it only shows the first one

## Building

Copy the reference DLLs from your game into `lib/KK` and `lib/KKS` (BepInEx.dll, 0Harmony.dll, KKAPI.dll or KKSAPI.dll, plus Assembly-CSharp, firstpass and the UnityEngine DLLs from `CharaStudio_Data/Managed`), then:

```
cd src/StudioHeight
dotnet build -c Release -p:GameTarget=KK
dotnet build -c Release -p:GameTarget=KKS
```

## The XMods

- [KK_PerspectiveX](https://github.com/bani4kaskashka/KK_PerspectiveX): first-person POV that doesn't clip through the body and doesn't make you dizzy
- [StudioHeightX](https://github.com/bani4kaskashka/KK-KKS-StudioHeightX): live height readout for the selected Studio character (this one)
- [PerceptiveHeightX](https://github.com/bani4kaskashka/PerceptiveHeightX): POV size-perception add-on for PerspectiveX

## My links

- Patreon: https://www.patreon.com/zamalkogts
- DeviantArt: https://www.deviantart.com/zamalkogts
