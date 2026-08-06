package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.List;
import java.util.Random;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Every fixture here is a REAL zip file on a REAL filesystem, because that is
 * what this reader is handed in production and because "not a zip" is a case it
 * has to survive rather than a case it never sees.
 *
 * <p>This class is the one-for-one twin of
 * {@code src/HOPPER.Tests/ModMetadata/ModIdReaderTests.cs}, with the same
 * fixture text and the same expected output. The two implementations only do
 * anything useful when they agree: this client moves a jar out of the player's
 * {@code mods/} folder exactly when the id it read matches an id the server
 * published. Change one, change both.
 *
 * <p>The TOML below is taken from real jars in a 102-mod Forge 1.20.1 instance -
 * embeddium, Compat_FarmersDelight and yet_another_config_lib_v3 in particular -
 * rather than invented, because the mistakes worth pinning are the ones real
 * files actually make.
 */
class ModIdsTest {

    /** @param entries name, content, name, content, ... */
    private static Path jar(Path dir, String name, String... entries) throws Exception {
        Path f = dir.resolve(name);
        OutputStream raw = Files.newOutputStream(f);
        try {
            ZipOutputStream zip = new ZipOutputStream(raw);
            for (int i = 0; i + 1 < entries.length; i += 2) {
                zip.putNextEntry(new ZipEntry(entries[i]));
                zip.write(entries[i + 1].getBytes(StandardCharsets.UTF_8));
                zip.closeEntry();
            }
            zip.finish();
            zip.close();
        } finally {
            raw.close();
        }
        return f;
    }

    private static List<String> read(Path dir, String... entries) throws Exception {
        return ModIds.read(jar(dir, "fixture.jar", entries), HopperLog.STDOUT);
    }

    // ---------------------------------------------------------------- mods.toml

    private static final String EMBEDDIUM_TOML =
            "modLoader=\"javafml\"\n"
            + "loaderVersion=\"[47,)\"\n"
            + "license=\"LGPL-3.0-only\"\n"
            + "[[mods]]\n"
            + "modId=\"embeddium\"\n"
            + "version=\"0.3.31+mc1.20.1\"\n"
            + "displayName=\"Embeddium\"\n"
            + "logoFile=\"icon.png\"\n"
            + "description='''\n"
            + "Embeddium is a fork of Rubidium, a fork of Sodium with patches for Forge\n"
            + "'''\n"
            + "credits=\"embeddedt, NanoLive, CaffeineMC\"\n"
            + "authors=\"embeddedt\"\n"
            + "\n"
            + "[[mods]]\n"
            + "modId = \"rubidium\"\n"
            + "version = \"0.7.1\"\n"
            + "displayName = \"Rubidium (Embeddium)\"\n"
            + "description = '''\n"
            + "Stub, to allow mods detecting Rubidium to function as expected.\n"
            + "'''\n"
            + "\n"
            + "# Enforce new enough Oculus\n"
            + "[[dependencies.embeddium]]\n"
            + "modId = \"oculus\"\n"
            + "mandatory = false\n"
            + "versionRange = \"(1.6.15,)\"\n"
            + "ordering = \"BEFORE\"\n"
            + "side = \"CLIENT\"\n"
            + "\n"
            + "# The new config screen supersedes TexTrue's Embeddium Options\n"
            + "[[dependencies.embeddium]]\n"
            + "modId = \"textrues_embeddium_options\"\n"
            + "mandatory = false\n"
            + "versionRange = \"[0.0.0-NOT-COMPATIBLE]\"\n"
            + "ordering = \"BEFORE\"\n"
            + "side = \"CLIENT\"\n";

    @Test
    void modsTomlSingleModsBlockReturnsTheModId(@TempDir Path dir) throws Exception {
        assertEquals(Arrays.asList("jei"), read(dir, "META-INF/mods.toml",
                "modLoader=\"javafml\"\nloaderVersion=\"[47,)\"\n"
                        + "[[mods]]\nmodId=\"jei\"\nversion=\"15.3.0.4\"\n"));
    }

    @Test
    void modsTomlTwoModsBlocksReturnsBothInFileOrder() {
        assertEquals(Arrays.asList("embeddium", "rubidium"), ModIds.fromModsToml(EMBEDDIUM_TOML));
    }

