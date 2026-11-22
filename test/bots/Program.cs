using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace TemporalRebirth.Bots;

/// <summary>
/// Reads commands from stdin: "join NAME", "NAME say TEXT", "NAME respawn", "NAME quit", "exit".
/// Only ever talks to this computer.
/// </summary>
internal static class Program
{
    [ModuleInitializer]
    internal static void ResolveGameAssemblies()
    {
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "Games/VintageStory");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            new[] { game, Path.Combine(game, "Lib"), Path.Combine(game, "Mods") }
                .Select(dir => Path.Combine(dir, name.Name + ".dll"))
                .Where(File.Exists)
                .Select(context.LoadFromAssemblyPath)
                .FirstOrDefault();
    }

    private static void Main(string[] args)
    {
        int port = int.Parse(args[0]);
        var bots = new Dictionary<string, Bot>();
        while (Console.ReadLine() is { } line)
        {
            string[] words = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            if (words[0] == "exit")
            {
                break;
            }

            if (words[0] == "join" && words.Length > 1)
            {
                if (!bots.TryGetValue(words[1], out Bot? bot))
                {
                    bots[words[1]] = bot = new Bot(words[1]);
                }

                bot.Connect("127.0.0.1", port);
                continue;
            }

            if (words.Length < 2 || !bots.TryGetValue(words[0], out Bot? target))
            {
                Console.WriteLine("? " + line);
                continue;
            }

            switch (words[1])
            {
                case "say": target.Say(words[2]); break;
                case "respawn": target.Respawn(); break;
                case "quit": target.Quit(); break;
                default: Console.WriteLine("? " + line); break;
            }
        }
    }
}
