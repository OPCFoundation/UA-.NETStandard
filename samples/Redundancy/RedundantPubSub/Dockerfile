# OPC UA RedundantPubSub sample image.
#
# The build context must be the repository ROOT so project references resolve.
# Build directly:
#   docker build -f samples/Redundancy/RedundantPubSub/Dockerfile -t opcua-redundant-pubsub .
# Or via docker compose:
#   docker compose -f samples/Redundancy/RedundantPubSub/docker-compose.yml up --build
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
ARG Version=0.0.0-local
ARG SimpleVersion=0.0.0
ARG FileVersion=0.0.0
ARG InformationalVersion=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
RUN dotnet publish "samples/Redundancy/RedundantPubSub/RedundantPubSub.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:Dockerbuild=true -p:Version="$Version" -p:AssemblyVersion="$SimpleVersion" \
    -p:FileVersion="$FileVersion" -p:InformationalVersion="$InformationalVersion" \
    -p:SourceRevisionId="$SourceRevisionId" -p:RepositoryUrl="$SourceRepositoryUrl" \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0.12@sha256:ff17a18b639a0327e52c7c296fa2e1abe6e03eb61d8121a8ef67cc6aa430a27e AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RedundantPubSub.dll"]
