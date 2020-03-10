#if LOCAL
#define DEBUG
#endif
using System;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Core;
using System.Linq;
using System.Diagnostics;
using MinecraftVersionDownloader.All;
using GitNet = Git.Net.Git;
using FaustVX.Temp;
using Newtonsoft.Json.Linq;
using static FaustVX.Process.Process;
using System.Collections.Generic;
using Octokit;

namespace MinecraftVersionDownloader.App
{
    public static class Program
    {
        private static async Task Main(string[] args)
        {
#if LOCAL
            var git = new DirectoryInfo(@"D:\Desktop\MinecraftVanillaDatapack");
            Environment.CurrentDirectory = git.FullName;
            var tmp = git.Parent.CreateSubdirectory("tmp");
#else
            using var git = TemporaryDirectory.CreateTemporaryDirectory(setCurrentDirectory: true);
            var tmp = Directory.GetParent(git).CreateSubdirectory("tmp");
#endif
#if DEBUG
            Debugger.Break();
#endif
            System.Console.WriteLine(Environment.CurrentDirectory);
            (GitNet.Clone(args[0], checkout: false, localDirectory: ".") || GitNet.Reset(GitNet.Ref.HEAD))
                .ThrowIfNonZero(new Exception());

            ((DirectoryInfo)git).Then(".git", "info").File("exclude").AppendAllText("*.nbt" + Environment.NewLine);
            var github = new GitHubClient(new ProductHeaderValue("MinecraftVersionDownloader"))
            {
                Credentials = new Credentials(args[1])
            };

            if(Options.OnlyTags)
            {
                var lastMessage = GitNet.GetLastCommit()?.message;
                bool? is1_8 = null;
                foreach (var (version, indexFromHead) in (await MinecraftHelper.GetVersionsInfoAsync(reverse: false))
                    .WithIndex()
                    .SkipWhile(v => v.item.Id != lastMessage)
                    .If(Options.LongRun && !Options.Debug, versions => versions.DoEvery(10, after: _ => GitNet.Push(tags: true, force: true))))
                {
                    var packages = await version.Version;
#if DEBUG
                    var versionType = packages.Type switch
                    {
                        VersionType.Alpha => "α",
                        VersionType.Beta => "β",
                        _ => ""
                    };
                    System.Console.WriteLine($"{versionType}[{packages.Type} - {packages.Assets}] {version.Id}");
#endif
                    if(packages.Assets == "1.8")
                        is1_8 = true;
                    else if(is1_8 is true && packages.Assets != "1.9")
                        break;
                    
                    GitNet.Tag($"Version_{packages.Assets}", @ref: ^indexFromHead, force: false);
                    if(packages.Type is VersionType.Release)
                        GitNet.Tag($"Release_{packages.Id}", @ref: ^indexFromHead, force: false);
                }
                GitNet.Push(tags: true, force: true);
                return;
            }
#if DEBUG
            GitNet.Reset(^1, GitNet.ResetMode.Mixed);
#endif
            var lastCommit = LastCommitMessage();
            var java = FaustVX.Process.Process.CreateProcess("java");
            foreach (var version in (await MinecraftHelper.GetVersionsInfoAsync(reverse: true))
                .SkipWhile(v => v.Id != lastCommit)
                .Skip(1))
            {
                var startTime = Stopwatch.GetTimestamp();
                Console.WriteLine($"Next version: {version.Id}");
                Console.Title = $"{lastCommit} => {version.Id}";
                lastCommit = version.Id;

                DeleteFiles();

                var packages = await version.Version;

                var generated = ((DirectoryInfo)git).CreateSubdirectory("generated");

                var server = tmp.File("server.jar");
                await Task.WhenAll(packages.Server!.JAR.DownloadFileAsync(server), packages.Client.JAR.DownloadFileAsync(tmp), Task.Run(async () =>
                {
                    if (packages.Client.TXT is Uri txtClient)
                        generated.File("clientMapping.txt").WriteAllText(await txtClient.GetStringAsync());

                    if (packages.Server?.TXT is Uri txtServer)
                        generated.File("serverMapping.txt").WriteAllText(await txtServer.GetStringAsync());
                }), Task.Run(async () =>
                {
                    using var jarStream = await packages.Client.JAR.GetStreamAsync();
                    UnzipFromStream(jarStream, "assets data pack. version.json".Split(' '));
                }));
                GitNet.Add(all: true);

                System.Console.WriteLine("Create 'generated' files");
                java($"-cp {server.MakeRelativeTo(git)} net.minecraft.data.Main --dev --reports --input {((DirectoryInfo)git).MakeRelativeTo(git)}")
                    .StartAndWaitForExit();

                var reports = generated.Then("reports");

                ExtractItems(reports.File("items.json"));
                ExtractRegisties(reports.File("registries.json"));
                ExtractBlocks(reports.File("blocks.json"));
                ExtractLootTables((DirectoryInfo)git, generated, "data", "minecraft", "loot_tables");

                GitNet.Add(reports.MakeRelativeTo(git));
                GitNet.Add(generated.Then("assets").MakeRelativeTo(git));
                GitNet.Add(generated.Then("data").MakeRelativeTo(git));
                GitNet.Commit(version.Id, allowEmpty: true, date: version.ReleaseTime);

                GitNet.Tag($"Version_{packages.Assets}", force: true);
                if (version.Type is VersionType.Release)
                    GitNet.Tag($"Release_{packages.Id}", force: true);
                
                await UploadGithubRelease(github, packages);
                Console.WriteLine(TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime));
#if DEBUG
                return;
#endif                
            }

