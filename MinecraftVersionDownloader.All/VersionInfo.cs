using System;
using Newtonsoft.Json.Linq;

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
            Json = new AsyncLazy<JObject>(async () => await url.GetJsonAsync());
            Version = new AsyncLazy<Version>(async () => GetVersionsAsync(await Json.Value));
        }

        public string Id { get; }
        public VersionType Type { get; }
        public DateTime Time { get; }
        public DateTime ReleaseTime { get; }
        public AsyncLazy<Version> Version { get; }
        public AsyncLazy<JObject> Json { get; }

        private static Version GetVersionsAsync(JObject jObject)
            => jObject.ToObject<Version>();

        public override string? ToString()
            => $"[{Type}] {Id}";
    }
}
