# BG3 Party Tool

Wycina postać gracza (awatara) z save'a Baldur's Gate 3, żeby nowa osoba dołączająca do kampanii
w multi dostała kreator postaci zamiast cudzej postaci.

## Użycie

- Linux: `dist/linux-x64/BG3PartyTool`
- Windows: `dist/win-x64/BG3PartyTool.exe`

Przeciągnij na okno save `.lsv` albo archiwum `.rar`/`.zip` z save'ami (albo wybierz save z gry z listy).

- **Odłóż postać**: usuwa ją z drużyny i zapisuje `<Postać>.postac.json`. Powstaje nowy save `<nazwa>_bez_<Postać>`.
  Domyślnie postać jest też chowana ze świata (off-stage + niewidzialność, tak jak gra chowa swoje postacie). Bez tego
  zostaje na miejscu jako NPC. W obu przypadkach nadal istnieje w save'ie ze swoim ekwipunkiem, więc da się ją przywrócić.
- **Odłożone postacie → Przywróć**: oddaje odłożonej postaci status gracza w dowolnym późniejszym save'ie tej samej
  kampanii, pierwotnemu albo innemu graczowi, który ma już postać w save'ie. Można też oddać jej towarzyszy, których
  przejął ktoś inny. Powstaje `<nazwa>_z_<Postać>`. Plik postaci z innego komputera wczytasz przyciskiem
  **Wczytaj postać z pliku…** albo przeciągając go na okno.

