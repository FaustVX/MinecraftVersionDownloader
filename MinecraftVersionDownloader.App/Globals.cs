#if LOCAL
#define DEBUG
#endif
using System.IO;
using System.Threading.Tasks;
using FaustVX.Temp;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using MinecraftVersionDownloader.All;
using Octokit;
using GitNet = Git.Net.Git;

namespace MinecraftVersionDownloader.App
{
    public static class Globals
    {
#if LOCAL
        public static DirectoryInfo Git { get; } = new DirectoryInfo(@"D:\Desktop\MinecraftVanillaDatapack");
#else
        public static TemporaryDirectory Git { get; } = TemporaryDirectory.CreateTemporaryDirectory(setCurrentDirectory: true);
        public static GitHubClient Github { get; } = new GitHubClient(new ProductHeaderValue("MinecraftVersionDownloader"))
        {
            Credentials = new Credentials(Options.GitHubCreditentials)
        };
#endif
        public static DirectoryInfo Tmp { get; }

        static Globals()
        {
#if LOCAL
            Tmp = Git.Parent.CreateSubdirectory("tmp");
#else
            Tmp = Directory.GetParent(Git).CreateSubdirectory("tmp");
#endif
        }

        public static async Task UploadGithubRelease(All.Version packages)
        {
#if !LOCAL
            if (!(GitNet.Push(force:false) && GitNet.Push(force:true, tags:true)))
                return;

            var result = await GetOrCreateRelease();

            var client = Globals.Tmp.File("client.jar");
            await UploadRelease(client, $"client_{packages.Id}.jar");

            var server = Globals.Tmp.File("server.jar");
            if (server.Exists)
                await UploadRelease(server, $"server_{packages.Id}.jar");

            await CreateZip("assets");
            await CreateZip("data");

            var updateRelease = result.ToUpdate();
            updateRelease.Draft = false;
            updateRelease.Prerelease = !(packages.Type is VersionType.Release);
            await Github.Repository.Release.Edit("FaustVX", "MinecraftVanillaDatapack", result.Id, updateRelease);


            async Task CreateZip(string folderName)
            {
                var git1 = ((DirectoryInfo)Globals.Git);
                var folder = git1.Then(folderName);
                var buffer = new byte[8 * 1024];
                if(folder.Exists)
                {
                    var zipFile = Globals.Tmp.File($"{folderName}_{packages.Id}.zip");
                    using (var zip = new ZipOutputStream(zipFile.OpenWrite()))
                    {
                        if(git1.File("pack.png") is FileInfo { Exists: true, Name: var namePng } packPng)
                            PutEntry(packPng, namePng);

                        foreach (var file in folder.EnumerateFiles("*", SearchOption.AllDirectories))
                            PutEntry(file, file.MakeRelativeTo(Globals.Git)[2..]);

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
                    await Github.Repository.Release.UploadAsset(result, new ReleaseAssetUpload()
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
                    var release = await Github.Repository.Release.Get("FaustVX", "MinecraftVanillaDatapack", $"Version_{packages.Assets}");
                    foreach (var asset in release.Assets)
                        await Github.Repository.Release.DeleteAsset("FaustVX", "MinecraftVanillaDatapack", asset.Id);
                    return release;
                }
                catch (NotFoundException)
                {
                    return await Github.Repository.Release.Create("FaustVX", "MinecraftVanillaDatapack", new NewRelease($"Version_{packages.Assets}")
                    {
                        Draft = true,
                        Prerelease = !(packages.Type is VersionType.Release)
                    });
                }
            }
#endif
        }
    }
}