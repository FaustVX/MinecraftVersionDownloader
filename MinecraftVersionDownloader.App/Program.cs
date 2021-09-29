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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static FaustVX.Process.Process;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MinecraftVersionDownloader.App
{
    public static class Program
    {
        private static async Task Main()
        {
#if DEBUG
            Debugger.Break();
#endif
            Environment.CurrentDirectory = ((DirectoryInfo)Globals.Git).FullName;
            System.Console.WriteLine(Environment.CurrentDirectory);
            (GitNet.Clone(Options.GitRepo.ToString(), checkout: false, localDirectory: ".") || GitNet.Reset(GitNet.Ref.HEAD))
                .ThrowIfNonZero(new Exception());

            ((DirectoryInfo)Globals.Git).Then(".git", "info").File("exclude").AppendAllText("*.nbt" + Environment.NewLine);

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
                .Skip(1)
                .Where(version => Regex.IsMatch(version.Id, @"^(\d\.\d+(?:\.\d+)?(?:[- ](?:pre(?:-release )?|rc)\d+)?|\d{2}w\d{2}\w)$", RegexOptions.IgnoreCase)))
            {
                var startTime = Stopwatch.GetTimestamp();
                Console.WriteLine($"Next version: {version.Id}");
                Console.Title = $"{lastCommit} => {version.Id}";
                lastCommit = version.Id;

                DeleteFiles();

                var packages = await version.Version;

                var generated = ((DirectoryInfo)Globals.Git).CreateSubdirectory("generated");

                var server = Globals.Tmp.File("server.jar");
                await Task.WhenAll(packages.Server!.JAR.DownloadFileAsync(server), packages.Client.JAR.DownloadFileAsync(Globals.Tmp), Task.Run(async () =>
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
                java($"-jar {server.MakeRelativeTo(Globals.Git)} DbundlerMainClass=net.minecraft.data.Main --dev --reports --input {((DirectoryInfo)Globals.Git).MakeRelativeTo(Globals.Git)}")
                    .StartAndWaitForExit();

                var reports = generated.Then("reports");

                ExtractItems(reports.File("items.json"));
                ExtractRegisties(reports.File("registries.json"));
                ExtractBlocks(reports.File("blocks.json"));
                ExtractRecipes(reports.File("recipes.json"), ((DirectoryInfo)Globals.Git).Then("data", "minecraft", "tags", "items"), ((DirectoryInfo)Globals.Git).Then("data", "minecraft", "recipes"));
                ExtractLootTables((DirectoryInfo)Globals.Git, generated, "data", "minecraft", "loot_tables");
                AddPackMcmeta(JObject.Parse(File.ReadAllText(Path.Combine("assets", "minecraft", "lang", "en_us.json"))), JObject.Parse(File.ReadAllText("version.json")));
                generated.File(version.Id + ".json").WriteAllText((await version.Json.Value).ToString(Formatting.Indented));

                using(var serverDir = new FaustVX.Temp.TemporaryDirectory(generated.Then("server"), true))
                {
                    java($"-jar {server.MakeRelativeTo(serverDir)} --nogui").StartAndWaitForExit();
                    GitNet.Add("*.properties");
                }

                GitNet.Add(reports.MakeRelativeTo(Globals.Git));
                GitNet.Add(generated.Then("assets").MakeRelativeTo(Globals.Git));
                GitNet.Add(generated.Then("data").MakeRelativeTo(Globals.Git));
                GitNet.Add("*.mcmeta");
                GitNet.Add(generated.File("*.json").MakeRelativeTo(Globals.Git));
                GitNet.Commit(version.Id, allowEmpty: true, date: version.ReleaseTime);

                GitNet.Tag($"Version_{packages.Assets}", force: true);
                if (version.Type is VersionType.Release)
                    GitNet.Tag($"Release_{packages.Id}", force: true);

                await Globals.UploadGithubRelease(packages);
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
                CreateLocalReportFileInfo(reports, key.Split('/')).WriteAllLines(entries.Properties().Select(p => p.Name));

                static FileInfo CreateLocalReportFileInfo(DirectoryInfo reports, string[] keys)
                {
                    if (keys.Length == 1)
                        return reports.File($"{keys[^1]}.txt");
                    else
                        return CreateLocalReportFileInfo(reports.CreateSubdirectory(keys[0]), keys[1..]); //reports.Then(keys[..^1]).File($"{keys[^1]}.txt");
                }
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

        private static void ExtractRecipes(FileInfo recipesFile, DirectoryInfo itemTags, DirectoryInfo data)
        {
            var tags = itemTags.EnumerateFiles().ToDictionary(file => Path.GetFileNameWithoutExtension(file.Name), file => CreateTags(file).ToArray());
            var recipes = new JObject(data.EnumerateFiles()
                .Select(Helper.ReadAllText)
                .Select(JObject.Parse)
                .SelectMany(GetInfo)
                .OrderBy(t => t.ingredient)
                .GroupBy(t => t.ingredient)
                .Select(g => new JProperty(g.Key, new JObject(g.OrderBy(t => t.recipe).GroupBy(t => t.recipe).Select(g1 => new JProperty(g1.Key, new JArray(g1.Select(t => t.result).ToHashSet()))))))
            );

            recipesFile.WriteAllText(recipes.ToString());

            IEnumerable<(string ingredient, JToken result, string recipe)> GetInfo(JObject obj)
            {
                var type = obj["type"].ToString();
                return type switch
                {
                    "minecraft:blasting" or "minecraft:smelting" or "minecraft:campfire_cooking" or "minecraft:smoking"
                        => GetItems(obj["ingredient"], tags).Select(i => (i, (JToken)new JObject(new JProperty("result", obj["result"]), new JProperty("experience", obj["experience"]), new JProperty("cookingtime", obj["cookingtime"])), type)),
                    "minecraft:crafting_shaped" => ((JObject)obj["key"]).Properties().Select(p => p.Value).SelectMany(i => GetItems(i, tags)).Select(i => (i, obj["result"]["item"], type)),
                    "minecraft:crafting_shapeless" => GetItems(obj["ingredients"], tags).Select(i => (i, obj["result"]["item"], type)),
                    "minecraft:smithing" => GetItems(obj["base"], tags).Concat(GetItems(obj["addition"], tags)).ToHashSet().Select(i => (i, obj["result"]["item"], type)),
                    "minecraft:stonecutting" => GetItems(obj["ingredient"], tags).Select(i => (i, obj["result"], type)),
                    _ => Enumerable.Empty<(string ingredient, JToken result, string recipe)>()
                };
            }

            static IEnumerable<string> GetItems(JToken token, Dictionary<string, string[]> tags)
            {
                switch (token)
                {
                    case JArray arr:
                        return arr.SelectMany(t => GetItems(t, tags));
                    case JObject { First: JProperty { Name: "item", Value: JToken { Type: JTokenType.String } item } }:
                        return Enumerable.Repeat(item.ToString(), 1);
                    case JObject { First: JProperty { Name: "tag", Value: JToken { Type: JTokenType.String } tag } }:
                        return tags[tag.ToString().Split(':')[1]];
                    default:
                        return Enumerable.Empty<string>();
                }
            }

            static IEnumerable<string> CreateTags(FileInfo file)
            {
                foreach (var item in ((JArray)JObject.Parse(file.ReadAllText())["values"]).Select(item => item.ToString()))
                {
                    if (item.StartsWith('#'))
                        foreach (var i in CreateTags(file.Directory.File(item.Split(':')[1] + ".json")))
                            yield return i;
                    else
                        yield return item;
                }
            }
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

        private static void AddPackMcmeta(JObject lang, JObject version)
        {
            if(((DirectoryInfo)Globals.Git).File("pack.mcmeta").Exists)
                return;

            AddPackMcmeta("data", "data");
            AddPackMcmeta("resource", "assets");

            void AddPackMcmeta(string packType, string folder)
            {
                var obj = new JObject()
                    {
                        {
                            "pack", new JObject()
                            {
                                { "description", lang[packType + "Pack.vanilla.description"].Value<string>() },
                                { "pack_format", version["pack_version"][packType].Value<string>() }
                            }
                        }
                    };

                    ((DirectoryInfo)Globals.Git).Then(folder).File("pack.mcmeta").WriteAllText(obj.ToString(Formatting.Indented));
            }
        }
    }
}
