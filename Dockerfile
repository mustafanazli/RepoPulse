# syntax=docker/dockerfile:1

# Multi-stage build for RepoPulse.AuthApi ONLY. Built from the repository
# root (build context = repo root) because that is the only location every
# path this Dockerfile references resolves from.
#
# RepoPulse.AuthApi has no ProjectReference to any other project in this
# repo, so only its own csproj/sources are ever copied into the image.
#
# TLS is intentionally NOT terminated here. This container listens on plain
# HTTP on port 8080 only. In production it runs behind Azure Container Apps
# ingress, which terminates TLS and forwards HTTP internally — see
# docs/adr/004-production-hosting.md and the Hosting:BehindTlsTerminatingProxy
# configuration option in src/RepoPulse.AuthApi/Program.cs. No certificate,
# private key, or secret value is ever baked into this image (see
# .dockerignore).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from just the csproj, so dependency restore is cached
# independently of source-code changes.
COPY src/RepoPulse.AuthApi/RepoPulse.AuthApi.csproj src/RepoPulse.AuthApi/
RUN dotnet restore src/RepoPulse.AuthApi/RepoPulse.AuthApi.csproj

COPY src/RepoPulse.AuthApi/ src/RepoPulse.AuthApi/
RUN dotnet publish src/RepoPulse.AuthApi/RepoPulse.AuthApi.csproj \
    --no-restore \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the runtime image's built-in non-root "app" user rather than root.
USER $APP_UID

COPY --from=build /app/publish .

# Plain HTTP only, on the port Azure Container Apps expects as its target
# port (see docs/adr/004-production-hosting.md). External HTTPS is provided
# entirely by the Container Apps ingress layer, not by this container.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RepoPulse.AuthApi.dll"]
