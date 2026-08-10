# The locator build

Everything here used to live in comments inside `src/HOPPER.Locator/**/build.gradle`. It is checked
against real loader artifacts rather than assumed, and it is the reason the eight adapters work, so
it is written down rather than deleted.

`docs/how-it-works.md` covers what the locator does at runtime. This covers how it is built and what
each adapter may and may not touch.

## Modules

One adapter per loader generation, plus a shared core.

| Module | Covers | `--release` | Declared through |
| --- | --- | --- | --- |
| `hopper-core` | nothing on its own | 8 | - |
| `hopper-forge-1122` | Forge 1.12.x | 8 | `FMLCorePlugin` manifest attribute |
| `hopper-forge-1165` | Forge 1.14.4 - 1.16.5 | 8 | `META-INF/services/...forgespi.locating.IModLocator` |
| `hopper-forge-1182` | Forge 1.17.1 - 1.18.2 | 16 | `META-INF/services/...forgespi.locating.IModLocator` |
| `hopper-forge-modern` | Forge 1.19 - 26.2 | 17 | `META-INF/services/...forgespi.locating.IModLocator` |
| `hopper-neoforge` | NeoForge 21.1.x - 26.2.x | 21 | `META-INF/services/...locating.IModFileCandidateLocator` |
| `hopper-fabric` | Fabric 1.14+ | 8 | `fabric.mod.json` `preLaunch` entrypoint |
| `hopper-quilt` | Quilt, built but not served | 8 | `quilt.mod.json` `experimental_quilt_loader_plugin` |

`hopper-quilt` ships under a name that is not its module name, which is why the root `templates`
task holds a map rather than a list.

## The staleness stamp

`templates` is a manual step, so `build/templates` drifts behind the sources beside it. It drifted
silently once already: the jars still spoke of a `replaced/` folder for a while after the rename to
`parked/`, and the API served them without a word. The Dockerfile builds the templates in its own
stage, so only local and bare-metal runs can drift - which is exactly where the human is expected to
remember a command.

`templatesStamp` writes `build/templates/templates.stamp`, a SHA-256 over every `.java` outside a
`build/` directory: sorted by path, hashing `relative/path\n` then the file bytes.
`LocatorSourceDigest.cs` recomputes that scheme and `LocatorTemplateHealthCheck` compares the two,
reporting **Degraded** - never Unhealthy, because a stale locator still works and refusing to serve
one would be the worse failure. The check does nothing when there is no source tree to compare
against, which is the container.

Two decisions are load-bearing and both were arrived at by getting them wrong first:

- **Content, not timestamps.** A timestamp comparison calls a `touch` or a branch switch stale.
  Gradle is content-based and rightly does nothing for those, so `./gradlew templates` could not
  clear the warning it caused.
- **`templatesStamp` is `upToDateWhen { false }` and finalizes `templates`.** A comment-only edit
  moves the source digest without changing a byte of any jar, so the `Copy` is correctly skipped. If
  the stamp were written inside that task it would be skipped too, and the warning would again be
  one the remedy could not clear.

The two implementations are a hand-maintained pair. Change the scheme on one side and the other
reports permanently stale.

## Rules that apply to every module

**`--release`, never a toolchain.** It pins both the bytecode and the visible JDK API while letting
the build run on whatever JDK the machine already has. A toolchain would demand six exact JDKs be
installed and fail outright when they are not.

**`-Werror`, with exactly two lint categories off.** A locator runs before the loader has loaded
anything, and a mistake there is a launch that silently does nothing, so warnings are not advisory.
Both suppressions were measured:

- `-options` - javac 22 emits "source value 8 is obsolete", "target value 8 is obsolete" and a third
  line pointing at the flag itself. Under `-Werror` that is a hard failure on every Java 8 module:
  3 warnings, 1 error, on an empty class. Suppressing the category is the only way to keep `-Werror`
  and `--release 8` together.
