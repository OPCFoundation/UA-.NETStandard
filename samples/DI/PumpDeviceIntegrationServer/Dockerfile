# PumpDeviceIntegrationServer container image.
#
# Multi-stage, framework-dependent build on the .NET AzureLinux 3 base
# images.
#
# IMPORTANT: the build context MUST be the repository ROOT (not this
# folder), because the project pulls in the whole source tree (src/,
# src/, tools/). Build it like this:
#
#   # from the repository root:
#   docker build -f samples/DI/PumpDeviceIntegrationServer/Dockerfile \
#                -t pumpdeviceintegrationserver:local .
#
# Running `docker build .` from inside this folder will fail fast with a
# helpful message (the context would be missing the rest of the repo).
#
# The final image runs as a non-root user and listens on 0.0.0.0:62542
# (override host/port via the `host` / `port` environment variables).

FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0@sha256:0a694e0479f1a604bc57dcdf95181a861f0c1b1ecd5b4ee8c1c2662b30c82150 AS build
WORKDIR /src

# JIT is disabled for the source generators unless W^X is turned off on
# some kernels; match the existing ConsoleReferenceServer Dockerfile.
ENV DOTNET_EnableWriteXorExecute=0

# Copy the full repository. The project pulls in many transitive
# ProjectReferences, a source-generator analyzer, and AdditionalFiles
# NodeSet2 XML/CSV inputs, so a per-csproj copy list would be fragile.
COPY . .

# Fail fast with a clear message when the build context is not the
# repository root (UA.slnx is the root marker).
RUN test -f UA.slnx || { \
        echo ''; \
        echo 'ERROR: build the repository ROOT as the Docker context, not this folder.'; \
        echo '       The image needs the full source tree (src/, src/, tools/).'; \
        echo ''; \
        echo '  From the repository root run:'; \
        echo '    docker build -f samples/DI/PumpDeviceIntegrationServer/Dockerfile -t pumpdeviceintegrationserver .'; \
        echo ''; \
        exit 1; \
    }

ARG Version=0.0.0-local
ARG SimpleVersion=0.0.0
ARG FileVersion=0.0.0
ARG InformationalVersion=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard

RUN dotnet restore "samples/DI/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj" \
        --force -p:CustomTestTarget=net10.0

RUN dotnet publish "samples/DI/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj" \
        --no-restore \
        -c Release \
        -f net10.0 \
        -p:PublishAot=false \
        -p:Dockerbuild=true \
        -p:CustomTestTarget=net10.0 \
        -p:Version="$Version" \
        -p:AssemblyVersion="$SimpleVersion" \
        -p:FileVersion="$FileVersion" \
        -p:InformationalVersion="$InformationalVersion" \
        -p:SourceRevisionId="$SourceRevisionId" \
        -p:RepositoryUrl="$SourceRepositoryUrl" \
        -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0@sha256:487cd067b8b58cf03a0ff5daed4212e66f33a68e0ee630d2bd65604cef88e034 AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl

# AzureLinux forces the SymCrypt OpenSSL provider as the default for all
# EVP operations (default_properties = "?provider=symcryptprovider" in
# openssl.cnf). SymCrypt's ECDSA SignHash path rejects the self-signing
# call the OPC UA stack makes when creating its ECC application-instance
# certificate ("operation not supported for this keytype"). Clearing the
# forced default lets the standard OpenSSL default provider handle ECDSA
# (still fully secure; just not the FIPS-validated SymCrypt module).
RUN sed -i 's/^default_properties = .*/default_properties = ""/' /etc/pki/tls/openssl.cnf

WORKDIR /app
COPY --from=build /app/publish .

# The .NET base images ship a predefined non-root user `app` (UID 1654,
# exposed via the APP_UID env var). The server creates its
# certificate/PKI store under /app at runtime. The shared /diag
# directory carries the diagnostic socket and collected artifacts for
# the companion Kubernetes diagnostics container.
ENV HOME=/app \
    DOTNET_DiagnosticPorts=/diag/dotnet-diagnostic.sock,suspend=n,listen
RUN mkdir -p /app/pki /diag \
    && chown -R $APP_UID:0 /app /diag \
    && chmod -R g=u /app /diag

USER $APP_UID

# Bind to all interfaces so the endpoint is reachable from outside the
# container. Override with `-e host=...` / `-e port=...`.
ENV host=0.0.0.0 \
    port=62542

EXPOSE 62542

ENTRYPOINT ["dotnet", "PumpDeviceIntegrationServer.dll"]
