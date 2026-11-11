# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Install Kerberos libraries required by Npgsql
RUN apt-get update && apt-get install -y \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Build stage
# Always build on the native architecture of the build machine and cross-compile for
# $TARGETARCH, so multi-arch builds never run the .NET SDK under QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy csproj files for caching
# Restore takes every package version from Directory.Packages.props, so it goes in first
COPY Directory.Packages.props ./
COPY src/DatabaseInitializer/DatabaseInitializer.csproj DatabaseInitializer/
COPY src/SharedLibrary/SharedLibrary.csproj SharedLibrary/
COPY src/DataLibrary/DataLibrary.csproj DataLibrary/

# Docker names the x86-64 architecture "amd64"; .NET calls it "x64" and rejects the
# invalid RID "linux-amd64". arm64 is spelled the same by both, so only amd64 is mapped.
RUN ARCH="$(echo "$TARGETARCH" | sed 's/^amd64$/x64/')" \
    && dotnet restore DatabaseInitializer/DatabaseInitializer.csproj -a "$ARCH"

# Copy everything else
COPY src/ .

# Publish DatabaseInitializer
WORKDIR /src/DatabaseInitializer
RUN ARCH="$(echo "$TARGETARCH" | sed 's/^amd64$/x64/')" \
    && dotnet publish -c Release -a "$ARCH" -o /app/publish

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DatabaseInitializer.dll"]
