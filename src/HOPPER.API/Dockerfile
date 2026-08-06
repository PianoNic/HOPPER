# syntax=docker/dockerfile:1.7

# ---------- Stage 1: frontend ----------
# Pinned to BUILDPLATFORM so the Angular build runs once on the runner's native arch instead of
# once per target platform under QEMU. The output is plain JS/CSS, so the same artifacts are valid
# for every target arch.
FROM --platform=$BUILDPLATFORM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend
COPY src/HOPPER.Frontend/package.json src/HOPPER.Frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/HOPPER.Frontend .
RUN bun run build:agent --output-path /artifacts/frontend

# ---------- Stage 2: backend restore ----------
# Also pinned to BUILDPLATFORM. The publish below is framework-dependent with UseAppHost=false and
# no RuntimeIdentifier, so it emits portable IL - one native build is valid for every target arch
# and only the small final stage is built per-arch.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS backend-deps
WORKDIR /src/backend
COPY ["src/HOPPER.API/HOPPER.API.csproj",                       "HOPPER.API/"]
COPY ["src/HOPPER.Application/HOPPER.Application.csproj",       "HOPPER.Application/"]
COPY ["src/HOPPER.Infrastructure/HOPPER.Infrastructure.csproj", "HOPPER.Infrastructure/"]
COPY ["src/HOPPER.Domain/HOPPER.Domain.csproj",                 "HOPPER.Domain/"]
COPY ["src/Directory.Build.props",                              "./"]
RUN dotnet restore "HOPPER.API/HOPPER.API.csproj"

# ---------- Stage 3: backend publish ----------
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY src .
# Directory.Build.props (at /src/backend) reads ..\application.properties to stamp the version - i.e.
# /src/application.properties. It lives at the repo root, not under src/, so copy it explicitly;
# without it the assembly version falls back to .NET's default 1.0.0 and the dashboard shows "v1.0.0".
COPY application.properties /src/application.properties
RUN dotnet publish "HOPPER.API/HOPPER.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------- Stage 3b: the Forge locator template jar ----------
# GET /api/servers/{id}/jar serves per-server copies of this jar, so a deployed HOPPER cannot hand
# out a client without it. Built here rather than committed as a binary, so the jar always matches
# the locator sources in this repo.
#
# BUILDPLATFORM again: the output is Java bytecode built with --release 17, identical on every arch.
# The Gradle wrapper pins its own version and checksum, so this stage needs only a JDK.
FROM --platform=$BUILDPLATFORM eclipse-temurin:21-jdk AS locator-build
WORKDIR /src/locator
COPY src/HOPPER.Locator/gradle ./gradle
COPY src/HOPPER.Locator/gradlew src/HOPPER.Locator/settings.gradle src/HOPPER.Locator/build.gradle ./
# Warm the wrapper and dependency cache in its own layer, so editing a .java file does not re-download
# Gradle itself on every build.
RUN chmod +x ./gradlew && ./gradlew --no-daemon --version
COPY src/HOPPER.Locator/src ./src
RUN ./gradlew --no-daemon build \
    && cp build/libs/hopper-*.jar /artifacts-locator.jar

# ---------- Stage 4: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-azurelinux3.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production

# The blob store is the only state this image owns; the database is a separate Postgres service.
# ConnectionStrings__HopperDatabase has no default here on purpose - the built-in fallback points
# at a dev host, which is not something a container should quietly reach for. compose.yml sets it.
ENV Blobs__Directory=/data/blobs

# The template the jar endpoint patches per server. Baked in rather than mounted, so the jar handed
# to players always matches the API serving it.
ENV Hopper__LocatorTemplatePath=/app/locator/hopper-template.jar

COPY --from=backend-build /app/publish .
COPY --from=locator-build /artifacts-locator.jar ./locator/hopper-template.jar

# This is what makes environment.prod.ts's `apiBaseUrl: window.location.origin` true: the built SPA
# is served by the API itself out of wwwroot (Program.cs: UseSpaStaticFiles + MapFallbackToFile),
# so the dashboard and the API are always the same origin in a deployed HOPPER and no cross-origin
# allowlist is needed.
COPY --from=frontend-build /artifacts/frontend/browser ./wwwroot

# Blobs are jars uploaded by an admin and the DB is a plain file - nothing here needs root, and the
# image writes only under /data.
RUN mkdir -p /data && chown -R app:app /data /app
USER app

ENTRYPOINT ["dotnet", "HOPPER.API.dll"]
