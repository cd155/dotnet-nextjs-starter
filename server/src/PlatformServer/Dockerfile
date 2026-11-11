FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install Kerberos libraries required by Npgsql and curl for health checks
RUN apt-get update && apt-get install -y \
    curl \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Always build on the native architecture of the build machine and cross-compile for
# $TARGETARCH, so multi-arch builds never run the .NET SDK under QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# copy csproj files first to leverage layer caching
# Restore takes every package version from Directory.Packages.props, so it goes in first
COPY Directory.Packages.props ./
COPY src/PlatformServer/PlatformServer.csproj PlatformServer/
COPY src/SharedLibrary/SharedLibrary.csproj SharedLibrary/
COPY src/DataLibrary/DataLibrary.csproj DataLibrary/

# Docker names the x86-64 architecture "amd64"; .NET calls it "x64" and rejects the
# invalid RID "linux-amd64". arm64 is spelled the same by both, so only amd64 is mapped.
RUN ARCH="$(echo "$TARGETARCH" | sed 's/^amd64$/x64/')" \
    && dotnet restore PlatformServer/PlatformServer.csproj -a "$ARCH"

# then copy everything else
COPY src/ .
WORKDIR /src/PlatformServer
RUN ARCH="$(echo "$TARGETARCH" | sed 's/^amd64$/x64/')" \
    && dotnet publish -c Release -a "$ARCH" -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PlatformServer.dll"]
