using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinecraftVersionDownloader.All
{
    public sealed class ZipFolder : ZipFolder.IEntry
    {
        public interface IEntry
        {
            string Name { get; }
            string[] Path { get; }
            ZipFolder? Parent { get; }
            bool IsSelected { get; set; }

            ZipFolder Root
            {
                get
                {
                    var lastParent = this as ZipFolder ?? Parent;
                    for (var parent = lastParent; parent is object; parent = parent.Parent)
                        lastParent = parent;
                    return lastParent!;
                }
            }

            void UpdateSelected()
                => Parent?.UpdateSelected();
        }

        public sealed class ZipFile : IEntry
        {
            public ZipFile(ZipEntry entry)
            {
                Parent = null!;
                Entry = entry;
                Path = Entry.Name.Split("/\\".ToCharArray());
                Path.HeadsTail(out var name);
                Name = name;
            }

            public ZipEntry Entry { get; }
            public string Name { get; }
            public string[] Path { get; }
            public ZipFolder Parent { get; }
            public bool IsSelected { get; set; }
        }

        public ZipFolder(string name, ZipFile[] entries)
            : this(null!, name, entries)
        { }

        private ZipFolder(ZipFolder parent, string name, ZipFile[] files)
        {
            Parent = parent;
            Path = SetPath();
            Name = name;
            Files = files
                .Where(f => !f.Path.Skip(Path.Length - 1).HeadsTail(out _).Any())
                .ToArray();

            foreach (var file in Files)
                file.ModifyReadOnlyProperty(f => f.Parent, this);

            Folders = files
                .Where(f => f.Path[0] != "META-INF")
                .Remove(Files)
                .Where(f => f.Path.Skip(Path.Length - 1).HeadTail(out _).Any())
                .GroupBy(f => f.Path.Skip(Path.Length - 1).HeadTail(out _))
                .Select(g => new ZipFolder(this, g.Key, g.ToArray()))
                .ToArray();

            Children = Files.Cast<IEntry>().Concat(Folders).ToArray();

            TotalFiles = Files.Length + Folders.Sum(f => f.TotalFiles);

            UpdateSelected();

            string[] SetPath()
            {
                var parentPath = Parent?.Path;
                var path = new string[(parentPath?.Length ?? 0) + 1];
                parentPath?.CopyTo(path, 0);
                path[^1] = name;
                return path;
            }
        }

        public ZipFolder? Parent { get; }
        public string[] Path { get; }
        public string Name { get; }

        public ZipFile[] Files { get; }
        public ZipFolder[] Folders { get; }
        public int TotalFiles { get; }
        public bool IsOpen { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value == _isSelected)
                    return;

                _isSelected = value;

                foreach (var entry in Children)
                    entry.IsSelected = value;
            }
        }
        public IEntry[] Children { get; }
        public IEntry this[string name]
            => Children.First(e => e.Name == name);
        public IEnumerable<ZipFile> GetAllFiles()
            => Files.Concat(Folders.SelectMany(f => f.GetAllFiles()));

        public static ZipFolder ListZipEntry(Stream zipStream, System.Func<ZipFile, bool> insert, System.Action<int>? onProgess = null)
        {
            onProgess ??= delegate { };

            return new ZipFolder(".", ZipEntries().ToArray());

            IEnumerable<ZipFile> ZipEntries()
            {
                using var zipInputStream = new ZipInputStream(zipStream);
                var count = 0;
                while (zipInputStream.GetNextEntry() is ZipEntry { Name: var name } zipEntry)
                    if (zipEntry.IsFile && !zipEntry.IsDirectory)
                    {
                        onProgess!(++count);
                        var zipFile = new ZipFile(zipEntry);
                        if (insert(zipFile))
                            yield return zipFile;
                    }
            }
        }

        public void UpdateSelected()
        {
            if (Children is null)
                return;

            _isSelected = Children.All(f => f.IsSelected);

            Parent?.UpdateSelected();
        }
    }
}
