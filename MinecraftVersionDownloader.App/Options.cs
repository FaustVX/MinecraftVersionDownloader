#if LOCAL
#define DEBUG
#endif
using System;
using System.Linq;

namespace MinecraftVersionDownloader.App
{
    internal static class Options
    {
        public static bool LongRun { get; }
        public static bool OnlyTags { get; }
        public static bool Debug { get; }

        static Options()
        {
            var args = Environment.GetCommandLineArgs()[1..].ToList();
            
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
