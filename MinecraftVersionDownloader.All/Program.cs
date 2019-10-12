using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftVersionDownloader.All
{
    public static class Program
    {
        private static async Task Main()
        {
            var lastVersion = LastCommitMessage();
            if (lastVersion is null)
            {
                DeleteFiles(true);
                var startInfo = new ProcessStartInfo("git", "init");
                Process.Start(startInfo).WaitForExit();
            }

            var lastCount = 1;
            foreach (var info in (await MinecraftHelper.GetVersionsInfoAsync(reverse: true)).SkipWhile(v => lastVersion is string version && v.Id != version).Skip(lastVersion is null ? 0 : 1))
            {
                Console.Clear();
                Console.Title = info.Id;
                Console.WriteLine($"Downloading infos for {info.Id}");
                AddCommit(await info.Version, ref lastCount);
            }

            Console.ReadLine();

            DeleteFiles(false);
        }

        private static string? LastCommitMessage()
        {

            if (Helper.Git.GetLastCommit() is string data && !data.StartsWith("fatal:"))
                return data.Split(' ')[1];
            return null;
        }

        private static void AddCommit(Version version, ref int lastCount)
        {
            Console.CursorVisible = false;
            var toCommit = new List<ZipFolder.ZipFile>();

            var lastCounting = 0;
            var last = lastCount;
            var excludedExtensions = new string[]
            {
                ".class",
                ".db"
            };
            var root = ZipFolder.ListZipEntry(version.Client.JAR.GetStreamAsync().Result, file =>
            {
                file.IsSelected = Directory
                    .EnumerateFiles(".", "*", SearchOption.AllDirectories)
                    .Where(path => !path.StartsWith(@".\\.git"))
                    .Select(path => Path.GetRelativePath(".", path).Replace('\\', '/'))
                    .Contains(file.Entry.Name);
                return !excludedExtensions.Contains(Path.GetExtension(file.Name));
            }, count =>
            {
                lastCounting = count;
                Console.WriteLine($"{count} / ~{last}");
                Console.CursorTop--;
            });
            lastCount = lastCounting;

            Console.Beep();

            DeleteFiles(false);

            SelectFiles(root);

            Console.WriteLine($"Downloading datas for {version.Id}");

            toCommit.AddRange(root.GetAllFiles().Where(e => e.IsSelected));

            var commitSize = toCommit.Count;
            using var stream = new ZipInputStream(version.Client.JAR.GetStreamAsync().Result);
            while (stream.GetNextEntry() is ZipEntry entry)
            {
                if (toCommit.FirstOrDefault(f => f.Entry.Name == entry.Name) is ZipFolder.ZipFile file)
                {
                    Console.WriteLine($"{commitSize-toCommit.Count+1} / {commitSize}");
                    Console.CursorTop--;
                    var fileInfo = new FileInfo(entry.Name);
                    if (!fileInfo.Directory.Exists)
                        fileInfo.Directory.Create();
                    using var streamWriter = fileInfo.Create();
                    StreamUtils.Copy(stream, streamWriter, new byte[4 * 1024]);
                    toCommit.Remove(file);
                    if (!toCommit.Any())
                        break;
                }
            }

            Helper.Git.AddAll();
            Helper.Git.Commit(version.Id, version.ReleaseTime);

            Console.Beep();
            Console.CursorVisible = true;
            Console.ReadLine();
        }

        private static void DeleteFiles(bool all)
        {
            var root = new DirectoryInfo(".");
            foreach (var item in root.EnumerateFileSystemInfos() is var e && all ? e : e.Where(d => d.Name != ".git"))
            {
                if (item is DirectoryInfo dir)
                {
                    foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    }
                    dir.Delete(true);
                }
                else
                    item.Delete();
            }
        }

        private static void SelectFiles(ZipFolder directory)
        {
            var loop = true;
            var hover = 0;
            var openFolder = false;
            var closeFolder = false;
            var select = false;

            do
            {
                Console.Clear();
                var isHover = false;
                var length = PrintFolder(directory, hover, openFolder, closeFolder, select, ref isHover);
                if (select)
                {
                    Console.Clear();
                    isHover = false;
                    _ = PrintFolder(directory, hover, openFolder, closeFolder, false, ref isHover);
                }

                openFolder = closeFolder = select = false;

                switch (Console.ReadKey().Key)
                {
                    case ConsoleKey.UpArrow when hover > 0:
                        hover--;
                        break;
                    case ConsoleKey.PageDown:
                    case ConsoleKey.UpArrow:
                        hover = length - 1;
                        break;
                    case ConsoleKey.DownArrow when hover < length - 1:
                        hover++;
                        break;
                    case ConsoleKey.PageUp:
                    case ConsoleKey.DownArrow:
                        hover = 0;
                        break;
                    case ConsoleKey.LeftArrow:
                        closeFolder = true;
                        break;
                    case ConsoleKey.RightArrow:
                        openFolder = true;
                        break;
                    case ConsoleKey.Spacebar:
                        select = true;
                        break;
                    case ConsoleKey.Enter:
                    case ConsoleKey.Escape:
                        loop = false;
                        break;
                }
            } while (loop);

            static int PrintFolder(ZipFolder directory, int hover, bool openFolder, bool closeFolder, bool select, ref bool isHover, int tab = 0)
            {
                var i = 0;
                var length = 0;
                for (; i < directory.Children.Length; i++)
                {
                    var entry = directory.Children[i];

                    if (!isHover && hover >= 0 && i == hover)
                    {
                        entry.IsSelected ^= select;
                        entry.UpdateSelected();
                        if (select)
                            return -1;
                    }

                    if (entry is ZipFolder folder)
                    {
                        if (!isHover && hover >= 0 && i == hover)
                            folder.IsOpen = openFolder
                                ? true
                                : closeFolder
                                    ? false
                                    : folder.IsOpen;

                        PrintName(ref isHover);

                        if (folder.IsOpen)
                            length += PrintFolder(folder, hover - i - 1 + length, openFolder, closeFolder, select, ref isHover, tab: tab + 1);
                    }
                    else if (entry is ZipFolder.ZipFile file)
                    {
                        PrintName(ref isHover);
                    }

                    void PrintName(ref bool isHover)
                    {
                        if (!isHover && hover >= 0 && i == hover)
                        {
                            isHover = true;
                            (Console.BackgroundColor, Console.ForegroundColor) = (ConsoleColor.DarkGray, ConsoleColor.Black);
                        }

                        Console.WriteLine(new string(' ', tab * 3) + $"[{(entry.IsSelected ? 'X' : ' ')}]{entry.Name}{(entry is ZipFolder ? "/" : "")}");

                        Console.ResetColor();
                    }
                }
                return i + length;
            }
        }
    }
}
