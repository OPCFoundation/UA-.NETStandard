# OPC UA RedundantPubSub sample image.
#
# The build context must be the repository ROOT so project references resolve.
# Build directly:
#   docker build -f Applications/RedundantPubSub/Dockerfile -t opcua-redundant-pubsub .
# Or via docker compose:
#   docker compose -f Applications/RedundantPubSub/docker-compose.yml up --build
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/RedundantPubSub/RedundantPubSub.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:NuGetAudit=false \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RedundantPubSub.dll"]
