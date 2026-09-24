# Build from the repository root; stack and generator dependencies are project references.
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
RUN dotnet publish "samples/Reference/ConsoleReferenceServer/ConsoleReferenceServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:Dockerbuild=true -p:Version="$Version" -p:AssemblyVersion="$SimpleVersion" \
    -p:FileVersion="$FileVersion" -p:InformationalVersion="$InformationalVersion" \
    -p:SourceRevisionId="$SourceRevisionId" -p:RepositoryUrl="$SourceRepositoryUrl" \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
