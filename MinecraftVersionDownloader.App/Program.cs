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

namespace MinecraftVersionDownloader.App
{
    public static class Program
    {
        private static async Task Main(string[] args)
        {
#if DEBUG
            Debugger.Break();
#endif
            using var git = TemporaryDirectory.CreateTemporaryDirectory(setCurrentDirectory: true);
            System.Console.WriteLine(git);
            GitNet.Clone(args.HeadTail(out args), checkout: false, localDirectory: ".");
#if DEBUG
            GitNet.Reset(^1, GitNet.ResetMode.Hard);
#endif
            long startTime = 0;
            foreach (var version in (await MinecraftHelper.GetVersionsInfoAsync(reverse: true))
                .SkipWhile(v => v.Id != LastCommitMessage())
                .Skip(1))
            {
                Console.WriteLine($"Next version: {version.Id}");

                DeleteFiles();

                var packages = await version.Version;

                using var jarStream = await packages.Client.JAR.GetStreamAsync();
                startTime = UnzipFromStream(jarStream, args);

                Console.ResetColor();

                GitNet.Add(all: true);

                var tmp = Directory.GetParent(git).CreateSubdirectory("tmp");
                using (var file = File.Create(Path.Combine(tmp.FullName, "server.jar")))
                using (var jar = await packages.Server!.JAR.GetStreamAsync())
                {
                    Memory<byte> block = new byte[8*1024];
                    var written = 0;
                    while (written < packages.Server!.JarSize)
                    {
                        var size = await jar.ReadAsync(block);
                        written += size;
                        await file.WriteAsync(block.Slice(0, size));
                    }
                }

                System.Console.WriteLine("Create 'generated' files");

                var extract = Process.Start(@"java", "-cp ../tmp/server.jar net.minecraft.data.Main --all");
                extract.WaitForExit();

                if(packages.Client.TXT is Uri txtClient)
                    File.WriteAllText(Path.Combine("generated", "clientMapping.txt"), await txtClient.GetStringAsync());

                if(packages.Server?.TXT is Uri txtServer)
                    File.WriteAllText(Path.Combine("generated", "serverMapping.txt"), await txtServer.GetStringAsync());

                Console.WriteLine(TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime));

                GitNet.Add(@"generated/reports/*");
                GitNet.Add(@"generated/*Mapping.txt");
                GitNet.Commit(version.Id, version.ReleaseTime);
                GitNet.Tag(packages.Assets, force:true);
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
            => GitNet.GetLastCommit()?.Split(' ')[1];

        private static bool CompareLastGitCommitMessage(string version)
            => !(GitNet.GetLastCommit()?.Contains(version, StringComparison.InvariantCultureIgnoreCase) ?? false);

        private static long UnzipFromStream(Stream zipStream, params string[] folderToUnzip)
        {
            var startTime = Stopwatch.GetTimestamp();
            Console.WriteLine($"Unzipping in {Environment.CurrentDirectory}");
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
                Console.Write($"{entryName}: ");
                Console.Write($"Unzipping");

                using var streamWriter = File.Create(fullZipToPath);
                StreamUtils.Copy(zipInputStream, streamWriter, buffer);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" X");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Unzipped");
            return startTime;
        }
    }
}
