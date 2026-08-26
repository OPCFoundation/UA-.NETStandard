# ModelManagementServer container image.
#
# Multi-stage, framework-dependent build on the .NET AzureLinux 3 base
# images, following samples/DI/PumpDeviceIntegrationServer/Dockerfile.
#
# IMPORTANT: the build context MUST be the repository ROOT (not this
# folder), because the project pulls in the whole source tree. Build it
# like this:
#
#   # from the repository root:
#   docker build -f samples/AI/ModelManagementServer/Dockerfile \
#                -t modelmanagementserver:local .
#
# The final image runs as a non-root user and listens on 0.0.0.0:62640
# (override host/port via the `host` / `port` environment variables).

FROM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS build
WORKDIR /src

# JIT is disabled for the source generators unless W^X is turned off on
# some kernels; match the existing sample Dockerfiles.
ENV DOTNET_EnableWriteXorExecute=0

# Copy the full repository. The project pulls in many transitive
# ProjectReferences, a source-generator analyzer, and an AdditionalFiles
# NodeSet2 XML input, so a per-csproj copy list would be fragile.
COPY . .

# Fail fast with a clear message when the build context is not the
# repository root (UA.slnx is the root marker).
RUN test -f UA.slnx || { \
        echo ''; \
        echo 'ERROR: build the repository ROOT as the Docker context, not this folder.'; \
        echo '       The image needs the full source tree (src/, tools/).'; \
        echo ''; \
        echo '  From the repository root run:'; \
        echo '    docker build -f samples/AI/ModelManagementServer/Dockerfile -t modelmanagementserver .'; \
        echo ''; \
        exit 1; \
    }

# The project imports use lower-case tools/, but the repository directory is
# Tools/. Windows hides the difference; the Linux container does not.
RUN if [ ! -d tools ] && [ -d Tools ]; then ln -s Tools tools; fi

# MSBuild in the Linux SDK needs a Unix separator and stable logical names here
# so the source generator embeds the design resources it reads during publish.
RUN sed -i 's#<EmbeddedResource Include="Design\\\*">#<EmbeddedResource Include="Design/*" LogicalName="Opc.Ua.SourceGeneration.Design.%(Filename)%(Extension)">#' \
        Tools/Opc.Ua.SourceGeneration.Core/Opc.Ua.SourceGeneration.Core.csproj

ARG Version
ARG SimpleVersion
ARG InformationalVersion

RUN dotnet restore "samples/AI/ModelManagementServer/ModelManagementServer.csproj" \
        --force -p:NoHttps=true

# PublishAot is off for this sample. The workload-identity credential path
# resolves tokens through a library that serialises by reflection, which the
# AOT analyzer warns on and this repository builds warnings as errors. Nothing
# in the OPC UA surface needs AOT; see the sample README.
RUN dotnet publish "samples/AI/ModelManagementServer/ModelManagementServer.csproj" \
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
# EVP operations. SymCrypt's ECDSA SignHash path rejects the self-signing
# call the OPC UA stack makes when creating its ECC application-instance
# certificate. Clearing the forced default lets the standard OpenSSL
# default provider handle ECDSA.
RUN sed -i 's/^default_properties = .*/default_properties = ""/' /etc/pki/tls/openssl.cnf

WORKDIR /app
COPY --from=build /app/publish .

# The .NET base images ship a predefined non-root user `app` (UID 1654,
# exposed via APP_UID). The Server creates its certificate store under
# /app at runtime.
#
# /var/run/secrets/ai is where a mounted credential is read from. It is
# created empty so the anonymous path works without a Secret, and so the
# directory permissions are right when one is mounted.
ENV HOME=/app
RUN mkdir -p /app/pki /var/run/secrets/ai \
    && chown -R $APP_UID:0 /app /var/run/secrets/ai \
    && chmod -R g=u /app /var/run/secrets/ai

USER $APP_UID

# Bind to all interfaces so the endpoint is reachable from outside the
# container. Override with `-e host=...` / `-e port=...`.
ENV host=0.0.0.0 \
    port=62640

EXPOSE 62640

ENTRYPOINT ["dotnet", "ModelManagementServer.dll"]
