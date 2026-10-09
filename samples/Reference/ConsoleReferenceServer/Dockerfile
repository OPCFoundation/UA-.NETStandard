# OPC UA ConsoleReferenceServer sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f samples/Reference/ConsoleReferenceServer/Dockerfile -t opcua-reference-server .
#
# The SDK stage always runs on the build platform ($BUILDPLATFORM): the
# publish output is portable IL, so a multi-platform build never compiles
# under emulation. Images CI publishes every sample once in a native pre-job
# and selects the "prebuilt" stage instead, so the image legs only copy files:
#   --build-arg PUBLISH_SOURCE=prebuilt --build-arg PUBLISH_DIR=<folder>
ARG PUBLISH_SOURCE=build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
ARG Version=0.0.0-local
ARG SimpleVersion=0.0.0
ARG FileVersion=0.0.0
ARG InformationalVersion=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
RUN dotnet publish "samples/Reference/ConsoleReferenceServer/ConsoleReferenceServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:UseAppHost=false -p:SelfContained=false \
    -p:Dockerbuild=true -p:Version="$Version" -p:AssemblyVersion="$SimpleVersion" \
    -p:FileVersion="$FileVersion" -p:InformationalVersion="$InformationalVersion" \
    -p:SourceRevisionId="$SourceRevisionId" -p:RepositoryUrl="$SourceRepositoryUrl" \
    -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish/refserver
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
