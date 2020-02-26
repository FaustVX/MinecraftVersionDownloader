using Newtonsoft.Json.Linq;
using System;

namespace MinecraftVersionDownloader.All
{
    public sealed class Version
    {
        public Version(string assets, JObject downloads, string id, DateTime releaseTime, VersionType type)
        {
            Assets = assets;
            var client = (JObject)downloads["client"];
            var server = (JObject)downloads["server"];
            Client = new Download((JProperty)client.Parent);
            Server = server is null ? null : new Download((JProperty)server.Parent);
            Id = id;
            ReleaseTime = releaseTime;
            Type = type;
        }

        public string Assets { get; }
        public Download Client { get; }
        public Download? Server { get; }
        public string Id { get; }
        public DateTime ReleaseTime { get; }
        public VersionType Type { get; }

        public override string? ToString()
            => $"[{Assets} - {Type}] {Id}";
    }

    public sealed class Download
    {
        public Download(JProperty side)
        {
            JAR = side.First["url"].ToObject<Uri>();
            TXT = side.Next is JProperty mapping && mapping.Name == $"{side.Name}_mappings" ? mapping?.First["url"].ToObject<Uri>() : null;
            JarSize = side.First["size"].ToObject<int>();
        }

        public Uri JAR { get; }
        public Uri? TXT { get; }
        public int JarSize { get; }
    }
}