- `-path` - `forge-1.12.2-14.23.5.2864-universal.jar`'s manifest carries a `Class-Path` pointing
  into an installer tree that does not exist in a Gradle cache. javac follows it and warns once per
  entry.

**Reproducible archives.** Gradle stamps every zip entry with the source file's mtime and emits
entries in filesystem order, both of which are wall-clock and machine noise. Fixed timestamps and
sorted entries make "the template did not change" a checkable statement. The guarantee is "same
sources, same JDK, same OS" - a different javac writes different class files, and permission bits
differ between a Windows host and the Docker JDK stage.

**Every adapter embeds the core.** The loader resolves one jar out of `mods/` and nothing else, so a
jar that merely referenced a separate core jar would die with `NoClassDefFoundError`. Each adapter
copies the core's class files in. The dependency is `compileOnly` rather than `implementation` on
purpose. That makes the copy the only mechanism putting the core anywhere, so nobody can start
relying on a Gradle runtime classpath that does not exist inside Minecraft. The copy takes the
`sourceSets` output rather than `zipTree(coreJar)` - it carries its own task dependency, brings no
second `META-INF/MANIFEST.MF` to merge away, and keeps the release-8 bytecode the core compiled to.

**`launchwrapper` 1.12 comes from `libraries.minecraft.net`.** It 404s on
`maven.minecraftforge.net`.

**The `templates` task is the only module-to-filename map.** `LocatorTemplates.For` addresses jars
by unversioned name, so a version bump never touches C#. The Dockerfile and a local `dotnet run`
both consume the task's output rather than restating it. Its copies use `from(task)` rather than
`from(file)` so `./gradlew templates` builds what it needs instead of silently copying nothing.

## Proving it on a real client

Nothing in `dotnet test` or `bun run test` starts a loader, so no test here can tell you the locator
works. `tools/locator-e2e/` can: it writes a throwaway Prism instance per adapter, serves it a
configured jar, launches the client, reads the log and closes it. Run it after touching anything in
this directory, and before a release. Its README explains each column of the result table.

## What the jar says it is

Every adapter jar carries the same identity: `hopper-icon.png` at the root, a loader descriptor, and
`Implementation-*`/`Specification-*` manifest attributes. The icon lives in `hopper-core` and
reaches
all seven jars through the same source-set copy that carries the core's classes.

**The version comes from `application.properties`,** the file the release workflow bumps, read by a
regex over its `<version>` element and searched for upwards from the locator root - the Docker build
puts it somewhere else than the repository does. The jars still land in `build/templates` under
unversioned names, because the `templates` task renames them and `LocatorTemplates` addresses them
that way.

**`ReplaceTokens`, not `expand`.** `ProcessResources` substitutes `@version@` by literal replacement.
`expand` was the obvious choice and was wrong: it runs a Groovy template engine, which also reads
backslash escapes, so the `
` in `mcmod.info`'s description became a real newline inside a JSON
string and the shipped file was not valid JSON. Nothing caught it, because Forge 1.12.2 never reads
that file and Groovy's own `JsonSlurper` accepts raw control characters. `ReplaceTokens` touches
nothing but the token, and `processResources` fails the build if any `@version@` survives it - which
is what happens when a new descriptor is added without listing it.

**Only Fabric and Quilt show it in game.** On the whole Forge family the locator jar is deliberately
excluded from mod scanning, so its descriptor is never parsed by the running game:

- Forge 1.16.5, 1.18.2 and modern - a jar providing `IModLocator` is collected by
  `ModDirTransformerDiscoverer` into `allExcluded()`, and `ModsFolderLocator.scanMods` filters that
  list out before it builds a single `ModFile`.
- NeoForge - `LaunchContext` seeds `locatedPaths` from every resolved module in every layer at
  construction. The locator jar is on the service layer by then, so `ModDiscoverer.addPath` answers
  "already located earlier" and skips it.
- Forge 1.12.2 - `CoreModManager` adds a coremod to `ignoredModFiles` and logs "it will not be
  examined again", unless the manifest carries `FMLCorePluginContainsFMLMod`. That marker makes FML
  warn that `@Mod`s belong in a separate jar and then demand a `@Mod` class this jar has no reason
