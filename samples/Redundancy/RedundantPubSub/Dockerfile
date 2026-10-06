# OPC UA RedundantPubSub sample image.
#
# The build context must be the repository ROOT so project references resolve.
# Build directly:
#   docker build -f samples/Redundancy/RedundantPubSub/Dockerfile -t opcua-redundant-pubsub .
# Or via docker compose:
#   docker compose -f samples/Redundancy/RedundantPubSub/docker-compose.yml up --build
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
RUN dotnet publish "samples/Redundancy/RedundantPubSub/RedundantPubSub.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:NuGetAudit=false \
    -p:UseAppHost=false \
    -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RedundantPubSub.dll"]
