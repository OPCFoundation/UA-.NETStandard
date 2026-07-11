# PumpDeviceIntegrationServer container image.
#
# Multi-stage, framework-dependent build on the .NET AzureLinux 3 base
# images.
#
# IMPORTANT: the build context MUST be the repository ROOT (not this
# folder), because the project pulls in the whole source tree (Stack/,
# Libraries/, Tools/). Build it like this:
#
#   # from the repository root:
#   docker build -f Applications/PumpDeviceIntegrationServer/Dockerfile \
#                -t pumpdeviceintegrationserver:local .
#
# Running `docker build .` from inside this folder will fail fast with a
# helpful message (the context would be missing the rest of the repo).
#
# The final image runs as a non-root user and listens on 0.0.0.0:62542
# (override host/port via the `host` / `port` environment variables).

FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS build
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
        echo '       The image needs the full source tree (Stack/, Libraries/, Tools/).'; \
        echo ''; \
        echo '  From the repository root run:'; \
        echo '    docker build -f Applications/PumpDeviceIntegrationServer/Dockerfile -t pumpdeviceintegrationserver .'; \
        echo ''; \
        exit 1; \
    }

ARG Version
ARG SimpleVersion
ARG InformationalVersion

RUN dotnet restore "Applications/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj" \
        --force -p:NoHttps=true

RUN dotnet publish "Applications/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj" \
        --no-restore \
        -c Release \
        -f net10.0 \
        -p:PublishAot=false \
        -p:NoHttps=true \
        -p:Dockerbuild=true \
        -p:Version=${Version} \
        -p:AssemblyVersion=${SimpleVersion} \
        -p:FileVersion=${Version} \
        -p:InformationalVersion=${InformationalVersion} \
        -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0 AS final

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
# certificate/PKI store under /app at runtime, so make it writable by
# that user.
ENV HOME=/app
RUN mkdir -p /app/pki && chown -R $APP_UID:$APP_UID /app

USER $APP_UID

# Bind to all interfaces so the endpoint is reachable from outside the
# container. Override with `-e host=...` / `-e port=...`.
ENV host=0.0.0.0 \
    port=62542

EXPOSE 62542

ENTRYPOINT ["dotnet", "PumpDeviceIntegrationServer.dll"]
