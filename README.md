# HeroForge - custom heroes for HotA

A mod for **Heroes of Might and Magic III: Horn of the Abyss 1.8.0** that lets you rework existing heroes: give them a new name and portrait, a different specialty (copied from any hero, built from scratch for any creature, spell, skill or resource, or a spell damage bonus for a school of magic), starting secondary skills, starting spells, starting army and starting primary stats. It comes with a GUI editor, `HeroForge.exe`.

Example from `examples/heroes.ini`: Vehr (normally *+350 Gold*) becomes an **Earth Magic** specialist, the way Adrienne is for Fire Magic. She gets the Earth Magic orb as her specialty icon, +50% damage for Earth Magic spells, Expert Earth Magic and Implosion at the start, and stats 0/0/2/3.

## Features

- **Specialty**
  - copy the specialty of any hero, including HotA heroes (effect, text and icon),
  - or build a **new specialty** of any kind the game has:
    - creature with your own Attack/Defense/Damage/Speed bonuses, for any creature (it and its upgrade),
    - creature that grows with the hero's level (like Creyle's Mammoths),
    - spell that gets stronger with level, secondary skill bonus per level, resource per day, Speed for all creatures,
  - or remove it and give a **+X% spell damage bonus** for chosen schools of magic,
  - English in-game name and description written automatically (or your own),
  - icon: automatic, the hero's own, any secondary skill icon, or another hero's icon. For a creature no hero specializes in, the icon is the creature's battle figure, cut out and scaled down.
- **Starting secondary skills** (up to 8, Basic/Advanced/Expert). The first two also replace the skills shown in hero previews.
- **Starting spells**. The hero's own starting spell can be kept, replaced or removed. The first spell is the one shown in previews.
- **Spell book** at start: game default, always, or never.
- **Starting army**: up to 7 stacks, each with a count range (random in between). Previews show the first 3.
- **Starting stats** (Attack/Defense/Power/Knowledge).
- **Name** (up to 12 letters) and **portrait**: any image, cropped and scaled to the game's large (58x64) and small (48x32) portraits.
- **HeroForge editor** (`HeroForge.exe`): edit all of the above without touching the ini file, in three tabs (Specialty, Skills & Spells, Army).
  - town filter, and each hero listed with class and specialty,
  - the hero's default stats, starting spell and army filled in,
  - a creature and its upgrade listed together for specialties ("Jotunns / Jotunn Warlords"),
  - portrait preview (original heroes from the game files; most HotA heroes from portraits the mod exports while the game runs),

  - search in the specialty and icon lists (for example `interference`).

## Requirements and limitations

- **HotA 1.8.0 only.** The mod patches the game in memory at fixed addresses. On any other version it detects the difference, writes `BLAD` (error) to its log and leaves that feature off. Turn off HotA auto-update in the copy you install it into.
- **Local games only** (single player, hot-seat). Every player in a game must use identical files, so don't use it for online games in the HotA lobby.
- Skills, spells, army and stats apply to heroes in a **new** game. Specialties also apply to loaded saves. An army that a map sets for a hero is kept.
- It modifies existing heroes. It cannot add new hero slots.
- Custom portraits and the editor's HotA portrait previews need one **new** game with the mod first (it records which portrait file belongs to which hero). A few HotA heroes (Cove, Kinkeria, Ranloo) have no preview in the editor.

## Installation

Get the zip from [Releases](../../releases) and follow `INSTALL.txt`:

1. Make a **copy** of your game folder and use the copy.
2. Rename the game's `SMACKW32.DLL` to `smackw32_org.dll`.
3. Copy `SMACKW32.DLL` and the `HeroForge` folder from the zip into the game folder.
4. Start a new game once (this writes the hero, specialty and portrait data the editor uses), then close it.
5. Run `HeroForge\HeroForge.exe`, set up your heroes, click **Save**, and start the game.

Updating from 1.2.0 or older (then called WlasniBohaterowie): replace `SMACKW32.DLL`, add the `HeroForge` folder and open `HeroForge.exe`. It imports your old `bohaterowie.ini` and custom portraits; click **Save**, then the old `WlasniBohaterowie` folder can be deleted.

To uninstall, delete `SMACKW32.DLL` and `HeroForge`, then rename `smackw32_org.dll` back to `SMACKW32.DLL`.

## How it works

HD Mod does not load Pack plugins in HotA mode. Instead, the mod ships a proxy `SMACKW32.DLL`, a library the game loads at startup. The proxy forwards all 61 exports to the original (`smackw32_org.dll`) and loads `HeroForge\HeroForge.dll`.

Hook points in `h3hota.exe` (HotA 1.8.0), found with Ghidra:

| Address | What |
|---|---|
| `0x4E59D0` | final spell damage `(hero, spell, damage, target)`. HotA.dll calls it too |
| `0x4D8990` | hero initialisation from defaults |
| `0x4D8DA0` | map editor hero customisation |
| `0x4E2540` | learn secondary skill (called, not hooked) |
| `0x4C93F0` | starting army draw `(game, hero id, ?, single creature)`. Heroes live at `game + 0x21620 + id * 0x492`, army at hero `+0x91` (types) / `+0xAD` (counts) |
| `0x4E6390` | creature specialty bonus `(hero, creature, stats*)`. The extra Speed is added here |
| `[0x6747B0]` | creature table, 0x74 bytes per creature (town, level, battle DEF name `+0x0C`, names) |
| `[0x679C80]` | specialty table, 0x28 bytes per hero (type, parameter, texts) |
| `[0x67DCE8]` | hero type table, 0x5C bytes per hero (starting skills, book, spell, army: creatures `+0x24`, {low, high} pairs `+0x44`, name `+0x40`) |
| `0x63EC48[4]` | picture control draw method. Specialty icons are frame *hero id* of `un44.def`/`un32.def` |
| `[0x67DCEC]` | hero class table, 0x40 bytes per class (town, name, starting stats) |
| `0x55AA10`, `0x55B1E0` | picture loaders (8-bit / screen format). Custom portraits are served through them |
| hero `+0x34` | portrait index. HotA keeps its own `char*` table of portrait files by index, which the mod finds by scanning memory for a run starting with `HPL000Kn` |

Config file: `HeroForge\heroes.ini` (keys are listed at the top of the file; the editor writes it for you). Log: `HeroForge\HeroForge.log`.

## Building

Requires Visual Studio 2022 or Build Tools with the C++ x86 tools. .NET Framework 4 (included with Windows) builds the editor.

```powershell
.\build.ps1 -Version 1.3.0
```

The output is `dist\HeroForge-1.3.0.zip`. `tools/ghidra` holds the small Ghidra scripts used to find the addresses.

## Disclaimer

Fan-made and not affiliated with the Horn of the Abyss team or Ubisoft. This repository contains no game files. You need your own copy of Heroes III Complete and HotA. Use at your own risk and keep backups.

## License

[MIT](LICENSE)