to
  have, so it is not set.

The `mods.toml`, `neoforge.mods.toml` and `mcmod.info` files are still worth shipping: launchers
read
them. Prism's local mod parser names and icons a jar in `mods/` from exactly these files, so without
one the entry is a bare filename. They declare `lowcodefml` because the jar genuinely carries no
mod entrypoint. Nothing in the game reads the field, so it costs nothing on 1.16.5, where that
language provider does not exist.

## hopper-core

Download, sha256 verify, stale sweep, report POST, config merge. The hash check and the
path-traversal check exist exactly once, here.

Release 8 is the constraint that shapes the whole build. Minecraft 1.16.5 and older run on Java 8,
so the core has to compile at 8 for every adapter to embed it. That holds whether the adapter
targets 1.12.2 on an 8u JVM or Forge 26.2 on Java 25. Concretely that costs `HttpURLConnection`
instead of
`java.net.http.HttpClient` (11+), plain classes instead of records (16+), no `var` (10+), no text
blocks (15+), no switch expressions (14+), no `InputStream.readAllBytes` (9+), no `List.of` or
`Files.readString` (9+/11+).

**Nothing is on its compile classpath.** Not log4j, not gson, and both are deliberate:

- gson - a Quilt loader plugin runs inside `QuiltPluginClassLoader`, which owns only the packages
  `quilt.mod.json` lists and whose parent has Quilt's shaded gson, not `com.google.gson`. Bundling
  real gson into every adapter would also be a split package against the real Gson module on Forge's
  SERVICE layer. A small hand-written `Json` class costs less and, like the hash check, then exists
  exactly once.
- log4j - the same classloader argument. The core logs through `HopperLog` and each adapter supplies
  a two-line implementation over whatever logger it actually has.

The consequence worth stating: this module cannot import from any mod loader, because it has no mod
loader on its classpath to import from. That is enforced by the build, not by discipline.

`compileTestJava` inherits `options.release = 8`, so a test that can only see the Java 8 API cannot
accidentally exercise a Java 9+ leak in the core.

## hopper-forge-1122

Not a locator. Legacy FML predates ModLauncher entirely, so this is an `IFMLLoadingPlugin` coremod
loaded by LaunchWrapper, with its own manifest attribute and its own lifecycle.

Forge 1.12.x and nothing older. 1.7.10 has `IFMLLoadingPlugin` too, but
`LibraryManager.gatherLegacyCanidates` and the `--mods` blackboard route do not exist there in this
form, which makes 1.7.10 an unverified claim rather than a supported one.

The forge dependency is fetched with `@jar` because the pom is `<packaging>pom</packaging>` with
zero `<dependency>` elements - there is no metadata worth resolving.

`FMLCorePlugin` is the only manifest attribute `CoreModManager.discoverCoreMods` reads to find the
jar; there is no services file on 1.12.2, since LaunchWrapper predates `ServiceLoader` discovery.
Three attributes are deliberately absent:

- `FMLCorePluginContainsFMLMod` - this jar is not an `@Mod`, and setting it makes FML log "This is
  not recommended" on every launch.
- `ModType` - a value not containing FML makes FML skip the jar outright. Unset is accepted.
- `ModSide` - unset is accepted; `BOTH` would be equivalent.

## hopper-forge-1165

An `IModLocator`, but not the modern one. At forgespi 3.2.0 and below the interface has no
`IModProvider` supertype, `scanMods()` returns `List<IModFile>`, and `findPath` / `findManifest`
are abstract. `HopperLocator` from `hopper-forge-modern` cannot be reused: same delegate pattern,
different method set.

1.13.2 is out of reach: it ships forgespi 0.13.0, which has no `IModLocator` class at all. The
ceiling is 1.16.5 and stays there - 1.17.1 ships forgespi 4.0.9, which deleted `findPath` and
`findManifest`, so that generation is `hopper-forge-1182`.

