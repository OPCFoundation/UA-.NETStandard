# OPC UA RedundantClient (managed client) sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f samples/RedundantClient/Dockerfile -t opcua-redundant-client .
# Or via docker compose (see samples/RedundantServer/docker-compose.*.yml).
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "samples/RedundantClient/RedundantClient.csproj" \
    -c Release -f net10.0 -p:PublishAot=false \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RedundantClient.dll"]
