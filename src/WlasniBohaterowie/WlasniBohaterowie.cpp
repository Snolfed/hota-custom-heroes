// WlasniBohaterowie - hero mod for HotA 1.8.0, loaded by the smackw32.dll proxy.
// Configured in bohaterowie.ini next to this DLL. Per hero it can give:
//   - a "+X% spell damage for a magic school" specialty,
//   - starting secondary skills (at least the given level),
//   - starting spells (plus a spell book).
//
// Hook points in h3hota.exe (HotA 1.8.0):
//   0x4E59D0 int  __thiscall (hero, spell, damage, target) -> final spell damage after
//            orbs, Sorcery and spell specialty. HotA.dll calls it directly too.
//   0x4D8990 void __thiscall (hero, short heroId)  -> initialises a hero from its defaults.
//   0x4D8DA0 void __thiscall (hero, mapHeroData*)  -> applies map editor customisation
//            (may wipe skills/spells), so starting extras are re-applied after it.
#include <windows.h>
#include <stdio.h>
#include <string.h>

// ---- game addresses and hero layout (h3hota.exe from HotA 1.8.0) ----
static const DWORD ADDR_SPELL_DAMAGE = 0x4E59D0;
static const DWORD ADDR_HERO_INIT = 0x4D8990;
static const DWORD ADDR_HERO_MAP_SETUP = 0x4D8DA0;
static const DWORD ADDR_LEARN_SKILL = 0x4E2540;     // int __thiscall (hero, skill, levelsToAdd)
static const BYTE  PROLOGUE_SUB8[6] = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08 };  // push ebp; mov ebp,esp; sub esp,8
static const BYTE  PROLOGUE_SUBC[6] = { 0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x0C };  // push ebp; mov ebp,esp; sub esp,0xC
static const DWORD ADDR_SPELL_TABLE_PTR = 0x687FA8;  // -> spell array, 0x88 bytes per spell
static const DWORD ADDR_HERO_TYPES_PTR = 0x67DCE8;   // -> hero type table (HOTRAITS), 0x5C bytes per hero
static const int   HERO_TYPE_SIZE = 0x5C;
static const int   HERO_TYPE_NAME = 0x40;           // char*
static const int   HERO_TYPE_SKILL1 = 0x0C;         // int skill (-1 none), int level at +4
static const int   HERO_TYPE_SKILL2 = 0x14;         // int skill (-1 none), int level at +4
static const int   HERO_TYPE_HAS_BOOK = 0x1C;       // BYTE: starts with a spell book
static const int   HERO_TYPE_SPELL = 0x20;          // int: starting spell (-1 none), also shown in hero previews
static const DWORD ADDR_SPECIALTY_PTR = 0x679C80;    // -> specialty table, 0x28 bytes per hero
static const int   SPEC_SIZE = 0x28;
static const int   SPEC_TYPE = 0x00;                // 0 skill, 1 creature, 2 resource, 3 spell, ...
static const int   SPEC_BONUS = 0x04;               // skill / creature / resource / spell id
static const int   SPEC_PARAMS = 0x08;              // ints up to 0x1C: type-specific numbers
static const int   SPEC_SHORT = 0x1C;               // char*, e.g. "+350 Gold"
static const int   SPEC_FULL = 0x20;                // char*
static const int   SPEC_DESC = 0x24;                // char*, long description
// Picture controls in dialogs (class vtable 0x63EC48): vtable[4] draws `def` frame `frame`
// of group `group`. Specialty icons are frame <hero id> of un44.def / un32.def.
static const DWORD ADDR_DLGDEF_VTABLE = 0x63EC48;
static const int   DLGDEF_VT_DRAW = 4;
static const int   DLGDEF_DEF = 0x30;
static const int   DLGDEF_FRAME = 0x34;
static const int   DLGDEF_GROUP = 0x38;
static const DWORD ADDR_LOAD_DEF = 0x55C9C0;        // void* __fastcall (const char* name), cached
static const int   DEF_GROUP_COUNT = 0x28;
static const int   DEF_GROUPS = 0x1C;               // group*[]; group+0 = frame count
static const int   SPELL_SIZE = 0x88;
static const int   SPELL_SCHOOL_FLAGS = 0x1C;       // 1 air, 2 fire, 4 water, 8 earth
static const int   HERO_MANA = 0x18;                // short
static const int   HERO_ID = 0x1A;
static const int   HERO_NAME = 0x23;                // char[13]
static const int   HERO_PRIMARY = 0x476;            // signed char[4]: attack, defense, power, knowledge
static const int   HERO_SKILL_LEVELS = 0xC9;        // BYTE[28]
static const int   HERO_SPELLBOOK_SLOT = 0x1B5;     // worn artifact slot 17, artifact id 0 = spell book
static const int   HERO_SPELLS_A = 0x3EA;           // BYTE[70], both set by the game for a starting spell
static const int   HERO_SPELLS_B = 0x430;

static const int SKILL_COUNT = 28;
static const int SPELL_COUNT = 70;