The build compiles against the oldest forgespi in the range so the compiler enforces the floor:
1.14.4 ships 1.5.0, 1.15.2 ships 3.0.0, 1.16.5 ships 3.2.0, and 3.2.0 only adds the
`findManifestAndSigners` default, so code written against 1.5.0 satisfies all three.
`Environment.Keys` at 1.5.0 is `DIST`, `MODFOLDERFACTORY`, `MODDIRECTORYFACTORY`,
`PROGRESSMESSAGE` -
`MODFILEFACTORY` does not exist yet, so do not touch that key.

modlauncher 8.1.3 is what 1.16.5 ships, and the one signature this module touches -
`Launcher.INSTANCE.environment().getProperty(TypesafeMap$Key)` - is byte-identical in 4.1.0, 5.1.0,
8.0.9 and 8.1.3, so compiling against any one of them covers the whole range.

The floor is the default, so an ordinary build proves it. Re-prove the ceiling with:

```bash
./gradlew :hopper-forge-1165:compileJava --rerun-tasks \
    -Pforgespi1165Version=3.2.0 -Pmodlauncher1165Version=8.1.3
```

Bare versions rather than whole GAVs, unlike `hopper-forge-modern`: modlauncher only moved from
`cpw.mods` to `net.minecraftforge` at 10.1.1, long after this range ended.

## hopper-forge-1182

Forge on 1.17.1, 1.18, 1.18.1 and 1.18.2 - the generation that `hopper-forge-1165` and
`hopper-forge-modern` each explicitly decline to cover. It is a separate module because forgespi's
`IModLocator` genuinely differs at both ends of it:

| forgespi | Minecraft | Shape |
| --- | --- | --- |
| 3.2.0 | <= 1.16.5 | `interface IModLocator` with `findPath` + `findManifest` |
| 4.0.9 | 1.17.1+ | `interface IModLocator`, both removed |
| 6.0.0 | 1.19+ | `interface IModLocator extends IModProvider`, `List<ModFileOrException>` |

Three shapes, three modules. What each version actually ships, read out of
`forge-<version>-installer.jar`'s `version.json` rather than the fmlloader pom, which only states
the dynamic range `4.0.+`:

| Forge | forgespi | modlauncher |
| --- | --- | --- |
| 1.17.1-37.0.0 | 4.0.9 | 9.0.7 |
| 1.17.1-37.1.1 | 4.0.10 | 9.0.7 |
| 1.18-38.0.17 | 4.0.10 | 9.0.7 |
| 1.18.1-39.1.2 | 4.0.10 | 9.1.3 |
| 1.18.2-40.0.0 | 4.0.10 | 9.1.3 |
| 1.18.2-40.2.0 | 4.0.15-4.x | 9.1.3 |
| 1.18.2-40.3.12 | 4.0.15-4.x | 9.1.3 |

No shipped Forge uses forgespi 4.0.2-4.0.8 (which still had `findPath`/`findManifest`) or 5.0.x
(which briefly had `IModProvider` with `List<IModFile>`), so neither is claimed. 5.0.x never reached
a release at all: 1.19-41.0.0 was still on 4.0.10 and 1.19-41.0.63 was already on 6.0.0.

Release 16, not 17: Minecraft 1.17.1 runs on Java 16 and is the floor. 1.18+ runs on Java 17,
which loads classfile 60 without complaint. Both forgespi 4.0.x and modlauncher 9.x are themselves
classfile 60, so 16 is also the lowest release that can see them.

4.0.9 is the oldest forgespi in the range. Its `IModLocator` declares exactly five abstract methods
- `name`, `scanMods`, `scanFile`, `initArguments`, `isValid` - and 4.0.10 declares the same five.
4.0.15-4.x turns `scanMods()` into a default and adds a defaulted `scanMods(Iterable)`, so a class
written against 4.0.9 satisfies all three. A class written against 4.0.2 satisfies none of them,
because that one also demands `findPath` and `findManifest`.