Oryginalny save zawsze zostaje nietknięty. Wynik daj hostowi (twórcy kampanii) do
`%LOCALAPPDATA%\Larian Studios\Baldur's Gate 3\PlayerProfiles\Public\Savegames\Story\`.

Przywracanie działa w obrębie kampanii, bo postać nadal istnieje w świecie jako NPC. Przenoszenie postaci do innej
kampanii nie jest obsługiwane.

Dane narzędzia (wgrane save'y, wyniki): `%LOCALAPPDATA%\BG3PartyTool` / `~/.local/share/BG3PartyTool`.

CLI (bez okna): `BG3PartyTool --list <save.lsv>`, `--import <plik>`, `--remove <save.lsv> <postać> [komu-towarzysze]`,
`--exports <save.lsv>`, `--restore <save.lsv> <postać.postac.json> [profil]`.

## Co dokładnie jest zmieniane

Usunięta postać zostaje w świecie jako zwykły NPC. Z save'a znika to, co czyni ją postacią gracza:

| Miejsce | Zmiana |
|---|---|
| `Globals.lsf` → blob ECS `NewAge` (LSMF) | elementy `AvatarComponent`, `PlayerComponent`, `ClientControlComponent`, `party.MemberComponent`; wpis w `party.CompositionComponent`; profil gracza w `party.UserSnapshotComponent`; towarzysze z `recruit.RecruitedBy`/`Recruiter` przechodzą do wybranej postaci |
| `Globals.lsf` → region `Characters` | flagi gracza (IsPlayer, Multiplayer, InParty, HostControl, CharacterControl) i profil właściciela |
| `StorySave.bin` (Osiris) | fakty postaci w `DB_Players`, `DB_Avatars`, `DB_PartyMembers`, `DB_PartOfTheTeam`, `DB_GLO_Playable` i kilku pokrewnych |
| `meta.lsf`, `SaveInfo.json` | wpis na ekranie wczytywania |
| schowanie (opcja) | flagi OffStage + IgnoresTriggers, bez Activated; elementy `OffStageComponent` i `InvisibilityComponent` (źródło 4, pozycja postaci), tak jak u postaci schowanych przez grę |

Obok nowego save'a zapisuje się `<Postać>.postac.json` (format 2) ze wszystkim, co wycięto: elementy ECS, ID członka
drużyny, wpis ekranu wczytywania, kolumny i oryginalne bajty faktów Osirisa oraz `GameID` kampanii.

Przywracanie robi to samo w drugą stronę. Nowe dane bloba są dopisywane na końcu obszaru danych (blok nazw i tablica
komponentów przesuwają się, offset w nagłówku jest aktualizowany), a tablice komponentów, które rosną, są przenoszone
tam w całości. Istniejące dane się nie ruszają, więc żaden wskaźnik się nie psuje. Fakty Osirisa wracają jako
oryginalne bajty gry. Fakty slotów awatara (flagi wyrzucania, frakcja `Hero_PlayerN`) są pomijane, jeśli w międzyczasie
zajęła je inna postać. Cykl odłóż i przywróć daje save semantycznie identyczny z oryginałem: te same fakty Osirisa
i te same komponenty ECS (po rozwiązaniu wskaźników).

Edycje bloba nie przesuwają danych między komponentami (tylko zmniejszanie liczników i kompaktowanie zakresów
w miejscu), a fakty Osirisa są wycinane bajtowo z oryginalnego pliku. LSLib zapisuje flagi wartości inaczej niż gra,
więc jego zapis służy tylko do namierzenia bajtów, a wynik jest weryfikowany ponownym odczytem.

Testowane na save'ach z wersji gry 4.1.1.x (LSMF 0x08000101). Inna wersja bloba jest odrzucana z komunikatem.

## Budowanie

Wymaga .NET SDK 10. `./build.sh` buduje obie binarki (cross-compile z Linuksa działa).
`vendor/lslib` to [LSLib](https://github.com/Norbyte/lslib) v1.20.4 (ExportTool). Font Cinzel: SIL OFL (`src/Assets/Cinzel-OFL.txt`).

Uwaga na LSLib: `Packager.CreatePackage` filtruje pliki po pełnej ścieżce (katalog z kropką, np. `.local`, wycina
wszystko), a przy `ExcludeHidden=false` nie dodaje żadnego pliku, więc pakowanie składa listę plików samo.

Sprawdzanie manipulacji: gra ostrzega „zapisany stan gry wykrył manipulację”, jeśli pole MD5 w nagłówku LSPK v18 nie
zawiera skrótu zawartości. Skrót to MD5 z rozpakowanej zawartości wszystkich plików posortowanych po ścieżce małymi
literami, do każdego bajtu wyniku +1. LSLib wpisuje tam zera, więc `SaveFile.StampHash` łata nagłówek po spakowaniu
(sprawdzone na nietkniętych save'ach z 4.1.1).

Suma kontrolna NewAge: nagłówek bloba LSMF (offset 8) trzyma MurmurHash3 x64_128 z danych za nagłówkiem
(blockOffset + blockSize bajtów), ziarno 0xf0015eed5eedf001 w obu połówkach, wynik h1+h2. Bez niej gra przerywa
wczytywanie komunikatem „Wyłączenie serwera”. `Lsmf.ToArray` ją przelicza, a konstruktor sprawdza na wejściu.
Diagnostyka: `BG3PT_STEPS` (np. `egh`) ogranicza odkładanie do wybranych kroków, `BG3PT_SAVEID` zmienia numer save'a
w menu gry, `--repack` przepakowuje save bez zmian. `tools/bg3-drugi-gracz.sh` odpala drugą kopię gry z nowym
profilem do testów dołączania.

Flaga „pełna drużyna graczy”: skrypt `PROC_CheckPartyFull` ustawia globalną flagę `GEN_MaxPlayerCountReached`
(823b5064…), gdy liczba graczy plus zarezerwowane miejsca osiągnie `GetMaxPartySize()`, a przelicza ją tylko przy
kilku zdarzeniach. Po wycięciu gracza zostałaby włączona i blokowała towarzyszy oraz kreator dla dołączających, więc
odkładanie postaci ją zdejmuje (Globals `OsirisVariableHelper/.../FlagMap` + fakt `DB_GlobalFlag`), a przywracanie
ustawia z powrotem, gdy graczy znów jest 4.

Dlaczego dołączający nie dostawał kreatora (ustalone debuggerem na hoście, `tools/trace-join.sh`): przy dołączaniu
`esv::JoiningProtocol` zbiera kandydatów z komponentem `tag::AvatarComponent`, który silnik wyprowadza z tagu
fabularnego **AVATAR** (306b9b05-1057-4770-aa17-01af21acd650), a nie z zapisanego `game.avatar.v0.AvatarComponent`.
Taka postać bez podłączonego użytkownika (także odesłana do szafy Marnieja, także nadmiarowa postać podłączonego
gracza) jest wręczana nowemu graczowi; dopiero gdy nie ma żadnej, serwer zgłasza gracza do
`esv::character_creation::System` i rusza kreator. Odkładanie zdejmuje więc tag AVATAR z
`game.tags.v0.OsirisComponent`, przywracanie go oddaje. Lalki z kreatora (`SYS_CC_I`) nie biorą w tym udziału.
Wniosek praktyczny: na czas tworzenia postaci przez nową osobę host nie może mieć drugiej postaci (dostałby ją
dołączający), trzeba ją odłożyć i przywrócić po kreatorze.

Narzędzia diagnostyczne w `tools/`: `bg3-drugi-gracz.sh` (druga kopia gry z własnym profilem i emulatorem Steam API,
do testów dołączania na jednym komputerze), `bg3-host-log.sh` (host z logiem silnika; natywna wersja pisze logi do
`~/.local/state/Larian Studios/Baldur's Gate 3/logs/`), `trace-join.sh` + `trace-join.py` (gdb na hoście: decyzje
dołączania i kreatora, adresy dla 4.1.1.7631656).

Interfejs jest po angielsku (domyślnie) i po polsku; przełącznik języka jest w lewym górnym rogu, wybór zapisuje się w
`settings.json` w folderze danych narzędzia. Poza odkładaniem i przywracaniem postaci narzędzie potrafi wgrać save z dysku
(przycisk lub przeciągnięcie), zmienić nazwę save'a (nowy folder `<prefiks>__<nazwa>` i plik `<nazwa>.lsv`, z opcją
zamiany szybkiego/autozapisu na zapis ręczny, bo tylko te gra pokazuje w menu pod własną nazwą) oraz usunąć save
(tylko z folderów, którymi zarządza: Story gry, wgrane, wyniki). CLI: `--rename <save.lsv> <nazwa> [--manual]`,
`--delete <save.lsv>`.