enum { SCHOOL_AIR = 1, SCHOOL_FIRE = 2, SCHOOL_WATER = 4, SCHOOL_EARTH = 8, SCHOOL_ALL = 15 };

// Secondary skill names (ASCII, lower case, no spaces) in game id order.
static const char* SKILL_NAMES[SKILL_COUNT] = {
    "znajomoscterenu", "lucznictwo", "logistyka", "zwiad", "dyplomacja", "nawigacja",
    "dowodzenie", "madrosc", "mistycyzm", "szczescie", "balistyka", "sokoleoko",
    "nekromancja", "zarzadzanie", "magiaognia", "magiapowietrza", "magiawody", "magiaziemi",
    "uczonosc", "taktyka", "artyleria", "nauka", "atak", "obrona",
    "inteligencja", "czarnoksiestwo", "odpornosc", "pierwszapomoc"
};

struct HeroConfig
{
    int id;
    int schools;
    int percent;
    char skillLevel[SKILL_COUNT]; // 0 = untouched, 1..3 = at least this level
    int skillOrder[2];            // first two skills listed, -1 = none; they replace the hero's
                                  // starting skills: 1st listed -> 2nd slot, 2nd listed -> 1st slot
    bool spell[SPELL_COUNT];
    int firstSpell;               // first spell listed in "czary", -1 = none; replaces the hero's starting spell
    bool hasStats;
    int stats[4];                 // attack, defense, power, knowledge
    bool hasStartExtras;
    int specFrom;                 // SPEC_UNSET, SPEC_NONE or a hero id to copy the specialty from
    char specName[64];            // shown in game; empty = keep the (copied) text
    char specDesc[256];
    int iconHero;                 // show this hero's specialty icon, -1 = no change
    int iconSkill;                // show this secondary skill's icon, -1 = no change
};

static const int SPEC_UNSET = -2;  // keep the hero's own specialty
static const int SPEC_NONE = -1;   // no specialty effect (only this mod's bonuses)

static const int MAX_HEROES = 64;
static HeroConfig g_heroes[MAX_HEROES];
static int g_heroCount = 0;
static char g_dir[MAX_PATH];
static char g_logPath[MAX_PATH];
static bool g_seenHero[512];

typedef int(__thiscall* SpellDamageFn)(void* hero, int spell, int damage, void* target);
typedef void(__thiscall* HeroInitFn)(void* hero, int heroId);
typedef void(__thiscall* HeroMapSetupFn)(void* hero, void* mapData);
typedef int(__thiscall* LearnSkillFn)(void* hero, int skill, int levels);
static SpellDamageFn g_origSpellDamage = NULL;
static HeroInitFn g_origHeroInit = NULL;
static HeroMapSetupFn g_origHeroMapSetup = NULL;
static const LearnSkillFn LearnSkill = (LearnSkillFn)ADDR_LEARN_SKILL;

static void Log(const char* fmt, ...)
{
    FILE* f = fopen(g_logPath, "a");
    if (!f) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    fprintf(f, "[%02d:%02d:%02d] ", t.wHour, t.wMinute, t.wSecond);
    va_list args;
    va_start(args, fmt);
    vfprintf(f, fmt, args);
    va_end(args);
    fputc('\n', f);
    fclose(f);
}

// Lower case, drop spaces/underscores/dashes, map Polish letters (cp1250) to ASCII.
static void Normalize(const char* in, char* out, int size)
{
    static const char* from = "\xB9\xE6\xEA\xB3\xF1\xF3\x9C\x9F\xBF\xA5\xC6\xCA\xA3\xD1\xD3\x8C\x8F\xAF";
    static const char* to = "acelnoszzACELNOSZZ";
    int n = 0;
    for (const char* p = in; *p && n < size - 1; ++p)
    {
        char c = *p;
        if (c == ' ' || c == '_' || c == '-' || c == '\t') continue;
        const char* hit = strchr(from, c);
        if (hit && c) c = to[hit - from];
        if (c >= 'A' && c <= 'Z') c = c - 'A' + 'a';
        out[n++] = c;
    }
    out[n] = 0;
}

static int ParseSchools(const char* s)
{
    char buf[128];
    Normalize(s, buf, sizeof(buf));
    int flags = 0;
    if (strstr(buf, "ziemi")) flags |= SCHOOL_EARTH;
    if (strstr(buf, "ogie") || strstr(buf, "ognia")) flags |= SCHOOL_FIRE;
    if (strstr(buf, "wod")) flags |= SCHOOL_WATER;
    if (strstr(buf, "powietrz")) flags |= SCHOOL_AIR;
    if (strstr(buf, "wszystk")) flags |= SCHOOL_ALL;
    return flags;
}

static int FindSkill(const char* name)
{
    char buf[64];
    Normalize(name, buf, sizeof(buf));
    if (buf[0] >= '0' && buf[0] <= '9')
    {
        int id = atoi(buf);
        return (id >= 0 && id < SKILL_COUNT) ? id : -1;
    }
    for (int i = 0; i < SKILL_COUNT; ++i)
        if (strcmp(buf, SKILL_NAMES[i]) == 0) return i;
    return -1;
}

