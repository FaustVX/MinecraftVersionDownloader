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
using HeadsTails;
using FaustVX.Temp;
using Newtonsoft.Json.Linq;

namespace MinecraftVersionDownloader.App
{
    public static class Program
    {
        private static async Task Main(string[] args)
        {
#if LOCAL
#if DEBUG
            Debugger.Break();
#endif
            args = @"https://github.com/FaustVX/MinecraftVanillaDatapack.git assets data pack. version.json".Split();
            var git = new DirectoryInfo(Environment.CurrentDirectory).CreateSubdirectory("MC");
            Environment.CurrentDirectory = git.FullName;
            var tmp = git.Parent.CreateSubdirectory("tmp");
#else
            using var git = TemporaryDirectory.CreateTemporaryDirectory(setCurrentDirectory: true);
            var tmp = Directory.GetParent(git).CreateSubdirectory("tmp");
#endif
            System.Console.WriteLine(Environment.CurrentDirectory);
            GitNet.Clone(args.HeadTail(out args), checkout: false, localDirectory: ".");
#if DEBUG
            GitNet.Reset(^1, GitNet.ResetMode.Mixed);
#endif
            long startTime = 0;
            var lastCommit = LastCommitMessage();
            foreach (var version in (await MinecraftHelper.GetVersionsInfoAsync(reverse: true))
                .SkipWhile(v => v.Id != lastCommit)
                .Skip(1))
            {
                Console.WriteLine($"Next version: {version.Id}");
                Console.Title = $"{lastCommit} => {version.Id}";
                lastCommit = version.Id;

                DeleteFiles();

                var packages = await version.Version;

                using var jarStream = await packages.Client.JAR.GetStreamAsync();
                startTime = UnzipFromStream(jarStream, args);

                Console.ResetColor();

                GitNet.Add(all: true);

                using (var file = tmp.File("server.jar").Create())
                using (var jar = await packages.Server!.JAR.GetStreamAsync())
                    StreamUtils.Copy(jar, file, new byte[8 * 1024]);

                System.Console.WriteLine("Create 'generated' files");
                var generated = ((DirectoryInfo)git).CreateSubdirectory("generated");

                var extract = Process.Start(@"java", "-cp ../tmp/server.jar net.minecraft.data.Main --all");

                if(packages.Client.TXT is Uri txtClient)
                    generated.File("clientMapping.txt").WriteAllText(await txtClient.GetStringAsync());

                if(packages.Server?.TXT is Uri txtServer)
                    generated.File("serverMapping.txt").WriteAllText(await txtServer.GetStringAsync());

                GitNet.Add(@"generated/*Mapping.txt");

                extract.WaitForExit();

                var reports = generated.CreateSubdirectory("reports");

                ExtractItems(reports.File("items.json"));
                ExtractRegisties(reports.File("registries.json"));
                ExtractBlocks(reports.File("blocks.json"));

                GitNet.Add(@"generated/reports/*");
                GitNet.Commit(version.Id, out _, allowEmpty: true, date: version.ReleaseTime);

                Console.WriteLine(TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime));
            }

            DeleteFiles();

#if !DEBUG
            if (GitNet.Push(force:false))
                GitNet.Push(force:true, tags:true);
#endif

            //Console.ReadLine();

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

        private static long UnzipFromStream(Stream zipStream, params string[] folderToUnzip)
        {
            var startTime = Stopwatch.GetTimestamp();
            DebugConsole.WriteLine($"Unzipping in {Environment.CurrentDirectory}");
            using var zipInputStream = new ZipInputStream(zipStream);
            while (zipInputStream.GetNextEntry() is ZipEntry { Name: var entryName })
            {
                Console.ResetColor();
                //Console.Write($"{entryName}: ");

                if (!folderToUnzip.Any(entryName.StartsWith))
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine($"Skipped");
                    continue;
                }

                var buffer = new byte[4*1024];

                var fullZipToPath = entryName;
                var directoryName = Path.GetDirectoryName(fullZipToPath);
                if ((directoryName?.Length ?? 0) > 0)
                    Directory.CreateDirectory(directoryName);

                if (Path.GetFileName(fullZipToPath).Length == 0)
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine($"Skipped folder");
                    continue;
                }

                Console.ResetColor();
                DebugConsole.Write($"{entryName}: ");
                DebugConsole.Write($"Unzipping");

                var file = new FileInfo(fullZipToPath);
                if(file.Extension is ".json" || file.Extension is ".mcmeta")
                {
                    using var stream = new MemoryStream();
                    StreamUtils.Copy(zipInputStream, stream, buffer);
                    stream.Flush();
                    stream.Position = 0;
                    
                    using var textReader = new StreamReader(stream);
                    using var jsonReader = new Newtonsoft.Json.JsonTextReader(textReader);
                    try
                    {
                        var obj = (JObject)JObject.ReadFrom(jsonReader);
                        Sort(obj, isAscending: true);
                        
                        file.WriteAllText(obj.ToString());
                    }
                    catch (Newtonsoft.Json.JsonReaderException)
                    {
                        new DirectoryInfo(Environment.CurrentDirectory).Parent.Then("tmp").File("log.log").AppendAllText("JsonReaderException");
                        using var fileStream = file.Create();
                        stream.Position = 0;
                        StreamUtils.Copy(stream, fileStream, buffer);
                    }
                }
                else
                {
                    using var stream = file.Create();
                    StreamUtils.Copy(zipInputStream, stream, buffer);
                }
                

                Console.ForegroundColor = ConsoleColor.Green;
                DebugConsole.WriteLine($" X");

                static void Sort(JObject obj, bool isAscending)
                {
                    var props = obj.Properties().ToList();

                    foreach (var prop in props)
                        obj.Remove(prop.Name);
                    
                    foreach (var prop in isAscending ? props.OrderBy(p => p.Name) : props.OrderByDescending(p => p.Name))
                    {
                        obj.Add(prop);
                        TrySort(prop.Value, isAscending);
                    }

                    static void TrySort(JToken token, bool isAscending)
                    {
                        switch (token)
                        {
                            case JObject obj:
                                Sort(obj, isAscending);
                                break;
                            case JArray array:
                                foreach (var elem in array)
                                    TrySort(elem, isAscending);
                                break;
                        }
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            DebugConsole.WriteLine($"Unzipped");
            return startTime;
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
    }
}