            DeleteFiles();

#if !DEBUG
            if (GitNet.Push(force:false))
                GitNet.Push(force:true, tags:true);
#endif

            static void DeleteFiles()
            {
                var root = new DirectoryInfo(".");
                foreach (var item in root.EnumerateFileSystemInfos().Where(d => d.Name != ".git"))
                {
                    if (item is DirectoryInfo dir)
                        dir.Delete(true);
                    else
                        item.Delete();
                }
            }

            async Task UploadGithubRelease(GitHubClient github, All.Version packages)
            {
                if (!(GitNet.Push(force:false) && GitNet.Push(force:true, tags:true)))
                    return;

                var result = await GetOrCreateRelease();

                var client = tmp.File("client.jar");
                await UploadRelease(client, $"client_{packages.Id}.jar");
                
                var server = tmp.File("server.jar");
                if (server.Exists)
                    await UploadRelease(server, $"server_{packages.Id}.jar");

                await CreateZip("assets");
                await CreateZip("data");

                var updateRelease = result.ToUpdate();
                updateRelease.Draft = false;
                updateRelease.Prerelease = !(packages.Type is VersionType.Release);
                await github.Repository.Release.Edit("FaustVX", "MinecraftVanillaDatapack", result.Id, updateRelease);
                
                
                async Task CreateZip(string folderName)
                {
                    var git1 = ((DirectoryInfo)git);
                    var folder = git1.Then(folderName);
                    var buffer = new byte[8 * 1024];
                    if(folder.Exists)
                    {
                        var zipFile = tmp.File($"{folderName}_{packages.Id}.zip");
                        using (var zip = new ZipOutputStream(zipFile.OpenWrite()))
                        {
                            if(git1.File("pack.png") is FileInfo { Exists: true, Name: var namePng } packPng)
                                PutEntry(packPng, namePng);
                            if(git1.File("pack.mcmeta") is FileInfo { Exists: true, Name: var nameMeta } packMeta)
                                PutEntry(packMeta, nameMeta);

                            foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
                                PutEntry(file, file.MakeRelativeTo(git)[2..]);

                            void PutEntry(FileInfo file, string name)
                            {
                                zip.PutNextEntry(new ZipEntry(name));
                                using (var stream = file.OpenRead())
                                    StreamUtils.Copy(stream, zip, buffer);
                            }
                        }
                        await UploadRelease(zipFile, zipFile.Name);
                    }
                }

                async Task UploadRelease(FileInfo file, string name)
                {
                    using (var archiveContents = file.OpenRead())
                        await github.Repository.Release.UploadAsset(result, new ReleaseAssetUpload()
                        {
                            FileName = name,
                            ContentType = "application/zip",
                            RawData = archiveContents
                        });
                }

                async Task<Release> GetOrCreateRelease()
                {
                    try
                    {
                        var release = await github.Repository.Release.Get("FaustVX", "MinecraftVanillaDatapack", $"Version_{packages.Assets}");
                        foreach (var asset in release.Assets)
                            await github.Repository.Release.DeleteAsset("FaustVX", "MinecraftVanillaDatapack", asset.Id);
                        return release;
                    }
                    catch (NotFoundException)
                    {
                        return await github.Repository.Release.Create("FaustVX", "MinecraftVanillaDatapack", new NewRelease($"Version_{packages.Assets}")
                        {
                            Draft = true,
                            Prerelease = !(packages.Type is VersionType.Release)
                        });
                    }
                }
            }
        }