`Launcher.INSTANCE.environment().getProperty(TypesafeMap$Key)` and `IEnvironment$Keys.GAMEDIR` are
byte-identical in modlauncher 9.0.7 and 9.1.3, checked with javap on both jars.

`Automatic-Module-Name` is required here, unlike on 1.16.5, and for the same reason as on 1.19+.
FML 37.x and 40.x both build the `ServiceLoader` with `ServiceLoader.load(ModuleLayer,
IModLocator.class)` against `IModuleLayerManager.Layer.SERVICE`, verified by javap on fmlloader
1.17.1-37.0.0 and 1.18.2-40.3.12. This jar becomes a JPMS module, so its name must not come from
the filename.

`ModDirTransformerDiscoverer` on both 1.17.1 and 1.18.2 opens each jar in `mods/` as a zip and looks
for the literal services entry name; it does not read the module descriptor here, unlike 1.19+. The
entry has to exist either way, so one jar layout satisfies the discoverer and the `ServiceLoader`.
No `mods.toml` and no `FMLModType`: `ModsFolderLocator` consults `ModDirTransformerDiscoverer`'s
exclusion list, so the locator jar is never offered to `ModValidator` as a mod.

Re-prove the ceiling with:

```bash
./gradlew :hopper-forge-1182:compileJava --rerun-tasks \
    -Pforgespi1182Version=4.0.15-4.x -Pmodlauncher1182Version=9.1.3
```

## hopper-forge-modern

Forge on Minecraft 1.19 through 26.2 (the newest being 26.2-65.1.0 as of 2026-08-06).

Not 1.17/1.18: those ship forgespi 4.0.x, whose `IModLocator` has no `IModProvider` supertype and
returns `List<IModFile>` from `scanMods()`. Do not try to lower the floor here to reach them - the
two interfaces cannot be implemented by one class, which is why `hopper-forge-1182` exists.

Release 17, not 21: Minecraft 1.19 runs on Java 17 and is the floor. Forge 26.x runs on Java 25,
which loads classfile 61 without complaint.

6.0.0 is the oldest forgespi whose `scanMods()` returns `List<ModFileOrException>`; it first shipped
in Forge 1.19-41.1.0. Its `IModProvider`, `IModDirectoryLocatorFactory`, `ModFileOrException` and
`Environment$Keys` are byte-identical to 8.0.0's - the whole locating package has a zero diff
between 7.0.1 and 8.0.0. Minecraft 1.20.1 (forgespi 7.0.1) therefore sits in the middle of this
range rather than at an edge, and cannot regress without both the floor and the ceiling regressing
with it.

The modlauncher signatures this module touches - `Launcher.INSTANCE`, `environment()`,
`IEnvironment$Keys.GAMEDIR` - are byte-identical between 10.0.1 and 10.2.6.

Everything is `compileOnly`: all of it is already on the SERVICE layer when the locator runs. No
ForgeGradle and no reobf either - a locator never touches a Minecraft class, so there is nothing to
remap.

`Automatic-Module-Name` is required. `SecureJar` derives the SERVICE-layer module name from it and
`ModDirTransformerDiscoverer` reads `provides` off that derived descriptor. Without it the name
comes from the filename, which breaks the moment someone renames the jar.

Re-prove the ceiling when a new Forge ships:

```bash
./gradlew :hopper-forge-modern:compileJava --rerun-tasks \
    -PforgespiVersion=8.0.0 -PmodlauncherCoordinate=net.minecraftforge:modlauncher:10.2.6
```

modlauncher is a whole GAV rather than a bare version, and that is not tidiness. The groupId
changed from `cpw.mods` to `net.minecraftforge` at 10.1.1 (Forge 1.20.4), and
`cpw.mods:modlauncher` stops at 10.0.9. A version-only knob cannot reach the ceiling at all: it
resolves nothing, and the check fails for the wrong reason. The Java package is
`cpw.mods.modlauncher` in both.

## hopper-neoforge

