using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.ModMetadata
{
    public class ModSideReaderTests
    {
        private static MemoryStream Jar(string name, string content)
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }

            buffer.Position = 0;
            return buffer;
        }

        [Test]
        public async Task Utf8BomInFrontOfTheFabricMetadata_StillYieldsTheSide()
        {
            using var jar = Jar("fabric.mod.json", "﻿{\"schemaVersion\":1,\"id\":\"sodium\",\"environment\":\"client\"}");

            await Assert.That(ModSideReader.Read(jar)).IsEqualTo(ModSide.ClientOnly);
        }

        [Test]
        public async Task Utf8BomInFrontOfTheQuiltMetadata_StillYieldsTheSide()
        {
            using var jar = Jar("quilt.mod.json", "﻿{\"quilt_loader\":{\"id\":\"thing\",\"minecraft\":{\"environment\":\"server\"}}}");

            await Assert.That(ModSideReader.Read(jar)).IsEqualTo(ModSide.ServerOnly);
        }
    }
}
