#if LOCAL
#define DEBUG
#endif
using System;
using System.Linq;

namespace MinecraftVersionDownloader.App
{
    internal static class Options
    {
        public static Uri GitRepo { get; }
        public static string GitHubCreditentials { get; }
        public static bool LongRun { get; }
        public static bool OnlyTags { get; }
        public static bool Debug { get; }

        static Options()
        {
            var args = Environment.GetCommandLineArgs()[1..].ToList();

            GitRepo = new Uri(args[0]);
            GitHubCreditentials = args[1];

            args.RemoveRange(0, 2);
            
            if(HasSwitch('l', "long-run"))
                LongRun = true;
            
            if(HasSwitch('t', "tags"))
                OnlyTags = true;
#if DEBUG
            Debug = true;
#endif
            bool HasSwitch(char shortCmd, string longCmd)
                => args.Remove($"-{shortCmd}") || args.Remove($"--{longCmd}");
        }
    }
}
