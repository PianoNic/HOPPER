using System.Text;
using HOPPER.API.Extensions;

namespace HOPPER.Tests.Infrastructure
{
    public class LocatorSourceDigestTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-digest-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private static void Write(string root, string relative, string content)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        }

        [Test]
        public async Task ATreeWithNoSources_HasNoDigest()
        {
            using var dir = new TempDir();

            await Assert.That(LocatorSourceDigest.Of(dir.Path)).IsNull();
        }

        [Test]
        public async Task TheSameTreeTwice_DigestsTheSame()
        {
            using var a = new TempDir();
            using var b = new TempDir();

            foreach (var dir in new[] { a.Path, b.Path })
            {
                Write(dir, "hopper-core/src/main/java/A.java", "class A {}");
                Write(dir, "hopper-forge/src/main/java/B.java", "class B {}");
            }

            await Assert.That(LocatorSourceDigest.Of(a.Path)).IsEqualTo(LocatorSourceDigest.Of(b.Path));
        }

        [Test]
        public async Task ChangingAComment_MovesTheDigest()
        {
            using var dir = new TempDir();
            Write(dir.Path, "hopper-core/src/main/java/A.java", "class A {}");

            var before = LocatorSourceDigest.Of(dir.Path);

            Write(dir.Path, "hopper-core/src/main/java/A.java", "// note\nclass A {}");

            await Assert.That(LocatorSourceDigest.Of(dir.Path)).IsNotEqualTo(before);
        }

        [Test]
        public async Task TheSamePathWithDifferentContent_DoesNotCollideWithADifferentPath()
        {
            using var a = new TempDir();
            using var b = new TempDir();

            // Path and bytes are both hashed, so moving a file has to move the digest too.
            Write(a.Path, "hopper-core/src/main/java/A.java", "class A {}");
            Write(b.Path, "hopper-fabric/src/main/java/A.java", "class A {}");

            await Assert.That(LocatorSourceDigest.Of(a.Path)).IsNotEqualTo(LocatorSourceDigest.Of(b.Path));
        }

        [Test]
        public async Task WhatGradleAlreadyBuilt_IsNotPartOfTheDigest()
        {
            using var dir = new TempDir();
            Write(dir.Path, "hopper-core/src/main/java/A.java", "class A {}");

            var before = LocatorSourceDigest.Of(dir.Path);

            Write(dir.Path, "build/generated/Ignored.java", "class Ignored {}");
            Write(dir.Path, "hopper-core/build/tmp/AlsoIgnored.java", "class AlsoIgnored {}");

            await Assert.That(LocatorSourceDigest.Of(dir.Path)).IsEqualTo(before);
        }
    }
}
