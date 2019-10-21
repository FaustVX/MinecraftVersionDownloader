using Microsoft.VisualStudio.TestTools.UnitTesting;
using static MinecraftVersionDownloader.All.Helper;
using System.Linq;

namespace MinecraftVersionDownloader.Test
{
    [TestClass]
    public class UnitTest2
    {

        [TestMethod]
        public void IfEmpty()
        {
            Enumerable.Empty<object>().IfEmpty(() => Assert.IsTrue(true), () => Assert.IsTrue(false));
            Enumerable.Repeat(new object(), 1).IfEmpty(() => Assert.IsFalse(true), () => Assert.IsFalse(false));
        }
    }
}