        private static string? LastCommitMessage()
            => GitNet.GetLastCommit()?.message;

        private static bool CompareLastGitCommitMessage(string version)
            => !(GitNet.GetLastCommit()?.message.Contains(version, StringComparison.InvariantCultureIgnoreCase) ?? false);

        private static void UnzipFromStream(Stream zipStream, params string[] folderToUnzip)
        {
            DebugConsole.WriteLine($"Unzipping in {Environment.CurrentDirectory}");
            using var zipInputStream = new ZipInputStream(zipStream);
            while (zipInputStream.GetNextEntry() is ZipEntry { Name: var entryName })
            {
                Console.ResetColor();

                if (!folderToUnzip.Any(entryName.StartsWith))
                    continue;

                var buffer = new byte[4*1024];

                var fullZipToPath = entryName;
                var directoryName = Path.GetDirectoryName(fullZipToPath);
                if ((directoryName?.Length ?? 0) > 0)
                    Directory.CreateDirectory(directoryName);

                if (Path.GetFileName(fullZipToPath).Length == 0)
                    continue;

                Console.ResetColor();
                DebugConsole.Write($"{entryName}: ");
                DebugConsole.Write($"Unzipping");

                using (var stream = new FileInfo(fullZipToPath).Create())
                    StreamUtils.Copy(zipInputStream, stream, buffer);

                Console.ForegroundColor = ConsoleColor.Green;
                DebugConsole.WriteLine($" X");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.ResetColor();
            DebugConsole.WriteLine($"Unzipped");
        }

        private static void ExtractItems(FileInfo items)
        {
            if (!items.Exists)
                return;
            
            var reports = items.Directory.CreateSubdirectory("registries");

            var obj = JObject.Parse(items.ReadAllText());
            reports.File("item.txt").WriteAllLines(obj.Properties().Select(p => p.Name));
        }

        private static void ExtractRegisties(FileInfo registries)
        {
            if (!registries.Exists)
                return;
            
            var reports = registries.Directory.CreateSubdirectory("registries");

            foreach (var obj in JObject.Parse(registries.ReadAllText()).Properties())
            {
                var (key, entries) = (obj.Name.Split(':')[1], (JObject)obj.Value["entries"]);
                reports.File($"{key}.txt").WriteAllLines(entries.Properties().Select(p => p.Name));
            }
        }

        private static void ExtractBlocks(FileInfo blocks)
        {
            if (!blocks.Exists)
                return;
            
            var reports = blocks.Directory;

            var jObj = JObject.Parse(blocks.ReadAllText());

            foreach (var obj in jObj.Properties())
                obj.Value = obj.Value["properties"] ?? new JObject();
            
            reports.File("blocks.simple.json").WriteAllText(jObj.ToString());
        }

        private static void ExtractLootTables(DirectoryInfo git, DirectoryInfo generated, params string[] relativeFolders)
        {
            git = git.Then(relativeFolders);
            if(!git.Exists)
                return;
            generated = generated.Then(relativeFolders);

            foreach (var folder in git.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                ExtractLootTables(git, git, generated, folder.Name);

            static void ExtractLootTables(DirectoryInfo lootTable, DirectoryInfo currentDir, DirectoryInfo generated, string relativeFolder)
            {
                currentDir = currentDir.Then(relativeFolder);
                generated = generated.CreateSubdirectory(relativeFolder);

                foreach (var folder in currentDir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                    ExtractLootTables(lootTable, currentDir, generated, folder.Name);

                foreach (var file in currentDir.EnumerateFiles("*.json", SearchOption.TopDirectoryOnly))
                    WriteToFile(ExtractFile(file), generated.File(file.Name));
                
                Dictionary<string, (float weight, float qty)> ExtractFile(FileInfo file)
                {
                    var json = JObject.Parse(file.ReadAllText());
                    var items = new Dictionary<string, (float weight, float qty)>();
                    if((JArray)json["pools"] is null)
                        return items;

                    foreach (var pool in ((JArray)json["pools"]).Cast<JObject>())
                    {
                        var rolls = GetAverage(pool["rolls"]) ?? 1;
                        foreach (var entry in ((JArray)pool["entries"]).Cast<JObject>())
                        {
                            var setCount = GetFunction(entry, "set_count")?["count"];
                            
                            switch (entry.Value<string>("type").Split(':')[^1])
                            {
                                case "item":
                                    AddItemWeight((string)entry.Value<string>((object)"name"), entry.Value<float>((object)"weight"), GetAverage(setCount) ?? 1);
                                    break;
                                case "loot_table":
                                {
                                    var name = entry.Value<string>("name");
                                    var weight = entry.Value<float>("weight");
                                    foreach (var item in ExtractFile(lootTable.File(name.Split(':')[^1] + ".json")))
                                        AddItemWeight(item.Key, item.Value.weight * weight, item.Value.qty);
                                    break;
                                }
                                case "empty":
                                    AddItemWeight((string)"", entry.Value<float>((object)"weight"), GetAverage(setCount) ?? 1);
                                    break;
                            }

                            void AddItemWeight(string name, float weight, float qty)
                            {
                                if(GetAverage(GetFunction(entry, "set_data")?["data"]) is float d)
                                    name += ":" + d;
                                if(GetFunction(entry, "set_nbt")?.Value<string>("tag") is string n)
                                    name += n;
                                if(GetAverage(GetFunction(entry, "enchant_with_levels")?["levels"]) is float l)
                                    name += $"/enchant:{l:#.#}L";
                                
                                if(items.TryGetValue(name, out var value))
                                    items[name] = ((weight * rolls) + value.weight, (value.weight * value.qty + weight * rolls * qty) / (value.weight + weight * rolls));
                                else
                                    items.Add(name, (weight * rolls, qty));
                            }
                        }

                        static JObject? GetFunction(JObject entry, string function)
                            => ((JArray)entry["functions"])
                                ?.Cast<JObject>()
                                .FirstOrDefault(func => func.Value<string>("function")
                                    .Split(':')[^1] == function);

                        static float? GetAverage(JToken? token)
                            => token switch
                            {
                                JObject obj => (obj.Value<float>("max") - obj.Value<float>("min")) / 2 + obj.Value<float>("min"),
                                JToken{ Type: JTokenType.Integer } t => t.Value<float>(),
                                JToken{ Type: JTokenType.Float } t => t.Value<float>(),
                                _ => null,
                            };
                    }

                    return items;
                }

                void WriteToFile(Dictionary<string, (float weight, float qty)> datas, FileInfo file)
                {
                    var totalWeight = datas.Select(kvp => kvp.Value.weight).Sum();

                    var jObj = new JObject();
                    foreach (var (id, (weight, qty)) in datas)
                    {
                        var obj = new JObject();
                        obj.Add("weight", weight is 0 ? float.PositiveInfinity : weight);
                        obj.Add("qty", qty);
                        obj.Add("percent", weight is 0 ? 100 : weight / totalWeight * 100);
                        jObj.Add(id, obj);
                    }

                    file.WriteAllText(jObj.ToString());
                }
            }
        }
    }
}