static const char* g_levelNames[4] = { "-", "Podstawowy", "Zaawansowany", "Ekspert" };

// "magiaziemi:3, madrosc:1" -> skill levels
static void ParseSkills(const char* section, const char* text, HeroConfig& h)
{
    char buf[512];
    lstrcpynA(buf, text, sizeof(buf));
    for (char* tok = strtok(buf, ",;"); tok; tok = strtok(NULL, ",;"))
    {
        char* colon = strchr(tok, ':');
        int level = 3;
        if (colon)
        {
            *colon = 0;
            level = atoi(colon + 1);
        }
        int skill = FindSkill(tok);
        if (skill < 0 || level < 1 || level > 3)
        {
            Log("[%s] nieznana umiejetnosc albo poziom: '%s'", section, tok);
            continue;
        }
        h.skillLevel[skill] = (char)level;
        if (h.skillOrder[0] < 0) h.skillOrder[0] = skill;
        else if (h.skillOrder[1] < 0 && h.skillOrder[0] != skill) h.skillOrder[1] = skill;
        h.hasStartExtras = true;
    }
}

// "18, 23" or "implozja, 23" -> spells
static void ParseSpells(const char* section, const char* text, HeroConfig& h)
{
    char buf[512];
    lstrcpynA(buf, text, sizeof(buf));
    for (char* tok = strtok(buf, ",; "); tok; tok = strtok(NULL, ",; "))
    {
        char name[64];
        Normalize(tok, name, sizeof(name));
        if (!name[0]) continue;
        int spell = -1;
        if (name[0] >= '0' && name[0] <= '9') spell = atoi(name);
        else if (strcmp(name, "implozja") == 0) spell = 18;
        if (spell < 0 || spell >= SPELL_COUNT)
        {
            Log("[%s] nieznany czar: '%s'", section, tok);
            continue;
        }
        h.spell[spell] = true;
        if (h.firstSpell < 0) h.firstSpell = spell;
        h.hasStartExtras = true;
    }
}

// "1/1/1/2" (attack/defense/power/knowledge) -> stats
static void ParseStats(const char* section, const char* text, HeroConfig& h)
{
    if (!text[0]) return;
    int a, d, p, k;
    if (sscanf(text, "%d/%d/%d/%d", &a, &d, &p, &k) != 4 ||
        a < 0 || d < 0 || p < 1 || k < 1 || a > 99 || d > 99 || p > 99 || k > 99)
    {
        Log("[%s] zle statystyki: '%s' (format A/O/M/W, np. 1/1/1/2; moc i wiedza min. 1, max 99)", section, text);
        return;
    }
    h.stats[0] = a; h.stats[1] = d; h.stats[2] = p; h.stats[3] = k;
    h.hasStats = true;
    h.hasStartExtras = true;
}

// Secondary skill id of the first magic school in `schools` (earth, fire, water, air order).
static int MagicSkillOf(int schools)
{
    if (schools & SCHOOL_EARTH) return 17;
    if (schools & SCHOOL_FIRE) return 14;
    if (schools & SCHOOL_WATER) return 16;
    if (schools & SCHOOL_AIR) return 15;
    return -1;
}

