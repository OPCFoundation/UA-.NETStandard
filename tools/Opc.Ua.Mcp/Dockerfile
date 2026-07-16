# OPC UA MCP server sample image (exposes OPC UA services as MCP tools).
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f Applications/McpServer/Dockerfile -t opcua-mcp-server .
#
# Defaults to the stdio MCP transport (run with `docker run -i ...`). For the
# HTTP/SSE transport run with `--transport sse --port 5100`.
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/McpServer/Opc.Ua.Mcp.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet AS final
WORKDIR /app
COPY --from=build /app/publish .
# HTTP/SSE MCP transport port (see --transport sse --port).
EXPOSE 5100
ENTRYPOINT ["dotnet", "Opc.Ua.Mcp.dll"]
