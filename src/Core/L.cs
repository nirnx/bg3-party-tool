using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bg3PartyTool.Core;

/// <summary>UI language: English by default, Polish on request; the choice is kept in the tool's data folder.</summary>
public static class L
{
    public static string Lang { get; private set; } = LoadSetting();
    public static event Action? Changed;

    static string SettingsPath => Path.Combine(Library.DataDir, "settings.json");

    static string LoadSetting()
    {
        try
        {
            if (File.Exists(SettingsPath) && JsonNode.Parse(File.ReadAllText(SettingsPath))?["language"]?.GetValue<string>() is { } l && Strings.Values.First().ContainsKey(l))
                return l;
        }
        catch (Exception) { /* a broken settings file just means defaults */ }
        return "en";
    }

    public static void Set(string lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        try
        {
            Directory.CreateDirectory(Library.DataDir);
            File.WriteAllText(SettingsPath, new JsonObject { ["language"] = lang }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* the choice simply won't persist */ }
        Changed?.Invoke();
    }

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (s.TryGetValue(Lang, out var v) ? v : s["en"]) : key;

    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    static Dictionary<string, string> E(string en, string pl) => new() { ["en"] = en, ["pl"] = pl };

    static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        // --- main window
        ["app.subtitle"] = E("Player characters in save games", "Postacie graczy w save'ach"),
        ["drop.line1a"] = E("Drop a ", "Upuść tu "),
        ["drop.line1b"] = E(" or ", " albo "),
        ["drop.line2"] = E("or click to choose a file", "lub kliknij, żeby wybrać plik"),
        ["add.disk"] = E("Add from disk…", "Dodaj z dysku…"),
        ["filter.all"] = E("All", "Wszystkie"),
        ["filter.imported"] = E("Imported", "Wgrane"),
        ["filter.game"] = E("My BG3", "Moje BG3"),
        ["filter.output"] = E("Results", "Wyniki"),
        ["list.empty"] = E("No saves in this view.", "Brak save'ów w tym widoku."),
        ["badge.game"] = E("BG3", "BG3"),
        ["badge.imported"] = E("imported", "wgrany"),
        ["badge.output"] = E("result", "wynik"),
        ["empty.title"] = E("Pick a save", "Wybierz save"),
        ["empty.text"] = E("On the left: saves from your game, imported files and the tool's results. You can also drag a file onto the window.",
                           "Po lewej są save'y z Twojej gry, wgrane pliki i wyniki narzędzia. Możesz też przeciągnąć plik na okno."),
        ["fact.level"] = E("Location", "Lokacja"),
        ["fact.version"] = E("Game version", "Wersja gry"),
        ["fact.avatars"] = E("Player characters", "Postacie graczy"),
        ["fact.players"] = E("Players", "Graczy"),
        ["fact.ingame"] = E("Shown in game as", "W grze widoczny jako"),
        ["save.rename"] = E("Rename…", "Zmień nazwę…"),
        ["save.delete"] = E("Delete save…", "Usuń save…"),
        ["save.reveal"] = E("Show in folder", "Pokaż w folderze"),
        ["avatars.title"] = E("Player characters", "Postacie graczy"),
        ["avatars.hint"] = E("bar colour = the player who owns the character", "kolor paska = gracz, do którego należy postać"),
        ["card.level"] = E("Level", "Poziom"),
        ["card.class"] = E("Class", "Klasa"),
        ["card.host"] = E("Host", "Host"),
        ["card.me"] = E("You", "Ty"),
        ["card.multi"] = E("player has {0} characters", "gracz ma {0} postacie"),
        ["card.recruited"] = E("Recruited: ", "Zrekrutował(a): "),
        ["card.profile"] = E("Player profile ID", "ID profilu gracza"),
        ["card.park"] = E("Park character", "Odłóż postać"),
        ["card.park.tip"] = E("Removes the character from the party and hides it from the world; its data goes to a file so it can be restored",
                              "Usuwa z drużyny i chowa ze świata; dane trafiają do pliku, żeby można było postać przywrócić"),
        ["parked.title"] = E("Parked characters", "Odłożone postacie"),
        ["parked.hint"] = E("Characters parked earlier from this campaign. Any of them can be restored to any player.",
                            "Postacie odłożone wcześniej z tej kampanii. Można je przywrócić dowolnemu graczowi."),
        ["parked.load"] = E("Load character from file…", "Wczytaj postać z pliku…"),
        ["parked.empty"] = E("None. Parking a character writes a .postac.json file, which shows up here.",
                             "Brak. Odłożenie postaci zapisuje ją do pliku .postac.json, który pojawi się tutaj."),
        ["parked.lvl"] = E(" · lvl {0}", " · poz. {0}"),
        ["parked.when"] = E("parked {0} from “{1}”", "odłożono {0} z „{1}”"),
        ["parked.restore"] = E("Restore", "Przywróć"),
        ["result.open"] = E("Open save folder", "Otwórz folder z save'em"),
        ["result.show"] = E("Show new save", "Pokaż nowy save"),
        ["help.label"] = E("How to use: ", "Jak użyć: "),
        ["help.text"] = E("park the character of the player who is leaving. It disappears from the world (hidden the way the game hides its own characters) and the data needed to restore it goes to a .postac.json file. The new save is written straight to your BG3 saves; if someone else hosts, save it to the tool's folder and send it to the host's Story folder. The host loads it in multiplayer. A newcomer gets character creation as long as no free player character is left in the save: the game also hands a joiner any character sitting in Withers' wardrobe and a host's second character, so park those too for the duration and restore them afterwards. When the old player returns, restore the character from Parked characters, to any player. The original save is never touched.",
                           "odłóż postać gracza, który odpada. Znika ze świata (gra ją chowa, tak jak swoje postacie), a dane potrzebne do przywrócenia trafiają do pliku .postac.json. Nowy save trafia od razu do Twoich save'ów BG3; jeśli hostuje ktoś inny, zapisz go w folderze narzędzia i wyślij hostowi do jego folderu Story. Host wczytuje go w trybie multi. Nowa osoba dostaje kreator postaci, pod warunkiem że w save'ie nie ma żadnej wolnej postaci gracza: gra wręcza dołączającemu także postać z szafy Marnieja i drugą postać hosta, więc na czas kreatora odłóż je również, a potem przywróć. Gdy stary gracz wraca, przywróć postać z sekcji Odłożone postacie, także innemu graczowi. Oryginalny save zawsze zostaje nietknięty."),
        ["busy"] = E("Working…", "Pracuję…"),
        ["busy.import"] = E("Importing {0}…", "Wgrywam {0}…"),
        ["busy.character"] = E("Loading character…", "Wczytuję postać…"),
        ["busy.save"] = E("Loading save…", "Wczytuję save…"),
        ["busy.park"] = E("Parking {0}…", "Odkładam {0}…"),
        ["busy.restore"] = E("Restoring {0}…", "Przywracam {0}…"),
        ["busy.rename"] = E("Renaming…", "Zmieniam nazwę…"),
        ["busy.delete"] = E("Deleting…", "Usuwam…"),
        ["toast.found"] = E("Found {0} saves, pick one from the list.", "Znaleziono {0} save'y, wybierz jeden z listy."),
        ["toast.charloaded"] = E("Character loaded. Pick a save of its campaign to restore it.", "Postać wczytana. Wybierz save kampanii, żeby ją przywrócić."),
        ["toast.charother"] = E("This character is not from this save's campaign.", "Ta postać nie pochodzi z kampanii tego save'a."),
        ["toast.deleted"] = E("Save deleted.", "Save usunięty."),
        ["done.park"] = E("Done: parked {0}", "Gotowe: odłożono postać {0}"),
        ["done.restore"] = E("Done: restored {0}", "Gotowe: przywrócono postać {0}"),
        ["done.rename"] = E("Done: save renamed", "Gotowe: nazwa zmieniona"),
        ["pick.save"] = E("Choose a BG3 save or an archive of saves", "Wybierz save BG3 albo archiwum z save'ami"),
        ["pick.save.filter"] = E("BG3 save / archive", "Save BG3 / archiwum"),
        ["pick.char"] = E("Choose a character file (.postac.json)", "Wybierz plik postaci (.postac.json)"),
        ["pick.char.filter"] = E("BG3 Party Tool character", "Postać BG3 Party Tool"),
        ["cancel"] = E("Cancel", "Anuluj"),
        // --- save target
        ["target.label"] = E("New save", "Nowy save"),
        ["target.name"] = E("Name", "Nazwa"),
        ["target.manual"] = E("List it in the game under this name (manual save instead of quicksave/autosave)", "Pokaż w grze pod tą nazwą (zapis ręczny zamiast szybkiego/autozapisu)"),
        ["target.where"] = E("Location", "Miejsce"),
        ["name.without"] = E("without", "bez"),
        ["name.with"] = E("with", "z"),
        ["target.game"] = E("To my BG3 saves (the game sees it right away)", "Do save'ów BG3 (gra od razu go zobaczy)"),
        ["target.tool"] = E("To the tool's folder (e.g. to send to the host)", "Do folderu narzędzia (np. żeby wysłać hostowi)"),
        ["target.json"] = E("The character file (.postac.json) stays in the tool's folder.", "Plik postaci (.postac.json) zostaje w folderze narzędzia."),
        ["target.nogame"] = E("No BG3 installation found on this computer.", "Nie znalazłem instalacji BG3 na tym komputerze."),
        // --- park dialog
        ["park.title"] = E("Park {0}", "Odłóż {0}"),
        ["park.intro"] = E("{0} ({1}, {2}) will be cut out of the save “{3}”. The character:", "{0} ({1}, {2}) zostanie wycięta z save'a „{3}”. Postać:"),
        ["park.b1"] = E("disappears from the world and stops being a player character (its data stays in the save, so it can be restored),",
                        "znika ze świata i przestaje być postacią gracza (jej dane zostają w save'ie, więc da się ją przywrócić),"),
        ["park.b2"] = E("frees a slot, so a new player gets character creation (as long as no other free player character remains, e.g. a host's second character),",
                        "zwalnia miejsce, więc nowy gracz dostanie kreator postaci (o ile nie zostaje żadna inna wolna postać gracza, np. druga postać hosta),"),
        ["park.b3"] = E("the original save stays untouched; a new copy is written.", "oryginalny save zostaje nietknięty, powstaje nowa kopia."),
        ["park.file"] = E("Character file (.postac.json)", "Plik postaci (.postac.json)"),
        ["park.file.default"] = E("Kept in the tool's folder (shows up under Parked characters).", "Zostaje w folderze narzędzia (pojawi się w Odłożonych postaciach)."),
        ["park.file.extra"] = E("Also saved as: {0}", "Dodatkowo zapisany jako: {0}"),
        ["park.file.pick"] = E("Save a copy as…", "Zapisz kopię jako…"),
        ["park.file.title"] = E("Save the character file", "Zapisz plik postaci"),
        ["park.recruits"] = E("Companions ({0}) go to", "Towarzysze ({0}) przejdą do"),
        ["park.host"] = E("{0} (host)", "{0} (host)"),
        ["park.ok"] = E("Remove from party", "Usuń z drużyny"),
        // --- restore dialog
        ["restore.title"] = E("Restore {0}", "Przywróć {0}"),
        ["restore.intro"] = E("{0} ({1}, {2}) returns to the party in save “{3}” as a player character, with everything it carries in that save. A new copy of the save is written.",
                              "{0} ({1}, {2}) wróci do drużyny w save'ie „{3}” jako postać gracza, ze wszystkim, co ma przy sobie w tym save'ie. Powstaje nowa kopia save'a."),
        ["restore.owner"] = E("The character goes to player", "Postać dostaje gracz"),
        ["restore.tag.host"] = E("host", "host"),
        ["restore.tag.me"] = E("you", "ty"),
        ["restore.tag.orig"] = E("original player", "pierwotny gracz"),
        ["restore.player"] = E("player of {0}", "gracz postaci {0}"),
        ["restore.orig"] = E("original player ({0}not in this save)", "pierwotny gracz ({0}nie ma go w tym save'ie)"),
        ["restore.orig.me"] = E("you, ", "ty, "),
        ["restore.note"] = E("Only players who have a character in this save are listed (the tool knows just their profiles).",
                             "Na liście są gracze, którzy mają postać w tym save'ie (narzędzie zna tylko ich profile)."),
        ["restore.recruits"] = E("Give the companions back: {0}", "Oddaj z powrotem towarzyszy: {0}"),
        ["restore.ok"] = E("Restore", "Przywróć"),
        // --- rename / delete
        ["rename.title"] = E("Rename save", "Zmień nazwę save'a"),
        ["rename.name"] = E("New name", "Nowa nazwa"),
        ["rename.info"] = E("Folder: {0}__<name>   File: <name>.lsv\nThe part before “__” is the campaign leader and an ID the game adds; the tool keeps it.",
                            "Folder: {0}__<nazwa>   Plik: <nazwa>.lsv\nCzłon przed „__” to lider kampanii i numer nadany przez grę; narzędzie go zachowuje."),
        ["rename.ingame.auto"] = E("In the game's Load menu this save is listed as “{0}”, not by its file name, because it is a {1}.",
                                   "W menu wczytywania gra pokazuje ten zapis jako „{0}”, a nie po nazwie pliku, bo to {1}."),
        ["rename.ingame.manual"] = E("In the game's Load menu this save is listed under its name.", "W menu wczytywania gra pokazuje ten zapis pod jego nazwą."),
        ["rename.becomes"] = E("After renaming it becomes a manual save listed under the new name.", "Po zmianie nazwy stanie się zapisem ręcznym widocznym pod nową nazwą."),
        ["rename.manual"] = E("Convert to a manual save, so the game shows the new name", "Zamień na zapis ręczny, żeby gra pokazywała nową nazwę"),
        ["rename.ok"] = E("Rename", "Zmień nazwę"),
        ["rename.bad"] = E("The name may not be empty or contain / \\ : * ? \" < > |", "Nazwa nie może być pusta ani zawierać / \\ : * ? \" < > |"),
        ["type.quick"] = E("quicksave", "szybki zapis"),
        ["type.auto"] = E("autosave", "autozapis"),
        ["type.manual"] = E("manual save", "zapis ręczny"),
        ["type.quick.name"] = E("QuickSave {0}", "QuickSave {0}"),
        ["type.auto.name"] = E("AutoSave {0}", "AutoSave {0}"),
        ["delete.title"] = E("Delete save", "Usuń save"),
        ["delete.text"] = E("Delete “{0}” from disk?\n{1}\nThis cannot be undone.", "Usunąć „{0}” z dysku?\n{1}\nTego nie da się cofnąć."),
        ["delete.ok"] = E("Delete", "Usuń"),
        // --- core: library
        ["lib.nolsv"] = E("the file contains no save (.lsv)", "w pliku nie ma żadnego save'a (.lsv)"),
        ["lib.nofile"] = E("file not found: {0}", "nie ma pliku {0}"),
        ["lib.saved"] = E("Saved: {0}", "Zapisano: {0}"),
        ["lib.charsaved"] = E("Character saved to: {0}", "Postać zapisana do: {0}"),
        ["lib.charcopy"] = E("Copy of the character file: {0}", "Kopia pliku postaci: {0}"),
        ["lib.verify.still"] = E("verification failed: the character is still in the new save", "weryfikacja nie przeszła: postać nadal jest w nowym save"),
        ["lib.verify.ok"] = E("Verified OK, player characters in the new save: {0}", "Weryfikacja OK, postacie graczy w nowym save: {0}"),
        ["lib.nogamedir"] = E("BG3 save folder not found (the game must have been run at least once)", "nie znalazłem folderu save'ów BG3 (gra musi być choć raz uruchomiona)"),
        ["lib.notchar"] = E("this is not a character file", "to nie jest plik postaci"),
        ["lib.notchar2"] = E("this is not a BG3 Party Tool character file", "to nie jest plik postaci z BG3 Party Tool"),
        ["lib.verify.missing"] = E("verification failed: the character is not among the player characters in the new save", "weryfikacja nie przeszła: postaci nie ma wśród postaci graczy w nowym save"),
        ["lib.notours"] = E("this save is not in a folder the tool manages; delete it by hand", "ten save nie leży w folderze obsługiwanym przez narzędzie; usuń go ręcznie"),
        ["lib.exists"] = E("a save named “{0}” already exists there", "save o nazwie „{0}” już tam jest"),
        // --- core: save file
        ["sf.nonewage"] = E("the save has no NewAge region (game version too old?)", "save nie ma regionu NewAge (za stara wersja gry?)"),
        ["sf.noavatar"] = E("no player character “{0}” (there are: {1})", "nie ma postaci gracza „{0}” (są: {1})"),
        ["sf.last"] = E("the only player character cannot be removed", "nie można usunąć jedynej postaci gracza"),
        ["sf.removed"] = E("NewAge: removed {0}", "NewAge: usunięto {0}"),
        ["sf.tag"] = E("NewAge: AVATAR tag removed", "NewAge: zdjęty tag AVATAR"),
        ["sf.flags"] = E("Characters: flags 0x{0:x} → 0x{1:x}, owner profile cleared", "Characters: flagi 0x{0:x} → 0x{1:x}, profil właściciela wyczyszczony"),
        ["sf.iscustom"] = E("NewAge: IsCustom marker removed", "NewAge: usunięto znacznik postaci własnej (IsCustom)"),
        ["sf.dummies"] = E("Characters: {0} character-creation dummies reserved for profile {1}…", "Characters: {0} lalki z kreatora zarezerwowane dla profilu {1}…"),
        ["sf.released"] = E("Characters: released the player's reservation on {0} character-creation dummy", "Characters: zwolniono rezerwację gracza na {0} postać z kreatora"),
        ["sf.meta.rm"] = E("meta/SaveInfo: removed from the party list", "meta/SaveInfo: usunięto z listy drużyny"),
        ["sf.osiris.rm"] = E("Osiris: removed {0} facts ({1})", "Osiris: usunięto {0} faktów ({1})"),
        ["sf.flag.rm"] = E("GEN_MaxPlayerCountReached flag cleared (there is a free party slot)", "Flaga GEN_MaxPlayerCountReached zdjęta (jest wolne miejsce w drużynie)"),
        ["sf.hidden"] = E("Character hidden from the world (off-stage, invisible)", "Postać schowana ze świata (off-stage, niewidoczna)"),
        ["sf.unhidden"] = E("Character back on stage", "Postać wraca na scenę"),
        ["sf.nomember"] = E("the save has no party member to copy data from", "w save'ie nie ma żadnego członka drużyny, nie ma od czego skopiować danych"),
        ["sf.added"] = E("NewAge: added Avatar, Player, ClientControl and party member", "NewAge: dodano Avatar, Player, ClientControl i członka drużyny"),
        ["sf.comp.add"] = E("NewAge: added to the party composition", "NewAge: dodano do składu drużyny"),
        ["sf.group.add"] = E("NewAge: character added to its player's group", "NewAge: postać dopisana do grupy gracza"),
        ["sf.profile.add"] = E("NewAge: profile {0} added to the player list", "NewAge: profil {0} dodany do listy graczy"),
        ["sf.flags.back"] = E("Characters: player flags and owner profile restored", "Characters: flagi gracza i profil właściciela przywrócone"),
        ["sf.meta.add"] = E("meta/SaveInfo: added to the party list", "meta/SaveInfo: dodano do listy drużyny"),
        ["sf.osiris.add"] = E("Osiris: added {0} facts", "Osiris: dodano {0} faktów"),
        ["sf.osiris.skip"] = E(", skipped {0} ({1})", ", pominięto {0} ({1})"),
        ["sf.slot.taken"] = E("slot taken by another character", "slot zajęty przez inną postać"),
        ["sf.flag.add"] = E("GEN_MaxPlayerCountReached flag set again (party full again)", "Flaga GEN_MaxPlayerCountReached przywrócona (drużyna znów pełna)"),
        ["sf.recruits.back"] = E("NewAge: companions {0} come back from {1}", "NewAge: towarzysze {0} wracają od {1}"),
        ["sf.comp.rm"] = E("NewAge: removed from the party composition", "NewAge: usunięto ze składu drużyny"),
        ["sf.profile.rm"] = E("NewAge: profile {0} removed from the player list", "NewAge: profil {0} usunięty z listy graczy"),
        ["sf.group.rm"] = E("NewAge: character removed from its player's group", "NewAge: postać usunięta z grupy swojego gracza"),
        ["sf.recruits.merge"] = E("cannot merge the recruit lists of {0} and {1}; pick another character", "nie da się scalić listy rekrutów {0} i {1}; wybierz inną postać"),
        ["sf.recruits.moved"] = E("NewAge: companions {0} now belong to {1}", "NewAge: towarzysze {0} należą teraz do {1}"),
        ["sf.cannot.old"] = E("export from an older tool version (no data to restore from)", "eksport ze starszej wersji narzędzia (bez danych do przywrócenia)"),
        ["sf.cannot.gone"] = E("the character no longer exists in this save", "tej postaci nie ma już w tym save'ie"),
        ["sf.cannot.already"] = E("the character is already a player character in this save", "ta postać już jest postacią gracza w tym save'ie"),
        ["sf.cannot.full"] = E("the party already has 4 player characters", "drużyna ma już 4 postacie graczy"),
    };
}