static void LoadConfig()
{
    char ini[MAX_PATH];
    wsprintfA(ini, "%s\\bohaterowie.ini", g_dir);
    for (int i = 1; i <= MAX_HEROES && g_heroCount < MAX_HEROES; ++i)
    {
        char section[32], text[512];
        wsprintfA(section, "Bohater%d", i);
        int id = GetPrivateProfileIntA(section, "id", -1, ini);
        if (id < 0) continue;

        HeroConfig& h = g_heroes[g_heroCount];
        memset(&h, 0, sizeof(h));
        h.id = id;
        h.firstSpell = -1;
        h.skillOrder[0] = h.skillOrder[1] = -1;
        GetPrivateProfileStringA(section, "szkola", "", text, sizeof(text), ini);
        h.schools = ParseSchools(text);
        h.percent = GetPrivateProfileIntA(section, "bonus", 0, ini);
        GetPrivateProfileStringA(section, "umiejetnosci", "", text, sizeof(text), ini);
        ParseSkills(section, text, h);
        GetPrivateProfileStringA(section, "czary", "", text, sizeof(text), ini);
        ParseSpells(section, text, h);
        GetPrivateProfileStringA(section, "statystyki", "", text, sizeof(text), ini);
        ParseStats(section, text, h);

        bool hasBonus = h.schools != 0 && h.percent != 0;
        h.specFrom = SPEC_UNSET;
        GetPrivateProfileStringA(section, "specjalizacja", "", text, sizeof(text), ini);
        char spec[64];
        Normalize(text, spec, sizeof(spec));
        if (spec[0] >= '0' && spec[0] <= '9') h.specFrom = atoi(spec);
        else if (strcmp(spec, "brak") == 0) h.specFrom = SPEC_NONE;
        else if (spec[0] && strcmp(spec, "wlasna") != 0)
            Log("[%s] nieznana specjalizacja: '%s' (podaj numer bohatera, 'brak' albo 'wlasna')", section, text);
        // A school bonus without any "specjalizacja" key replaces the hero's own specialty.
        if (!spec[0] && hasBonus) h.specFrom = SPEC_NONE;
        GetPrivateProfileStringA(section, "nazwa_specjalizacji", "", h.specName, sizeof(h.specName), ini);
        GetPrivateProfileStringA(section, "opis_specjalizacji", "", h.specDesc, sizeof(h.specDesc), ini);
        // Icon: "ikona = <hero id>" or "ikona = <skill name>"; by default the copied hero's icon,
        // or for a school bonus without a specialty the magic skill icon (like Adrienne's Fire Magic).
        h.iconHero = -1;
        h.iconSkill = -1;
        GetPrivateProfileStringA(section, "ikona", "", text, sizeof(text), ini);
        char icon[64];
        Normalize(text, icon, sizeof(icon));
        if (icon[0] >= '0' && icon[0] <= '9') h.iconHero = atoi(icon);
        else if (icon[0] && strcmp(icon, "wlasna") != 0)
        {
            h.iconSkill = FindSkill(icon);
            if (h.iconSkill < 0) Log("[%s] nieznana ikona: '%s'", section, text);
        }
        else if (!icon[0])
        {
            if (h.specFrom >= 0) h.iconHero = h.specFrom;
            else if (h.specFrom == SPEC_NONE && hasBonus) h.iconSkill = MagicSkillOf(h.schools);
        }

        if (h.specFrom == SPEC_NONE && hasBonus && !h.specName[0])
        {
            // In-game text, in English like the game itself.
            static const char* schoolNames[4] = { "Air", "Fire", "Water", "Earth" };
            char names[64] = "";
            for (int s = 3; s >= 0; --s)
            {
                if (!(h.schools & (1 << s))) continue;
                if (names[0]) lstrcatA(names, "/");
                lstrcatA(names, schoolNames[s]);
            }
            if (h.schools == SCHOOL_ALL) lstrcpyA(names, "All");
            wsprintfA(h.specName, "%s Magic", names);
            if (!h.specDesc[0])
                wsprintfA(h.specDesc, "Increases the damage of %s Magic spells by %d%%.", names, h.percent);
        }

        if (!hasBonus && !h.hasStartExtras && h.specFrom == SPEC_UNSET && !h.specName[0] && !h.specDesc[0])
        {
            Log("[%s] pominiety: nic do zmiany", section);
            continue;
        }
        Log("[%s] bohater id=%d, szkoly=0x%X, bonus=%d%%", section, h.id, h.schools, h.percent);
        if (h.specFrom >= 0) Log("    specjalizacja skopiowana od bohatera id=%d", h.specFrom);
        if (h.specFrom == SPEC_NONE) Log("    oryginalna specjalizacja wylaczona");
        if (h.specName[0]) Log("    nazwa specjalizacji: %s", h.specName);
        if (h.iconHero >= 0) Log("    ikona specjalizacji bohatera id=%d", h.iconHero);
        if (h.iconSkill >= 0) Log("    ikona umiejetnosci %s", SKILL_NAMES[h.iconSkill]);
        for (int s = 0; s < SKILL_COUNT; ++s)
            if (h.skillLevel[s]) Log("    umiejetnosc %s: %s", SKILL_NAMES[s], g_levelNames[h.skillLevel[s]]);
        for (int s = 0; s < SPELL_COUNT; ++s)
            if (h.spell[s]) Log("    czar %d", s);
        if (h.hasStats)
            Log("    statystyki %d/%d/%d/%d", h.stats[0], h.stats[1], h.stats[2], h.stats[3]);
        ++g_heroCount;
    }
    Log("Wczytano %d bohaterow z %s", g_heroCount, ini);
}

static const HeroConfig* FindHero(int id)
{
    for (int i = 0; i < g_heroCount; ++i)
        if (g_heroes[i].id == id) return &g_heroes[i];
    return NULL;
}

static void SafeHeroName(const BYTE* hero, char* out)
{
    int i = 0;
    for (; i < 12; ++i)
    {
        char c = (char)hero[HERO_NAME + i];
        if (c == 0) break;
        out[i] = (c >= 32) ? c : '?';
    }
    out[i] = 0;
}

