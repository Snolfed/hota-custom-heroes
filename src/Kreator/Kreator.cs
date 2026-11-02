// Kreator bohaterów - GUI editor for bohaterowie.ini (WlasniBohaterowie mod).
// Lives next to the ini in the game's WlasniBohaterowie folder. Hero names come from
// lista_bohaterow.txt, which the mod writes when a game is started.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Kreator
{
    class HeroEntry
    {
        public int Id = -1;
        public int Schools;          // 1 air, 2 fire, 4 water, 8 earth (as in the mod)
        public int Bonus;
        public List<KeyValuePair<int, int>> Skills = new List<KeyValuePair<int, int>>(); // skill id, level 1..3
        public HashSet<int> Spells = new HashSet<int>();
        public int[] Stats;          // null = keep game defaults
        public int SpecMode;         // SpecOwn, SpecNone or SpecCopy
        public int SpecFrom = -1;    // hero id to copy from (SpecCopy)
        public string Name = "";     // new hero name, empty = keep
        public string SpecName = "";
        public string SpecDesc = "";

        public const int SpecOwn = 0, SpecNone = 1, SpecCopy = 2;

        // "ikona" key: absent = automatic, "wlasna" = own, skill key = skill icon, number = that hero's icon.
        public int IconMode;
        public int IconSkill = 17;
        public int IconHero = -1;
        public const int IconAuto = 0, IconOwn = 1, IconSkillMode = 2, IconHeroMode = 3;
    }

    static class Data
    {
        // Must match SKILL_NAMES in WlasniBohaterowie.cpp (ini keys) - same order as game ids.
        public static readonly string[] SkillKeys = {
            "znajomoscterenu", "lucznictwo", "logistyka", "zwiad", "dyplomacja", "nawigacja",
            "dowodzenie", "madrosc", "mistycyzm", "szczescie", "balistyka", "sokoleoko",
            "nekromancja", "zarzadzanie", "magiaognia", "magiapowietrza", "magiawody", "magiaziemi",
            "uczonosc", "taktyka", "artyleria", "nauka", "atak", "obrona",
            "inteligencja", "czarnoksiestwo", "odpornosc", "pierwszapomoc" };

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
        PictureBox picPortrait;
        Label portraitNote;
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
        void SelectSearchable(ComboBox box, int id)
        {
            box.Items.Clear();
            box.Items.AddRange(specItems.ToArray());
            box.SelectedIndex = specItems.FindIndex(i => i.Id == id);
            if (box.SelectedIndex < 0) box.Text = "";
        }

        void UpdatePortrait(int id)
        {
            string pcx;
            var old = picPortrait.Image;
            Bitmap img = null;
            // A custom portrait wins; then original heroes straight from the game files.
            string custom = Path.Combine(dir, "portrety_wlasne", id + "_L.bmp");
            if (id >= 0 && File.Exists(custom))
                using (var fromFile = new Bitmap(custom)) img = new Bitmap(fromFile);
            else
            // Original heroes: straight from the game files. HotA heroes: portraits the mod
            // exported from the running game (most of them; the rest have no preview).
            if (id >= 0 && heroPortraits.TryGetValue(id, out pcx))
                img = GameImages.LoadPcx(pcx) ?? GameImages.LoadPcx(pcx + ".pcx");
            string exported = Path.Combine(dir, "portrety", id + ".bmp");
            if (img == null && id >= 0 && File.Exists(exported))
                using (var fromFile = new Bitmap(exported)) img = new Bitmap(fromFile);
            picPortrait.Image = img;
            portraitNote.Visible = id >= 0 && img == null;
            if (old != null) old.Dispose();
        }

        // Custom portrait: any picture, cropped to the portrait's aspect ratio and scaled to the
        // game's large (58x64) and small (48x32) portrait sizes, saved as 24-bit BMPs for the mod.
        void ChooseCustomPortrait()
        {
            var e = Current;
            if (e == null || e.Id < 0) return;
            using (var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*", Title = "Choose a portrait for " + HeroLabel(e.Id) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string folder = Path.Combine(dir, "portrety_wlasne");
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
            if (!File.Exists(Path.Combine(dir, "portrety_mapa.txt")))
                MessageBox.Show(this, "Saved. To show it in the game, start a NEW game once with the mod (it maps heroes to portrait files), then restart the game.",
                    "Custom portrait", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void RemoveCustomPortrait()
        {
            var e = Current;
            if (e == null || e.Id < 0) return;
            foreach (var size in new[] { "_L.bmp", "_S.bmp" })
            {
                string f = Path.Combine(dir, "portrety_wlasne", e.Id + size);
                if (File.Exists(f)) File.Delete(f);
            }
            UpdatePortrait(e.Id);
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
        ComboBox cboHero, cboTown;
        CheckBox chkSpec, chkEarth, chkFire, chkWater, chkAir;
        RadioButton rbSpecOwn, rbSpecNone, rbSpecCopy;
        ComboBox cboSpecFrom;
        TextBox txtSpecName, txtSpecDesc, txtName;
        ComboBox cboIconMode, cboIconSkill, cboIconHero;
        Label lblOwnSpec;
        NumericUpDown numBonus;
        ComboBox[] cboSkill = new ComboBox[SkillRows];
        ComboBox[] cboLevel = new ComboBox[SkillRows];
        CheckedListBox lstSpells;
        CheckBox chkStats;
        NumericUpDown[] numStats = new NumericUpDown[4];
        Panel editor;

        public MainForm()
        {
            dir = AppDomain.CurrentDomain.BaseDirectory;
            iniPath = Path.Combine(dir, "bohaterowie.ini");
            Text = "Hero Creator - Custom Heroes (HotA)";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(900, 876);
            MinimumSize = new Size(916, 915);
            StartPosition = FormStartPosition.CenterScreen;

            LoadHeroNames();
            string gameDir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            GameImages.Open(Path.Combine(gameDir, "Data"));
            foreach (var kv in GameImages.ReadExePortraits(Path.Combine(gameDir, "h3hota.exe")))
                if (!heroPortraits.ContainsKey(kv.Key)) heroPortraits[kv.Key] = kv.Value;
            BuildUi();
            LoadIni();
            RefreshList(0);
        }

        // ---------- UI ----------

        void BuildUi()
        {
            var left = new Panel { Dock = DockStyle.Left, Width = 230, Padding = new Padding(10) };
            var lblList = new Label { Text = "Your heroes:", Dock = DockStyle.Top, Height = 22 };
            lstEntries = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            lstEntries.SelectedIndexChanged += (s, e) => ShowEntry();
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, FlowDirection = FlowDirection.LeftToRight };
            var btnAdd = new Button { Text = "Add", Width = 98, Height = 28 };
            var btnDel = new Button { Text = "Remove", Width = 98, Height = 28 };
            var btnSave = new Button { Text = "Save", Width = 202, Height = 34, Font = new Font(Font, FontStyle.Bold) };
            btnAdd.Click += (s, e) => { entries.Add(new HeroEntry()); RefreshList(entries.Count - 1); };
            btnDel.Click += (s, e) => DeleteEntry();
            btnSave.Click += (s, e) => SaveIni();
            btns.Controls.AddRange(new Control[] { btnAdd, btnDel, btnSave });
            left.Controls.Add(lstEntries);
            left.Controls.Add(lblList);
            left.Controls.Add(btns);

            editor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            int y = 10;

            // Town filter + hero
            editor.Controls.Add(new Label { Text = "Town:", Location = new Point(10, y + 3), AutoSize = true });
            cboTown = new ComboBox { Location = new Point(110, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cboTown.Items.Add(new TownItem(-1));
            foreach (int town in heroTowns.Values.Where(t => t >= 0).Distinct().OrderBy(t => t)) cboTown.Items.Add(new TownItem(town));
            cboTown.SelectedIndex = 0;
            cboTown.SelectedIndexChanged += (s, e) => FillHeroCombo();
            editor.Controls.Add(cboTown);
            y += 32;

            editor.Controls.Add(new Label { Text = "Hero:", Location = new Point(10, y + 3), AutoSize = true });
            cboHero = new ComboBox { Location = new Point(110, y), Width = 440, DropDownStyle = ComboBoxStyle.DropDown, MaxDropDownItems = 20 };
            FillHeroCombo();
            cboHero.TextChanged += (s, e) => OnEdit();
            editor.Controls.Add(cboHero);
            picPortrait = new PictureBox { Location = new Point(566, 6), Size = new Size(58, 64), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.CenterImage };
            portraitNote = new Label { Text = "no preview", Location = new Point(567, 30), Size = new Size(56, 16), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Visible = false };
            editor.Controls.Add(portraitNote);
            editor.Controls.Add(picPortrait);
            var btnPortrait = new Button { Text = "Custom...", Location = new Point(540, 73), Size = new Size(58, 23) };
            var btnPortraitReset = new Button { Text = "Reset", Location = new Point(600, 73), Size = new Size(48, 23) };
            btnPortrait.Click += (s, e) => ChooseCustomPortrait();
            btnPortraitReset.Click += (s, e) => RemoveCustomPortrait();
            editor.Controls.Add(btnPortrait);
            editor.Controls.Add(btnPortraitReset);
            editor.Controls.Add(new Label { Text = "New name:", Location = new Point(10, 77), AutoSize = true });
            txtName = new TextBox { Location = new Point(110, 74), Width = 160, MaxLength = 12 };
            txtName.TextChanged += (s, e) => OnEdit();
            editor.Controls.Add(txtName);
            editor.Controls.Add(new Label { Text = "(max 12 letters, empty = keep)", Location = new Point(276, 77), AutoSize = true, ForeColor = Color.DimGray });
            if (heroNames.Count == 0)
                editor.Controls.Add(new Label
                {
                    Text = "No hero list yet: start the game once with the mod, then reopen the creator. For now type the hero number (id).",
                    Location = new Point(380, y - 36), Size = new Size(270, 44), ForeColor = Color.DarkRed
                });
            y += 64; // room for the portrait buttons

            // Specialty
            var grpSpec = new GroupBox { Text = "Specialty", Location = new Point(10, y), Size = new Size(630, 250) };
            rbSpecOwn = new RadioButton { Text = "Hero's original specialty", Location = new Point(12, 22), AutoSize = true };
            lblOwnSpec = new Label { Location = new Point(250, 24), Size = new Size(370, 18), ForeColor = Color.DimGray };
            rbSpecCopy = new RadioButton { Text = "Same specialty as:", Location = new Point(12, 46), AutoSize = true };
            cboSpecFrom = new ComboBox { Location = new Point(160, 44), Width = 460, MaxDropDownItems = 20 };
            specItems = heroNames.OrderBy(k => k.Value)
                .Select(kv => new SpecItem(kv.Key, kv.Value, specTexts.ContainsKey(kv.Key) ? specTexts[kv.Key] : "")).ToList();
            MakeSearchable(cboSpecFrom, specItems);
            rbSpecNone = new RadioButton { Text = "No specialty (only the bonus below)", Location = new Point(12, 70), AutoSize = true };
            chkSpec = new CheckBox { Text = "Spell damage bonus for school:", Location = new Point(12, 100), AutoSize = true };
            chkEarth = new CheckBox { Text = "Earth", Location = new Point(250, 100), AutoSize = true };
            chkFire = new CheckBox { Text = "Fire", Location = new Point(330, 100), AutoSize = true };
            chkWater = new CheckBox { Text = "Water", Location = new Point(400, 100), AutoSize = true };
            chkAir = new CheckBox { Text = "Air", Location = new Point(470, 100), AutoSize = true };
            grpSpec.Controls.Add(new Label { Text = "Bonus (%):", Location = new Point(30, 127), AutoSize = true });
            numBonus = new NumericUpDown { Location = new Point(110, 124), Width = 70, Minimum = 1, Maximum = 500, Value = 50 };
            grpSpec.Controls.Add(new Label { Text = "In-game name:", Location = new Point(12, 158), AutoSize = true });
            txtSpecName = new TextBox { Location = new Point(108, 155), Width = 140, MaxLength = 60 };
            grpSpec.Controls.Add(new Label { Text = "Description:", Location = new Point(258, 158), AutoSize = true });
            txtSpecDesc = new TextBox { Location = new Point(335, 155), Width = 285, MaxLength = 250 };
            grpSpec.Controls.Add(new Label { Text = "Icon:", Location = new Point(12, 188), AutoSize = true });
            cboIconMode = new ComboBox { Location = new Point(108, 185), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            cboIconMode.Items.AddRange(new object[] { "Automatic", "Hero's own", "Skill icon:", "Same as hero:" });
            cboIconSkill = new ComboBox { Location = new Point(258, 185), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cboIconSkill.Items.AddRange(Data.SkillNames);
            cboIconHero = new ComboBox { Location = new Point(258, 185), Width = 362, MaxDropDownItems = 20 };
            MakeSearchable(cboIconHero, specItems);
            grpSpec.Controls.Add(new Label
            {
                Text = "Name and description are optional (empty = automatic). Use plain English letters. Automatic icon: copied hero's icon, or the magic school orb.",
                Location = new Point(12, 212), Size = new Size(610, 32), ForeColor = Color.DimGray
            });
            foreach (var c in new Control[] { rbSpecOwn, lblOwnSpec, rbSpecCopy, cboSpecFrom, rbSpecNone, chkSpec, chkEarth, chkFire, chkWater, chkAir, numBonus, txtSpecName, txtSpecDesc, cboIconMode, cboIconSkill, cboIconHero })
                grpSpec.Controls.Add(c);
            cboIconMode.SelectedIndexChanged += (s, e) => { UpdateIconControls(); OnEdit(); };
            cboIconSkill.SelectedIndexChanged += (s, e) => OnEdit();
            cboIconHero.SelectedIndexChanged += (s, e) => OnEdit();
            foreach (var c in new CheckBox[] { chkSpec, chkEarth, chkFire, chkWater, chkAir }) c.CheckedChanged += (s, e) => OnEdit();
            foreach (var r in new RadioButton[] { rbSpecOwn, rbSpecCopy, rbSpecNone }) r.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) OnEdit(); };
            cboSpecFrom.SelectedIndexChanged += (s, e) => { if (!loading && cboSpecFrom.SelectedIndex >= 0) rbSpecCopy.Checked = true; OnEdit(); };
            numBonus.ValueChanged += (s, e) => OnEdit();
            txtSpecName.TextChanged += (s, e) => OnEdit();
            txtSpecDesc.TextChanged += (s, e) => OnEdit();
            editor.Controls.Add(grpSpec);
            y += 260;

            // Skills
            var grpSkills = new GroupBox { Text = "Starting skills (at least this level, max 8)", Location = new Point(10, y), Size = new Size(320, 270) };
            for (int i = 0; i < SkillRows; i++)
            {
                cboSkill[i] = new ComboBox { Location = new Point(12, 24 + i * 30), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
                cboSkill[i].Items.Add("(none)");
                cboSkill[i].Items.AddRange(Data.SkillNames);
                cboSkill[i].SelectedIndex = 0;
                cboLevel[i] = new ComboBox { Location = new Point(180, 24 + i * 30), Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
                cboLevel[i].Items.AddRange(Data.Levels);
                cboLevel[i].SelectedIndex = 2;
                cboSkill[i].SelectedIndexChanged += (s, e) => OnEdit();
                cboLevel[i].SelectedIndexChanged += (s, e) => OnEdit();
                grpSkills.Controls.Add(cboSkill[i]);
                grpSkills.Controls.Add(cboLevel[i]);
            }
            editor.Controls.Add(grpSkills);

            // Stats
            var grpStats = new GroupBox { Text = "Starting stats", Location = new Point(10, y + 280), Size = new Size(320, 90) };
            chkStats = new CheckBox { Text = "Custom stats (otherwise the hero's defaults)", Location = new Point(12, 22), AutoSize = true };
            chkStats.CheckedChanged += (s, e) => { foreach (var n in numStats) n.Enabled = chkStats.Checked; OnEdit(); };
            grpStats.Controls.Add(chkStats);
            string[] statNames = { "Attack", "Defense", "Power", "Knowledge" };
            for (int i = 0; i < 4; i++)
            {
                grpStats.Controls.Add(new Label { Text = statNames[i], Location = new Point(12 + i * 75, 48), AutoSize = true });
                numStats[i] = new NumericUpDown { Location = new Point(12 + i * 75, 62), Width = 60, Minimum = i < 2 ? 0 : 1, Maximum = 99, Value = i < 2 ? 0 : 1, Enabled = false };
                numStats[i].ValueChanged += (s, e) => OnEdit();
                grpStats.Controls.Add(numStats[i]);
            }
            editor.Controls.Add(grpStats);

            // Spells
            var grpSpells = new GroupBox { Text = "Starting spells (spell book)", Location = new Point(340, y), Size = new Size(300, 370) };
            lstSpells = new CheckedListBox { Location = new Point(10, 22), Size = new Size(280, 338), CheckOnClick = true, IntegralHeight = false };
            for (int i = 0; i < Data.SpellNames.Length; i++)
                if (Data.SpellNames[i] != null) lstSpells.Items.Add(new SpellItem(i));
            // ItemCheck fires before the check state changes, so read it after the event.
            lstSpells.ItemCheck += (s, e) => { if (!loading && IsHandleCreated) BeginInvoke((Action)OnEdit); };
            grpSpells.Controls.Add(lstSpells);
            editor.Controls.Add(grpSpells);

            var hint = new Label
            {
                Text = "Changes take effect after saving and restarting the game; skills, spells and stats apply in a NEW game.",
                Location = new Point(10, y + 380), AutoSize = true, ForeColor = Color.DimGray
            };
            editor.Controls.Add(hint);

            Controls.Add(editor);
            Controls.Add(left);
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

        string HeroLabel(int id)
        {
            string name;
            return heroNames.TryGetValue(id, out name) ? name + " (" + id + ")" : id.ToString();
        }

        // "Vehr (212) - Elder - Earth Magic" in the hero box, with class and specialty.
        string HeroLabelFull(int id)
        {
            string label = HeroLabel(id), cls, spec;
            if (heroClasses.TryGetValue(id, out cls) && cls.Length > 0) label += " - " + cls;
            if (specTexts.TryGetValue(id, out spec) && spec.Length > 0) label += " - " + spec;
            return label;
        }

        void FillHeroCombo()
        {
            int town = cboTown.SelectedItem != null ? ((TownItem)cboTown.SelectedItem).Town : -1;
            bool wasLoading = loading;
            loading = true;
            string text = cboHero.Text;
            cboHero.Items.Clear();
            foreach (var kv in heroNames.OrderBy(k => k.Value))
            {
                int t;
                if (town < 0 || (heroTowns.TryGetValue(kv.Key, out t) && t == town))
                    cboHero.Items.Add(HeroLabelFull(kv.Key));
            }
            cboHero.Text = text;
            loading = wasLoading;
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
            foreach (var e in entries) lstEntries.Items.Add(e.Id >= 0 ? HeroLabel(e.Id) : "(new - pick a hero)");
            loading = false;
            editor.Enabled = entries.Count > 0;
            if (entries.Count > 0) lstEntries.SelectedIndex = Math.Max(0, Math.Min(select, entries.Count - 1));
        }

        HeroEntry Current { get { return lstEntries.SelectedIndex >= 0 ? entries[lstEntries.SelectedIndex] : null; } }

        void ShowEntry()
        {
            var e = Current;
            if (e == null || loading) return;
            loading = true;
            // Jump the town filter to the hero's town so the hero is in the list.
            int heroTown;
            if (e.Id >= 0 && heroTowns.TryGetValue(e.Id, out heroTown) && ((TownItem)cboTown.SelectedItem).Town >= 0)
                for (int i = 0; i < cboTown.Items.Count; i++)
                    if (((TownItem)cboTown.Items[i]).Town == heroTown) cboTown.SelectedIndex = i;
            cboHero.Text = e.Id >= 0 ? HeroLabelFull(e.Id) : "";
            rbSpecOwn.Checked = e.SpecMode == HeroEntry.SpecOwn;
            rbSpecNone.Checked = e.SpecMode == HeroEntry.SpecNone;
            rbSpecCopy.Checked = e.SpecMode == HeroEntry.SpecCopy;
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
            for (int i = 0; i < lstSpells.Items.Count; i++)
                lstSpells.SetItemChecked(i, e.Spells.Contains(((SpellItem)lstSpells.Items[i]).Id));
            // Without custom stats the boxes show the hero's defaults (read only).
            chkStats.Checked = e.Stats != null;
            for (int i = 0; i < 4; i++) numStats[i].Enabled = e.Stats != null;
            ShowStats(e.Stats ?? DefaultStats(e.Id));
            loading = false;
        }

        void OnEdit()
        {
            var e = Current;
            if (e == null || loading) return;
            int previousId = e.Id;
            e.Id = ParseHeroId(cboHero.Text);
            UpdateOwnSpecLabel(e.Id);
            if (e.Id != previousId)
            {
                // New hero: show its portrait and start the stat boxes from its defaults.
                UpdatePortrait(e.Id);
                loading = true;
                ShowStats(DefaultStats(e.Id));
                loading = false;
            }
            e.SpecMode = rbSpecCopy.Checked ? HeroEntry.SpecCopy : rbSpecNone.Checked ? HeroEntry.SpecNone : HeroEntry.SpecOwn;
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
            e.Spells.Clear();
            foreach (SpellItem item in lstSpells.CheckedItems) e.Spells.Add(item.Id);
            e.Stats = chkStats.Checked ? numStats.Select(n => (int)n.Value).ToArray() : null;

            int idx = lstEntries.SelectedIndex;
            string label = e.Id >= 0 ? HeroLabel(e.Id) : "(new - pick a hero)";
            if ((string)lstEntries.Items[idx] != label)
            {
                loading = true;
                lstEntries.Items[idx] = label;
                loading = false;
            }
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
            if (MessageBox.Show(this, "Remove " + lstEntries.Items[idx] + "?", "Remove", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            entries.RemoveAt(idx);
            RefreshList(idx);
        }

        // ---------- files ----------

        void LoadHeroNames()
        {
            string path = Path.Combine(dir, "lista_bohaterow.txt");
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
            }

            // "id;type;param;short text;description"
            string specPath = Path.Combine(dir, "lista_specjalizacji.txt");
            if (!File.Exists(specPath)) return;
            foreach (var line in File.ReadAllLines(specPath, Encoding.Default))
            {
                var parts = line.Split(';');
                int id;
                if (parts.Length >= 4 && int.TryParse(parts[0], out id)) specTexts[id] = parts[3].Trim();
            }
        }

        void LoadIni()
        {
            if (!File.Exists(iniPath)) return;
            HeroEntry cur = null;
            var specKey = new HashSet<HeroEntry>();
            foreach (var raw in File.ReadAllLines(iniPath, Encoding.Default))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("["))
                {
                    cur = line.StartsWith("[Bohater", StringComparison.OrdinalIgnoreCase) ? new HeroEntry() : null;
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
                switch (key)
                {
                    case "id": int.TryParse(val, out cur.Id); break;
                    case "bonus": int.TryParse(val, out cur.Bonus); break;
                    case "szkola":
                        string v = val.ToLowerInvariant();
                        if (v.Contains("ziemi")) cur.Schools |= 8;
                        if (v.Contains("ogie") || v.Contains("ognia")) cur.Schools |= 2;
                        if (v.Contains("wod")) cur.Schools |= 4;
                        if (v.Contains("powietrz")) cur.Schools |= 1;
                        if (v.Contains("wszystk")) cur.Schools |= 15;
                        break;
                    case "umiejetnosci":
                        foreach (var tok in val.Split(',', ';'))
                        {
                            var p = tok.Split(':');
                            int skill = Array.IndexOf(Data.SkillKeys, p[0].Trim().ToLowerInvariant().Replace(" ", ""));
                            int level = 3;
                            if (p.Length > 1) int.TryParse(p[1].Trim(), out level);
                            if (skill >= 0 && level >= 1 && level <= 3) cur.Skills.Add(new KeyValuePair<int, int>(skill, level));
                        }
                        break;
                    case "czary":
                        foreach (var tok in val.Split(',', ';', ' '))
                        {
                            string t = tok.Trim().ToLowerInvariant();
                            int spell;
                            if (t == "implozja") cur.Spells.Add(18);
                            else if (int.TryParse(t, out spell) && spell >= 0 && spell < 70) cur.Spells.Add(spell);
                        }
                        break;
                    case "specjalizacja":
                        string sv = val.ToLowerInvariant();
                        int from;
                        if (sv == "brak") cur.SpecMode = HeroEntry.SpecNone;
                        else if (int.TryParse(sv, out from)) { cur.SpecMode = HeroEntry.SpecCopy; cur.SpecFrom = from; }
                        else cur.SpecMode = HeroEntry.SpecOwn;
                        specKey.Add(cur);
                        break;
                    case "ikona":
                        string iv = val.ToLowerInvariant().Replace(" ", "");
                        int iconHero;
                        if (iv == "wlasna") cur.IconMode = HeroEntry.IconOwn;
                        else if (int.TryParse(iv, out iconHero)) { cur.IconMode = HeroEntry.IconHeroMode; cur.IconHero = iconHero; }
                        else if (Array.IndexOf(Data.SkillKeys, iv) >= 0) { cur.IconMode = HeroEntry.IconSkillMode; cur.IconSkill = Array.IndexOf(Data.SkillKeys, iv); }
                        break;
                    case "imie": cur.Name = val; break;
                    case "nazwa_specjalizacji": cur.SpecName = val; break;
                    case "opis_specjalizacji": cur.SpecDesc = val; break;
                    case "statystyki":
                        var s = val.Split('/');
                        int[] st = new int[4];
                        if (s.Length == 4 && Enumerable.Range(0, 4).All(i => int.TryParse(s[i].Trim(), out st[i]))) cur.Stats = st;
                        break;
                }
            }
            // Same rule as the mod: a school bonus without a "specjalizacja" key replaces the own specialty.
            foreach (var e in entries)
                if (!specKey.Contains(e))
                    e.SpecMode = e.Schools != 0 && e.Bonus != 0 ? HeroEntry.SpecNone : HeroEntry.SpecOwn;
        }

        void SaveIni()
        {
            OnEdit();
            var bad = entries.Where(e => e.Id < 0).ToList();
            if (bad.Count > 0)
            {
                MessageBox.Show(this, "Every entry needs a hero.", "Not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var dup = entries.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1);
            if (dup != null)
            {
                MessageBox.Show(this, "Hero " + HeroLabel(dup.Key) + " is on the list more than once.", "Not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var noSource = entries.FirstOrDefault(e => e.SpecMode == HeroEntry.SpecCopy && e.SpecFrom < 0);
            if (noSource != null)
            {
                MessageBox.Show(this, HeroLabel(noSource.Id) + ": pick the hero whose specialty should be copied.", "Not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("; Custom Heroes (WlasniBohaterowie) - configuration");
            sb.AppendLine("; Written by the Hero Creator (Kreator.exe) - easiest to edit it there.");
            sb.AppendLine(";");
            sb.AppendLine("; Keys: id, imie (new name, max 12), szkola (school: ziemia/ogien/woda/powietrze), bonus (%),");
            sb.AppendLine(";   umiejetnosci (skill:level 1-3), czary (spell numbers), statystyki (A/D/P/K),");
            sb.AppendLine(";   specjalizacja (wlasna = own / brak = none / hero number to copy from),");
            sb.AppendLine(";   nazwa_specjalizacji, opis_specjalizacji (in-game specialty name and description),");
            sb.AppendLine(";   ikona (wlasna = own / skill name = skill icon / hero number = that hero's icon; absent = automatic).");
            sb.AppendLine("; Changes take effect after restarting the game (skills/spells/stats: in a NEW game).");
            int n = 1;
            foreach (var e in entries)
            {
                sb.AppendLine();
                sb.AppendLine("[Bohater" + n++ + "]");
                string name;
                if (heroNames.TryGetValue(e.Id, out name)) sb.AppendLine("; " + name);
                sb.AppendLine("id = " + e.Id);
                string newName = StripDiacritics(e.Name);
                if (newName.Length > 12) newName = newName.Substring(0, 12);
                if (newName.Length > 0) sb.AppendLine("imie = " + newName);
                if (e.SpecMode == HeroEntry.SpecCopy)
                {
                    string src;
                    sb.AppendLine("specjalizacja = " + e.SpecFrom + (heroNames.TryGetValue(e.SpecFrom, out src) ? "   ; like " + src : ""));
                }
                else sb.AppendLine("specjalizacja = " + (e.SpecMode == HeroEntry.SpecNone ? "brak" : "wlasna"));
                string specName = StripDiacritics(e.SpecName), specDesc = StripDiacritics(e.SpecDesc);
                if (specName.Length > 0) sb.AppendLine("nazwa_specjalizacji = " + specName);
                if (specDesc.Length > 0) sb.AppendLine("opis_specjalizacji = " + specDesc);
                if (e.IconMode == HeroEntry.IconOwn) sb.AppendLine("ikona = wlasna");
                else if (e.IconMode == HeroEntry.IconSkillMode && e.IconSkill >= 0) sb.AppendLine("ikona = " + Data.SkillKeys[e.IconSkill]);
                else if (e.IconMode == HeroEntry.IconHeroMode && e.IconHero >= 0)
                {
                    string iconSrc;
                    sb.AppendLine("ikona = " + e.IconHero + (heroNames.TryGetValue(e.IconHero, out iconSrc) ? "   ; like " + iconSrc : ""));
                }
                if (e.Schools != 0 && e.Bonus != 0)
                {
                    var schools = new List<string>();
                    if ((e.Schools & 8) != 0) schools.Add("ziemia");
                    if ((e.Schools & 2) != 0) schools.Add("ogien");
                    if ((e.Schools & 4) != 0) schools.Add("woda");
                    if ((e.Schools & 1) != 0) schools.Add("powietrze");
                    sb.AppendLine("szkola = " + string.Join(" ", schools));
                    sb.AppendLine("bonus = " + e.Bonus);
                }
                if (e.Skills.Count > 0)
                    sb.AppendLine("umiejetnosci = " + string.Join(", ", e.Skills.Select(k => Data.SkillKeys[k.Key] + ":" + k.Value)));
                if (e.Spells.Count > 0)
                    sb.AppendLine("czary = " + string.Join(", ", e.Spells.OrderBy(x => x)));
                if (e.Stats != null)
                    sb.AppendLine("statystyki = " + string.Join("/", e.Stats));
            }
            try
            {
                if (File.Exists(iniPath)) File.Copy(iniPath, iniPath + ".bak", true);
                File.WriteAllText(iniPath, sb.ToString(), Encoding.ASCII);
                MessageBox.Show(this, "Saved. Restart the game for the changes to take effect.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the file:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Test helper: Kreator.exe --dump-pcx <data dir> <name.pcx> <out.png>
            if (args.Length == 4 && args[0] == "--dump-pcx")
            {
                GameImages.Open(args[1]);
                var bmp = GameImages.LoadPcx(args[2]);
                if (bmp != null) bmp.Save(args[3], System.Drawing.Imaging.ImageFormat.Png);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