    @Test
    void modsTomlDependencyTablesAreNeverMistakenForMods() {
        // The most important test in this file. 98 of the 104 real toml files in the reference
        // instance carry [[dependencies.<id>]] tables, each with its own modId key naming a
        // DIFFERENT mod. A naive grep for modId returns four ids here, and the two extras - oculus
        // and textrues_embeddium_options - are mods a player very likely has installed for real.
        // Getting this wrong does not fail safe: it makes this client move an unrelated jar out of
        // a folder HOPPER was told never to manage.
        List<String> ids = ModIds.fromModsToml(EMBEDDIUM_TOML);

        assertFalse(ids.contains("oculus"));
        assertFalse(ids.contains("textrues_embeddium_options"));
    }

    @Test
    void modsTomlQuotedDependencyTableNameIsNotAModsTable() {
        // yet_another_config_lib_v3 writes its dependency headers with the table path itself
        // quoted, so the name has to be unquoted before it is compared to "mods".
        assertEquals(Arrays.asList("yet_another_config_lib_v3"), ModIds.fromModsToml(
                "[[mods]]\n"
                + "modId = \"yet_another_config_lib_v3\"\n"
                + "\n"
                + "[[\"dependencies.yet_another_config_lib_v3\"]]\n"
                + "modId = \"minecraft\"\n"
                + "mandatory = true\n"));
    }

    @Test
    void modsTomlInlineModsArrayIsRead() {
        // Compat_FarmersDelight.jar verbatim. [[mods]] never appears; the lowcodefml datapack
        // toolchain writes the array inline. Note the mixed quoting inside one inline table - the
        // double quotes exist because the value contains an apostrophe.
        assertEquals(Arrays.asList("farmersdelightcompat"), ModIds.fromModsToml(
                "modLoader = 'lowcodefml'\n"
                + "loaderVersion = '[40,)'\n"
                + "license = 'LicenseRef-Unknown'\n"
                + "showAsResourcePack = false\n"
                + "mods = [\n"
                + "\t{ modId = 'farmersdelightcompat', version = '1.1.1',"
                + " displayName = \"Farmer's Delight Compat\","
                + " description = \"Remove duplicate items and blocks with the goal of integrating"
                + " Farmer's Delight and other similar mods so they work as if they were a single"
                + " mod!\", logoFile = 'farmers-delight-compat_pack.png',"
                + " updateJSONURL = 'https://api.modrinth.com/updates/bVyGNtz1/forge_updates.json',"
                + " authors = 'Kanadeyoru',"
                + " displayURL = 'https://modrinth.com/mod/farmers-delight-compat' },\n"
                + "]\n"
                + "issueTrackerURL = 'https://github.com/FusionSwarly/Create-Structures/issues'\n"));
    }

    @Test
    void modsTomlInlineModsArrayFollowedByDependencyTablesReadsOnlyTheInlineIds() {
        // The create-structures shape: inline mods array, then real dependency tables after it.
        assertEquals(Arrays.asList("create_structures"), ModIds.fromModsToml(
                "modLoader = 'lowcodefml'\n"
                + "mods = [\n"
                + "\t{ modId = 'create_structures', version = '0.1.1' },\n"
                + "]\n"
                + "\n"
                + "[[dependencies.create_structures]]\n"
                + "modId = \"create\"\n"
                + "mandatory = true\n"));
    }

    @Test
    void modsTomlInlineModsArrayOnOneLineIsRead() {
        assertEquals(Arrays.asList("onelinemod"),
                ModIds.fromModsToml("mods = [ { modId = 'onelinemod', version = '1.0' } ]\n"));
    }

    @Test
    void modsTomlMultilineStringContainingATableHeaderIsSkipped() {
        // A description is free text and "[[mods]]" or "# Features" inside one is entirely
        // ordinary. 67 of the 104 real files carry a triple-quoted string.
        assertEquals(Arrays.asList("realmod"), ModIds.fromModsToml(
                "[[mods]]\n"
                + "modId = \"realmod\"\n"
                + "description = '''\n"
                + "# Features\n"
                + "[[mods]]\n"
                + "modId = \"evil\"\n"
                + "'''\n"));
    }

    @Test
    void modsTomlUnterminatedMultilineStringSwallowsTheRestOfTheFile() {
        // Parser state hygiene: an unbalanced ''' must not be treated as closed on the next line,
        // and must not leak structure out of the string body.
        assertEquals(Arrays.asList("realmod"), ModIds.fromModsToml(
                "[[mods]]\n"
                + "modId = \"realmod\"\n"
                + "description = '''\n"
                + "never closed\n"
                + "modId = \"evil\"\n"));
    }