static void ApplyStartExtras(BYTE* hero, const char* when)
{
    const HeroConfig* h = FindHero(*(int*)(hero + HERO_ID));
    if (!h || !h->hasStartExtras) return;

    for (int s = 0; s < SKILL_COUNT; ++s)
    {
        int have = (signed char)hero[HERO_SKILL_LEVELS + s];
        if (h->skillLevel[s] > have)
            LearnSkill(hero, s, h->skillLevel[s] - (have > 0 ? have : 0));
    }

    bool anySpell = false;
    for (int s = 0; s < SPELL_COUNT; ++s)
    {
        if (!h->spell[s]) continue;
        hero[HERO_SPELLS_A + s] = 1;
        hero[HERO_SPELLS_B + s] = 1;
        anySpell = true;
    }
    if (anySpell && *(int*)(hero + HERO_SPELLBOOK_SLOT) == -1)
        *(int*)(hero + HERO_SPELLBOOK_SLOT) = 0;

    if (h->hasStats)
    {
        for (int i = 0; i < 4; ++i)
            hero[HERO_PRIMARY + i] = (BYTE)h->stats[i];
        // Start with a full mana pool for the new knowledge (10 per point).
        short& mana = *(short*)(hero + HERO_MANA);
        if (mana < h->stats[3] * 10) mana = (short)(h->stats[3] * 10);
    }

    // The game re-initialises heroes many times while setting up a map; log once per hero.
    static bool s_logged[512];
    if (h->id >= 0 && h->id < 512 && !s_logged[h->id])
    {
        s_logged[h->id] = true;
        char name[13];
        SafeHeroName(hero, name);
        Log("Startowe umiejetnosci/czary nadane: id=%d (%s) [%s]", h->id, name, when);
    }
}

// Writes "id;name" for every hero in the game's hero type table, for the editor GUI.
// Called by SpecialtyThread once the table (HOTRAITS data, extended by HotA) is filled.
static void DumpHeroList()
{
    char path[MAX_PATH];
    wsprintfA(path, "%s\\lista_bohaterow.txt", g_dir);
    FILE* f = fopen(path, "w");
    if (!f) return;
    const BYTE* table = *(const BYTE**)ADDR_HERO_TYPES_PTR;
    int count = 0;
    for (int id = 0; id < 512; ++id)
    {
        const BYTE* entry = table + id * HERO_TYPE_SIZE;
        if (IsBadReadPtr(entry, HERO_TYPE_SIZE)) break;
        const char* name = *(const char* const*)(entry + HERO_TYPE_NAME);
        if (!name || IsBadReadPtr(name, 1) || !name[0]) break;
        fprintf(f, "%d;%.12s\n", id, name);
        ++count;
    }
    fclose(f);
    Log("Zapisano liste %d bohaterow do %s", count, path);
}

// ---- specialties ----
// The game and HotA.dll read a hero's specialty from the table at [0x679C80] by hero id,
// so changing the table entry changes both the effect and the text shown in game.
// The table is filled after startup (and may be refilled), so a background thread waits
// for it, snapshots the original entries and keeps the configured ones applied.

static BYTE* g_specOriginal = NULL;  // snapshot of the untouched table
static int g_specCount = 0;

static int CountHeroTypes()
{
    const BYTE* table = *(const BYTE* volatile*)ADDR_HERO_TYPES_PTR;
    if (!table) return 0;
    int count = 0;
    for (; count < 512; ++count)
    {
        const BYTE* entry = table + count * HERO_TYPE_SIZE;
        if (IsBadReadPtr(entry, HERO_TYPE_SIZE)) break;
        const char* name = *(const char* const*)(entry + HERO_TYPE_NAME);
        if (!name || IsBadReadPtr(name, 1) || !name[0]) break;
    }
    return count;
}

static bool SpecTextOk(const BYTE* entry, int offset)
{
    const char* s = *(const char* const*)(entry + offset);
    return s && !IsBadReadPtr(s, 1);
}

static void CleanText(const char* in, char* out, int size)
{
    int n = 0;
    for (const char* p = in; p && *p && n < size - 1; ++p)
        out[n++] = (*p == ';' || *p == '\r' || *p == '\n' || *p == '\t') ? ' ' : *p;
    out[n] = 0;
}

static void DumpSpecialtyList()
{
    char path[MAX_PATH];
    wsprintfA(path, "%s\\lista_specjalizacji.txt", g_dir);
    FILE* f = fopen(path, "w");
    if (!f) return;
    for (int id = 0; id < g_specCount; ++id)
    {
        const BYTE* e = g_specOriginal + id * SPEC_SIZE;
        char shortText[128] = "", desc[512] = "";
        if (SpecTextOk(e, SPEC_SHORT)) CleanText(*(const char* const*)(e + SPEC_SHORT), shortText, sizeof(shortText));
        if (SpecTextOk(e, SPEC_DESC)) CleanText(*(const char* const*)(e + SPEC_DESC), desc, sizeof(desc));
        fprintf(f, "%d;%d;%d;%s;%s\n", id, *(const int*)(e + SPEC_TYPE), *(const int*)(e + SPEC_BONUS), shortText, desc);
    }
    fclose(f);
    Log("Zapisano liste %d specjalizacji do %s", g_specCount, path);
}

