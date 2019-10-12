using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftVersionDownloader.All
{
    public static class MinecraftHelper
    {
        private static AsyncLazy<JObject> VersionManifest { get; } = new AsyncLazy<JObject>(()
                => new Uri(@"https://launchermeta.mojang.com/mc/game/version_manifest.json").GetJsonAsync());

        public static async Task<(string release, string snapshot)> GetLatestAsync()
        {
            var latest = (JObject)(await VersionManifest)["latest"];
            return (latest["release"].ToObject<string>(), latest["snapshot"].ToObject<string>());
        }

        public static async Task<IEnumerable<VersionInfo>> GetVersionsInfoAsync(bool reverse = false)
            => ((reverse, (JArray)(await VersionManifest)["versions"]) switch
            {
                (false, var versions) => versions,
                (true, var versions) => versions.Reverse(),
            }).Select(v => v.ToObject<VersionInfo>());
    }
}
