using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

/// <summary>Display names for the race and class IDs found in SaveInfo.json, in the current UI language.</summary>
static class Labels
{
    static readonly Dictionary<string, (string en, string pl)> Races = new()
    {
        ["Human"] = ("Human", "Człowiek"), ["Githyanki"] = ("Githyanki", "Githyanki"),
        ["Tiefling_Asmodeus"] = ("Tiefling (Asmodeus)", "Diablę (Asmodeusz)"), ["Tiefling_Mephistopheles"] = ("Tiefling (Mephistopheles)", "Diablę (Mefistofeles)"),
        ["Tiefling_Zariel"] = ("Tiefling (Zariel)", "Diablę (Zariel)"),
        ["Elf_HighElf"] = ("High Elf", "Elf wysoki"), ["Elf_WoodElf"] = ("Wood Elf", "Elf leśny"),
        ["HalfElf_High"] = ("High Half-Elf", "Półelf wysoki"), ["HalfElf_Wood"] = ("Wood Half-Elf", "Półelf leśny"), ["HalfElf_Drow"] = ("Drow Half-Elf", "Półelf drow"),
        ["Drow_Lolth"] = ("Drow (Lolth)", "Drow (Lolth)"), ["Drow_Seldarine"] = ("Drow (Seldarine)", "Drow (Seldarine)"),
        ["Dwarf_Gold"] = ("Gold Dwarf", "Krasnolud złoty"), ["Dwarf_Shield"] = ("Shield Dwarf", "Krasnolud tarczowy"), ["Dwarf_Duergar"] = ("Duergar", "Duergar"),
        ["Halfling_Lightfoot"] = ("Lightfoot Halfling", "Niziołek lekkostopy"), ["Halfling_Stout"] = ("Strongheart Halfling", "Niziołek krzepki"),
        ["Gnome_Rock"] = ("Rock Gnome", "Gnom skalny"), ["Gnome_Forest"] = ("Forest Gnome", "Gnom leśny"), ["Gnome_Deep"] = ("Deep Gnome", "Gnom głębinowy"),
        ["Dragonborn"] = ("Dragonborn", "Smoczy"), ["HalfOrc"] = ("Half-Orc", "Półork"),
    };

    static readonly Dictionary<string, (string en, string pl)> Classes = new()
    {
        ["Barbarian"] = ("Barbarian", "Barbarzyńca"), ["Bard"] = ("Bard", "Bard"), ["Cleric"] = ("Cleric", "Kapłan"), ["Druid"] = ("Druid", "Druid"),
        ["Fighter"] = ("Fighter", "Wojownik"), ["Monk"] = ("Monk", "Mnich"), ["Paladin"] = ("Paladin", "Paladyn"), ["Ranger"] = ("Ranger", "Łowca"),
        ["Rogue"] = ("Rogue", "Łotrzyk"), ["Sorcerer"] = ("Sorcerer", "Zaklinacz"), ["Warlock"] = ("Warlock", "Czarownik"), ["Wizard"] = ("Wizard", "Czarodziej"),
    };

    static string Pick((string en, string pl) v) => L.Lang == "pl" ? v.pl : v.en;

    public static string Race(string id) => Races.TryGetValue(id, out var r) ? Pick(r) : id.Replace('_', ' ');

    /// <summary>"Ranger (Swarmkeeper), Rogue" -> localized class names, subclass kept as is</summary>
    public static string ClassList(string classes) => string.Join(", ", classes.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(c =>
    {
        var i = c.IndexOf(" (", StringComparison.Ordinal);
        var main = i < 0 ? c : c[..i];
        return (Classes.TryGetValue(main, out var v) ? Pick(v) : main) + (i < 0 ? "" : c[i..]);
    }));

    public static string MainClass(string classes)
    {
        var first = ClassList(classes).Split(", ")[0];
        var i = first.IndexOf(" (", StringComparison.Ordinal);
        return i < 0 ? first : first[..i];
    }
}