static void BuildSpecEntry(const HeroConfig& h, BYTE* out)
{
    memcpy(out, g_specOriginal + h.id * SPEC_SIZE, SPEC_SIZE);
    if (h.specFrom >= 0 && h.specFrom < g_specCount)
    {
        memcpy(out, g_specOriginal + h.specFrom * SPEC_SIZE, SPEC_SIZE);
    }
    else if (h.specFrom == SPEC_NONE)
    {
        // Type 0 (secondary skill) like Adrienne's Fire Magic: with a school bonus it names that
        // school's magic skill, which the game gives no effect (the mod adds the bonus);
        // otherwise skill -1, which never matches anything.
        *(int*)(out + SPEC_TYPE) = 0;
        *(int*)(out + SPEC_BONUS) = (h.schools && h.percent) ? MagicSkillOf(h.schools) : -1;
        memset(out + SPEC_PARAMS, 0, SPEC_SHORT - SPEC_PARAMS);
    }
    if (h.specName[0])
    {
        *(const char**)(out + SPEC_SHORT) = h.specName;
        *(const char**)(out + SPEC_FULL) = h.specName;
    }
    if (h.specDesc[0]) *(const char**)(out + SPEC_DESC) = h.specDesc;
}

static void ApplySpecialties(bool logChanges)
{
    BYTE* table = *(BYTE* volatile*)ADDR_SPECIALTY_PTR;
    BYTE* types = *(BYTE* volatile*)ADDR_HERO_TYPES_PTR;
    if (!table || !types) return;
    for (int i = 0; i < g_heroCount; ++i)
    {
        const HeroConfig& h = g_heroes[i];

        // Starting spell and skills in the hero type data: what new heroes get and hero
        // previews (tavern, scenario setup) show.
        if (h.id >= 0 && h.id < g_specCount && (h.firstSpell >= 0 || h.skillOrder[0] >= 0))
        {
            BYTE wanted[HERO_TYPE_SIZE];
            BYTE* type = types + h.id * HERO_TYPE_SIZE;
            memcpy(wanted, type, HERO_TYPE_SIZE);
            if (h.firstSpell >= 0)
            {
                *(int*)(wanted + HERO_TYPE_SPELL) = h.firstSpell;
                wanted[HERO_TYPE_HAS_BOOK] = 1;
            }
            const int slots[2] = { HERO_TYPE_SKILL2, HERO_TYPE_SKILL1 };
            for (int s = 0; s < 2; ++s)
            {
                int skill = h.skillOrder[s];
                if (skill < 0) continue;
                *(int*)(wanted + slots[s]) = skill;
                *(int*)(wanted + slots[s] + 4) = h.skillLevel[skill];
            }
            // The other slot must not hold the same skill twice.
            if (*(int*)(wanted + HERO_TYPE_SKILL1) == *(int*)(wanted + HERO_TYPE_SKILL2))
                *(int*)(wanted + HERO_TYPE_SKILL1) = -1;
            if (memcmp(wanted, type, HERO_TYPE_SIZE) != 0)
            {
                DWORD old;
                VirtualProtect(type, HERO_TYPE_SIZE, PAGE_READWRITE, &old);
                memcpy(type, wanted, HERO_TYPE_SIZE);
                VirtualProtect(type, HERO_TYPE_SIZE, old, &old);
                if (logChanges)
                    Log("Start bohatera id=%d: czar %d, umiejetnosci %d:%d, %d:%d", h.id,
                        *(int*)(wanted + HERO_TYPE_SPELL),
                        *(int*)(wanted + HERO_TYPE_SKILL1), *(int*)(wanted + HERO_TYPE_SKILL1 + 4),
                        *(int*)(wanted + HERO_TYPE_SKILL2), *(int*)(wanted + HERO_TYPE_SKILL2 + 4));
            }
        }

        if (h.specFrom == SPEC_UNSET && !h.specName[0] && !h.specDesc[0]) continue;
        if (h.id < 0 || h.id >= g_specCount) continue;
        BYTE wanted[SPEC_SIZE];
        BuildSpecEntry(h, wanted);
        BYTE* entry = table + h.id * SPEC_SIZE;
        if (memcmp(entry, wanted, SPEC_SIZE) == 0) continue;
        DWORD old;
        VirtualProtect(entry, SPEC_SIZE, PAGE_READWRITE, &old);
        memcpy(entry, wanted, SPEC_SIZE);
        VirtualProtect(entry, SPEC_SIZE, old, &old);
        if (logChanges) Log("Specjalizacja ustawiona: id=%d (typ %d, parametr %d)", h.id, *(int*)(wanted + SPEC_TYPE), *(int*)(wanted + SPEC_BONUS));
    }
}

