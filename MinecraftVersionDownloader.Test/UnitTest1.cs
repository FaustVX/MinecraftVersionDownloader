using Microsoft.VisualStudio.TestTools.UnitTesting;
using static MinecraftVersionDownloader.All.Helper;
using System.Linq;
using System.IO;

namespace MinecraftVersionDownloader.Test
{
    [TestClass]
    public class UnitTest1
    {

        [TestMethod]
        public void IfEmpty()
        {
            Enumerable.Empty<object>().IfEmpty(() => Assert.IsTrue(true), () => Assert.IsTrue(false));
            Enumerable.Repeat(new object(), 1).IfEmpty(() => Assert.IsFalse(true), () => Assert.IsFalse(false));
        }

        [TestMethod]
        public void MakeRelative()
        {
            var file = new FileInfo(@"a\b\c.txt");
            var dir1 = new DirectoryInfo(@"a\b\");
            var dir2 = new DirectoryInfo(@"a\b\d");
            var dir3 = new DirectoryInfo(@"Z:\a\b\d");

            Assert.AreEqual(@".\c.txt", file.MakeRelativeTo(dir1));
            Assert.AreEqual(@".\d\", dir2.MakeRelativeTo(dir1));
            Assert.AreEqual(@"..\c.txt", file.MakeRelativeTo(dir2));
            Assert.AreEqual(@".\", dir2.MakeRelativeTo(dir2));
            Assert.AreEqual(file.FullName, file.MakeRelativeTo(dir3));

            var git = new DirectoryInfo(@"D:\Desktop\MinecraftVanillaDatapack");
            var generated = ((DirectoryInfo)git).Then("generated");
            Assert.AreEqual(@".\generated\", generated.MakeRelativeTo(git));
        }
    }
}