NeoForge 21.1.x through 26.2.x. One adapter serves both ends: the whole
`net.neoforged.neoforgespi.locating` package is byte-identical between loader 4.0.24 and 11.0.16
except for `IDiscoveryPipeline`'s `addJarContent`/`readModFile` parameter type, which this module
must not touch.

Release 21, not 25: NeoForge 21.1 runs on Java 21 (loader 4.0.24 is classfile 65) and NeoForge 26.x
runs on Java 25, which loads classfile 65 without complaint.

The SPI classes live inside the loader jar. Not `net.neoforged:neoforgespi` - that artifact is the
abandoned NeoForge-1.20.1 fork of forgespi and still carries the old `IModLocator` - and not
`net.neoforged.fancymodloader:spi`, which is superseded and discontinued. 4.0.24 is the loader
NeoForge 21.1.9 pins, the oldest in the claimed range, so an ordinary build proves the floor.

**The cross-version contract.** This adapter may touch only:

- `IModFileCandidateLocator`, `IOrderedProvider`
- `IDiscoveryPipeline.addPath(Path, ModFileDiscoveryAttributes, IncompatibleFileReporting)`
- `ModFileDiscoveryAttributes.DEFAULT`, `IncompatibleFileReporting.*`
- `ILaunchContext.isLocated(Path)`, `ILaunchContext.addLocated(Path)`

It must not touch `IModFile`, `JarContents`, `addJarContent`, `readModFile` or
`ILaunchContext.environment()` - all of those broke between 4.0.43 and 11.0.16. It must not touch
`net.neoforged.fml.ModLoadingIssue` either: it resolves in both, but reporting an error issue blocks
the launch, and HOPPER's rule is that a failed sync never does.

`getPriority()` must return above `IOrderedProvider.HIGHEST_SYSTEM_PRIORITY` (1000).
`ServiceLoaderUtil.loadServices` sorts by `getPriority()` reversed, higher runs earlier, and
`hopper/` has to be populated before `ModsFolderLocator` walks `mods/`.

Do not use `IModFileCandidateLocator.forFolder(File, String)` - it returns a `ModsFolderLocator`
that would scan `hopper/` before the sync had a chance to run.

`Automatic-Module-Name` is needed on 21.1.x, where the jar becomes a JPMS automatic module on the
ModLauncher SERVICE layer and `META-INF/services` is read off the derived module descriptor. It is
harmless on 26.x, which dropped ModLauncher and reads the same file literally out of a plain
`URLClassLoader` entry. One jar layout satisfies both.

Do not claim below NeoForge 21.1. `IModFileCandidateLocator` first appears in loader somewhere
between 3.0.10 (absent) and 3.0.20 (present), around NeoForge 20.6, and that range has not been
verified.

## hopper-fabric

Degraded on purpose, and the adapter says so in its log line.

Fabric exposes no pre-discovery hook. `Knot.init` runs `loader.load()` - all discovery and
resolution - then `loader.freeze()`, then Mixin bootstrap, then the transformers, and only then
`invokeEntrypoints("preLaunch", ...)`. By the time HOPPER runs the loader is frozen and additional
mods throw `IllegalStateException`. There is no ordering trick that changes this.

The one lever is the `fabric.addMods` system property, read inside `discoverMods`, so setting it
from `preLaunch` is far too late. Setting it early enough means a JVM command-line argument, which
is a launcher setting HOPPER refuses to need. So: sync in `preLaunch`, and when something changed,
tell the player a restart is required.

Release 8: fabric-loader's own classes are classfile 52, and Fabric spans Minecraft 1.14 (Java 8) to
1.21+ (Java 21). Minecraft has shipped log4j in every version Fabric supports, and Knot unlocks the
game classpath one line before it invokes `preLaunch`, so `log4j-api` is reachable there.

Declared in `src/main/resources/fabric.mod.json`, not a services file, because Knot invokes the
literal key `preLaunch`. The manifest carries nothing beyond `Implementation-Version`.