static DWORD WINAPI SpecialtyThread(LPVOID)
{
    // The tables count as ready when hero names and specialty texts are filled and the
    // table pointer and hero count have not changed for 2 s (HotA may first fill the
    // original 156-hero table and swap in its bigger one later). If they change again,
    // take a new snapshot.
    const BYTE* snapTable = NULL;
    int snapCount = 0;
    const BYTE* candTable = NULL;
    int candCount = 0, stableTicks = 0;
    for (;;)
    {
        Sleep(500);
        int count = CountHeroTypes();
        const BYTE* spec = *(const BYTE* volatile*)ADDR_SPECIALTY_PTR;
        if (count < 156 || !spec || IsBadReadPtr(spec, count * SPEC_SIZE)) continue;
        if (!SpecTextOk(spec + (count - 1) * SPEC_SIZE, SPEC_SHORT)) continue;

        if (spec == snapTable && count == snapCount)
        {
            ApplySpecialties(false);
            continue;
        }
        if (spec != candTable || count != candCount)
        {
            candTable = spec;
            candCount = count;
            stableTicks = 0;
            continue;
        }
        if (++stableTicks < 4) continue;

        free(g_specOriginal);
        g_specOriginal = (BYTE*)malloc(count * SPEC_SIZE);
        memcpy(g_specOriginal, spec, count * SPEC_SIZE);
        g_specCount = count;
        snapTable = spec;
        snapCount = count;
        Log("Tablica specjalizacji gotowa: %d bohaterow", count);
        DumpHeroList();
        DumpSpecialtyList();
        ApplySpecialties(true);
    }
}

// ---- specialty icons ----
// Every picture control is drawn through DLGDEF vtable[4]. When one shows frame <hero id> of
// un44.def/un32.def for a configured hero, the frame (or the whole DEF, which carries its own
// palette) is swapped for the duration of that one draw.

typedef void* (__fastcall* LoadDefFn)(const char* name);
typedef void(__fastcall* DefDrawFn)(BYTE* ctrl);
static DefDrawFn g_origDefDraw = NULL;
static void* g_un44 = NULL;
static void* g_un32 = NULL;
static void* g_secskill = NULL;   // 44x44 skill icons, pairs with un44
static void* g_secsk32 = NULL;    // 32x32 skill icons, pairs with un32
static bool g_anyIcon = false;

static int DefFrameCount(void* def, int group)
{
    const BYTE* d = (const BYTE*)def;
    if (!d || group >= *(const int*)(d + DEF_GROUP_COUNT)) return 0;
    const int* g = (*(const int* const* const*)(d + DEF_GROUPS))[group];
    return g ? *g : 0;
}

// secskill.def / secsk32.def: 3 frames first, then Basic/Advanced/Expert per skill.
// Specialty icons use the Expert one (for magic schools: the orb, as on Adrienne's Fire Magic).
static int SkillIconFrame(int skill) { return 3 + skill * 3 + 2; }

static void __fastcall DefDrawHook(BYTE* ctrl)
{
    if (g_anyIcon && ctrl)
    {
        static bool s_loaded = false;
        if (!s_loaded)
        {
            s_loaded = true;
            LoadDefFn LoadDef = (LoadDefFn)ADDR_LOAD_DEF;
            g_un44 = LoadDef("un44.def");
            g_un32 = LoadDef("un32.def");
            g_secskill = LoadDef("secskill.def");
            g_secsk32 = LoadDef("secsk32.def");
            Log("Ikony: un44 %d klatek, un32 %d, secskill %d, secsk32 %d", DefFrameCount(g_un44, 0),
                DefFrameCount(g_un32, 0), DefFrameCount(g_secskill, 0), DefFrameCount(g_secsk32, 0));
        }
        void* def = *(void**)(ctrl + DLGDEF_DEF);
        if (def && (def == g_un44 || def == g_un32) && *(int*)(ctrl + DLGDEF_GROUP) == 0)
        {
            const HeroConfig* h = FindHero(*(int*)(ctrl + DLGDEF_FRAME));
            if (h && (h->iconHero >= 0 || h->iconSkill >= 0))
            {
                void* newDef = def;
                int newFrame = h->iconHero;
                if (h->iconSkill >= 0)
                {
                    newDef = (def == g_un44) ? g_secskill : g_secsk32;
                    newFrame = SkillIconFrame(h->iconSkill);
                }
                if (newDef && newFrame >= 0 && newFrame < DefFrameCount(newDef, 0))
                {
                    int oldFrame = *(int*)(ctrl + DLGDEF_FRAME);
                    *(void**)(ctrl + DLGDEF_DEF) = newDef;
                    *(int*)(ctrl + DLGDEF_FRAME) = newFrame;
                    g_origDefDraw(ctrl);
                    *(void**)(ctrl + DLGDEF_DEF) = def;
                    *(int*)(ctrl + DLGDEF_FRAME) = oldFrame;
                    return;
                }
            }
        }
    }
    g_origDefDraw(ctrl);
}

