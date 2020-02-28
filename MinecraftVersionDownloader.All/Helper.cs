using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using IO = System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;

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
