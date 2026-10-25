using Avalonia;
using Bg3PartyTool.Core;

namespace Bg3PartyTool;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--")) return Cli(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    // BG3PartyTool --list <save.lsv> | --import <file> | --remove <save.lsv> <name> [recruits-to]
    //              | --exports <save.lsv> | --restore <save.lsv> <export.postac.json> [profile] | --game-dir
    //              (--remove/--restore take --to-game to write straight into BG3's saves)
    static int Cli(string[] args)
    {
        var toGame = args.Contains("--to-game");
        try
        {
            switch (args[0])
            {
                case "--list":
                    var s = Library.Open(args[1]);
                    Console.WriteLine($"{s.SaveName}  ({s.CurrentLevel}, {s.GameVersion}, in game: \"{s.InGameName}\")");
                    foreach (var a in s.Avatars)
                        Console.WriteLine($"  {a.Name,-16} {a.Race,-14} {a.Classes,-24} L{a.Level} XP {a.Xp,-6} {(a.IsHost ? "host " : "")}{(a.IsMe ? "you " : "")}profile {a.Profile}  recruits: {string.Join(", ", a.Recruits)}");
                    return 0;
                case "--import":
                    foreach (var f in Library.Import(args[1])) Console.WriteLine(f);
                    return 0;
                case "--remove":
                    var rest = args.Where(x => x != "--to-game").ToArray();
                    var r = Library.RemoveAvatar(rest[1], rest[2], rest.Length > 3 ? rest[3] : null, toGame, Console.WriteLine);
                    Console.WriteLine($"export: {r.Export}");
                    return 0;
                case "--restore":
                    var ra = args.Where(x => x != "--to-game").ToArray();
                    Library.RestoreAvatar(ra[1], ra[2], ra.Length > 3 ? ra[3] : null, true, toGame, Console.WriteLine);
                    return 0;
                case "--repack": // diagnostic: rewrite a save through the tool without changing anything
                    var rp = Library.Open(args[1]);
                    Console.WriteLine(rp.Write(toGame ? Library.GameSaveDir()! : Library.OutputDir, rp.SaveName + "_kopia"));
                    return 0;
                case "--rename":
                    Console.WriteLine(Library.RenameSave(args[1], args[2], Console.WriteLine));
                    return 0;
                case "--delete":
                    Library.DeleteSave(args[1]);
                    return 0;
                case "--game-dir":
                    Console.WriteLine(Library.GameSaveDir() ?? "(nie znaleziono BG3)");
                    return 0;
                case "--exports":
                    foreach (var e in Library.FindExports(Library.Open(args[1])))
                        Console.WriteLine($"{e.Info.Name,-16} {e.Info.Exported}  {(e.Problem ?? "restorable")}  {e.Path}");
                    return 0;
                default:
                    Console.Error.WriteLine("usage: --list <save.lsv> | --import <file.lsv|.rar|.zip> | --remove <save.lsv> <name> [recruits-to] [--to-game] | --exports <save.lsv> | --restore <save.lsv> <postac.json> [profile] [--to-game] | --rename <save.lsv> <name> | --delete <save.lsv> | --game-dir");
                    return 2;
            }
        }
        catch (SaveException e)
        {
            Console.Error.WriteLine($"Error: {e.Message}");
            return 1;
        }
    }
}
