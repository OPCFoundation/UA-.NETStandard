# OPC UA MCP server sample image (exposes OPC UA services as MCP tools).
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f tools/Opc.Ua.Mcp/Dockerfile -t opcua-mcp-server .
#
# Defaults to the stdio MCP transport (run with `docker run -i ...`). For the
# HTTP/SSE transport run with `--transport sse --port 5100`.
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
RUN dotnet publish "tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:Dockerbuild=true -p:Version="$Version" -p:AssemblyVersion="$SimpleVersion" \
    -p:FileVersion="$FileVersion" -p:InformationalVersion="$InformationalVersion" \
    -p:SourceRevisionId="$SourceRevisionId" -p:RepositoryUrl="$SourceRepositoryUrl" \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:011bb5f30180717b1c8b65822ff2c99bcb96bc65af0164589751b83c7b4949f7 AS final
ARG Version=0.0.0-local
ARG SourceRevisionId
ARG SourceRepositoryUrl=https://github.com/OPCFoundation/UA-.NETStandard
LABEL org.opencontainers.image.version=$Version \
      org.opencontainers.image.revision=$SourceRevisionId \
      org.opencontainers.image.source=$SourceRepositoryUrl
WORKDIR /app
COPY --from=build /app/publish .
# HTTP/SSE MCP transport port (see --transport sse --port).
EXPOSE 5100
ENTRYPOINT ["dotnet", "Opc.Ua.Mcp.dll"]