**This is the one adapter that writes into a directory the player owns**, and therefore the one that
carries its own tests rather than leaning on hopper-core's. Syncing into `hopper/` alone is useless
on Fabric - nothing scans that directory, so a restart would change nothing - so this adapter, and
only this adapter, also reconciles `mods/`. The core still syncs `hopper/` with no `mods/` write in
it, and a `ModsFolderMirror` owns exactly the filenames it records in `hopper/mods-mirror.txt`. A
file in `mods/` that is not in that list is never touched.

And it is opt-in. Writing into a player's directory is the one thing in this project that can
destroy something of theirs, so it needs a human to have said yes. That is `fabricMirrorMods=true`
in `config/hopper.properties`, read from the player's own file and never from the server-written
properties embedded in the jar. Off, the adapter still syncs `hopper/` and then states plainly
that nothing
will load and which line to add. See `Config.mirrorMods()`.

## hopper-quilt

Built, never served, and the archive name has to shout it.

`org.quiltmc.loader.api.plugin.QuiltLoaderPlugin` is a real, documented plugin API - the right shape
for HOPPER, with `QuiltPluginContext.addFolderToScan(Path)` doing exactly what is needed. The catch
is the declaration: `V1ModMetadataImpl` throws a `ParseException`, a hard failure rather than a
degradation, the moment it sees the `experimental_quilt_loader_plugin` key while
`-Dloader.experimental.allow_loading_plugins=true` is unset.

**The plugin jar is not served, and the flag above is not enough.** Measured on Quilt Loader 0.29.2
with a real client:

- Flag unset: `ParseException: Mod hopper provides a loader plugin, which is not yet allowed!`, and
  the client does not start. That is the failure this section always predicted.
- Flag set: it gets past parsing, HOPPER appears in the mod table, and then Quilt's own plugin
  classloader gives up - `NoClassDefFoundError: org/quiltmc/loader/api/plugin/QuiltLoaderPlugin`,
  `already has a package defined, refusing to load it's classes from elsewhere`. Our jar carries
  `ch/pianonic` and `META-INF` only; nothing of Quilt's is shaded into it.

So the flag buys a different crash, not a working locator, and either way the client will not boot.
`LocatorTemplates.For` therefore refuses every variant and the download always gets
`hopper-fabric.jar`, which Quilt runs through `quilted_fabric_loader` - degraded, restart required,
verified working on the same instance.

`hopper-quilt-plugin.jar` is still built and still checked by `ShippedTemplateTests`. It is correct
code waiting on Quilt; the moment loader plugins are allowed, serving it again is one arm in `For`.

Release 8: quilt-loader's own `.module` metadata declares `"org.gradle.jvm.version": 8`, and
`QuiltLoaderPlugin` itself is classfile 52. The pom is `<packaging>pom</packaging>` with no
dependencies; the real graph is in the `.module` metadata.

Declared in `src/main/resources/quilt.mod.json` as a top-level key, sibling of `quilt_loader`:

```json
"experimental_quilt_loader_plugin": {
    "class": "ch.pianonic.hopper.HopperQuiltPlugin",
    "packages": ["ch.pianonic.hopper"]
}
```

**That single `packages` entry is why every class in every module stays in `ch.pianonic.hopper`.**
`QuiltPluginClassLoader` owns only the packages listed there, and a sub-package added later would be
forgotten here and fail at runtime.

`HopperQuiltPlugin` needs a public no-arg constructor - `QuiltPluginContextImpl` does
`loadClassDirectly(...)` then `getDeclaredConstructor().newInstance()`. It must log through
`HopperLog.STDOUT` rather than log4j, because `QuiltPluginClassLoader`'s parent carries Quilt's
shaded logging and not `org.apache.logging.log4j`. That is why the core has `HopperLog` at all.

Do not call `addFileToScan` - it has two overloads (`PluginGuiTreeNode` and `QuiltTreeNode`) and
passing null is ambiguous. `addFolderToScan(Path)` has a single signature.
