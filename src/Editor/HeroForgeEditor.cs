// HeroForge editor - GUI for heroes.ini (HeroForge mod for HotA).
// Lives next to the ini in the game's HeroForge folder. Hero names come from
// game_heroes.txt, which the mod writes when a game is started.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HeroForge
{
    class HeroEntry
    {
        public int Id = -1;
        public int Schools;          // 1 air, 2 fire, 4 water, 8 earth (as in the mod)
        public int Bonus;
        public List<KeyValuePair<int, int>> Skills = new List<KeyValuePair<int, int>>(); // skill id, level 1..3
        public HashSet<int> Spells = new HashSet<int>();
        public bool SpellsSet;       // "spells" given: Spells replace the hero's own starting spell (empty = none)
        public List<int[]> Army;     // starting army (creature, min, max), null = game default
        public int[] Stats;          // null = keep game defaults
        public int SpecMode;         // SpecOwn, SpecNone or SpecCopy
        public int SpecFrom = -1;    // hero id to copy from (SpecCopy)
        public string Name = "";     // new hero name, empty = keep
        public string SpecName = "";
        public string SpecDesc = "";

        public const int SpecOwn = 0, SpecNone = 1, SpecCopy = 2, SpecBuilt = 3;
        // SpecBuilt: game specialty type (as in the mod): 0 skill, 1 creature growing with level,
        // 2 resource, 3 spell, 4 creature fixed bonus, 5 speed for all.
        public int BuildKind = 4;
        public int Creature = -1;    // kinds 1 and 4: creature id (with its upgrade)
        public int[] CreatureBonus = new int[4]; // kind 4: attack, defense, damage, speed
        public int BuildSpell = -1, BuildSkill = -1, BuildResource = -1;
        public int StartBook = -1;   // spell book at start: -1 game default, 1 yes, 0 no

        // "icon" key: absent = automatic, "own" = own, skill name = skill icon, number = that hero's icon.
        public int IconMode;
        public int IconSkill = 17;
        public int IconHero = -1;
        public const int IconAuto = 0, IconOwn = 1, IconSkillMode = 2, IconHeroMode = 3;
    }

    static class Data
    {
        // Must match SKILL_NAMES in HeroForge.cpp (names without spaces) - same order as game ids.
        public static readonly string[] SkillKeys = {
            "pathfinding", "archery", "logistics", "scouting", "diplomacy", "navigation",
            "leadership", "wisdom", "mysticism", "luck", "ballistics", "eagleeye",
            "necromancy", "estates", "firemagic", "airmagic", "watermagic", "earthmagic",
            "scholar", "tactics", "artillery", "learning", "offense", "armorer",
            "intelligence", "sorcery", "resistance", "firstaid" };

        // Display names as in the English game.
        public static readonly string[] SkillNames = {
            "Pathfinding", "Archery", "Logistics", "Scouting", "Diplomacy", "Navigation",
            "Leadership", "Wisdom", "Mysticism", "Luck", "Ballistics", "Eagle Eye",
            "Necromancy", "Estates", "Fire Magic", "Air Magic", "Water Magic", "Earth Magic",
            "Scholar", "Tactics", "Artillery", "Learning", "Offense", "Armorer",
            "Intelligence", "Sorcery", "Resistance", "First Aid" };

        public static readonly string[] Levels = { "Basic", "Advanced", "Expert" };

        public static readonly string[] SpellNames = {
            "Summon Boat", "Scuttle Boat", "Visions", "View Earth", "Disguise",
            "View Air", "Fly", "Water Walk", "Dimension Door", "Town Portal",
            "Quicksand", "Land Mine", "Force Field", "Fire Wall", "Earthquake",
            "Magic Arrow", "Ice Bolt", "Lightning Bolt", "Implosion", "Chain Lightning",
            "Frost Ring", "Fireball", "Inferno", "Meteor Shower", "Death Ripple",
            "Destroy Undead", "Armageddon", "Shield", "Air Shield", "Fire Shield",
            "Protection from Air", "Protection from Fire", "Protection from Water", "Protection from Earth", "Anti-Magic",
            "Dispel", "Magic Mirror", "Cure", "Resurrection", "Animate Dead",
            "Sacrifice", "Bless", "Curse", "Bloodlust", "Precision",
            "Weakness", "Stone Skin", "Disrupting Ray", "Prayer", "Mirth",
            "Sorrow", "Fortune", "Misfortune", "Haste", "Slow",
            "Slayer", "Frenzy", null /* Titan's Lightning Bolt - creatures only */, "Counterstrike", "Berserk",
            "Hypnotize", "Forgetfulness", "Blind", "Teleport", "Remove Obstacle",
            "Clone", "Fire Elemental", "Earth Elemental", "Water Elemental", "Air Elemental" };
    }

    // Reads pictures straight from the game's .lod archives (read only).
    // LOD: "LOD\0", version, entry count, 80 reserved bytes, then 32-byte entries:
    // name[16], offset, size, type, compressed size (0 = stored; else zlib).
    // H3 PCX: size, width, height, then 8-bit pixels + 768-byte RGB palette, or 24-bit BGR.
    static class GameImages
    {
        class Entry { public string Lod; public int Offset, Size, CSize; }
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Later archives override earlier ones, as in the game (HotA files win).
        public static void Open(string dataDir)
        {
            string[] order = { "H3bitmap.lod", "H3ab_bmp.lod", "HotA.lod", "HotA_ext.lod", "HotA_lng.lod", "HotA_l_ext.lod" };
            foreach (var name in order)
            {
                string path = Path.Combine(dataDir, name);
                if (!File.Exists(path)) continue;
                try
                {
                    using (var r = new BinaryReader(File.OpenRead(path)))
                    {
                        if (r.ReadUInt32() != 0x00444F4C) continue; // "LOD\0"
                        r.ReadUInt32();
                        int count = r.ReadInt32();
                        r.BaseStream.Position = 92;
                        for (int i = 0; i < count; i++)
                        {
                            string n = Encoding.ASCII.GetString(r.ReadBytes(16));
                            int z = n.IndexOf('\0');
                            if (z >= 0) n = n.Substring(0, z);
                            var e = new Entry { Lod = path, Offset = r.ReadInt32(), Size = r.ReadInt32() };
                            r.ReadInt32();
                            e.CSize = r.ReadInt32();
                            entries[n] = e;
                        }
                    }
                }
                catch (IOException) { }
            }
        }

        static byte[] Read(string name)
        {
            Entry e;
            if (!entries.TryGetValue(name, out e)) return null;
            using (var f = File.OpenRead(e.Lod))
            {
                f.Position = e.Offset;
                var raw = new byte[e.CSize > 0 ? e.CSize : e.Size];
                f.Read(raw, 0, raw.Length);
                if (e.CSize == 0) return raw;
                using (var z = new System.IO.Compression.DeflateStream(new MemoryStream(raw, 2, raw.Length - 2), System.IO.Compression.CompressionMode.Decompress))
                {
                    var data = new byte[e.Size];
                    int got = 0, n;
                    while (got < data.Length && (n = z.Read(data, got, data.Length - got)) > 0) got += n;
                    return data;
                }
            }
        }

        // Large portrait names of the original 156 heroes, from the static hero table in
        // h3hota.exe (VA 0x679DD0, 0x5C per hero, char* large portrait at +0x34). HotA keeps
        // its own heroes' portraits in its encrypted archive, so those have none here.
        public static Dictionary<int, string> ReadExePortraits(string exePath)
        {
            var result = new Dictionary<int, string>();
            try
            {
                byte[] b = File.ReadAllBytes(exePath);
                int pe = BitConverter.ToInt32(b, 0x3C);
                int nsec = BitConverter.ToInt16(b, pe + 6), opt = BitConverter.ToInt16(b, pe + 20), sec = pe + 24 + opt;
                Func<uint, int> toOffset = va =>
                {
                    for (int i = 0; i < nsec; i++)
                    {
                        int o = sec + i * 40;
                        uint start = (uint)BitConverter.ToInt32(b, o + 12) + 0x400000;
                        int raw = BitConverter.ToInt32(b, o + 20), rawSize = BitConverter.ToInt32(b, o + 16);
                        if (va >= start && va < start + rawSize) return (int)(va - start) + raw;
                    }
                    return -1;
                };
                for (int id = 0; id < 156; id++)
                {
                    int entry = toOffset((uint)(0x679DD0 + id * 0x5C + 0x34));
                    if (entry < 0) break;
                    int str = toOffset((uint)BitConverter.ToInt32(b, entry));
                    if (str < 0) continue;
                    int end = Array.IndexOf(b, (byte)0, str);
                    if (end > str && end - str < 32) result[id] = Encoding.ASCII.GetString(b, str, end - str);
                }
            }
            catch (Exception) { }
            return result;
        }

        public static Bitmap LoadPcx(string name)
        {
            try
            {
                var d = Read(name);
                if (d == null || d.Length < 12) return null;
                int size = BitConverter.ToInt32(d, 0), w = BitConverter.ToInt32(d, 4), h = BitConverter.ToInt32(d, 8);
                if (w <= 0 || h <= 0 || w > 2048 || h > 2048) return null;
                var bmp = new Bitmap(w, h);
                if (size == w * h && d.Length >= 12 + size + 768)
                {
                    int pal = 12 + size;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = d[12 + y * w + x] * 3;
                            bmp.SetPixel(x, y, Color.FromArgb(d[pal + i], d[pal + i + 1], d[pal + i + 2]));
                        }
                }
                else if (size == w * h * 3 && d.Length >= 12 + size)
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = 12 + (y * w + x) * 3;
                            bmp.SetPixel(x, y, Color.FromArgb(d[i + 2], d[i + 1], d[i]));
                        }
                }
                else return null;
                return bmp;
            }
            catch (Exception) { return null; }
        }
    }

    class MainForm : Form
    {
        const int SkillRows = 8;

        readonly string dir;
        readonly string iniPath;
        readonly Dictionary<int, string> heroNames = new Dictionary<int, string>();
        readonly Dictionary<int, string> specTexts = new Dictionary<int, string>(); // hero id -> short specialty text
        readonly Dictionary<int, string> heroClasses = new Dictionary<int, string>(); // hero id -> class name
        readonly Dictionary<int, int> heroTowns = new Dictionary<int, int>();         // hero id -> town id
        readonly Dictionary<int, int[]> heroStats = new Dictionary<int, int[]>();     // hero id -> default A/D/P/K
        readonly Dictionary<int, string> heroPortraits = new Dictionary<int, string>(); // hero id -> large portrait .pcx
        readonly Dictionary<int, int> heroSpell = new Dictionary<int, int>();         // hero id -> default starting spell (-1 none)
        readonly Dictionary<int, List<int[]>> heroArmy = new Dictionary<int, List<int[]>>(); // hero id -> default army (creature, low, high)
        readonly List<SpecItem> armyItems = new List<SpecItem>();                     // every creature, one by one
        readonly Dictionary<int, string> creatureNames = new Dictionary<int, string>();
        const int ArmyRows = 7;
        CheckBox chkArmy;
        ComboBox[] cboArmy = new ComboBox[ArmyRows];
        NumericUpDown[] numArmyMin = new NumericUpDown[ArmyRows], numArmyMax = new NumericUpDown[ArmyRows];
        int[] armyPick = new int[ArmyRows];
        Label lblArmyDefault, lblSpellDefault;
        PictureBox picPortrait;
        List<SpecItem> specItems;

        // Typing filters the list to items containing the text (hero name or specialty, e.g.
        // "interference"); picking an item selects it.
        void MakeSearchable(ComboBox box, List<SpecItem> all)
        {
            box.DropDownStyle = ComboBoxStyle.DropDown;
            box.Items.AddRange(all.ToArray());
            box.TextUpdate += (s, e) =>
            {
                string q = box.Text;
                var matches = all.Where(i => i.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                bool wasLoading = loading;
                loading = true;
                box.Items.Clear();
                box.Items.AddRange(matches);
                box.Text = q;
                box.SelectionStart = q.Length;
                loading = wasLoading;
                if (matches.Length > 0)
                {
                    box.DroppedDown = true;
                    Cursor.Current = Cursors.Default;
                }
            };
        }

        // Shows the full list again and selects the item for hero `id` (or nothing).
        void SelectSearchable(ComboBox box, int id) { SelectSearchable(box, id, specItems); }

        void SelectSearchable(ComboBox box, int id, List<SpecItem> all)
        {
            box.Items.Clear();
            box.Items.AddRange(all.ToArray());
            box.SelectedIndex = all.FindIndex(i => i.Id == id);
            if (box.SelectedIndex < 0) box.Text = "";
        }

        void UpdatePortrait(int id)
        {
            string pcx;
            var old = picPortrait.Image;
            Bitmap img = null;
            // A custom portrait wins; then original heroes straight from the game files.
            string custom = Path.Combine(dir, "custom_portraits", id + "_L.bmp");
            if (id >= 0 && File.Exists(custom))
                using (var fromFile = new Bitmap(custom)) img = new Bitmap(fromFile);
            else
            // Original heroes: straight from the game files. HotA heroes: portraits the mod
            // exported from the running game (most of them; the rest have no preview).
            if (id >= 0 && heroPortraits.TryGetValue(id, out pcx))
                img = GameImages.LoadPcx(pcx) ?? GameImages.LoadPcx(pcx + ".pcx");
            string exported = Path.Combine(dir, "portraits", id + ".bmp");
            if (img == null && id >= 0 && File.Exists(exported))
                using (var fromFile = new Bitmap(exported)) img = new Bitmap(fromFile);
            // Hard pixels at a whole-number scale, like the game's own art; no picture = initials.
            if (img != null) { picPortrait.Image = ScaleToFit(img, picPortrait.Width, picPortrait.Height); img.Dispose(); }
            else picPortrait.Image = id >= 0 ? InitialsPortrait(HeroName(id), picPortrait.Width, picPortrait.Height) : null;
            if (old != null) old.Dispose();
        }

        // Custom portrait: any picture, cropped to the portrait's aspect ratio and scaled to the
        // game's large (58x64) and small (48x32) portrait sizes, saved as 24-bit BMPs for the mod.
        void ChooseCustomPortrait()
        {
            var e = Current;
            if (e == null || e.Id < 0) return;
            using (var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*", Title = "Choose a portrait for " + HeroName(e.Id) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string folder = Path.Combine(dir, "custom_portraits");
                    Directory.CreateDirectory(folder);
                    using (var src = new Bitmap(dlg.FileName))
                    {
                        SaveScaled(src, 58, 64, Path.Combine(folder, e.Id + "_L.bmp"));
                        SaveScaled(src, 48, 32, Path.Combine(folder, e.Id + "_S.bmp"));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not use this image:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            UpdatePortrait(e.Id);
            UpdateVisibility();
            if (!File.Exists(GameFile("portrait_map.txt", "portrety_mapa.txt")))
                MessageBox.Show(this, "Saved. To show it in the game, start a NEW game once with the mod (it maps heroes to portrait files), then restart the game.",
                    "Custom portrait", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void RemoveCustomPortrait()
        {
            var e = Current;
            if (e == null || e.Id < 0) return;
            foreach (var size in new[] { "_L.bmp", "_S.bmp" })
            {
                string f = Path.Combine(dir, "custom_portraits", e.Id + size);
                if (File.Exists(f)) File.Delete(f);
            }
            UpdatePortrait(e.Id);
            UpdateVisibility();
        }

        static void SaveScaled(Bitmap src, int w, int h, string path)
        {
            // Crop the middle of the source to the target aspect ratio, then scale.
            float target = (float)w / h, aspect = (float)src.Width / src.Height;
            var crop = aspect > target
                ? new Rectangle((int)((src.Width - src.Height * target) / 2), 0, (int)(src.Height * target), src.Height)
                : new Rectangle(0, (int)((src.Height - src.Width / target) / 2), src.Width, (int)(src.Width / target));
            using (var dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(dst))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
                }
                dst.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            }
        }

        int[] DefaultStats(int id)
        {
            int[] st;
            return heroStats.TryGetValue(id, out st) ? st : new[] { 0, 0, 1, 1 };
        }

        void ShowStats(int[] st)
        {
            for (int i = 0; i < 4; i++) numStats[i].Value = Math.Max(numStats[i].Minimum, Math.Min(99, st[i]));
        }

        // Town ids as in the game's hero class table (HotA order).
        static readonly string[] TownNames = {
            "Castle", "Rampart", "Tower", "Inferno", "Necropolis", "Dungeon",
            "Stronghold", "Fortress", "Conflux", "Cove", "Factory", "Bulwark" };

        static string TownName(int town)
        {
            return town >= 0 && town < TownNames.Length ? TownNames[town] : "Town " + town;
        }

        class TownItem
        {
            public readonly int Town; // -1 = all
            public TownItem(int town) { Town = town; }
            public override string ToString() { return Town < 0 ? "(all towns)" : TownName(Town); }
        }
        readonly List<HeroEntry> entries = new List<HeroEntry>();
        bool loading;

        ListBox lstEntries;
        ComboBox cboHero;
        CheckBox chkSpec, chkEarth, chkFire, chkWater, chkAir;
        RadioButton rbSpecOwn, rbSpecNone, rbSpecCopy, rbSpecCreature;
        ComboBox cboCreature, cboKind, cboBuildSpell, cboBuildSkill, cboBuildRes, cboBook;
        Label lblKindInfo;
        Label[] lblCreature = new Label[4];

        // "New specialty" kinds in display order: game type, label, what it does.
        static readonly int[] KindTypes = { 4, 1, 3, 0, 2, 5 };
        static readonly string[] KindLabels = {
            "Creature - fixed bonus", "Creature - grows with level", "Spell - stronger with level",
            "Secondary skill - bonus per level", "Resource per day", "Speed for all creatures" };
        static readonly string[] KindInfo = {
            "",
            "Like Creyle: +1 Speed, and Attack and Defense grow with the hero's level (game formula).",
            "Like Solmyr: the spell's effect grows with the hero's level.",
            "Like Adrienne: +5% per level to the skill's effect, for skills the game supports (e.g. Archery, Offense, Armorer, Estates).",
            "+1 of the resource every day (Gold: +350).",
            "Like Sir Mullich: +2 Speed for all creatures in the hero's army." };
        static readonly string[] ResourceNames = { "Wood", "Mercury", "Ore", "Sulfur", "Crystal", "Gems", "Gold" };
        static readonly string[] KindKeys = { "skill", "creature_level", "resource", "spell", "creature", "speed" }; // by game type
        NumericUpDown[] numCreature = new NumericUpDown[4];   // attack, defense, damage, speed
        List<SpecItem> creatureItems = new List<SpecItem>();
        readonly Dictionary<int, int> upgradeToBase = new Dictionary<int, int>();
        ComboBox cboSpecFrom;
        TextBox txtSpecName, txtSpecDesc, txtName;
        ComboBox cboIconMode, cboIconSkill, cboIconHero;
        Label lblOwnSpec;
        NumericUpDown numBonus;
        ComboBox[] cboSkill = new ComboBox[SkillRows];
        ComboBox[] cboLevel = new ComboBox[SkillRows];
        ListBox lstSpells;
        CheckBox chkStats;
        NumericUpDown[] numStats = new NumericUpDown[4];
        Panel editor;

        public MainForm()
        {
            dir = AppDomain.CurrentDomain.BaseDirectory;
            iniPath = Path.Combine(dir, "heroes.ini");
            Text = "HeroForge - Hero Editor for HotA";
            StartPosition = FormStartPosition.CenterScreen;
            using (var screen = Graphics.FromHwnd(IntPtr.Zero)) Ui = screen.DpiX / 96f * TestScale;

            LoadHeroNames();
            string gameDir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            GameImages.Open(Path.Combine(gameDir, "Data"));
            foreach (var kv in GameImages.ReadExePortraits(Path.Combine(gameDir, "h3hota.exe")))
                if (!heroPortraits.ContainsKey(kv.Key)) heroPortraits[kv.Key] = kv.Value;
            // Built at 96 DPI; WinForms scales every control to the display when layout resumes.
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f / TestScale, 96f / TestScale);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1140, 720);
            BuildUi();
            ResumeLayout(false);
            lstEntries.ItemHeight = S(48);
            lstNav.ItemHeight = S(56);
            lstSpells.ItemHeight = S(26);
            // Fit the screen: on a 1366x768 laptop the window must not end under the taskbar.
            Load += (s, e) =>
            {
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                // 1120 wide fits the hero card and a whole section (side 260 + nav 208 + rows 600 + margins).
                MinimumSize = new Size(Math.Min(S(1120), area.Width), Math.Min(S(600), area.Height));
                if (Width > area.Width || Height > area.Height)
                {
                    Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                    if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
                }
            };
            LoadIni();
            RefreshList(0);
            // Compare against the state once the window is up (creating controls can fire edits).
            // An imported old file counts as unsaved until it is saved as heroes.ini.
            Shown += (s, e) =>
            {
                savedSnapshot = importedFrom != null ? null : Snapshot();
                dirty = importedFrom == null; // the opposite, so UpdateDirty refreshes the title
                UpdateDirty();
            };
            if (importedFrom != null)
                Shown += (s, e) => MessageBox.Show(this, "Your heroes were imported from the older version's file:\n" + importedFrom +
                    "\n\nClick Save to store them in heroes.ini.", "Imported", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- UI ----------
        // "Tavern Night": dark warm surfaces, one gold accent for selection and Save,
        // hairline borders, spacing from 4 / 8 / 12 / 16 / 24.

        static class T
        {
            public static readonly Color Bg = Color.FromArgb(0x17, 0x13, 0x0F);
            public static readonly Color Side = Color.FromArgb(0x11, 0x0E, 0x0B);
            public static readonly Color Surface = Color.FromArgb(0x22, 0x1C, 0x16);
            public static readonly Color Input = Color.FromArgb(0x2C, 0x25, 0x1D);
            public static readonly Color Hover = Color.FromArgb(0x36, 0x2D, 0x23);
            public static readonly Color Line = Color.FromArgb(0x3D, 0x33, 0x28);
            public static readonly Color Text = Color.FromArgb(0xED, 0xE6, 0xD8);
            public static readonly Color Text2 = Color.FromArgb(0xA8, 0x9C, 0x88);
            public static readonly Color Accent = Color.FromArgb(0xD4, 0xA6, 0x4A);
            public static readonly Color OnAccent = Color.FromArgb(0x1A, 0x14, 0x0C);
            // A user's mistake is amber (it teaches), not red.
            public static readonly Color Warn = Color.FromArgb(0xEC, 0x8E, 0x3E);
            public static readonly Color WarnBg = Color.FromArgb(0x3B, 0x27, 0x15);
            // Secondary text is muted by colour only, not also smaller (so Small = Body size).
            public static readonly Font Body, Strong, Small, Caption, H2, H1, Brand;
            static T()
            {
                Body = F("Segoe UI", 9.5f); Strong = F("Segoe UI Semibold", 9.5f); Small = F("Segoe UI", 9.5f);
                Caption = F("Segoe UI Semibold", 8f); H2 = F("Segoe UI Semibold", 12.5f);
                H1 = F("Segoe UI Semibold", 17f); Brand = F("Segoe UI Semibold", 16f);
            }
            static Font F(string name, float pt) { return new Font(name, pt * TestScale); }
        }

        // Display scale: 1 at 100% Windows scaling, 1.5 at 150%. Controls built in BuildUi are
        // scaled by WinForms (AutoScaleMode.Dpi); sizes used later in code go through S().
        static float Ui = 1f;
        static float TestScale = 1f;  // --scale test helper: pretend a scaled display
        static int S(int px) { return (int)Math.Round(px * Ui); }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, string lp);

        // Spell schools by spell id (1 air, 2 fire, 4 water, 8 earth; Magic Arrow is in all four).
        static readonly int[] SpellSchool = {
            4, 4, 1, 8, 1, 1, 1, 4, 1, 8,  8, 2, 8, 2, 8, 15, 4, 1, 8, 1,
            4, 2, 2, 8, 8, 1, 2, 8, 1, 2,  1, 2, 4, 8, 8, 4, 1, 4, 8, 8,
            2, 4, 2, 2, 1, 4, 8, 1, 4, 4,  8, 1, 2, 1, 8, 2, 2, 1, 1, 2,
            1, 4, 2, 4, 4, 4, 2, 8, 4, 1 };
        static readonly string[] SchoolNames = { "All", "Air", "Fire", "Water", "Earth" };
        static readonly int[] SchoolFlags = { 15, 1, 2, 4, 8 };
        static readonly string[] SectionNames = { "Specialty", "Skills", "Spells", "Army", "Stats" };

        const int ContentWidth = 572;        // width of a section's rows
        ListBox lstNav;
        Panel[] sections = new Panel[5];
        Panel rowCopy, rowKind, rowCrNums, rowKindInfo, rowSchools;
        Panel[] skillRow = new Panel[SkillRows];
        Panel[] armyRow = new Panel[ArmyRows];
        Panel armyHeader, rowStats, rowStatsDefault;
        Label lblStatsDefault;
        FlowLayoutPanel spellChips;
        TextBox txtSpellSearch;
        Button[] btnSchool = new Button[5];
        int spellSchoolFilter;               // index into SchoolFlags
        readonly HashSet<int> spellSel = new HashSet<int>();
        Label lblStatus, lblHeroTitle, lblHeroSub;
        bool dirty;

        static Label Lbl(string text, Font font, Color color)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent };
        }

        // A fixed-height row in a section's vertical stack; controls go in at absolute positions.
        static Panel Row(Control stack, int height, int indent = 0)
        {
            var row = new Panel { Width = ContentWidth, Height = height, Margin = new Padding(indent, 0, 0, 8), BackColor = Color.Transparent };
            stack.Controls.Add(row);
            return row;
        }

        static FlowLayoutPanel Stack(Panel section, string title, string subtitle)
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(20, 16, 8, 16), BackColor = T.Surface
            };
            section.Controls.Add(flow);
            var head = new Panel { Width = ContentWidth, Height = subtitle == null ? 30 : 50, Margin = new Padding(0, 0, 0, 12) };
            head.Controls.Add(Lbl(title, T.H2, T.Text));
            if (subtitle != null)
            {
                var sub = Lbl(subtitle, T.Small, T.Text2);
                sub.Location = new Point(1, 28);
                head.Controls.Add(sub);
            }
            flow.Controls.Add(head);
            return flow;
        }

        static Label Caption(Control parent, string text, int x, int y)
        {
            var l = Lbl(text.ToUpperInvariant(), T.Caption, T.Text2);
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
            return l;
        }

        static Button FlatButton(string text, int w, int h, bool primary = false)
        {
            var b = new Button
            {
                Text = text, Width = w, Height = h, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? T.Accent : T.Input, ForeColor = primary ? T.OnAccent : T.Text,
                Font = primary ? T.Strong : T.Body, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? T.Accent : T.Line;
            b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(T.Accent, .15f) : T.Hover;
            b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(T.Accent, .05f) : T.Line;
            return b;
        }

        // Colours every input control for the dark theme (labels keep the colour they were made with).
        static void Theme(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBox) { c.BackColor = T.Input; c.ForeColor = T.Text; ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is ComboBox) { var cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = T.Input; cb.ForeColor = T.Text; }
                else if (c is NumericUpDown) { c.BackColor = T.Input; c.ForeColor = T.Text; ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is CheckBox || c is RadioButton)
                {
                    var bb = (ButtonBase)c;
                    bb.FlatStyle = FlatStyle.Flat; c.ForeColor = T.Text; c.BackColor = Color.Transparent;
                    bb.Cursor = Cursors.Hand;
                    // The flat glyph is white on white; paint our own over it.
                    c.Paint += DrawToggleGlyph;
                }
                if (c.HasChildren) Theme(c);
            }
        }

        static void DrawToggleGlyph(object sender, PaintEventArgs e)
        {
            var c = (Control)sender;
            bool radio = c is RadioButton;
            bool on = radio ? ((RadioButton)c).Checked : ((CheckBox)c).Checked;
            var g = e.Graphics;
            int size = S(14), y = (c.Height - size) / 2;
            var box = new Rectangle(0, y, size, size);
            Color under = T.Surface;
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) { under = p.BackColor; break; }
            using (var b = new SolidBrush(under)) g.FillRectangle(b, -1, y - 2, size + 3, size + 4);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color edge = !c.Enabled ? T.Line : on ? T.Accent : T.Text2;
            using (var fill = new SolidBrush(T.Input))
            using (var pen = new Pen(edge, 1.4f))
            using (var mark = new SolidBrush(c.Enabled ? T.Accent : T.Text2))
            {
                if (radio)
                {
                    g.FillEllipse(fill, box.X, box.Y, size - 1, size - 1);
                    g.DrawEllipse(pen, box.X, box.Y, size - 1, size - 1);
                    if (on) g.FillEllipse(mark, box.X + size * 0.25f, box.Y + size * 0.25f, size * 0.43f, size * 0.43f);
                }
                else
                {
                    g.FillRectangle(fill, box.X, box.Y, size - 1, size - 1);
                    g.DrawRectangle(pen, box.X, box.Y, size - 1, size - 1);
                    if (on) g.FillRectangle(mark, box.X + S(3), box.Y + S(3), size - S(7), size - S(7));
                }
            }
        }

        // Dark scrollbars, combo lists and title bar (Windows 10 1809+; ignored elsewhere).
        static void DarkChrome(Control root)
        {
            foreach (Control c in root.Controls)
            {
                try
                {
                    if (c is ComboBox) SetWindowTheme(c.Handle, "DarkMode_CFD", null);
                    else if (c is ListBox || c is TextBox || c is ScrollableControl) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
                catch (Exception) { }
                if (c.HasChildren) DarkChrome(c);
            }
        }

        Panel welcome, body, pickPanel, errorBar, spellsEmpty;
        Label lblError, lblSpellsEmpty;
        Button btnPortrait, btnPortraitReset;
        LinkLabel lnkUndo;
        Timer undoTimer;
        HeroEntry removedEntry;
        int removedIndex;
        string savedNote = "", nameCue = "";
        Control errorControl;
        Color errorControlBack;
        readonly List<SpecItem> heroItems = new List<SpecItem>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        void BuildUi()
        {
            BackColor = T.Bg;
            ForeColor = T.Text;
            Font = T.Body;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.S) { SaveIni(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.N) { AddHero(); e.SuppressKeyPress = true; }
                // Ctrl+Z brings back a removed hero, unless a text box wants it for its own undo.
                else if (e.Control && e.KeyCode == Keys.Z && removedEntry != null && !(ActiveControl is TextBoxBase) &&
                         !(ActiveControl is ComboBox && ((ComboBox)ActiveControl).DropDownStyle != ComboBoxStyle.DropDownList))
                { UndoRemove(); e.SuppressKeyPress = true; }
            };
            FormClosing += (s, e) =>
            {
                if (!dirty) return;
                var answer = MessageBox.Show(this, "Save your changes before closing?", "HeroForge", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveIni())) e.Cancel = true;
            };
            HandleCreated += (s, e) =>
            {
                int on = 1;
                try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch (Exception) { }
            };

            // ---- sidebar: the hero list ----
            var side = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = T.Side };
            var brand = Lbl("HeroForge", T.Brand, T.Accent);
            brand.Location = new Point(14, 16);
            side.Controls.Add(brand);
            var tagline = Lbl("Hero editor for HotA 1.8", T.Small, T.Text2);
            tagline.Location = new Point(17, 46);
            side.Controls.Add(tagline);
            Caption(side, "Your heroes", 17, 84);
            lstEntries = new ListBox
            {
                Location = new Point(12, 104), Size = new Size(236, 400),
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 48, BorderStyle = BorderStyle.None,
                BackColor = T.Side, ForeColor = T.Text, IntegralHeight = false
            };
            lstEntries.DrawItem += DrawHeroRow;
            lstEntries.SelectedIndexChanged += (s, e) => { ClearError(); ShowEntry(); lstEntries.Invalidate(); };
            lstEntries.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { DeleteEntry(); e.Handled = true; } };
            side.Controls.Add(lstEntries);
            var btnAdd = FlatButton("+  Add hero", 112, 32);
            var btnDel = FlatButton("Remove", 112, 32);
            var btnSave = FlatButton("Save   (Ctrl+S)", 228, 40, true);
            btnAdd.Click += (s, e) => AddHero();
            btnDel.Click += (s, e) => DeleteEntry();
            btnSave.Click += (s, e) => SaveIni();
            foreach (var b in new[] { btnAdd, btnDel, btnSave }) side.Controls.Add(b);
            lblStatus = Lbl("", T.Small, T.Text2);
            side.Controls.Add(lblStatus);
            lnkUndo = new LinkLabel
            {
                Text = "Undo", AutoSize = true, Visible = false, Font = T.Strong, BackColor = Color.Transparent,
                LinkColor = T.Accent, ActiveLinkColor = T.Text, VisitedLinkColor = T.Accent, LinkBehavior = LinkBehavior.HoverUnderline
            };
            lnkUndo.LinkClicked += (s, e) => UndoRemove();
            side.Controls.Add(lnkUndo);
            undoTimer = new Timer { Interval = 10000 };
            undoTimer.Tick += (s, e) => { undoTimer.Stop(); removedEntry = null; ShowStatus(); };
            // Runs after scaling too, so it works in scaled pixels.
            side.Layout += (s, e) =>
            {
                int h = side.ClientSize.Height;
                lstEntries.Height = Math.Max(S(60), h - lstEntries.Top - S(124));
                btnAdd.Location = new Point(S(16), h - S(112));
                btnDel.Location = new Point(S(132), h - S(112));
                btnSave.Location = new Point(S(16), h - S(72));
                lblStatus.Location = new Point(S(17), h - S(26));
                lnkUndo.Location = new Point(lblStatus.Right + S(2), lblStatus.Top);
            };

            // ---- main area ----
            editor = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg, Padding = new Padding(24, 20, 24, 12) };

            // Hero card: portrait (2x), who it is, and the identity fields.
            var card = new Panel { Dock = DockStyle.Top, Height = 160, BackColor = T.Surface };
            card.Paint += (s, e) => { using (var p = new Pen(T.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            picPortrait = new PictureBox { Location = new Point(16, 16), Size = new Size(116, 128), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = T.Input };
            card.Controls.Add(picPortrait);
            lblHeroTitle = Lbl("", T.H2, T.Text);
            lblHeroTitle.Location = new Point(148, 14);
            card.Controls.Add(lblHeroTitle);
            lblHeroSub = Lbl("", T.Small, T.Text2);
            lblHeroSub.Location = new Point(150, 42);
            card.Controls.Add(lblHeroSub);

            // One search box for the hero, like every other picker: name, class, town or specialty.
            foreach (var kv in heroNames.OrderBy(k => k.Value))
            {
                string cls, spec;
                int town;
                var about = new List<string>();
                if (heroClasses.TryGetValue(kv.Key, out cls) && cls.Length > 0) about.Add(cls);
                if (heroTowns.TryGetValue(kv.Key, out town)) about.Add(TownName(town));
                string where = string.Join(", ", about);
                if (specTexts.TryGetValue(kv.Key, out spec) && spec.Length > 0) where += (where.Length > 0 ? " - " : "") + spec;
                heroItems.Add(new SpecItem(kv.Key, kv.Value, where));
            }
            Caption(card, "Hero", 150, 68);
            cboHero = new ComboBox { Location = new Point(150, 86), Width = 404, MaxDropDownItems = 20 };
            MakeSearchable(cboHero, heroItems);
            cboHero.SelectedIndexChanged += (s, e) => OnEdit();
            cboHero.TextChanged += (s, e) => OnEdit();
            cboHero.HandleCreated += (s, e) => SendMessage(cboHero.Handle, 0x1703, IntPtr.Zero,
                heroItems.Count > 0 ? "Type a name, class, town or specialty" : "Type the hero's number");
            card.Controls.Add(cboHero);
            Caption(card, "New name · max 12", 566, 68);
            txtName = new TextBox { Location = new Point(566, 86), Width = 150, MaxLength = 12 };
            txtName.TextChanged += (s, e) => OnEdit();
            txtName.HandleCreated += (s, e) => SendMessage(txtName.Handle, 0x1501, (IntPtr)1, nameCue);
            card.Controls.Add(txtName);
            btnPortrait = FlatButton("Custom portrait…", 148, 28);
            btnPortraitReset = FlatButton("Use game portrait", 148, 28);
            btnPortrait.Location = new Point(150, 120);
            btnPortraitReset.Location = new Point(306, 120);
            btnPortrait.Click += (s, e) => ChooseCustomPortrait();
            btnPortraitReset.Click += (s, e) => RemoveCustomPortrait();
            card.Controls.Add(btnPortrait);
            card.Controls.Add(btnPortraitReset);

            // Save problems show here, under the card, next to what needs fixing.
            errorBar = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(0, 12, 0, 0), BackColor = T.Bg, Visible = false };
            var errorInner = new Panel { Dock = DockStyle.Fill, BackColor = T.WarnBg };
            errorInner.Paint += (s, e) => { using (var b = new SolidBrush(T.Warn)) e.Graphics.FillRectangle(b, 0, 0, S(3), errorInner.Height); };
            lblError = Lbl("", T.Strong, T.Warn);
            lblError.Location = new Point(16, 8);
            errorInner.Controls.Add(lblError);
            errorBar.Controls.Add(errorInner);

            // Section navigation and the sections themselves.
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0), BackColor = T.Bg };
            lstNav = new ListBox
            {
                Dock = DockStyle.Left, Width = 196, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 56,
                BorderStyle = BorderStyle.None, BackColor = T.Bg, ForeColor = T.Text, IntegralHeight = false
            };
            lstNav.Items.AddRange(SectionNames);
            lstNav.DrawItem += DrawNavRow;
            lstNav.SelectedIndexChanged += (s, e) => ShowSection(lstNav.SelectedIndex);
            var gap = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = T.Bg };
            var host = new Panel { Dock = DockStyle.Fill, BackColor = T.Surface };
            for (int i = 0; i < sections.Length; i++)
            {
                sections[i] = new Panel { Dock = DockStyle.Fill, BackColor = T.Surface, Visible = i == 0 };
                host.Controls.Add(sections[i]);
            }
            body.Controls.Add(host);
            body.Controls.Add(gap);
            body.Controls.Add(lstNav);

            BuildSpecialtySection(sections[0]);
            BuildSkillsSection(sections[1]);
            BuildSpellsSection(sections[2]);
            BuildArmySection(sections[3]);
            BuildStatsSection(sections[4]);

            // A new entry: the sections wait until a hero is picked.
            pickPanel = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg, Visible = false };
            var pickTitle = Lbl("Pick a hero first", T.H2, T.Text);
            pickTitle.Location = new Point(0, 28);
            pickPanel.Controls.Add(pickTitle);
            var pickText = Lbl(heroItems.Count > 0
                ? "Type in the Hero box above: a name (Vehr), a class (Elder), a town (Bulwark) or a specialty (Gold).\nThen change the specialty, skills, spells, army and stats here."
                : "Type the hero's number in the Hero box above. Hero names appear here after you start a new game once with the mod.",
                T.Body, T.Text2);
            pickText.Location = new Point(1, 60);
            pickPanel.Controls.Add(pickText);

            var footer = Lbl("Changes apply after Save and a game restart. Skills, spells, army and stats apply in a NEW game.", T.Small, T.Text2);
            footer.Dock = DockStyle.Bottom;
            footer.AutoSize = false;
            footer.Height = 28;
            footer.TextAlign = ContentAlignment.BottomLeft;

            editor.Controls.Add(body);
            editor.Controls.Add(pickPanel);
            editor.Controls.Add(errorBar);
            editor.Controls.Add(card);
            editor.Controls.Add(footer);

            BuildWelcome();
            Controls.Add(welcome);
            Controls.Add(editor);
            Controls.Add(side);
            Theme(this);
            HandleCreated += (s, e) => DarkChrome(this);
            lstNav.SelectedIndex = 0;
            Shown += (s, e) => { if (entries.Count > 0) ActiveControl = lstEntries; }; // not the hero box (its text would show selected)
        }

        // Empty list: a starting point that carries its action, instead of a disabled form.
        void BuildWelcome()
        {
            welcome = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg, Visible = false };
            var card = new Panel { Location = new Point(48, 48), Size = new Size(580, 336), BackColor = T.Surface };
            card.Paint += (s, e) => { using (var p = new Pen(T.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            welcome.Controls.Add(card);
            var title = Lbl("Forge your first hero", T.H1, T.Text);
            title.Location = new Point(30, 28);
            card.Controls.Add(title);
            var sub = Lbl("Give any hero a new specialty, skills, spells, starting army and stats.", T.Body, T.Text2);
            sub.Location = new Point(32, 68);
            card.Controls.Add(sub);

            bool haveList = heroNames.Count > 0;
            StepBadge(card, 1, 32, 112, haveList);
            var s1 = Lbl("Start a new game once with the mod", T.Strong, T.Text);
            s1.Location = new Point(68, 112);
            card.Controls.Add(s1);
            var s1note = haveList
                ? Lbl("Done: hero list found (" + heroNames.Count + " heroes)", T.Body, T.Text2)
                : Lbl("Not found yet. Run HD_Launcher.exe in the mod's game folder, start a NEW game,\nthen reopen HeroForge. Without it you can still type hero numbers.", T.Body, T.Warn);
            s1note.Location = new Point(68, 134);
            card.Controls.Add(s1note);

            StepBadge(card, 2, 32, 192, false);
            var s2 = Lbl("Pick a hero and change what you like", T.Strong, T.Text);
            s2.Location = new Point(68, 192);
            card.Controls.Add(s2);
            var s2note = Lbl("Then Save. The changes apply in your next new game.", T.Body, T.Text2);
            s2note.Location = new Point(68, 214);
            card.Controls.Add(s2note);

            var add = FlatButton("+  Add your first hero", 230, 40, true);
            add.Location = new Point(32, 264);
            add.Click += (s, e) => AddHero();
            card.Controls.Add(add);
            var keys = Lbl("Ctrl+N", T.Body, T.Text2);
            keys.Location = new Point(276, 275);
            card.Controls.Add(keys);
        }

        static void StepBadge(Control parent, int number, int x, int y, bool done)
        {
            var badge = new Panel { Location = new Point(x, y - 2), Size = new Size(24, 24), BackColor = Color.Transparent };
            badge.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rc = new Rectangle(0, 0, badge.Width - 1, badge.Height - 1);
                using (var fill = new SolidBrush(done ? T.Accent : T.Input)) g.FillEllipse(fill, rc);
                using (var pen = new Pen(T.Accent, 1.4f)) g.DrawEllipse(pen, rc);
                TextRenderer.DrawText(g, done ? "✓" : number.ToString(), T.Strong, rc, done ? T.OnAccent : T.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            parent.Controls.Add(badge);
        }

        // A new entry goes straight to the hero search (the one thing it needs first).
        void AddHero()
        {
            entries.Add(new HeroEntry());
            RefreshList(entries.Count - 1);
            UpdateDirty();
            lstNav.SelectedIndex = 0;
            cboHero.Focus();
            if (heroItems.Count > 0 && IsHandleCreated)
                BeginInvoke((Action)(() => { if (cboHero.Focused) cboHero.DroppedDown = true; }));
        }

        void UndoRemove()
        {
            if (removedEntry == null) return;
            entries.Insert(Math.Min(removedIndex, entries.Count), removedEntry);
            int at = entries.IndexOf(removedEntry);
            removedEntry = null;
            undoTimer.Stop();
            RefreshList(at);
            UpdateDirty();
            ShowStatus();
        }

        void ShowStatus()
        {
            if (removedEntry != null)
            {
                lblStatus.Text = "Removed " + (removedEntry.Id >= 0 ? HeroName(removedEntry.Id) : "a new hero") + "  ·";
                lblStatus.ForeColor = T.Text2;
            }
            else if (dirty) { lblStatus.Text = "Unsaved changes"; lblStatus.ForeColor = T.Accent; }
            else { lblStatus.Text = savedNote; lblStatus.ForeColor = T.Text2; }
            lnkUndo.Visible = removedEntry != null;
            lnkUndo.Location = new Point(lblStatus.Right + S(2), lblStatus.Top);
        }

        // Save problems: select the entry and section, mark the field, say what to do.
        void ShowError(HeroEntry entry, int section, Control field, string message)
        {
            int i = entries.IndexOf(entry);
            if (i >= 0 && lstEntries.SelectedIndex != i) lstEntries.SelectedIndex = i;
            if (section >= 0) lstNav.SelectedIndex = section;
            ClearError();
            lblError.Text = "!   " + message;
            errorBar.Visible = true;
            if (field != null)
            {
                errorControl = field;
                errorControlBack = field.BackColor;
                field.BackColor = T.WarnBg;
                field.Focus();
            }
        }

        void ClearError()
        {
            if (errorBar != null && errorBar.Visible) errorBar.Visible = false;
            if (errorControl != null) { errorControl.BackColor = errorControlBack; errorControl = null; }
        }

        bool HasCustomPortrait(int id)
        {
            return id >= 0 && File.Exists(Path.Combine(dir, "custom_portraits", id + "_L.bmp"));
        }

        string HeroName(int id)
        {
            string name;
            return heroNames.TryGetValue(id, out name) ? name : "Hero #" + id;
        }

        // Scales by a whole number with hard pixels (2x at 100% Windows scaling, 3x at 150%).
        static Bitmap ScaleToFit(Bitmap src, int w, int h)
        {
            int k = Math.Max(1, Math.Min(w / src.Width, h / src.Height));
            var dst = new Bitmap(src.Width * k, src.Height * k);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(src, 0, 0, dst.Width, dst.Height);
            }
            return dst;
        }

        // No picture for this hero: initials on a colour that always comes out the same for the name.
        static Bitmap InitialsPortrait(string name, int w, int h)
        {
            int hash = 0;
            foreach (char c in name) hash = hash * 31 + c;
            double hue = ((hash & 0x7FFFFFFF) % 360) / 360.0;
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            using (var bg = new SolidBrush(Hsl(hue, 0.30, 0.27)))
            using (var font = new Font("Segoe UI Semibold", h * 0.30f, GraphicsUnit.Pixel))
            {
                g.FillRectangle(bg, 0, 0, w, h);
                var words = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                string initials = string.Concat(words.Take(2).Select(x => char.ToUpperInvariant(x[0])));
                TextRenderer.DrawText(g, initials, font, new Rectangle(0, 0, w, h), T.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            return bmp;
        }

        static Color Hsl(double h, double s, double l)
        {
            Func<double, double, double, double> f = (p, q, t) =>
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            };
            double qq = l < 0.5 ? l * (1 + s) : l + s - l * s, pp = 2 * l - qq;
            return Color.FromArgb((int)(f(pp, qq, h + 1 / 3.0) * 255), (int)(f(pp, qq, h) * 255), (int)(f(pp, qq, h - 1 / 3.0) * 255));
        }

        void BuildSpecialtySection(Panel section)
        {
            var f = Stack(section, "Specialty", "What the hero is good at: keep it, copy another hero's, or build a new one.");
            var r = Row(f, 24);
            rbSpecOwn = new RadioButton { Text = "Hero's original specialty", Location = new Point(0, 2), AutoSize = true };
            lblOwnSpec = Lbl("", T.Body, T.Text2);
            lblOwnSpec.Location = new Point(200, 4);
            r.Controls.Add(rbSpecOwn); r.Controls.Add(lblOwnSpec);
            r = Row(f, 24);
            rbSpecCopy = new RadioButton { Text = "Same specialty as another hero", Location = new Point(0, 2), AutoSize = true };
            r.Controls.Add(rbSpecCopy);
            rowCopy = Row(f, 28, 24);
            rowCopy.Width = ContentWidth - 24;
            cboSpecFrom = new ComboBox { Location = new Point(0, 2), Width = ContentWidth - 24, MaxDropDownItems = 20 };
            specItems = heroNames.OrderBy(k => k.Value)
                .Select(kv => new SpecItem(kv.Key, kv.Value, specTexts.ContainsKey(kv.Key) ? specTexts[kv.Key] : "")).ToList();
            MakeSearchable(cboSpecFrom, specItems);
            rowCopy.Controls.Add(cboSpecFrom);
            r = Row(f, 24);
            rbSpecNone = new RadioButton { Text = "No specialty", Location = new Point(0, 2), AutoSize = true };
            r.Controls.Add(rbSpecNone);
            r = Row(f, 24);
            rbSpecCreature = new RadioButton { Text = "New specialty", Location = new Point(0, 2), AutoSize = true };
            r.Controls.Add(rbSpecCreature);
            rowKind = Row(f, 28, 24);
            rowKind.Width = ContentWidth - 24;
            cboKind = new ComboBox { Location = new Point(0, 2), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            cboKind.Items.AddRange(KindLabels);
            cboKind.SelectedIndex = 0;
            int pickX = 228, pickW = ContentWidth - 24 - 228;
            cboCreature = new ComboBox { Location = new Point(pickX, 2), Width = pickW, MaxDropDownItems = 20 };
            MakeSearchable(cboCreature, creatureItems);
            cboBuildSpell = new ComboBox { Location = new Point(pickX, 2), Width = pickW, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
            for (int i = 0; i < Data.SpellNames.Length; i++)
                if (Data.SpellNames[i] != null) cboBuildSpell.Items.Add(new SpellItem(i));
            cboBuildSkill = new ComboBox { Location = new Point(pickX, 2), Width = pickW, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
            cboBuildSkill.Items.AddRange(Data.SkillNames);
            cboBuildRes = new ComboBox { Location = new Point(pickX, 2), Width = pickW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboBuildRes.Items.AddRange(ResourceNames);
            foreach (var c in new Control[] { cboKind, cboCreature, cboBuildSpell, cboBuildSkill, cboBuildRes }) rowKind.Controls.Add(c);
            rowCrNums = Row(f, 28, 24);
            string[] crLabels = { "Attack", "Defense", "Damage", "Speed" };
            for (int i = 0; i < 4; i++)
            {
                lblCreature[i] = Lbl(crLabels[i], T.Body, T.Text2);
                lblCreature[i].Location = new Point(i * 136, 5);
                rowCrNums.Controls.Add(lblCreature[i]);
                numCreature[i] = new NumericUpDown { Location = new Point(i * 136 + 62, 2), Width = 56, Minimum = -20, Maximum = 99 };
                numCreature[i].ValueChanged += (s, e) => OnEdit();
                rowCrNums.Controls.Add(numCreature[i]);
            }
            rowKindInfo = Row(f, 36, 24);
            lblKindInfo = Lbl("", T.Small, T.Text2);
            lblKindInfo.AutoSize = false;
            lblKindInfo.Size = new Size(ContentWidth - 24, 34);
            rowKindInfo.Controls.Add(lblKindInfo);

            r = Row(f, 24);
            r.Margin = new Padding(0, 12, 0, 8);
            chkSpec = new CheckBox { Text = "Extra spell damage for a school of magic", Location = new Point(0, 2), AutoSize = true };
            r.Controls.Add(chkSpec);
            rowSchools = Row(f, 28, 24);
            chkEarth = new CheckBox { Text = "Earth", Location = new Point(0, 4), AutoSize = true };
            chkFire = new CheckBox { Text = "Fire", Location = new Point(70, 4), AutoSize = true };
            chkWater = new CheckBox { Text = "Water", Location = new Point(132, 4), AutoSize = true };
            chkAir = new CheckBox { Text = "Air", Location = new Point(204, 4), AutoSize = true };
            var lblBonus = Lbl("Bonus %", T.Body, T.Text2);
            lblBonus.Location = new Point(276, 6);
            numBonus = new NumericUpDown { Location = new Point(338, 2), Width = 64, Minimum = 1, Maximum = 500, Value = 50 };
            foreach (var c in new Control[] { chkEarth, chkFire, chkWater, chkAir, lblBonus, numBonus }) rowSchools.Controls.Add(c);

            r = Row(f, 22);
            r.Margin = new Padding(0, 16, 0, 4);
            Caption(r, "In-game text and icon", 0, 4);
            r = Row(f, 28);
            var l = Lbl("Name", T.Body, T.Text2); l.Location = new Point(0, 5); r.Controls.Add(l);
            txtSpecName = new TextBox { Location = new Point(90, 2), Width = 200, MaxLength = 60 };
            r.Controls.Add(txtSpecName);
            r = Row(f, 28);
            l = Lbl("Description", T.Body, T.Text2); l.Location = new Point(0, 5); r.Controls.Add(l);
            txtSpecDesc = new TextBox { Location = new Point(90, 2), Width = ContentWidth - 90, MaxLength = 250 };
            r.Controls.Add(txtSpecDesc);
            r = Row(f, 28);
            l = Lbl("Icon", T.Body, T.Text2); l.Location = new Point(0, 5); r.Controls.Add(l);
            cboIconMode = new ComboBox { Location = new Point(90, 2), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cboIconMode.Items.AddRange(new object[] { "Automatic", "Hero's own", "Skill icon", "Same as hero" });
            cboIconSkill = new ComboBox { Location = new Point(238, 2), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cboIconSkill.Items.AddRange(Data.SkillNames);
            cboIconHero = new ComboBox { Location = new Point(238, 2), Width = ContentWidth - 238, MaxDropDownItems = 20 };
            MakeSearchable(cboIconHero, specItems);
            foreach (var c in new Control[] { cboIconMode, cboIconSkill, cboIconHero }) r.Controls.Add(c);
            r = Row(f, 34);
            var hint = Lbl("Name and description are optional (empty = written for you, in English). Automatic icon: a hero with the same specialty, else the creature, skill or magic school.", T.Small, T.Text2);
            hint.AutoSize = false;
            hint.Size = new Size(ContentWidth, 34);
            r.Controls.Add(hint);

            cboKind.SelectedIndexChanged += (s, e) => { UpdateKindControls(); if (!loading) rbSpecCreature.Checked = true; OnEdit(); };
            foreach (var box in new[] { cboBuildSpell, cboBuildSkill, cboBuildRes })
                box.SelectedIndexChanged += (s, e) => { if (!loading) rbSpecCreature.Checked = true; OnEdit(); };
            cboIconMode.SelectedIndexChanged += (s, e) => { UpdateIconControls(); OnEdit(); };
            cboIconSkill.SelectedIndexChanged += (s, e) => OnEdit();
            cboIconHero.SelectedIndexChanged += (s, e) => OnEdit();
            foreach (var c in new CheckBox[] { chkSpec, chkEarth, chkFire, chkWater, chkAir }) c.CheckedChanged += (s, e) => OnEdit();
            // The four choices sit in separate rows (panels), so Windows does not group them:
            // checking one unchecks the others here.
            var specChoices = new RadioButton[] { rbSpecOwn, rbSpecCopy, rbSpecNone, rbSpecCreature };
            foreach (var rb in specChoices)
                rb.CheckedChanged += (s, e) =>
                {
                    var me = (RadioButton)s;
                    if (!me.Checked) return;
                    foreach (var other in specChoices) if (other != me) other.Checked = false;
                    OnEdit();
                };
            cboCreature.SelectedIndexChanged += (s, e) => { if (!loading && cboCreature.SelectedIndex >= 0) rbSpecCreature.Checked = true; OnEdit(); };
            cboSpecFrom.SelectedIndexChanged += (s, e) => { if (!loading && cboSpecFrom.SelectedIndex >= 0) rbSpecCopy.Checked = true; OnEdit(); };
            numBonus.ValueChanged += (s, e) => OnEdit();
            txtSpecName.TextChanged += (s, e) => OnEdit();
            txtSpecDesc.TextChanged += (s, e) => OnEdit();
        }

        void BuildSkillsSection(Panel section)
        {
            var f = Stack(section, "Starting skills", "At least this level. The first two also show in hero previews. Pick a skill to add another row.");
            for (int i = 0; i < SkillRows; i++)
            {
                skillRow[i] = Row(f, 28);
                cboSkill[i] = new ComboBox { Location = new Point(0, 2), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
                cboSkill[i].Items.Add("(none)");
                cboSkill[i].Items.AddRange(Data.SkillNames);
                cboSkill[i].SelectedIndex = 0;
                cboLevel[i] = new ComboBox { Location = new Point(268, 2), Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
                cboLevel[i].Items.AddRange(Data.Levels);
                cboLevel[i].SelectedIndex = 2;
                cboSkill[i].SelectedIndexChanged += (s, e) => OnEdit();
                cboLevel[i].SelectedIndexChanged += (s, e) => OnEdit();
                skillRow[i].Controls.Add(cboSkill[i]);
                skillRow[i].Controls.Add(cboLevel[i]);
            }
        }

        void BuildSpellsSection(Panel section)
        {
            var f = Stack(section, "Starting spells", null);
            var r = Row(f, 28);
            var l = Lbl("Spell book", T.Body, T.Text2); l.Location = new Point(0, 5); r.Controls.Add(l);
            cboBook = new ComboBox { Location = new Point(90, 2), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cboBook.Items.AddRange(new object[] { "Game default (spells below add one)", "Yes, at start", "No spell book" });
            cboBook.SelectedIndex = 0;
            cboBook.SelectedIndexChanged += (s, e) => OnEdit();
            r.Controls.Add(cboBook);

            r = Row(f, 18);
            r.Margin = new Padding(0, 8, 0, 4);
            Caption(r, "Chosen", 0, 2);
            spellChips = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                MinimumSize = new Size(ContentWidth, 34), MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 0, 0, 12), BackColor = Color.Transparent
            };
            f.Controls.Add(spellChips);

            r = Row(f, 30);
            txtSpellSearch = new TextBox { Location = new Point(0, 3), Width = 200 };
            txtSpellSearch.HandleCreated += (s, e) => SendMessage(txtSpellSearch.Handle, 0x1501, (IntPtr)1, "Search spells…");
            txtSpellSearch.TextChanged += (s, e) => FillSpellList();
            r.Controls.Add(txtSpellSearch);
            for (int i = 0; i < SchoolNames.Length; i++)
            {
                int school = i;
                btnSchool[i] = FlatButton(SchoolNames[i], 64, 28);
                btnSchool[i].Location = new Point(212 + i * 72, 1);
                btnSchool[i].Click += (s, e) => { spellSchoolFilter = school; StyleSchoolButtons(); FillSpellList(); };
                r.Controls.Add(btnSchool[i]);
            }
            lstSpells = new ListBox
            {
                Width = ContentWidth, Height = 210, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26,
                BorderStyle = BorderStyle.None, BackColor = T.Input, ForeColor = T.Text, IntegralHeight = false, Margin = new Padding(0, 0, 0, 8)
            };
            lstSpells.DrawItem += DrawSpellRow;
            lstSpells.MouseDown += (s, e) =>
            {
                int i = lstSpells.IndexFromPoint(e.Location);
                if (i >= 0) ToggleSpell(((SpellItem)lstSpells.Items[i]).Id);
            };
            lstSpells.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && lstSpells.SelectedItem != null) { ToggleSpell(((SpellItem)lstSpells.SelectedItem).Id); e.Handled = true; }
            };
            f.Controls.Add(lstSpells);
            // No results: say what was searched and offer the way back.
            spellsEmpty = new Panel { Width = ContentWidth, Height = 210, BackColor = T.Input, Margin = new Padding(0, 0, 0, 8), Visible = false };
            lblSpellsEmpty = Lbl("", T.Body, T.Text2);
            lblSpellsEmpty.AutoSize = false;
            lblSpellsEmpty.Size = new Size(ContentWidth - 32, 24);
            lblSpellsEmpty.Location = new Point(16, 66);
            lblSpellsEmpty.TextAlign = ContentAlignment.MiddleCenter;
            spellsEmpty.Controls.Add(lblSpellsEmpty);
            var btnClear = FlatButton("Clear search", 120, 28);
            btnClear.Location = new Point((ContentWidth - 120) / 2, 100);
            btnClear.Click += (s, e) => { txtSpellSearch.Text = ""; spellSchoolFilter = 0; StyleSchoolButtons(); FillSpellList(); txtSpellSearch.Focus(); };
            spellsEmpty.Controls.Add(btnClear);
            f.Controls.Add(spellsEmpty);
            r = Row(f, 34);
            lblSpellDefault = Lbl("", T.Small, T.Text2);
            lblSpellDefault.AutoSize = false;
            lblSpellDefault.Size = new Size(ContentWidth, 34);
            r.Controls.Add(lblSpellDefault);
            StyleSchoolButtons();
            FillSpellList();
        }

        void BuildArmySection(Panel section)
        {
            var f = Stack(section, "Starting army", null);
            var r = Row(f, 24);
            chkArmy = new CheckBox { Text = "Custom starting army (otherwise the hero's default)", Location = new Point(0, 2), AutoSize = true };
            chkArmy.CheckedChanged += (s, e) =>
            {
                // Back to the default: show the hero's own army again (read only).
                if (!loading && !chkArmy.Checked && Current != null) ShowArmy(DefaultArmy(Current.Id));
                UpdateArmyEnabled();
                OnEdit();
            };
            r.Controls.Add(chkArmy);
            armyHeader = Row(f, 18);
            armyHeader.Margin = new Padding(0, 8, 0, 4);
            Caption(armyHeader, "Creature", 0, 2);
            Caption(armyHeader, "Count, from – to", 400, 2);
            for (int i = 0; i < ArmyRows; i++)
            {
                int row = i;
                armyRow[i] = Row(f, 28);
                cboArmy[i] = new ComboBox { Location = new Point(0, 2), Width = 390, MaxDropDownItems = 20 };
                MakeSearchable(cboArmy[i], armyItems);
                cboArmy[i].SelectedIndexChanged += (s, e) =>
                {
                    if (cboArmy[row].SelectedIndex >= 0) armyPick[row] = ((SpecItem)cboArmy[row].SelectedItem).Id;
                    OnEdit();
                };
                cboArmy[i].TextChanged += (s, e) => { if (cboArmy[row].Text.Trim().Length == 0) { armyPick[row] = -1; OnEdit(); } };
                numArmyMin[i] = new NumericUpDown { Location = new Point(400, 2), Width = 72, Minimum = 1, Maximum = 9999, Value = 1 };
                var dash = Lbl("–", T.Body, T.Text2); dash.Location = new Point(478, 5);
                numArmyMax[i] = new NumericUpDown { Location = new Point(496, 2), Width = 72, Minimum = 1, Maximum = 9999, Value = 1 };
                numArmyMin[i].ValueChanged += (s, e) => OnEdit();
                numArmyMax[i].ValueChanged += (s, e) => OnEdit();
                armyRow[i].Controls.Add(cboArmy[i]);
                armyRow[i].Controls.Add(numArmyMin[i]);
                armyRow[i].Controls.Add(dash);
                armyRow[i].Controls.Add(numArmyMax[i]);
                armyPick[i] = -1;
            }
            r = Row(f, 50);
            lblArmyDefault = Lbl("", T.Small, T.Text2);
            lblArmyDefault.AutoSize = false;
            lblArmyDefault.Size = new Size(ContentWidth, 50);
            r.Controls.Add(lblArmyDefault);
            UpdateArmyEnabled();
        }

        void BuildStatsSection(Panel section)
        {
            var f = Stack(section, "Starting stats", "Attack, Defense, Power and Knowledge at level 1.");
            var r = Row(f, 24);
            chkStats = new CheckBox { Text = "Custom stats (otherwise the hero's defaults)", Location = new Point(0, 2), AutoSize = true };
            chkStats.CheckedChanged += (s, e) => { foreach (var n in numStats) n.Enabled = chkStats.Checked; OnEdit(); };
            r.Controls.Add(chkStats);
            r = Row(f, 22, 24);
            lblStatsDefault = Lbl("", T.Body, T.Text2);
            r.Controls.Add(lblStatsDefault);
            rowStatsDefault = r;
            r = Row(f, 52);
            r.Margin = new Padding(0, 8, 0, 8);
            rowStats = r;
            string[] statNames = { "Attack", "Defense", "Power", "Knowledge" };
            for (int i = 0; i < 4; i++)
            {
                Caption(r, statNames[i], i * 100, 0);
                numStats[i] = new NumericUpDown { Location = new Point(i * 100, 20), Width = 80, Minimum = i < 2 ? 0 : 1, Maximum = 99, Value = i < 2 ? 0 : 1, Enabled = false };
                numStats[i].ValueChanged += (s, e) => OnEdit();
                r.Controls.Add(numStats[i]);
            }
        }

        void ShowSection(int i)
        {
            if (i < 0) return;
            for (int k = 0; k < sections.Length; k++) sections[k].Visible = k == i;
        }

        // ---- owner-drawn rows ----

        void DrawHeroRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= entries.Count) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var rc = e.Bounds;
            using (var bg = new SolidBrush(sel ? T.Surface : T.Side)) g.FillRectangle(bg, rc);
            if (sel) using (var bar = new SolidBrush(T.Accent)) g.FillRectangle(bar, rc.X, rc.Y + S(8), S(3), rc.Height - S(16));
            var en = entries[e.Index];
            string title = en.Id >= 0 ? HeroName(en.Id) : "New hero – pick one";
            string newName = en.Name.Trim();
            if (newName.Length > 0) title += "  →  " + newName;
            TextRenderer.DrawText(g, title, T.Strong, new Rectangle(rc.X + S(14), rc.Y + S(5), rc.Width - S(18), S(21)), T.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, HeroSummary(en), T.Small, new Rectangle(rc.X + S(14), rc.Y + S(25), rc.Width - S(18), S(20)), T.Text2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void DrawNavRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var rc = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height - S(4));
            using (var bg = new SolidBrush(T.Bg)) g.FillRectangle(bg, e.Bounds);
            using (var bg = new SolidBrush(sel ? T.Surface : T.Bg)) g.FillRectangle(bg, rc);
            if (sel) using (var bar = new SolidBrush(T.Accent)) g.FillRectangle(bar, rc.X, rc.Y + S(10), S(3), rc.Height - S(20));
            TextRenderer.DrawText(g, SectionNames[e.Index], T.Strong, new Rectangle(rc.X + S(14), rc.Y + S(6), rc.Width - S(18), S(21)), sel ? T.Accent : T.Text, TextFormatFlags.NoPrefix);
            var cur = Current;
            string summary = cur == null ? "" : SectionSummary(cur, e.Index);
            TextRenderer.DrawText(g, summary, T.Small, new Rectangle(rc.X + S(14), rc.Y + S(26), rc.Width - S(18), S(20)), T.Text2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void DrawSpellRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            var item = (SpellItem)lstSpells.Items[e.Index];
            bool on = spellSel.Contains(item.Id);
            bool focus = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(focus ? T.Hover : T.Input)) g.FillRectangle(bg, e.Bounds);
            int size = S(14);
            var box = new Rectangle(e.Bounds.X + S(10), e.Bounds.Y + (e.Bounds.Height - size) / 2, size, size);
            using (var p = new Pen(on ? T.Accent : T.Text2)) g.DrawRectangle(p, box);
            if (on)
            {
                using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, box.X + S(3), box.Y + S(3), size - S(5), size - S(5));
            }
            var line = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Data.SpellNames[item.Id], on ? T.Strong : T.Body, new Rectangle(e.Bounds.X + S(34), e.Bounds.Y, S(300), e.Bounds.Height), T.Text, line);
            string school = item.Id < SpellSchool.Length ? SchoolLabel(SpellSchool[item.Id]) : "";
            TextRenderer.DrawText(g, school, T.Small, new Rectangle(e.Bounds.Right - S(130), e.Bounds.Y, S(120), e.Bounds.Height), T.Text2, line | TextFormatFlags.Right);
        }

        static string SchoolLabel(int flags)
        {
            if (flags == 15) return "All schools";
            for (int i = 1; i < SchoolFlags.Length; i++) if (SchoolFlags[i] == flags) return SchoolNames[i];
            return "";
        }

        // ---- spells ----

        void FillSpellList()
        {
            if (lstSpells == null) return;
            string q = txtSpellSearch.Text.Trim();
            int flags = SchoolFlags[spellSchoolFilter];
            lstSpells.BeginUpdate();
            lstSpells.Items.Clear();
            for (int i = 0; i < Data.SpellNames.Length; i++)
            {
                if (Data.SpellNames[i] == null) continue;
                if (q.Length > 0 && Data.SpellNames[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (flags != 15 && (SpellSchool[i] & flags) == 0) continue;
                lstSpells.Items.Add(new SpellItem(i));
            }
            lstSpells.EndUpdate();
            if (spellsEmpty == null) return;
            bool none = lstSpells.Items.Count == 0;
            lstSpells.Visible = !none;
            spellsEmpty.Visible = none;
            if (none)
                lblSpellsEmpty.Text = "No spell matches “" + q + "”" + (flags != 15 ? " in " + SchoolNames[spellSchoolFilter] + " magic" : "");
        }

        void StyleSchoolButtons()
        {
            for (int i = 0; i < btnSchool.Length; i++)
            {
                bool on = i == spellSchoolFilter;
                btnSchool[i].BackColor = on ? T.Accent : T.Input;
                btnSchool[i].ForeColor = on ? T.OnAccent : T.Text;
                btnSchool[i].FlatAppearance.BorderColor = on ? T.Accent : T.Line;
            }
        }

        void ToggleSpell(int id)
        {
            if (!spellSel.Remove(id)) spellSel.Add(id);
            lstSpells.Invalidate();
            RebuildSpellChips();
            OnEdit();
        }

        void RebuildSpellChips()
        {
            spellChips.SuspendLayout();
            foreach (Control c in spellChips.Controls.Cast<Control>().ToList()) { spellChips.Controls.Remove(c); c.Dispose(); }
            if (spellSel.Count == 0)
            {
                var none = Lbl("No starting spell", T.Body, T.Text2);
                none.Margin = new Padding(0, S(6), 0, 0);
                spellChips.Controls.Add(none);
            }
            foreach (int id in spellSel.OrderBy(x => Data.SpellNames[x]))
            {
                int spell = id;
                var chip = FlatButton(Data.SpellNames[id] + "   ×", S(10), S(28));
                chip.AutoSize = true;
                chip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                chip.Padding = new Padding(S(6), 0, S(6), 0);
                chip.Margin = new Padding(0, 0, S(8), S(6));
                chip.FlatAppearance.BorderColor = T.Accent;
                chip.Click += (s, e) => ToggleSpell(spell);
                spellChips.Controls.Add(chip);
            }
            spellChips.ResumeLayout();
        }

        // ---- visibility, summaries, dirty state ----

        // Shows only the fields that belong to the current choices.
        void UpdateVisibility()
        {
            rowCopy.Visible = rbSpecCopy.Checked;
            rowKind.Visible = rbSpecCreature.Checked;
            rowCrNums.Visible = rbSpecCreature.Checked && SelectedKind == 4;
            rowKindInfo.Visible = rbSpecCreature.Checked && SelectedKind != 4;
            rowSchools.Visible = chkSpec.Checked;
            int lastSkill = -1;
            for (int i = 0; i < SkillRows; i++) if (cboSkill[i].SelectedIndex > 0) lastSkill = i;
            for (int i = 0; i < SkillRows; i++) skillRow[i].Visible = i <= lastSkill + 1;
            int lastStack = -1;
            for (int i = 0; i < ArmyRows; i++) if (armyPick[i] >= 0) lastStack = i;
            // Defaults are described in text below the box, not shown as disabled fields.
            for (int i = 0; i < ArmyRows; i++) armyRow[i].Visible = chkArmy.Checked && i <= lastStack + 1;
            armyHeader.Visible = chkArmy.Checked;
            rowStats.Visible = chkStats.Checked;
            rowStatsDefault.Visible = !chkStats.Checked;
            var e = Current;
            if (e != null)
            {
                var st = DefaultStats(e.Id);
                lblStatsDefault.Text = "Hero's defaults:  Attack " + st[0] + "  ·  Defense " + st[1] + "  ·  Power " + st[2] + "  ·  Knowledge " + st[3];
            }
            // Until a hero is picked, only the hero search matters.
            bool hasHero = e != null && e.Id >= 0;
            body.Visible = hasHero;
            pickPanel.Visible = !hasHero;
            btnPortrait.Enabled = hasHero;
            btnPortraitReset.Enabled = hasHero && HasCustomPortrait(e.Id);
            nameCue = hasHero ? "Keep “" + HeroName(e.Id) + "”" : "";
            if (txtName.IsHandleCreated) SendMessage(txtName.Handle, 0x1501, (IntPtr)1, nameCue);
            if (e != null)
            {
                string label = e.Id >= 0 ? HeroName(e.Id) : "New hero";
                lblHeroTitle.Text = e.Name.Trim().Length > 0 ? label + "  →  " + e.Name.Trim() : label;
                string cls, spec;
                var sub = new List<string>();
                if (heroClasses.TryGetValue(e.Id, out cls) && cls.Length > 0) sub.Add(cls);
                int town;
                if (heroTowns.TryGetValue(e.Id, out town)) sub.Add(TownName(town));
                if (specTexts.TryGetValue(e.Id, out spec) && spec.Length > 0) sub.Add("normally " + spec);
                lblHeroSub.Text = string.Join("  ·  ", sub);
            }
            lstNav.Invalidate();
            lstEntries.Invalidate();
        }

        string SectionSummary(HeroEntry e, int section)
        {
            switch (section)
            {
                case 0:
                    string spec = "";
                    if (e.SpecMode == HeroEntry.SpecCopy) spec = "Like " + (e.SpecFrom >= 0 ? HeroLabelShort(e.SpecFrom) : "…");
                    else if (e.SpecMode == HeroEntry.SpecNone) spec = "None";
                    else if (e.SpecMode == HeroEntry.SpecBuilt) spec = BuiltLabel(e);
                    else { string own; spec = specTexts.TryGetValue(e.Id, out own) ? own : "Original"; }
                    if (e.Schools != 0 && e.Bonus != 0) spec = (e.SpecMode == HeroEntry.SpecNone ? "" : spec + " · ") + SchoolList(e.Schools) + " +" + e.Bonus + "%";
                    return spec;
                case 1:
                    return e.Skills.Count == 0 ? "Default" : string.Join(", ", e.Skills.Select(k => Data.SkillNames[k.Key] + " " + Data.Levels[k.Value - 1][0]));
                case 2:
                    var spells = e.SpellsSet ? e.Spells : DefaultSpells(e.Id);
                    string list = spells.Count == 0 ? "None" : string.Join(", ", spells.Select(x => Data.SpellNames[x]));
                    return (e.SpellsSet ? "" : "Default: ") + list;
                case 3:
                    if (e.Army == null) return "Default";
                    return e.Army.Count + (e.Army.Count == 1 ? " stack" : " stacks") + ": " + string.Join(", ", e.Army.Select(a => CreatureLabel(a[0])));
                default:
                    var st = e.Stats ?? DefaultStats(e.Id);
                    return (e.Stats == null ? "Default " : "Custom ") + string.Join("/", st);
            }
        }

        // One line for the hero list: what this entry changes.
        string HeroSummary(HeroEntry e)
        {
            var parts = new List<string>();
            if (e.SpecMode != HeroEntry.SpecOwn || (e.Schools != 0 && e.Bonus != 0)) parts.Add(SectionSummary(e, 0));
            if (e.Skills.Count > 0) parts.Add(SectionSummary(e, 1));
            if (e.SpellsSet) parts.Add(e.Spells.Count == 0 ? "no spell" : string.Join(", ", e.Spells.Select(x => Data.SpellNames[x])));
            if (e.Army != null) parts.Add("army");
            if (e.Stats != null) parts.Add(string.Join("/", e.Stats));
            return parts.Count == 0 ? "No changes yet" : string.Join(" · ", parts);
        }

        string BuiltLabel(HeroEntry e)
        {
            switch (e.BuildKind)
            {
                case 4: case 1:
                    var cr = creatureItems.FirstOrDefault(i => i.Id == e.Creature);
                    string name = cr != null ? cr.ToString().Split(new[] { " - " }, StringSplitOptions.None)[0] : "creature";
                    return name + (e.BuildKind == 1 ? " (grows)" : "");
                case 3: return e.BuildSpell >= 0 ? Data.SpellNames[e.BuildSpell] : "Spell";
                case 0: return e.BuildSkill >= 0 ? Data.SkillNames[e.BuildSkill] : "Skill";
                case 2: return e.BuildResource >= 0 ? ResourceNames[e.BuildResource] + " per day" : "Resource";
                default: return "Speed for all";
            }
        }

        static string SchoolList(int schools)
        {
            if (schools == 15) return "All magic";
            var names = new List<string>();
            if ((schools & 8) != 0) names.Add("Earth");
            if ((schools & 2) != 0) names.Add("Fire");
            if ((schools & 4) != 0) names.Add("Water");
            if ((schools & 1) != 0) names.Add("Air");
            return string.Join("/", names) + " Magic";
        }

        string HeroLabelShort(int id)
        {
            string name;
            return heroNames.TryGetValue(id, out name) ? name : id.ToString();
        }

        List<int[]> ArmyFromRows()
        {
            var army = new List<int[]>();
            for (int i = 0; i < ArmyRows; i++)
                if (armyPick[i] >= 0) army.Add(new[] { armyPick[i], (int)numArmyMin[i].Value, (int)numArmyMax[i].Value });
            return army;
        }

        // Unsaved = the entries differ from what was loaded or last saved.
        string savedSnapshot;

        string Snapshot()
        {
            return string.Join("\n", entries.Select(e => string.Join("|", new object[] {
                e.Id, e.Schools, e.Bonus, string.Join(",", e.Skills.Select(k => k.Key + ":" + k.Value)),
                string.Join(",", e.Spells.OrderBy(x => x)), e.SpellsSet,
                e.Army == null ? "-" : string.Join(",", e.Army.Select(a => string.Join(":", a))),
                e.Stats == null ? "-" : string.Join("/", e.Stats), e.SpecMode, e.SpecFrom, e.Name, e.SpecName, e.SpecDesc,
                e.BuildKind, e.Creature, string.Join(",", e.CreatureBonus), e.BuildSpell, e.BuildSkill, e.BuildResource,
                e.StartBook, e.IconMode, e.IconSkill, e.IconHero })));
        }

        void UpdateDirty()
        {
            bool now = Snapshot() != savedSnapshot;
            if (now == dirty) return;
            dirty = now;
            Text = (dirty ? "● " : "") + "HeroForge - Hero Editor for HotA";
            ShowStatus();
        }

        class SpecItem
        {
            public readonly int Id;
            readonly string label;
            public SpecItem(int id, string name, string spec) { Id = id; label = name + (spec.Length > 0 ? " - " + spec : "") + " (" + id + ")"; }
            public override string ToString() { return label; }
        }

        static string StripDiacritics(string s)
        {
            const string from = "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ", to = "acelnoszzACELNOSZZ";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                int i = from.IndexOf(c);
                char r = i >= 0 ? to[i] : c;
                if (r >= 32 && r < 127 && r != ';') sb.Append(r);
            }
            return sb.ToString().Trim();
        }

        class SpellItem
        {
            public readonly int Id;
            public SpellItem(int id) { Id = id; }
            public override string ToString() { return Data.SpellNames[Id] + " (" + Id + ")"; }
        }

        static int ParseHeroId(string text)
        {
            // "Vehr (212) - Elder - ..." or "212"
            var m = System.Text.RegularExpressions.Regex.Match(text, @"\((\d+)\)");
            string num = m.Success ? m.Groups[1].Value : text.Trim();
            int id;
            return int.TryParse(num, out id) && id >= 0 ? id : -1;
        }

        void RefreshList(int select)
        {
            loading = true;
            lstEntries.Items.Clear();
            foreach (var e in entries) lstEntries.Items.Add(e.Id >= 0 ? HeroName(e.Id) : "(new - pick a hero)");
            loading = false;
            // No entries: the welcome card instead of an empty, disabled form.
            editor.Visible = entries.Count > 0;
            welcome.Visible = entries.Count == 0;
            if (entries.Count > 0) lstEntries.SelectedIndex = Math.Max(0, Math.Min(select, entries.Count - 1));
        }

        HeroEntry Current { get { return lstEntries.SelectedIndex >= 0 ? entries[lstEntries.SelectedIndex] : null; } }

        void ShowEntry()
        {
            var e = Current;
            if (e == null || loading) return;
            loading = true;
            SelectSearchable(cboHero, e.Id, heroItems);
            if (e.Id >= 0 && cboHero.SelectedIndex < 0) cboHero.Text = e.Id.ToString(); // no hero list yet
            cboHero.SelectionLength = 0;
            rbSpecOwn.Checked = e.SpecMode == HeroEntry.SpecOwn;
            rbSpecNone.Checked = e.SpecMode == HeroEntry.SpecNone;
            rbSpecCopy.Checked = e.SpecMode == HeroEntry.SpecCopy;
            rbSpecCreature.Checked = e.SpecMode == HeroEntry.SpecBuilt;
            cboKind.SelectedIndex = Math.Max(0, Array.IndexOf(KindTypes, e.BuildKind));
            int baseCreature;
            if (upgradeToBase.TryGetValue(e.Creature, out baseCreature)) e.Creature = baseCreature;
            SelectSearchable(cboCreature, e.Creature, creatureItems);
            for (int i = 0; i < 4; i++) numCreature[i].Value = Math.Max(-20, Math.Min(99, e.CreatureBonus[i]));
            cboBuildSpell.SelectedIndex = -1;
            for (int i = 0; i < cboBuildSpell.Items.Count; i++)
                if (((SpellItem)cboBuildSpell.Items[i]).Id == e.BuildSpell) cboBuildSpell.SelectedIndex = i;
            cboBuildSkill.SelectedIndex = e.BuildSkill >= 0 && e.BuildSkill < Data.SkillNames.Length ? e.BuildSkill : -1;
            cboBuildRes.SelectedIndex = e.BuildResource >= 0 && e.BuildResource < ResourceNames.Length ? e.BuildResource : -1;
            cboBook.SelectedIndex = e.StartBook < 0 ? 0 : e.StartBook == 1 ? 1 : 2;
            UpdateKindControls();
            SelectSearchable(cboSpecFrom, e.SpecMode == HeroEntry.SpecCopy ? e.SpecFrom : -1);
            UpdatePortrait(e.Id);
            txtSpecName.Text = e.SpecName;
            txtSpecDesc.Text = e.SpecDesc;
            txtName.Text = e.Name;
            cboIconMode.SelectedIndex = e.IconMode;
            cboIconSkill.SelectedIndex = e.IconSkill >= 0 && e.IconSkill < Data.SkillNames.Length ? e.IconSkill : 17;
            SelectSearchable(cboIconHero, e.IconHero);
            UpdateIconControls();
            UpdateOwnSpecLabel(e.Id);
            chkSpec.Checked = e.Schools != 0 && e.Bonus != 0;
            chkEarth.Checked = (e.Schools & 8) != 0;
            chkFire.Checked = (e.Schools & 2) != 0;
            chkWater.Checked = (e.Schools & 4) != 0;
            chkAir.Checked = (e.Schools & 1) != 0;
            numBonus.Value = Math.Max(1, Math.Min(500, e.Bonus == 0 ? 50 : e.Bonus));
            for (int i = 0; i < SkillRows; i++)
            {
                bool has = i < e.Skills.Count;
                cboSkill[i].SelectedIndex = has ? e.Skills[i].Key + 1 : 0;
                cboLevel[i].SelectedIndex = has ? e.Skills[i].Value - 1 : 2;
            }
            ShowSpells(e.SpellsSet ? e.Spells : DefaultSpells(e.Id));
            UpdateSpellDefaultLabel(e.Id);
            chkArmy.Checked = e.Army != null;
            ShowArmy(e.Army ?? DefaultArmy(e.Id));
            UpdateArmyEnabled();
            UpdateArmyDefaultLabel(e.Id);
            // Without custom stats the boxes show the hero's defaults (read only).
            chkStats.Checked = e.Stats != null;
            for (int i = 0; i < 4; i++) numStats[i].Enabled = e.Stats != null;
            ShowStats(e.Stats ?? DefaultStats(e.Id));
            loading = false;
            UpdateVisibility();
            // Picking an item selects the box's text; that highlight means nothing when it is not being typed in.
            if (!cboHero.Focused) cboHero.SelectionLength = 0;
        }

        void OnEdit()
        {
            var e = Current;
            if (e == null || loading) return;
            ClearError();
            int previousId = e.Id;
            // A picked item, or a typed hero number; while a search is typed keep the last hero.
            int picked = cboHero.SelectedIndex >= 0 ? ((SpecItem)cboHero.SelectedItem).Id : ParseHeroId(cboHero.Text);
            if (picked >= 0) e.Id = picked;
            UpdateOwnSpecLabel(e.Id);
            if (e.Id != previousId)
            {
                // New hero: show its portrait and start the stat boxes from its defaults.
                UpdatePortrait(e.Id);
                loading = true;
                ShowStats(DefaultStats(e.Id));
                // Spells and army not customised yet follow the new hero's defaults.
                if (!e.SpellsSet) ShowSpells(DefaultSpells(e.Id));
                if (e.Army == null) ShowArmy(DefaultArmy(e.Id));
                loading = false;
                UpdateSpellDefaultLabel(e.Id);
                UpdateArmyDefaultLabel(e.Id);
            }
            e.SpecMode = rbSpecCopy.Checked ? HeroEntry.SpecCopy : rbSpecNone.Checked ? HeroEntry.SpecNone :
                         rbSpecCreature.Checked ? HeroEntry.SpecBuilt : HeroEntry.SpecOwn;
            e.BuildKind = SelectedKind;
            if (cboCreature.SelectedIndex >= 0) e.Creature = ((SpecItem)cboCreature.SelectedItem).Id;
            for (int i = 0; i < 4; i++) e.CreatureBonus[i] = (int)numCreature[i].Value;
            if (cboBuildSpell.SelectedIndex >= 0) e.BuildSpell = ((SpellItem)cboBuildSpell.SelectedItem).Id;
            if (cboBuildSkill.SelectedIndex >= 0) e.BuildSkill = cboBuildSkill.SelectedIndex;
            if (cboBuildRes.SelectedIndex >= 0) e.BuildResource = cboBuildRes.SelectedIndex;
            e.StartBook = cboBook.SelectedIndex <= 0 ? -1 : cboBook.SelectedIndex == 1 ? 1 : 0;
            // While a search is typed nothing is selected; keep the last picked hero.
            if (cboSpecFrom.SelectedIndex >= 0) e.SpecFrom = ((SpecItem)cboSpecFrom.SelectedItem).Id;
            e.SpecName = txtSpecName.Text;
            e.SpecDesc = txtSpecDesc.Text;
            e.Name = txtName.Text;
            e.IconMode = Math.Max(0, cboIconMode.SelectedIndex);
            e.IconSkill = cboIconSkill.SelectedIndex;
            if (cboIconHero.SelectedIndex >= 0) e.IconHero = ((SpecItem)cboIconHero.SelectedItem).Id;
            int schools = (chkEarth.Checked ? 8 : 0) | (chkFire.Checked ? 2 : 0) | (chkWater.Checked ? 4 : 0) | (chkAir.Checked ? 1 : 0);
            e.Schools = chkSpec.Checked ? schools : 0;
            e.Bonus = chkSpec.Checked ? (int)numBonus.Value : 0;
            e.Skills.Clear();
            for (int i = 0; i < SkillRows; i++)
            {
                int skill = cboSkill[i].SelectedIndex - 1;
                if (skill >= 0 && !e.Skills.Any(k => k.Key == skill))
                    e.Skills.Add(new KeyValuePair<int, int>(skill, cboLevel[i].SelectedIndex + 1));
            }
            // Spells: the hero's own starting spell is shown checked; anything else than
            // exactly that is a custom choice (nothing checked = no starting spell).
            var checkedSpells = new HashSet<int>(spellSel);
            bool wasNone = e.SpellsSet && e.Spells.Count == 0;
            if (heroSpell.ContainsKey(e.Id)) e.SpellsSet = !checkedSpells.SetEquals(DefaultSpells(e.Id));
            else e.SpellsSet = checkedSpells.Count > 0 || wasNone;
            e.Spells = checkedSpells;
            if (chkArmy.Checked)
            {
                e.Army = new List<int[]>();
                for (int i = 0; i < ArmyRows; i++)
                {
                    if (armyPick[i] < 0) continue;
                    int lo = (int)numArmyMin[i].Value, hi = (int)numArmyMax[i].Value;
                    e.Army.Add(new[] { armyPick[i], Math.Min(lo, hi), Math.Max(lo, hi) });
                }
            }
            else e.Army = null;
            e.Stats = chkStats.Checked ? numStats.Select(n => (int)n.Value).ToArray() : null;

            int idx = lstEntries.SelectedIndex;
            string label = e.Id >= 0 ? HeroName(e.Id) : "(new - pick a hero)";
            if ((string)lstEntries.Items[idx] != label)
            {
                loading = true;
                lstEntries.Items[idx] = label;
                loading = false;
            }
            UpdateVisibility();
            UpdateDirty();
        }

        int SelectedKind { get { return KindTypes[Math.Max(0, cboKind.SelectedIndex)]; } }

        // Show the list and fields that belong to the chosen kind of new specialty.
        void UpdateKindControls()
        {
            int kind = SelectedKind;
            cboCreature.Visible = kind == 4 || kind == 1;
            cboBuildSpell.Visible = kind == 3;
            cboBuildSkill.Visible = kind == 0;
            cboBuildRes.Visible = kind == 2;
            for (int i = 0; i < 4; i++) numCreature[i].Visible = lblCreature[i].Visible = kind == 4;
            lblKindInfo.Visible = kind != 4;
            lblKindInfo.Text = KindInfo[Math.Max(0, cboKind.SelectedIndex)];
        }

        HashSet<int> DefaultSpells(int id)
        {
            int spell;
            var set = new HashSet<int>();
            if (heroSpell.TryGetValue(id, out spell) && spell >= 0) set.Add(spell);
            return set;
        }

        void ShowSpells(ICollection<int> spells)
        {
            spellSel.Clear();
            foreach (int id in spells) spellSel.Add(id);
            lstSpells.Invalidate();
            RebuildSpellChips();
        }

        void UpdateSpellDefaultLabel(int id)
        {
            int spell;
            if (!heroSpell.TryGetValue(id, out spell))
                lblSpellDefault.Text = id >= 0 ? "Start the game once with the mod to see this hero's own starting spell here." : "";
            else if (spell >= 0 && spell < Data.SpellNames.Length && Data.SpellNames[spell] != null)
                lblSpellDefault.Text = "Own starting spell: " + Data.SpellNames[spell] + " (checked by default). Uncheck it to replace it or start without one.";
            else
                lblSpellDefault.Text = "This hero has no starting spell of its own.";
        }

        List<int[]> DefaultArmy(int id)
        {
            List<int[]> army;
            return heroArmy.TryGetValue(id, out army) ? army : new List<int[]>();
        }

        void ShowArmy(List<int[]> army)
        {
            bool wasLoading = loading;
            loading = true;
            for (int i = 0; i < ArmyRows; i++)
            {
                bool has = i < army.Count;
                armyPick[i] = has ? army[i][0] : -1;
                SelectSearchable(cboArmy[i], armyPick[i], armyItems);
                numArmyMin[i].Value = has ? Math.Max(1, Math.Min(9999, army[i][1])) : 1;
                numArmyMax[i].Value = has ? Math.Max(1, Math.Min(9999, army[i][2])) : 1;
            }
            loading = wasLoading;
        }

        void UpdateArmyEnabled()
        {
            for (int i = 0; i < ArmyRows; i++)
                cboArmy[i].Enabled = numArmyMin[i].Enabled = numArmyMax[i].Enabled = chkArmy.Checked;
        }

        void UpdateArmyDefaultLabel(int id)
        {
            var army = DefaultArmy(id);
            if (army.Count == 0)
                lblArmyDefault.Text = id >= 0 ? "Start the game once with the mod to see this hero's default army here." : "";
            else
                lblArmyDefault.Text = "Default army: " + string.Join(", ", army.Select(a => CreatureLabel(a[0]) + " " + a[1] + "-" + a[2])) +
                    ". Custom army: every slot you fill, the count picked at random between the two numbers. Applies to heroes in a NEW game (an army set by the map is kept).";
        }

        string CreatureLabel(int id)
        {
            string name;
            return creatureNames.TryGetValue(id, out name) ? name : "#" + id;
        }

        void UpdateIconControls()
        {
            cboIconSkill.Visible = cboIconMode.SelectedIndex == HeroEntry.IconSkillMode;
            cboIconHero.Visible = cboIconMode.SelectedIndex == HeroEntry.IconHeroMode;
        }

        void UpdateOwnSpecLabel(int id)
        {
            string spec;
            lblOwnSpec.Text = id >= 0 && specTexts.TryGetValue(id, out spec) ? "(" + spec + ")" : "";
        }

        void DeleteEntry()
        {
            int idx = lstEntries.SelectedIndex;
            if (idx < 0) return;
            // No "Are you sure?": remove now, offer Undo for 10 seconds (nothing is lost before Save anyway).
            ClearError();
            removedEntry = entries[idx];
            removedIndex = idx;
            entries.RemoveAt(idx);
            RefreshList(idx);
            UpdateDirty();
            undoTimer.Stop();
            undoTimer.Start();
            ShowStatus();
        }

        // ---------- files ----------

        void LoadHeroNames()
        {
            string path = GameFile("game_heroes.txt", "lista_bohaterow.txt");
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.Default))
            {
                // "id;name" or "id;name;class id;class name;town id"
                var parts = line.Split(';');
                int id, town;
                if (parts.Length < 2 || !int.TryParse(parts[0], out id)) continue;
                heroNames[id] = parts[1].Trim();
                if (parts.Length >= 5)
                {
                    heroClasses[id] = parts[3].Trim();
                    if (int.TryParse(parts[4], out town)) heroTowns[id] = town;
                }
                if (parts.Length >= 6)
                {
                    var s = parts[5].Split('/');
                    int[] st = new int[4];
                    if (s.Length == 4 && Enumerable.Range(0, 4).All(i => int.TryParse(s[i], out st[i]))) heroStats[id] = st;
                }
                if (parts.Length >= 7)
                {
                    // Large portraits are HPL*.pcx; take that one, else the first file.
                    var files = parts[6].Split('|').Where(p => p.Trim().Length > 0).ToArray();
                    string large = files.FirstOrDefault(p => p.StartsWith("HPL", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
                    if (large != null) heroPortraits[id] = large;
                }
                int spell;
                if (parts.Length >= 8 && int.TryParse(parts[7], out spell)) heroSpell[id] = spell;
                if (parts.Length >= 10)
                {
                    // "creature:low-high|..." (3 stacks)
                    var army = new List<int[]>();
                    foreach (var stack in parts[9].Split('|'))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(stack, @"^\s*(\d+):(\d+)-(\d+)\s*$");
                        int c, lo, hi;
                        if (m.Success && int.TryParse(m.Groups[1].Value, out c) && int.TryParse(m.Groups[2].Value, out lo) &&
                            int.TryParse(m.Groups[3].Value, out hi) && lo > 0)
                            army.Add(new[] { c, lo, Math.Max(lo, hi) });
                    }
                    if (army.Count > 0) heroArmy[id] = army;
                }
            }

            // "id;plural name;town;level;upgrade id". A creature and its upgrade share one entry
            // ("Jotunns / Jotunn Warlords"), keyed by the base creature, as the specialty covers both.
            string crPath = GameFile("game_creatures.txt", "lista_stworow.txt");
            if (File.Exists(crPath))
            {
                var rows = new List<string[]>();
                foreach (var line in File.ReadAllLines(crPath, Encoding.Default))
                {
                    var parts = line.Split(';');
                    int id;
                    if (parts.Length >= 4 && int.TryParse(parts[0], out id)) rows.Add(parts);
                }
                var names = rows.ToDictionary(p => int.Parse(p[0]), p => p[1].Trim());
                var townOf = rows.ToDictionary(p => int.Parse(p[0]), p => { int t; int.TryParse(p[2], out t); return t; });
                var levelOf = rows.ToDictionary(p => int.Parse(p[0]), p => { int l; int.TryParse(p[3], out l); return l; });
                var baseToUpgrade = new Dictionary<int, int>();
                foreach (var p in rows)
                {
                    int id = int.Parse(p[0]), upgrade = -1;
                    if (p.Length >= 5) int.TryParse(p[4], out upgrade);
                    if (upgrade >= 0 && upgrade != id && names.ContainsKey(upgrade)) baseToUpgrade[id] = upgrade;
                }
                // Without the game's upgrade data: town creatures come in pairs of the same town and
                // level in id order (base, upgrade), so pair them within each such run.
                if (baseToUpgrade.Count == 0)
                {
                    var ids = names.Keys.OrderBy(i => i).ToList();
                    for (int k = 0; k < ids.Count; k++)
                    {
                        int id = ids[k];
                        if (townOf[id] < 0) continue;
                        int start = k;
                        while (start > 0 && townOf[ids[start - 1]] == townOf[id] && levelOf[ids[start - 1]] == levelOf[id]) start--;
                        bool first = (k - start) % 2 == 0;
                        if (first && k + 1 < ids.Count && townOf[ids[k + 1]] == townOf[id] && levelOf[ids[k + 1]] == levelOf[id])
                            baseToUpgrade[id] = ids[k + 1];
                    }
                }
                foreach (var kv in baseToUpgrade) upgradeToBase[kv.Value] = kv.Key;
                // The army list has every creature on its own (a stack is one exact creature).
                foreach (var p in rows)
                {
                    int id = int.Parse(p[0]);
                    creatureNames[id] = names[id];
                    string where = townOf[id] >= 0 && townOf[id] < TownNames.Length ? TownNames[townOf[id]] + ", level " + (levelOf[id] + 1) : "neutral";
                    armyItems.Add(new SpecItem(id, names[id], where));
                }
                foreach (var p in rows)
                {
                    int id = int.Parse(p[0]), town, level, upgrade;
                    if (upgradeToBase.ContainsKey(id)) continue; // listed with its base creature
                    int.TryParse(p[2], out town);
                    int.TryParse(p[3], out level);
                    string label = names[id];
                    if (baseToUpgrade.TryGetValue(id, out upgrade)) label += " / " + names[upgrade];
                    string where = town >= 0 && town < TownNames.Length ? TownNames[town] + ", level " + (level + 1) : "neutral";
                    creatureItems.Add(new SpecItem(id, label, where));
                }
            }

            // "id;type;param;short text;description"
            string specPath = GameFile("game_specialties.txt", "lista_specjalizacji.txt");
            if (!File.Exists(specPath)) return;
            foreach (var line in File.ReadAllLines(specPath, Encoding.Default))
            {
                var parts = line.Split(';');
                int id;
                if (parts.Length >= 4 && int.TryParse(parts[0], out id)) specTexts[id] = parts[3].Trim();
            }
        }

        // Keys and values of the config before 1.3 (WlasniBohaterowie\bohaterowie.ini, Polish names),
        // so an old file can be imported.
        static readonly Dictionary<string, string> LegacyKeys = new Dictionary<string, string> {
            { "imie", "name" }, { "szkola", "school" }, { "umiejetnosci", "skills" }, { "czary", "spells" },
            { "statystyki", "stats" }, { "armia", "army" }, { "specjalizacja", "specialty" },
            { "specjalizacja_typ", "specialty_type" }, { "stwor", "creature" }, { "stwor_atak", "creature_attack" },
            { "stwor_obrona", "creature_defense" }, { "stwor_obrazenia", "creature_damage" }, { "stwor_szybkosc", "creature_speed" },
            { "spec_czar", "specialty_spell" }, { "spec_umiejetnosc", "specialty_skill" }, { "spec_surowiec", "specialty_resource" },
            { "ksiega", "spellbook" }, { "nazwa_specjalizacji", "specialty_name" }, { "opis_specjalizacji", "specialty_description" },
            { "ikona", "icon" } };
        static readonly Dictionary<string, string> LegacyValues = new Dictionary<string, string> {
            { "brak", "none" }, { "wlasna", "own" }, { "tak", "yes" }, { "nie", "no" }, { "implozja", "18" },
            { "umiejetnosc", "skill" }, { "stwor_poziom", "creature_level" }, { "surowiec", "resource" },
            { "czar", "spell" }, { "stwor", "creature" }, { "szybkosc", "speed" } };
        static readonly string[] LegacySkillKeys = {
            "znajomoscterenu", "lucznictwo", "logistyka", "zwiad", "dyplomacja", "nawigacja",
            "dowodzenie", "madrosc", "mistycyzm", "szczescie", "balistyka", "sokoleoko",
            "nekromancja", "zarzadzanie", "magiaognia", "magiapowietrza", "magiawody", "magiaziemi",
            "uczonosc", "taktyka", "artyleria", "nauka", "atak", "obrona",
            "inteligencja", "czarnoksiestwo", "odpornosc", "pierwszapomoc" };
        static readonly HashSet<string> KeysWithWords = new HashSet<string> { "specialty", "specialty_type", "spellbook", "icon", "spells" };

        // Lower case without spaces and underscores, as the mod compares names.
        static string Norm(string s) { return s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", ""); }

        static int FindSkill(string name)
        {
            string n = Norm(name);
            int i = Array.IndexOf(Data.SkillKeys, n);
            return i >= 0 ? i : Array.IndexOf(LegacySkillKeys, n);
        }

        // A file the mod writes in the game: the current name, else the name an older version used
        // (here or in the old WlasniBohaterowie folder) until the game has been started once.
        string GameFile(string name, string legacy)
        {
            string current = Path.Combine(dir, name);
            if (File.Exists(current)) return current;
            foreach (var folder in new[] { dir, LegacyDir })
            {
                string old = Path.Combine(folder, legacy);
                if (File.Exists(old)) return old;
            }
            return current;
        }

        string LegacyDir { get { return Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\')), "WlasniBohaterowie"); } }
        string importedFrom;

        void LoadIni()
        {
            string path = iniPath;
            if (!File.Exists(path))
            {
                // First start after updating from an older version: import its config and portraits.
                foreach (var old in new[] { Path.Combine(dir, "bohaterowie.ini"), Path.Combine(LegacyDir, "bohaterowie.ini") })
                    if (File.Exists(old)) { path = importedFrom = old; break; }
                if (importedFrom == null) return;
                try
                {
                    string oldPortraits = Path.Combine(Path.GetDirectoryName(importedFrom), "portrety_wlasne");
                    string newPortraits = Path.Combine(dir, "custom_portraits");
                    if (Directory.Exists(oldPortraits))
                    {
                        Directory.CreateDirectory(newPortraits);
                        foreach (var file in Directory.GetFiles(oldPortraits))
                        {
                            string target = Path.Combine(newPortraits, Path.GetFileName(file));
                            if (!File.Exists(target)) File.Copy(file, target);
                        }
                    }
                }
                catch (IOException) { }
            }
            HeroEntry cur = null;
            var specKey = new HashSet<HeroEntry>();
            foreach (var raw in File.ReadAllLines(path, Encoding.Default))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("["))
                {
                    bool hero = line.StartsWith("[Hero", StringComparison.OrdinalIgnoreCase) || line.StartsWith("[Bohater", StringComparison.OrdinalIgnoreCase);
                    cur = hero ? new HeroEntry() : null;
                    if (cur != null) entries.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                int semi = val.IndexOf(';');
                if (semi >= 0) val = val.Substring(0, semi).Trim();
                string mapped;
                if (LegacyKeys.TryGetValue(key, out mapped)) key = mapped;
                if (KeysWithWords.Contains(key) && LegacyValues.TryGetValue(val.ToLowerInvariant(), out mapped)) val = mapped;
                switch (key)
                {
                    case "id": int.TryParse(val, out cur.Id); break;
                    case "bonus": int.TryParse(val, out cur.Bonus); break;
                    case "school":
                        string v = val.ToLowerInvariant();
                        if (v.Contains("earth") || v.Contains("ziemi")) cur.Schools |= 8;
                        if (v.Contains("fire") || v.Contains("ogie") || v.Contains("ognia")) cur.Schools |= 2;
                        if (v.Contains("water") || v.Contains("wod")) cur.Schools |= 4;
                        if (v.Contains("air") || v.Contains("powietrz")) cur.Schools |= 1;
                        if (v.Contains("all") || v.Contains("wszystk")) cur.Schools |= 15;
                        break;
                    case "skills":
                        foreach (var tok in val.Split(','))
                        {
                            var p = tok.Split(':');
                            int skill = FindSkill(p[0]);
                            int level = 3;
                            if (p.Length > 1) int.TryParse(p[1].Trim(), out level);
                            if (skill >= 0 && level >= 1 && level <= 3) cur.Skills.Add(new KeyValuePair<int, int>(skill, level));
                        }
                        break;
                    case "army":
                        cur.Army = new List<int[]>();
                        foreach (var tok in val.Split(','))
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(tok, @"^\s*(\d+)\s*:\s*(\d+)\s*(?:-\s*(\d+))?\s*$");
                            if (!m.Success) continue;
                            int c = int.Parse(m.Groups[1].Value), lo = int.Parse(m.Groups[2].Value);
                            int hi = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : lo;
                            if (lo > 0 && cur.Army.Count < ArmyRows) cur.Army.Add(new[] { c, lo, Math.Max(lo, hi) });
                        }
                        break;
                    case "spells":
                        cur.SpellsSet = true;
                        foreach (var tok in val.Split(',', ' '))
                        {
                            string t = tok.Trim().ToLowerInvariant();
                            int spell;
                            if (t == "implosion" || t == "implozja") cur.Spells.Add(18);
                            else if (int.TryParse(t, out spell) && spell >= 0 && spell < 70) cur.Spells.Add(spell);
                        }
                        break;
                    case "specialty":
                        string sv = val.ToLowerInvariant();
                        int from;
                        if (sv == "none") cur.SpecMode = HeroEntry.SpecNone;
                        else if (int.TryParse(sv, out from)) { cur.SpecMode = HeroEntry.SpecCopy; cur.SpecFrom = from; }
                        else cur.SpecMode = HeroEntry.SpecOwn;
                        specKey.Add(cur);
                        break;
                    case "icon":
                        int iconHero;
                        if (val.ToLowerInvariant() == "own") cur.IconMode = HeroEntry.IconOwn;
                        else if (int.TryParse(val, out iconHero)) { cur.IconMode = HeroEntry.IconHeroMode; cur.IconHero = iconHero; }
                        else if (FindSkill(val) >= 0) { cur.IconMode = HeroEntry.IconSkillMode; cur.IconSkill = FindSkill(val); }
                        break;
                    case "name": cur.Name = val; break;
                    case "creature": int.TryParse(val, out cur.Creature); cur.SpecMode = HeroEntry.SpecBuilt; specKey.Add(cur); break;
                    case "specialty_type":
                        int kindType = Array.FindIndex(KindKeys, k => Norm(k) == Norm(val));
                        if (kindType >= 0) { cur.BuildKind = kindType; cur.SpecMode = HeroEntry.SpecBuilt; specKey.Add(cur); }
                        break;
                    case "specialty_spell": int.TryParse(val, out cur.BuildSpell); break;
                    case "specialty_skill": cur.BuildSkill = FindSkill(val); break;
                    case "specialty_resource": int.TryParse(val, out cur.BuildResource); break;
                    case "spellbook": cur.StartBook = val.ToLowerInvariant() == "yes" ? 1 : val.ToLowerInvariant() == "no" ? 0 : -1; break;
                    case "creature_attack": int.TryParse(val, out cur.CreatureBonus[0]); break;
                    case "creature_defense": int.TryParse(val, out cur.CreatureBonus[1]); break;
                    case "creature_damage": int.TryParse(val, out cur.CreatureBonus[2]); break;
                    case "creature_speed": int.TryParse(val, out cur.CreatureBonus[3]); break;
                    case "specialty_name": cur.SpecName = val; break;
                    case "specialty_description": cur.SpecDesc = val; break;
                    case "stats":
                        var s = val.Split('/');
                        int[] st = new int[4];
                        if (s.Length == 4 && Enumerable.Range(0, 4).All(i => int.TryParse(s[i].Trim(), out st[i]))) cur.Stats = st;
                        break;
                }
            }
            // Same rule as the mod: a school bonus without a "specialty" key replaces the own specialty.
            foreach (var e in entries)
                if (!specKey.Contains(e))
                    e.SpecMode = e.Schools != 0 && e.Bonus != 0 ? HeroEntry.SpecNone : HeroEntry.SpecOwn;
        }

        static int BuildParam(HeroEntry e)
        {
            switch (e.BuildKind)
            {
                case 0: return e.BuildSkill;
                case 1: case 4: return e.Creature;
                case 2: return e.BuildResource;
                case 3: return e.BuildSpell;
                default: return 0; // speed for all: nothing to pick
            }
        }

        bool SaveIni()
        {
            OnEdit();
            // Problems are shown at the field that needs fixing, not in a dialog.
            var noHero = entries.FirstOrDefault(e => e.Id < 0);
            if (noHero != null)
            {
                ShowError(noHero, -1, cboHero, "Not saved: pick which hero this entry changes (or Remove it).");
                return false;
            }
            var dup = entries.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                ShowError(dup.Last(), -1, cboHero, "Not saved: " + HeroName(dup.Key) + " is on the list twice. Pick another hero here, or Remove this entry.");
                return false;
            }
            var noParam = entries.FirstOrDefault(e => e.SpecMode == HeroEntry.SpecBuilt && BuildParam(e) < 0);
            if (noParam != null)
            {
                string what = noParam.BuildKind == 3 ? "spell" : noParam.BuildKind == 0 ? "skill" : noParam.BuildKind == 2 ? "resource" : "creature";
                Control field = noParam.BuildKind == 3 ? cboBuildSpell : noParam.BuildKind == 0 ? cboBuildSkill : noParam.BuildKind == 2 ? (Control)cboBuildRes : cboCreature;
                ShowError(noParam, 0, field, "Not saved: pick the " + what + " this new specialty applies to.");
                return false;
            }
            var noSource = entries.FirstOrDefault(e => e.SpecMode == HeroEntry.SpecCopy && e.SpecFrom < 0);
            if (noSource != null)
            {
                ShowError(noSource, 0, cboSpecFrom, "Not saved: pick the hero whose specialty " + HeroName(noSource.Id) + " should copy.");
                return false;
            }

            var sb = new StringBuilder();
            sb.AppendLine("; HeroForge - hero configuration");
            sb.AppendLine("; Written by the HeroForge editor (HeroForge.exe) - easiest to edit it there.");
            sb.AppendLine(";");
            sb.AppendLine("; Keys: id, name (new name, max 12), school (earth / fire / water / air), bonus (%),");
            sb.AppendLine(";   skills (skill:level 1-3), spells (spell numbers, none = no starting spell), stats (A/D/P/K),");
            sb.AppendLine(";   army (starting army: creature:count or creature:min-max, up to 7, comma separated),");
            sb.AppendLine(";   specialty (own / none / hero number to copy from),");
            sb.AppendLine(";   specialty_type (creature / creature_level / spell / skill / resource / speed) with");
            sb.AppendLine(";   creature (creature number) + creature_attack, creature_defense, creature_damage, creature_speed,");
            sb.AppendLine(";   specialty_spell (spell number), specialty_skill (skill), specialty_resource (0-6, 6 = gold),");
            sb.AppendLine(";   spellbook (yes / no: spell book at start),");
            sb.AppendLine(";   specialty_name, specialty_description (in-game specialty name and description),");
            sb.AppendLine(";   icon (own / skill name = skill icon / hero number = that hero's icon; absent = automatic).");
            sb.AppendLine("; Changes take effect after restarting the game (skills/spells/stats: in a NEW game).");
            int n = 1;
            foreach (var e in entries)
            {
                sb.AppendLine();
                sb.AppendLine("[Hero" + n++ + "]");
                string name;
                if (heroNames.TryGetValue(e.Id, out name)) sb.AppendLine("; " + name);
                sb.AppendLine("id = " + e.Id);
                string newName = StripDiacritics(e.Name);
                if (newName.Length > 12) newName = newName.Substring(0, 12);
                if (newName.Length > 0) sb.AppendLine("name = " + newName);
                if (e.SpecMode == HeroEntry.SpecBuilt)
                {
                    sb.AppendLine("specialty_type = " + KindKeys[e.BuildKind]);
                    if (e.BuildKind == 4 || e.BuildKind == 1)
                    {
                        var cr = creatureItems.FirstOrDefault(i => i.Id == e.Creature);
                        sb.AppendLine("creature = " + e.Creature + (cr != null ? "   ; " + cr : ""));
                    }
                    if (e.BuildKind == 4)
                    {
                        string[] keys = { "creature_attack", "creature_defense", "creature_damage", "creature_speed" };
                        for (int i = 0; i < 4; i++)
                            if (e.CreatureBonus[i] != 0) sb.AppendLine(keys[i] + " = " + e.CreatureBonus[i]);
                    }
                    if (e.BuildKind == 3)
                        sb.AppendLine("specialty_spell = " + e.BuildSpell + "   ; " + Data.SpellNames[e.BuildSpell]);
                    if (e.BuildKind == 0) sb.AppendLine("specialty_skill = " + Data.SkillNames[e.BuildSkill]);
                    if (e.BuildKind == 2)
                        sb.AppendLine("specialty_resource = " + e.BuildResource + "   ; " + ResourceNames[e.BuildResource]);
                }
                else if (e.SpecMode == HeroEntry.SpecCopy)
                {
                    string src;
                    sb.AppendLine("specialty = " + e.SpecFrom + (heroNames.TryGetValue(e.SpecFrom, out src) ? "   ; like " + src : ""));
                }
                else sb.AppendLine("specialty = " + (e.SpecMode == HeroEntry.SpecNone ? "none" : "own"));
                if (e.StartBook >= 0) sb.AppendLine("spellbook = " + (e.StartBook == 1 ? "yes" : "no"));
                string specName = StripDiacritics(e.SpecName), specDesc = StripDiacritics(e.SpecDesc);
                if (specName.Length > 0) sb.AppendLine("specialty_name = " + specName);
                if (specDesc.Length > 0) sb.AppendLine("specialty_description = " + specDesc);
                if (e.IconMode == HeroEntry.IconOwn) sb.AppendLine("icon = own");
                else if (e.IconMode == HeroEntry.IconSkillMode && e.IconSkill >= 0) sb.AppendLine("icon = " + Data.SkillNames[e.IconSkill]);
                else if (e.IconMode == HeroEntry.IconHeroMode && e.IconHero >= 0)
                {
                    string iconSrc;
                    sb.AppendLine("icon = " + e.IconHero + (heroNames.TryGetValue(e.IconHero, out iconSrc) ? "   ; like " + iconSrc : ""));
                }
                if (e.Schools != 0 && e.Bonus != 0)
                {
                    var schools = new List<string>();
                    if ((e.Schools & 8) != 0) schools.Add("earth");
                    if ((e.Schools & 2) != 0) schools.Add("fire");
                    if ((e.Schools & 4) != 0) schools.Add("water");
                    if ((e.Schools & 1) != 0) schools.Add("air");
                    sb.AppendLine("school = " + string.Join(" ", schools));
                    sb.AppendLine("bonus = " + e.Bonus);
                }
                if (e.Skills.Count > 0)
                    sb.AppendLine("skills = " + string.Join(", ", e.Skills.Select(k => Data.SkillNames[k.Key] + ":" + k.Value)));
                if (e.SpellsSet)
                    sb.AppendLine("spells = " + (e.Spells.Count > 0 ? string.Join(", ", e.Spells.OrderBy(x => x)) +
                        "   ; " + string.Join(", ", e.Spells.OrderBy(x => x).Select(x => Data.SpellNames[x])) : "none"));
                if (e.Army != null && e.Army.Count > 0)
                    sb.AppendLine("army = " + string.Join(", ", e.Army.Select(a => a[0] + ":" + (a[1] == a[2] ? a[1].ToString() : a[1] + "-" + a[2]))) +
                        "   ; " + string.Join(", ", e.Army.Select(a => CreatureLabel(a[0]))));
                if (e.Stats != null)
                    sb.AppendLine("stats = " + string.Join("/", e.Stats));
            }
            try
            {
                if (File.Exists(iniPath)) File.Copy(iniPath, iniPath + ".bak", true);
                File.WriteAllText(iniPath, sb.ToString(), Encoding.ASCII);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save heroes.ini:\n" + ex.Message +
                    "\n\nYour changes are still here in the editor. Check that the HeroForge folder is not read-only, then Save again.",
                    "Not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            // Quiet confirmation in the sidebar instead of a dialog.
            savedSnapshot = Snapshot();
            savedNote = "Saved " + DateTime.Now.ToString("HH:mm") + " · restart the game";
            UpdateDirty();
            ShowStatus();
            return true;
        }

        [STAThread]
        void TestState(string state)
        {
            switch (state)
            {
                case "add": AddHero(); break;
                case "error": AddHero(); SaveIni(); break;
                case "error-spec": rbSpecCreature.Checked = true; cboKind.SelectedIndex = 2; cboBuildSpell.SelectedIndex = -1; Current.BuildSpell = -1; SaveIni(); break;
                case "undo": DeleteEntry(); break;
                case "search-none": lstNav.SelectedIndex = 2; txtSpellSearch.Text = "zzz"; break;
                case "search-fire": lstNav.SelectedIndex = 2; spellSchoolFilter = 2; StyleSchoolButtons(); FillSpellList(); break;
                case "small": Size = MinimumSize; lstNav.SelectedIndex = 0; break;
                case "dirty": txtName.Text = "Test"; break;
                case "copy": lstNav.SelectedIndex = 0; rbSpecCopy.Checked = true; break;
            }
        }

        static void Main(string[] args)
        {
            // Test helper: HeroForge.exe --dump-pcx <data dir> <name.pcx> <out.png>
            if (args.Length == 4 && args[0] == "--dump-pcx")
            {
                GameImages.Open(args[1]);
                var bmp = GameImages.LoadPcx(args[2]);
                if (bmp != null) bmp.Save(args[3], System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
            // Sharp text on scaled displays (125%, 150%...) instead of a stretched, blurry window.
            try { SetProcessDPIAware(); } catch (Exception) { }
            // Test helper: --scale 1.5 before the other test options pretends a 150% display.
            if (args.Length >= 2 && args[0] == "--scale")
            {
                float scale;
                if (float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale)) TestScale = scale;
                args = args.Skip(2).ToArray();
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Test helper: HeroForge.exe --state <out.png> <state> [entry] saves a picture of one
            // situation (empty search, a new entry, the smallest window...). Nothing is saved.
            if ((args.Length == 3 || args.Length == 4) && args[0] == "--state")
            {
                var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false };
                form.Show();
                Application.DoEvents();
                int entry;
                if (args.Length == 4 && int.TryParse(args[3], out entry) && entry < form.entries.Count) form.lstEntries.SelectedIndex = entry;
                form.TestState(args[2]);
                Application.DoEvents();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                }
                form.dirty = false;
                form.Close();
                return;
            }
            // Test helper: HeroForge.exe --shots <out dir> [entry] saves a picture of every section
            // (nothing is saved to heroes.ini).
            if ((args.Length == 2 || args.Length == 3) && args[0] == "--shots")
            {
                var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false };
                form.Show();
                Application.DoEvents();
                int entry;
                if (args.Length == 3 && int.TryParse(args[2], out entry) && entry < form.entries.Count) form.lstEntries.SelectedIndex = entry;
                for (int i = 0; i < form.lstNav.Items.Count; i++)
                {
                    form.lstNav.SelectedIndex = i;
                    Application.DoEvents();
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(Path.Combine(args[1], "section" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                form.dirty = false;
                form.Close();
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
