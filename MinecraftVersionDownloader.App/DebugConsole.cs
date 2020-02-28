#if LOCAL
#define DEBUG
#endif
using System;
using System.Diagnostics;

namespace MinecraftVersionDownloader.App
{
    public static class DebugConsole
    {
        [Conditional("DEBUG")]
        public static void Write(string value)
            => Console.Write(value);

        [Conditional("DEBUG")]
        public static void WriteLine(string value)
            => Console.WriteLine(value);
    }
}
