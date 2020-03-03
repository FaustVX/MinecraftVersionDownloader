using System;
using System.Linq;

namespace MinecraftVersionDownloader.App
{
    internal static class Options
    {
        public static bool LongRun { get; }

        static Options()
        {
            var args = Environment.GetCommandLineArgs()[1..].ToList();
            
            if(HasSwitch('l', "long-run"))
                LongRun = true;

            bool HasSwitch(char shortCmd, string longCmd)
                => args.Remove($"-{shortCmd}") || args.Remove($"--{longCmd}");
        }
    }
}