    @Test
    void modsTomlTrailingCommentAfterAQuotedValueIsStripped() {
        // 43 of the 104 real files do this.
        assertEquals(Arrays.asList("commented"),
                ModIds.fromModsToml("[[mods]]\nmodId = \"commented\" # this is the mod\n"));
    }

    @Test
    void modsTomlHashInsideAQuotedValueIsNotAComment() {
        assertEquals(Arrays.asList("hashvalue"), ModIds.fromModsToml(
                "[[mods]]\ndescription = \"sharp # sign\"\nmodId = \"hashvalue\"\n"));
    }

    @Test
    void modsTomlHeaderWithATrailingCommentIsRecognised() {
        // 23 of the 104 real files put a comment on the header line itself.
        assertEquals(Arrays.asList("headercomment"),
                ModIds.fromModsToml("[[mods]] # the mod\nmodId = \"headercomment\"\n"));
    }

    @Test
    void modsTomlSingleQuotedValueIsRead() {
        assertEquals(Arrays.asList("singlequoted"),
                ModIds.fromModsToml("[[mods]]\nmodId = 'singlequoted'\n"));
    }

    @Test
    void modsTomlNoWhitespaceAroundEqualsIsRead() {
        // 80 of the 104 real files write it this way.
        assertEquals(Arrays.asList("tight"), ModIds.fromModsToml("[[mods]]\nmodId=\"tight\"\n"));
    }

    @Test
    void modsTomlIndentedKeysAndTabsAreRead() {
        // 65 of the 104 real files indent the keys under their header.
        assertEquals(Arrays.asList("indented"),
                ModIds.fromModsToml("  [[mods]]\n\t  modId = \"indented\"\n"));
    }

    @Test
    void modsTomlCrlfLineEndingsAreRead() {
        // 12 of the 104 real files are CRLF.
        assertEquals(Arrays.asList("crlf"), ModIds.fromModsToml("[[mods]]\r\nmodId = \"crlf\"\r\n"));
    }

    @Test
    void modsTomlIdFailingTheForgeRegexIsDropped() {
        // ^[a-z][a-z0-9_.-]{1,63}$, taken verbatim from ModInfo.class. A cheap backstop against a
        // parser bug turning a fragment of a description into an "id" this client matches on.
        String[] candidates = {"Not An Id", "1starts-with-a-digit", "UPPERCASE", "x", ""};
        for (String candidate : candidates) {
            assertTrue(ModIds.fromModsToml("[[mods]]\nmodId = \"" + candidate + "\"\n").isEmpty(),
                    candidate);
        }
    }

    @Test
    void modsTomlLowercaseModidSpellingIsNotRead() {
        // The toml key is modId, camelCase. "modid" is the mcmod.info spelling and appears in zero
        // of the 104 real toml files.
        assertTrue(ModIds.fromModsToml("[[mods]]\nmodid = \"wrongcase\"\n").isEmpty());
    }

    // ---------------------------------------------------------------- precedence

    @Test
    void neoForgeTomlWinsOverModsTomlWhenBothArePresent(@TempDir Path dir) throws Exception {
        // The two declare different ids so the precedence is observable. NeoForge 21.1+ reads
        // META-INF/neoforge.mods.toml and treats META-INF/mods.toml as a legacy Forge marker.
        assertEquals(Arrays.asList("modernid"), read(dir,
                "META-INF/mods.toml", "[[mods]]\nmodId = \"legacyid\"\n",
                "META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"modernid\"\n"));
    }

    @Test
    void neoForgeTomlThatYieldsNothingFallsBackToModsToml(@TempDir Path dir) throws Exception {
        // A broken new file must not cost us the ids the old one still carries.
        assertEquals(Arrays.asList("stillhere"), read(dir,
                "META-INF/mods.toml", "[[mods]]\nmodId = \"stillhere\"\n",
                "META-INF/neoforge.mods.toml", "modLoader = \"javafml\"\n"));
    }

