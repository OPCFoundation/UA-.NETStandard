# OPC UA MCP server sample image (exposes OPC UA services as MCP tools).
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f tools/Opc.Ua.Mcp/Dockerfile -t opcua-mcp-server .
#
# Defaults to the stdio MCP transport (run with `docker run -i ...`). For the
# HTTP/SSE transport run with `--transport sse --port 5100`.
#
# The SDK stage always runs on the build platform ($BUILDPLATFORM): the
# publish output is portable IL, so a multi-platform build never compiles
# under emulation. Images CI publishes every sample once in a native pre-job
# and selects the "prebuilt" stage instead, so the image legs only copy files:
#   --build-arg PUBLISH_SOURCE=prebuilt --build-arg PUBLISH_DIR=<folder>
ARG PUBLISH_SOURCE=build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:UseAppHost=false \
    -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/aspnet AS final
WORKDIR /app
COPY --from=publish /app/publish .
# HTTP/SSE MCP transport port (see --transport sse --port).
EXPOSE 5100
ENTRYPOINT ["dotnet", "Opc.Ua.Mcp.dll"]
