using System;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Core;
using System.Linq;
using System.Diagnostics;
using MinecraftVersionDownloader.All;

namespace MinecraftVersionDownloader.App
{
    public static class Program
    {
        private static async Task Main(string[] args)
        {
#if DEBUG
            Console.ReadLine();
            args = new[] { @"C:\Users\Jonathan\Desktop\MinecraftVanillaDatapack", "assets", "data", "pack.", "version.json" };
            Debugger.Break();
#endif
            Environment.CurrentDirectory = new DirectoryInfo(args.HeadTail(out args)).FullName;
            long startTime = 0;
            foreach (var version in (await MinecraftHelper.GetVersionsInfoAsync(reverse: true))
                .SkipWhile(v => v.Id != LastCommitMessage())
                .Skip(1))
            {
                Console.WriteLine($"Latest version: {version.Id}");

                //if (CompareLastGitCommitMessage(version.Id))
                //    continue;

                DeleteFiles();

                var releaseTime = version.ReleaseTime;

                var packages = await version.Version;

                using var jarStream = await packages.Client.JAR.GetStreamAsync();
                startTime = UnzipFromStream(jarStream, args);

                Console.ResetColor();

                using (var file = File.CreateText("clientMapping.txt"))
                    file.Write(await packages.Client.TXT!.GetStringAsync());

                Console.WriteLine(TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime));

                Helper.Git.AddAll();
                Helper.Git.Commit(version.Id, version.ReleaseTime);
                Helper.Git.AddTag(packages.Assets, force:true);

                DeleteFiles();
            }

            Helper.Git.Push(force:true);

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

        private static string LastCommitMessage()
            => Helper.Git.GetLastCommit().Split(' ')[1];

        private static bool CompareLastGitCommitMessage(string version)
            => !(Helper.Git.GetLastCommit()?.Contains(version, StringComparison.InvariantCultureIgnoreCase) ?? false);

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