    @Test
    void bothTomlsDeclaringTheSameIdReturnItOnce(@TempDir Path dir) throws Exception {
        // All three jars in the reference instance that ship both declare identical ids.
        assertEquals(Arrays.asList("terralith"), read(dir,
                "META-INF/mods.toml", "[[mods]]\nmodId = \"terralith\"\n",
                "META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"terralith\"\n"));
    }

    // ---------------------------------------------------------------- fabric and quilt

    @Test
    void fabricJsonTopLevelIdIsRead(@TempDir Path dir) throws Exception {
        assertEquals(Arrays.asList("terralith"), read(dir, "fabric.mod.json",
                "{\"schemaVersion\": 1, \"id\": \"terralith\", \"version\": \"2.5.4\","
                        + " \"name\": \"Terralith\"}"));
    }

    @Test
    void fabricJsonDependsKeysAreNotRead(@TempDir Path dir) throws Exception {
        // "depends" is an object whose KEYS are mod ids, and fabricloader and minecraft are on
        // every install in the world.
        assertEquals(Arrays.asList("terralith"), read(dir, "fabric.mod.json",
                "{\"schemaVersion\": 1, \"id\": \"terralith\", \"depends\":"
                        + " {\"fabricloader\": \">=0.12.7\", \"fabric-api-base\": \"*\","
                        + " \"minecraft\": \">=1.20\"}}"));
    }

    @Test
    void quiltJsonNestedLoaderIdIsRead(@TempDir Path dir) throws Exception {
        assertEquals(Arrays.asList("terralith"), read(dir, "quilt.mod.json",
                "{\"schema_version\": 1, \"quilt_loader\": {\"group\": \"net.stardustlabs\","
                        + " \"id\": \"terralith\", \"version\": \"2.5.4\"}}"));
    }

    @Test
    void quiltJsonDependsArrayIdsAreNotRead(@TempDir Path dir) throws Exception {
        // Terralith verbatim. Quilt's depends is an array of objects each carrying an id key -
        // exactly the same hazard as [[dependencies.*]] in toml. Reading the exact path is what
        // keeps minecraft and quilt_resource_loader out.
        assertEquals(Arrays.asList("terralith"), read(dir, "quilt.mod.json",
                "{\"schema_version\": 1, \"quilt_loader\": {\"id\": \"terralith\","
                        + " \"version\": \"2.5.4\", \"depends\": ["
                        + "{\"id\": \"minecraft\", \"versions\": \">=1.20\"},"
                        + "{\"id\": \"quilt_resource_loader\", \"versions\": \"*\","
                        + " \"unless\": \"fabric-resource-loader-v0\"}]}}"));
    }

    @Test
    void quiltJsonProvidesArrayIsIgnored(@TempDir Path dir) throws Exception {
        // provides is an aliasing mechanism ("this mod also satisfies X"), not an identity.
        // Treating it as one would migrate every jar that declares the same alias.
        assertEquals(Arrays.asList("realid"), read(dir, "quilt.mod.json",
                "{\"quilt_loader\": {\"id\": \"realid\","
                        + " \"provides\": [{\"id\": \"aliasid\"}]}}"));
    }

    @Test
    void quiltJsonTopLevelIdWithoutQuiltLoaderIsNotRead() {
        // Wrong depth. Fabric reads $.id, Quilt reads $.quilt_loader.id.
        assertTrue(ModIds.fromQuiltJson("{\"id\": \"wrongdepth\"}").isEmpty());
    }

    // ---------------------------------------------------------------- mcmod.info

    @Test
    void mcmodInfoRootArrayIsRead(@TempDir Path dir) throws Exception {
        // The Forge 1.12.2 universal jar's own mcpmod.info, byte-identical in format.
        assertEquals(Arrays.asList("mcp"), read(dir, "mcmod.info",
                "[\n{\n  \"modid\": \"mcp\",\n  \"name\": \"Minecraft Coder Pack\",\n"
                        + "  \"version\": \"9.42\",\n  \"mcversion\": \"1.12.2\",\n"
                        + "  \"authors\": [\"Searge\", \"ProfMobius\"],\n"
                        + "  \"dependencies\": []\n}\n]\n"));
    }

    @Test
    void mcmodInfoModListWrapperIsRead(@TempDir Path dir) throws Exception {
        // MetadataCollection.from branches on isJsonArray, so both shapes are valid input.
        assertEquals(Arrays.asList("examplemod"), read(dir, "mcmod.info",
                "{ \"modListVersion\": 2, \"modList\": [ { \"modid\": \"examplemod\","
                        + " \"name\": \"Example\", \"version\": \"1.0\" } ] }"));
    }