static void InstallIconHook()
{
    for (int i = 0; i < g_heroCount; ++i)
        if (g_heroes[i].iconHero >= 0 || g_heroes[i].iconSkill >= 0) g_anyIcon = true;
    if (!g_anyIcon) return;

    DWORD* slot = (DWORD*)ADDR_DLGDEF_VTABLE + DLGDEF_VT_DRAW;
    if (*slot != 0x4EAC20)
    {
        Log("BLAD: metoda rysowania obrazkow ma inny adres niz oczekiwany (0x%08X). Ikony wylaczone.", *slot);
        return;
    }
    g_origDefDraw = (DefDrawFn)*slot;
    DWORD old;
    VirtualProtect(slot, 4, PAGE_READWRITE, &old);
    *slot = (DWORD)&DefDrawHook;
    VirtualProtect(slot, 4, old, &old);
    Log("Hook zainstalowany: ikony specjalizacji (vtable 0x%08X)", (DWORD)slot);
}

static void __fastcall HeroInitHook(void* hero, void* /*edx*/, int heroId)
{
    g_origHeroInit(hero, heroId);
    ApplyStartExtras((BYTE*)hero, "nowy bohater");
}

static void __fastcall HeroMapSetupHook(void* hero, void* /*edx*/, void* mapData)
{
    g_origHeroMapSetup(hero, mapData);
    ApplyStartExtras((BYTE*)hero, "bohater z mapy");
}

static int __fastcall SpellDamageHook(void* heroPtr, void* /*edx*/, int spell, int damage, void* target)
{
    int result = g_origSpellDamage(heroPtr, spell, damage, target);
    if (!heroPtr || spell < 0 || spell >= 128) return result;

    const BYTE* hero = (const BYTE*)heroPtr;
    int id = *(const int*)(hero + HERO_ID);

    if (id >= 0 && id < 512 && !g_seenHero[id])
    {
        g_seenHero[id] = true;
        char name[13];
        SafeHeroName(hero, name);
        Log("Czar rzucil bohater id=%d (%s)", id, name);
    }

    const HeroConfig* h = FindHero(id);
    if (!h || h->percent == 0) return result;

    const BYTE* spells = *(const BYTE**)ADDR_SPELL_TABLE_PTR;
    int schools = *(const int*)(spells + spell * SPELL_SIZE + SPELL_SCHOOL_FLAGS);
    if (!(h->schools & schools)) return result;

    int boosted = result * (100 + h->percent) / 100;
    static int s_logged = 0;
    if (s_logged < 20)
    {
        ++s_logged;
        Log("Bonus +%d%% dla id=%d, czar %d: %d -> %d", h->percent, id, spell, result, boosted);
    }
    return boosted;
}

// Replaces a 6-byte function prologue with a jmp to `hook`; returns a trampoline that
// runs the original prologue and continues in the original function, or NULL.
static void* InstallHook(DWORD address, const BYTE* expected, void* hook, const char* what)
{
    BYTE* target = (BYTE*)address;
    if (memcmp(target, expected, 6) != 0)
    {
        Log("BLAD: kod gry pod 0x%08X (%s) jest inny niz oczekiwany (inna wersja HotA?). "
            "Bajty: %02X %02X %02X %02X %02X %02X. Ta funkcja moda wylaczona.",
            address, what, target[0], target[1], target[2], target[3], target[4], target[5]);
        return NULL;
    }

    BYTE* tramp = (BYTE*)VirtualAlloc(NULL, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!tramp) return NULL;
    memcpy(tramp, target, 6);
    tramp[6] = 0xE9;
    *(DWORD*)(tramp + 7) = (DWORD)(target + 6) - (DWORD)(tramp + 11);

    DWORD old;
    VirtualProtect(target, 6, PAGE_EXECUTE_READWRITE, &old);
    target[0] = 0xE9;
    *(DWORD*)(target + 1) = (DWORD)hook - (DWORD)(target + 5);
    target[5] = 0x90;
    VirtualProtect(target, 6, old, &old);
    FlushInstructionCache(GetCurrentProcess(), target, 6);
    Log("Hook zainstalowany: %s (0x%08X)", what, address);
    return tramp;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(inst);

    GetModuleFileNameA(inst, g_dir, MAX_PATH);
    char* slash = strrchr(g_dir, '\\');
    if (slash) *slash = 0;
    wsprintfA(g_logPath, "%s\\WlasniBohaterowie.log", g_dir);

    Log("=== Wtyczka WlasniBohaterowie zaladowana ===");
    LoadConfig();
    g_origSpellDamage = (SpellDamageFn)InstallHook(ADDR_SPELL_DAMAGE, PROLOGUE_SUB8, (void*)&SpellDamageHook, "obrazenia z czarow");
    g_origHeroInit = (HeroInitFn)InstallHook(ADDR_HERO_INIT, PROLOGUE_SUBC, (void*)&HeroInitHook, "tworzenie bohatera");
    g_origHeroMapSetup = (HeroMapSetupFn)InstallHook(ADDR_HERO_MAP_SETUP, PROLOGUE_SUB8, (void*)&HeroMapSetupHook, "bohater z edytora map");
    InstallIconHook();
    CloseHandle(CreateThread(NULL, 0, SpecialtyThread, NULL, 0, NULL));
    return TRUE;
}
