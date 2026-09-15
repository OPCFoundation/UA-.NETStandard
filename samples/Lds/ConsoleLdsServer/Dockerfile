# OPC UA ConsoleLdsServer (Local Discovery Server) sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
ARG Version=0.0.0-local
ARG SimpleVersion=0.0.0
ARG FileVersion=0.0.0
ARG InformationalVersion=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
RUN dotnet publish "samples/Lds/ConsoleLdsServer/ConsoleLdsServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:Dockerbuild=true -p:Version="$Version" -p:AssemblyVersion="$SimpleVersion" \
    -p:FileVersion="$FileVersion" -p:InformationalVersion="$InformationalVersion" \
    -p:SourceRevisionId="$SourceRevisionId" -p:RepositoryUrl="$SourceRepositoryUrl" \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:cd45a6df90f98d55605fe958a6f6a89cfb746a4dc143ca451757e3e24e4f4b50 AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl
WORKDIR /app
COPY --from=build /app/publish .
# OPC UA Local Discovery Server endpoint port.
EXPOSE 4840
ENTRYPOINT ["dotnet", "ConsoleLdsServer.dll"]