    @Test
    void mcmodInfoSeveralEntriesReturnsAll(@TempDir Path dir) throws Exception {
        // The "parent" field exists precisely so one file can list a mod and its children.
        assertEquals(Arrays.asList("parentmod", "childmod"), read(dir, "mcmod.info",
                "[{\"modid\": \"parentmod\", \"name\": \"Parent\"},"
                        + "{\"modid\": \"childmod\", \"parent\": \"parentmod\"}]"));
    }

    @Test
    void mcmodInfoCamelCaseModIdIsNotRead() {
        // This format alone is lowercase. ModMetadata.class's Java field is modId but it carries
        // @SerializedName("modid"), and getting this backwards is the single most likely mistake
        // because it is the opposite convention from mods.toml.
        assertTrue(ModIds.fromMcmodInfo("[{\"modId\": \"wrongcase\"}]").isEmpty());
    }

    @Test
    void mcmodInfoDependenciesAndRequiredModsAreIgnored(@TempDir Path dir) throws Exception {
        assertEquals(Arrays.asList("themod"), read(dir, "mcmod.info",
                "[{\"modid\": \"themod\", \"dependencies\": [\"forge\", \"jei\"],"
                        + " \"requiredMods\": [\"Forge@[14.23,)\"],"
                        + " \"authorList\": [\"someone\"]}]"));
    }

    // ---------------------------------------------------------------- union and jar-in-jar

    @Test
    void jarWithTomlAndFabricAndQuiltAllDeclaringOneIdReturnsThatIdOnce(@TempDir Path dir)
            throws Exception {
        // Terralith is the one jar in the reference instance that ships all three.
        assertEquals(Arrays.asList("terralith"), read(dir,
                "META-INF/mods.toml", "[[mods]]\nmodId = \"terralith\"\n",
                "fabric.mod.json", "{\"schemaVersion\": 1, \"id\": \"terralith\"}",
                "quilt.mod.json", "{\"quilt_loader\": {\"id\": \"terralith\"}}"));
    }

    @Test
    void jarDeclaringDifferentIdsInDifferentFormatsReturnsTheUnion(@TempDir Path dir)
            throws Exception {
        assertEquals(Arrays.asList("forgeside", "fabricside"), read(dir,
                "META-INF/mods.toml", "[[mods]]\nmodId = \"forgeside\"\n",
                "fabric.mod.json", "{\"id\": \"fabricside\"}"));
    }

    @Test
    void nestedJarModIdsAreNotRead(@TempDir Path dir) throws Exception {
        // DO NOT "FIX" THIS BY ADDING RECURSION.
        //
        // Fourteen different top-level mods in the 102-jar reference instance bundle a nested copy
        // of mixinextras. If HOPPER read nested jars, one distributed jar containing mixinextras
        // would make "mixinextras" a manifest mod id, and this client would then see thirteen
        // unrelated jars in the player's mods/ folder as the same mod and start moving them into
        // hoppermods/replaced/. That is data movement against jars HOPPER was told never to touch.
        //
        // It is also unnecessary: jar-in-jar exists so nested copies do not collide, and the loader
        // version-selects them. The hard duplicate-mod failure is between top-level files only.
        Path nested = jar(dir, "mixinextras-0.2.0.jar",
                "META-INF/mods.toml", "[[mods]]\nmodId = \"mixinextras\"\n");

        Path outer = dir.resolve("container.jar");
        OutputStream raw = Files.newOutputStream(outer);
        try {
            ZipOutputStream zip = new ZipOutputStream(raw);
            zip.putNextEntry(new ZipEntry("META-INF/jarjar/mixinextras-0.2.0.jar"));
            zip.write(Files.readAllBytes(nested));
            zip.closeEntry();
            zip.putNextEntry(new ZipEntry("META-INF/jarjar/metadata.json"));
            zip.write("{\"jars\":[]}".getBytes(StandardCharsets.UTF_8));
            zip.closeEntry();
            zip.finish();
            zip.close();
        } finally {
            raw.close();
        }

        assertTrue(ModIds.read(outer, HopperLog.STDOUT).isEmpty());
    }

    // ---------------------------------------------------------------- never throws

