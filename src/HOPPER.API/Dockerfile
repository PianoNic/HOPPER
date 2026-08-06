# syntax=docker/dockerfile:1.7

FROM --platform=$BUILDPLATFORM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend
COPY src/HOPPER.Frontend/package.json src/HOPPER.Frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/HOPPER.Frontend .
RUN bun run build:agent --output-path /artifacts/frontend

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS backend-deps
WORKDIR /src/backend
COPY ["src/HOPPER.API/HOPPER.API.csproj",                       "HOPPER.API/"]
COPY ["src/HOPPER.Application/HOPPER.Application.csproj",       "HOPPER.Application/"]
COPY ["src/HOPPER.Infrastructure/HOPPER.Infrastructure.csproj", "HOPPER.Infrastructure/"]
COPY ["src/HOPPER.Domain/HOPPER.Domain.csproj",                 "HOPPER.Domain/"]
COPY ["src/Directory.Build.props",                              "./"]
RUN dotnet restore "HOPPER.API/HOPPER.API.csproj"

FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY src .
COPY application.properties /src/application.properties
RUN dotnet publish "HOPPER.API/HOPPER.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM --platform=$BUILDPLATFORM eclipse-temurin:21-jdk AS locator-build
WORKDIR /src/locator
COPY src/HOPPER.Locator/gradle ./gradle
COPY src/HOPPER.Locator/gradlew src/HOPPER.Locator/settings.gradle src/HOPPER.Locator/build.gradle ./
RUN chmod +x ./gradlew && ./gradlew --no-daemon --version
COPY src/HOPPER.Locator ./
RUN chmod +x ./gradlew && ./gradlew --no-daemon build templates \
    && cp -r build/templates /artifacts-locator

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-azurelinux3.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production

ENV Blobs__Directory=/data/blobs

ENV Hopper__LocatorTemplateDirectory=/app/locator

COPY --from=backend-build /app/publish .
COPY --from=locator-build /artifacts-locator ./locator

COPY --from=frontend-build /artifacts/frontend/browser ./wwwroot

RUN mkdir -p /data && chown -R app:app /data /app
USER app

ENTRYPOINT ["dotnet", "HOPPER.API.dll"]
