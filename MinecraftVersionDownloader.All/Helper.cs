using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using IO = System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

namespace MinecraftVersionDownloader.All
{
    public static partial class Helper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<JObject> GetJsonAsync(this Uri uri)
            => JObject.Parse(await uri.GetStringAsync());

        public static Task<string> GetStringAsync(this Uri uri)
            => _httpClient.GetStringAsync(uri);

        public static Task<Stream> GetStreamAsync(this Uri uri)
            => _httpClient.GetStreamAsync(uri);

        public static string MakeRelativeTo(this FileInfo file, FileSystemInfo relativeTo)
        {
            if (relativeTo is DirectoryInfo di)
            {
                if (!di.FullName.EndsWith(Path.DirectorySeparatorChar))
                    di = new DirectoryInfo(di.FullName + Path.DirectorySeparatorChar);

                return MakeRelativeTo((FileSystemInfo)file, di);
            }
            if (relativeTo is FileInfo fi)
            {
                if (file.FullName == relativeTo.FullName)
                    return file.Name;
                return MakeRelativeTo((FileSystemInfo)file, fi.Directory);
            }

            throw new InvalidCastException();
        }
        
        public static string MakeRelativeTo(this DirectoryInfo directory, FileSystemInfo relativeTo)
        {
            if(!directory.FullName.EndsWith(Path.DirectorySeparatorChar))
                directory = new DirectoryInfo(directory.FullName + Path.DirectorySeparatorChar);

            if (relativeTo is DirectoryInfo di)
            {
                if (!di.FullName.EndsWith(Path.DirectorySeparatorChar))
                    di = new DirectoryInfo(di.FullName + Path.DirectorySeparatorChar);

                if(directory.FullName == di.FullName)
                    return $".{Path.DirectorySeparatorChar}";

                return MakeRelativeTo((FileSystemInfo)directory, di);
            }
            if (relativeTo is FileInfo fi)
                return MakeRelativeTo((FileSystemInfo)directory, fi.Directory);

            throw new InvalidCastException();
        }
        
        private static string MakeRelativeTo(FileSystemInfo file, DirectoryInfo relativeTo)
        {
            if (file.FullName.StartsWith(relativeTo.FullName))
                return "." + Path.DirectorySeparatorChar + file.FullName.Remove(0, relativeTo.FullName.Length);
            
            var sbRel = new StringBuilder(relativeTo.FullName);
            var sbFile = new StringBuilder(file.FullName);

            for (int i = 0; i < relativeTo.FullName.Length; i++)
            {
                if(sbFile.Length == 0 || file.FullName[i] != relativeTo.FullName[i])
                    if(i == 0)
                        return file.FullName;
                    else
                        break;
                sbRel = sbRel.Remove(0, 1);
                sbFile = sbFile.Remove(0, 1);
            }

            if(sbRel.Length == 0)
                return "." + Path.DirectorySeparatorChar + sbFile.ToString();

            var countParentsFolder = sbRel.ToString().Count(c => c == Path.DirectorySeparatorChar);
            return sbFile.Insert(0, string.Join("", Enumerable.Repeat($"..{Path.DirectorySeparatorChar}", countParentsFolder))).ToString();
        }

        public static IEnumerable<T> Remove<T>(this IEnumerable<T> source, IEnumerable<T> toRemove)
        {
            foreach (var item in source)
                if (!toRemove.Contains(item))
                    yield return item;
        }

        public static IEnumerable<T> IfEmpty<T>(this IEnumerable<T> source, Action empty, Action nonEmpty)
        {
            var isEmpty = true;
            
            foreach (var item in source)
            {
                isEmpty = false;
                yield return item;
            }

            if (isEmpty)
                empty();
            else
                nonEmpty();
        }
        
        public static DirectoryInfo Then(this DirectoryInfo directory, string next)
            => new DirectoryInfo(Path.Combine(directory.FullName, next));
        
        public static DirectoryInfo Then(this DirectoryInfo directory, string next1, string next2)
            => new DirectoryInfo(Path.Combine(directory.FullName, next1, next2));
        
        public static DirectoryInfo Then(this DirectoryInfo directory, string next1, string next2, string next3)
            => new DirectoryInfo(Path.Combine(directory.FullName, next1, next2, next3));
        
        public static FileInfo File(this DirectoryInfo directory, string filename)
            => new FileInfo(Path.Combine(directory.FullName, filename));
        
        public static void AppendAllText(this FileInfo file, string content)
            => IO.File.AppendAllText(file.FullName, content);
        
        public static void AppendAllLines(this FileInfo file, IEnumerable<string> contents)
            => IO.File.AppendAllLines(file.FullName, contents);
        
        public static void WriteAllText(this FileInfo file, string content)
            => IO.File.WriteAllText(file.FullName, content);
        
        public static void WriteAllLines(this FileInfo file, IEnumerable<string> contents)
            => IO.File.WriteAllLines(file.FullName, contents);
        
        public static string ReadAllText(this FileInfo file)
            => IO.File.ReadAllText(file.FullName);
        
        public static string[] ReadAllLines(this FileInfo file)
            => IO.File.ReadAllLines(file.FullName);
    }
}