    @Test
    void jarWithNoMetadataReturnsEmpty(@TempDir Path dir) throws Exception {
        // A coremod or a plain library. Legitimate and extremely common.
        assertTrue(read(dir, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n",
                "com/example/Thing.class", "not really").isEmpty());
    }

    @Test
    void notAZipAtAllReturnsEmpty(@TempDir Path dir) throws Exception {
        Path f = dir.resolve("fake.jar");
        Files.write(f, "PK pretend forge jar payload".getBytes(StandardCharsets.UTF_8));

        assertTrue(ModIds.read(f, HopperLog.STDOUT).isEmpty());
    }

    @Test
    void emptyFileReturnsEmpty(@TempDir Path dir) throws Exception {
        Path f = dir.resolve("empty.jar");
        Files.write(f, new byte[0]);

        assertTrue(ModIds.read(f, HopperLog.STDOUT).isEmpty());
    }

    @Test
    void truncatedZipReturnsEmpty(@TempDir Path dir) throws Exception {
        Path full = jar(dir, "full.jar", "META-INF/mods.toml", "[[mods]]\nmodId = \"truncated\"\n");
        byte[] bytes = Files.readAllBytes(full);

        Path half = dir.resolve("half.jar");
        Files.write(half, Arrays.copyOf(bytes, bytes.length / 2));

        assertTrue(ModIds.read(half, HopperLog.STDOUT).isEmpty());
    }

    @Test
    void randomBytesReturnEmptyRatherThanThrow(@TempDir Path dir) throws Exception {
        byte[] noise = new byte[4096];
        new Random(1234).nextBytes(noise);

        Path f = dir.resolve("noise.jar");
        Files.write(f, noise);

        assertTrue(ModIds.read(f, HopperLog.STDOUT).isEmpty());
    }

    @Test
    void aMissingFileReturnsEmptyRatherThanThrow(@TempDir Path dir) {
        assertTrue(ModIds.read(dir.resolve("nothing-here.jar"), HopperLog.STDOUT).isEmpty());
    }

    @Test
    void aDirectoryPassedAsAJarReturnsEmptyRatherThanThrow(@TempDir Path dir) throws Exception {
        Path sub = dir.resolve("a-directory.jar");
        Files.createDirectories(sub);

        assertTrue(ModIds.read(sub, HopperLog.STDOUT).isEmpty());
    }

    @Test
    void metadataEntryOverTheSizeCapReturnsEmpty(@TempDir Path dir) throws Exception {
        // A real mods.toml is about a kilobyte. A jar is untrusted input.
        StringBuilder padding = new StringBuilder(2 * 1024 * 1024);
        for (int i = 0; i < 2 * 1024 * 1024; i++) padding.append('x');

        assertTrue(read(dir, "META-INF/mods.toml",
                "[[mods]]\nmodId = \"toobig\"\ndescription = \"" + padding + "\"\n").isEmpty());
    }

    @Test
    void malformedJsonYieldsNoIds(@TempDir Path dir) throws Exception {
        // A trailing comma. Json.parse is strict on purpose - it also parses the manifest - and the
        // .NET side uses JsonDocument's equally strict defaults, so both sides derive the same
        // (empty) set from this file. The .NET twin of this test is MalformedJson_ReturnsEmpty.
        assertTrue(read(dir, "fabric.mod.json", "{ \"id\": \"trailing\", }").isEmpty());
    }

    @Test
    void jsonThatIsNotAnObjectYieldsNoIds() {
        assertTrue(ModIds.fromFabricJson("\"just a string\"").isEmpty());
        assertTrue(ModIds.fromQuiltJson("[]").isEmpty());
        assertTrue(ModIds.fromMcmodInfo("42").isEmpty());
    }

    @Test
    void utf8BomInFrontOfTheMetadataIsStripped(@TempDir Path dir) throws Exception {
        // Real files carry one, and a BOM is a parse error for a JSON reader.
        assertEquals(Arrays.asList("bommed"),
                read(dir, "fabric.mod.json", "﻿{ \"id\": \"bommed\" }"));
    }

    @Test
    void entryNamesAreMatchedCaseSensitively(@TempDir Path dir) throws Exception {
        // All five names are exact and case-sensitive in every loader checked, so a loose match
        // could only ever invent one.
        assertTrue(read(dir, "META-INF/Mods.toml", "[[mods]]\nmodId = \"wrongcase\"\n").isEmpty());
    }
}
