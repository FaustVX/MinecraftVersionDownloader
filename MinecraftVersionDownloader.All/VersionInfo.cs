using System;
using System.Threading.Tasks;

namespace MinecraftVersionDownloader.All
{
    public sealed class VersionInfo
    {
        public VersionInfo(string id, VersionType type, Uri url, DateTime time, DateTime releaseTime)
        {
            Id = id;
            Type = type;
            Time = time;
            ReleaseTime = releaseTime;
            Version = new AsyncLazy<Version>(() => GetVersionsAsync(url));
        }

        public string Id { get; }
        public VersionType Type { get; }
        public DateTime Time { get; }
        public DateTime ReleaseTime { get; }
        public AsyncLazy<Version> Version { get; }

        private static async Task<Version> GetVersionsAsync(Uri uri)
            => (await uri.GetJsonAsync()).ToObject<Version>();

        public override string? ToString()
            => $"[{Type}] {Id}";
    }
}
