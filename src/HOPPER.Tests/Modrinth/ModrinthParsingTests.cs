using System.Text.Json;
using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    /// <summary>
    /// Parsing has to survive a response nobody anticipated, which is precisely what a live call will
    /// never show you: Modrinth add fields without notice, mark almost everything optional, and ship
    /// values that are not in their own published schema (a real pack in the wild carries env values
    /// of "unknown", which the spec does not define). A browser that throws on any of that breaks on
    /// their release schedule rather than ours.
    ///
    /// The fixtures below are trimmed from real responses. Nothing here touches the network.
    /// </summary>
    public class ModrinthParsingTests
    {
        private static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, ModrinthJson.Options)!;

        [Test]
        public async Task Version_RealResponse_ParsesEveryFieldTheInstallerNeeds()
        {
            // Trimmed from GET /v2/project/jei/version?loaders=["forge"]&game_versions=["1.20.1"].
            const string json = """
            {
              "game_versions": ["1.20.1"],
              "loaders": ["forge"],
              "id": "mcC2LhSG",
              "project_id": "u6dRKJwZ",
              "author_id": "nMXRSbhF",
              "featured": false,
              "name": "15.48.0.179 for Forge 1.20.1",
              "version_number": "15.48.0.179",
              "changelog": "notes",
              "changelog_url": null,
              "date_published": "2026-08-04T15:12:01.094230Z",
              "downloads": 45703,
              "version_type": "beta",
              "status": "listed",
              "requested_status": null,
              "files": [
                {
                  "id": "yVAnrtuf",
                  "hashes": {
                    "sha512": "39b2f2cd",
                    "sha1": "8bebffd4"
                  },
                  "url": "https://cdn.modrinth.com/data/u6dRKJwZ/versions/mcC2LhSG/jei.jar",
                  "filename": "jei.jar",
                  "primary": true,
                  "size": 1657261,
                  "file_type": null
                }
              ],
              "dependencies": []
            }
            """;

            var version = Parse<ModrinthVersion>(json);

            await Assert.That(version.Id).IsEqualTo("mcC2LhSG");
            await Assert.That(version.ProjectId).IsEqualTo("u6dRKJwZ");
            await Assert.That(version.VersionNumber).IsEqualTo("15.48.0.179");
            await Assert.That(version.VersionType).IsEqualTo("beta");
            await Assert.That(version.IsRelease()).IsFalse();
            await Assert.That(version.GameVersions).Contains("1.20.1");

            var file = version.PrimaryFile()!;
            await Assert.That(file.FileName).IsEqualTo("jei.jar");
            await Assert.That(file.Size).IsEqualTo(1657261L);
            await Assert.That(file.Sha1).IsEqualTo("8bebffd4");
            await Assert.That(file.Sha512).IsEqualTo("39b2f2cd");
        }

        [Test]
        public async Task VersionFile_NeverCarriesASha256()
        {
            // The single most consequential fact about this API. Modrinth publish sha1 and sha512 and
            // nothing else, while Mod.Sha256 is the blob address AND the pinned client wire format's
            // hash - which is why adding a mod has to download it and hash it a third time.
            const string json = """
            { "hashes": { "sha1": "aa", "sha512": "bb" }, "url": "https://cdn.modrinth.com/x.jar",
              "filename": "x.jar", "primary": true, "size": 1 }
            """;

            var file = Parse<ModrinthVersionFile>(json);

            await Assert.That(file.Hashes.ContainsKey("sha256")).IsFalse();
            await Assert.That(file.Hashes.Keys.Order().ToList()).IsEquivalentTo(new[] { "sha1", "sha512" });
        }

        [Test]
        public async Task Version_UnknownFieldsAndAnUnknownVersionType_DoNotThrow()
        {
            // A field they added last week and a version_type outside release|beta|alpha. Neither may
            // take the browser down; version_type is a string for exactly this reason, and the only
            // decision made on it is "is this a release".
            const string json = """
            {
              "id": "v1", "project_id": "p1", "version_type": "something-new",
              "brand_new_field": { "nested": [1, 2, 3] },
              "files": [], "dependencies": []
            }
            """;

            var version = Parse<ModrinthVersion>(json);

            await Assert.That(version.VersionType).IsEqualTo("something-new");
            await Assert.That(version.IsRelease()).IsFalse();
        }

        [Test]
        public async Task Version_MissingOptionalCollections_ComeBackEmptyRatherThanNull()
        {
            // Every consumer iterates these. A null here would be a NullReferenceException in the
            // resolver rather than a mod that happens to declare nothing.
            var version = Parse<ModrinthVersion>("""{ "id": "v1", "project_id": "p1" }""");

            await Assert.That(version.Files).IsNotNull().And.IsEmpty();
            await Assert.That(version.Dependencies).IsNotNull().And.IsEmpty();
            await Assert.That(version.GameVersions).IsNotNull().And.IsEmpty();
            await Assert.That(version.Loaders).IsNotNull().And.IsEmpty();
        }

        [Test]
        public async Task Version_ExplicitNullCollections_AreAlsoNormalisedToEmpty()
        {
            var version = Parse<ModrinthVersion>(
                """{ "id": "v1", "project_id": "p1", "files": null, "dependencies": null, "loaders": null }""");

            await Assert.That(version.Files).IsEmpty();
            await Assert.That(version.Dependencies).IsEmpty();
            await Assert.That(version.Loaders).IsEmpty();
        }

        [Test]
        public async Task VersionFile_HashesAbsent_IsAnEmptyMapNotACrash()
        {
            var file = Parse<ModrinthVersionFile>("""{ "url": "https://x/y.jar", "filename": "y.jar" }""");

            await Assert.That(file.Sha1).IsNull();
            await Assert.That(file.Sha512).IsNull();
        }

        [Test]
        public async Task Dependency_AllFourShapesFoundLive_Parse()
        {
            // Only dependency_type is required in the published schema, and all three of the other
            // fields turn up null in practice. Every consumer has to handle pinned, "any version of
            // this project", and "not identifiable at all".
            const string json = """
            [
              {"version_id": null, "project_id": "nmoqTijg", "file_name": null, "dependency_type": "required"},
              {"version_id": "xp7zKZ1z", "project_id": "5lpsZoRi", "file_name": null, "dependency_type": "required"},
              {"version_id": null, "project_id": "4ZqxOvjD", "file_name": null, "dependency_type": "incompatible"},
              {"version_id": null, "project_id": null, "file_name": "external.jar", "dependency_type": "required"},
              {"dependency_type": "embedded"}
            ]
            """;

            var dependencies = Parse<List<ModrinthDependency>>(json);

            await Assert.That(dependencies.Count).IsEqualTo(5);
            await Assert.That(dependencies[1].VersionId).IsEqualTo("xp7zKZ1z");
            await Assert.That(dependencies[3].ProjectId).IsNull();
            await Assert.That(dependencies[3].FileName).IsEqualTo("external.jar");
            await Assert.That(dependencies[4].ProjectId).IsNull();
            await Assert.That(dependencies[4].DependencyType).IsEqualTo("embedded");
        }

        [Test]
        public async Task PrimaryFile_NoFileIsMarkedPrimary_TakesTheFirst()
        {
            // The published rule verbatim: at most one file has primary=true, and if none does, the
            // first file is the primary one.
            const string json = """
            { "id": "v1", "project_id": "p1", "files": [
                { "url": "https://x/a.jar", "filename": "a.jar", "primary": false, "size": 1, "hashes": {} },
                { "url": "https://x/b.jar", "filename": "b.jar", "primary": false, "size": 2, "hashes": {} }
            ] }
            """;

            var version = Parse<ModrinthVersion>(json);

            await Assert.That(version.PrimaryFile()!.FileName).IsEqualTo("a.jar");
        }

        [Test]
        public async Task PrimaryFile_SkipsFilesWithAFileType()
        {
            // A non-null file_type marks an extra - a resource pack shipped alongside a datapack - and
            // is never the mod jar. Handing one to the installer would put a non-jar in mods/.
            const string json = """
            { "id": "v1", "project_id": "p1", "files": [
                { "url": "https://x/rp.zip", "filename": "rp.zip", "primary": true, "size": 1,
                  "file_type": "required-resource-pack", "hashes": {} },
                { "url": "https://x/mod.jar", "filename": "mod.jar", "primary": false, "size": 2, "hashes": {} }
            ] }
            """;

            var version = Parse<ModrinthVersion>(json);

            await Assert.That(version.PrimaryFile()!.FileName).IsEqualTo("mod.jar");
        }

        [Test]
        public async Task PrimaryFile_OnlyExtras_IsNull()
        {
            const string json = """
            { "id": "v1", "project_id": "p1", "files": [
                { "url": "https://x/rp.zip", "filename": "rp.zip", "primary": true, "size": 1,
                  "file_type": "optional-resource-pack", "hashes": {} }
            ] }
            """;

            await Assert.That(Parse<ModrinthVersion>(json).PrimaryFile()).IsNull();
        }

        [Test]
        public async Task SearchHit_AndProject_KeepTheirColLidingFieldNamesApart()
        {
            // The easiest bug in this feature. A HIT says project_id and its "versions" are GAME
            // versions; a PROJECT says id, its "game_versions" are game versions and its "versions"
            // are VERSION IDS. Modelling them as one type would silently swap the two lists.
            var hit = Parse<ModrinthHit>("""
            { "project_id": "u6dRKJwZ", "slug": "jei", "title": "JEI",
              "versions": ["1.20.1", "1.21"], "categories": ["forge", "utility"], "follows": 10528 }
            """);

            var project = Parse<ModrinthProject>("""
            { "id": "u6dRKJwZ", "slug": "jei", "title": "JEI",
              "game_versions": ["1.20.1", "1.21"], "versions": ["6QsZu0uX", "vddb9IRK"],
              "categories": ["library"], "loaders": ["forge", "fabric"], "followers": 10537 }
            """);

            await Assert.That(hit.ProjectId).IsEqualTo("u6dRKJwZ");
            await Assert.That(hit.Versions).Contains("1.20.1");
            await Assert.That(hit.Categories).Contains("forge");
            await Assert.That(hit.Follows).IsEqualTo(10528L);

            await Assert.That(project.Id).IsEqualTo("u6dRKJwZ");
            await Assert.That(project.GameVersions).Contains("1.20.1");
            await Assert.That(project.Versions).Contains("6QsZu0uX");
            await Assert.That(project.Loaders).Contains("forge");
            await Assert.That(project.Followers).IsEqualTo(10537L);
        }

        [Test]
        public async Task SearchResponse_RealShape_Parses()
        {
            var response = Parse<ModrinthSearchResponse>("""
            { "hits": [ { "project_id": "p1", "title": "T", "downloads": 5 } ],
              "offset": 0, "limit": 20, "total_hits": 69 }
            """);

            await Assert.That(response.TotalHits).IsEqualTo(69);
            await Assert.That(response.Limit).IsEqualTo(20);
            await Assert.That(response.Hits.Single().ProjectId).IsEqualTo("p1");
        }

        [Test]
        public async Task Numbers_ArrivingAsStrings_StillParse()
        {
            // Cheap insurance. A proxy or a schema change that quotes a size would otherwise take the
            // whole browser down rather than one field.
            var file = Parse<ModrinthVersionFile>(
                """{ "url": "https://x/y.jar", "filename": "y.jar", "size": "12432", "hashes": {} }""");

            await Assert.That(file.Size).IsEqualTo(12432L);
        }

        [Test]
        public async Task LoaderTag_DropsTheInlineSvgIcon()
        {
            // The tag endpoint returns a full inline SVG per entry, tens of kilobytes of markup across
            // 29 loaders. It is not modelled, so it is discarded at the parse boundary and never
            // reaches the dashboard.
            var tag = Parse<ModrinthLoaderTag>("""
            { "icon": "<svg viewBox=\"0 0 24 24\">…very long…</svg>", "name": "forge",
              "supported_project_types": ["mod", "modpack"] }
            """);

            await Assert.That(tag.Name).IsEqualTo("forge");
            await Assert.That(tag.SupportedProjectTypes).Contains("mod");
        }

        [Test]
        public async Task GameVersionTag_RealShape_Parses()
        {
            var tag = Parse<ModrinthGameVersionTag>("""
            {"version":"1.20.1","version_type":"release","date":"2023-06-12T13:25:51Z","major":true}
            """);

            await Assert.That(tag.Version).IsEqualTo("1.20.1");
            await Assert.That(tag.VersionType).IsEqualTo("release");
            await Assert.That(tag.Major).IsTrue();
        }
    }
}
