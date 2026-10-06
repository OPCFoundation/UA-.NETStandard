# OPC UA ConsoleLdsServer (Local Discovery Server) sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f samples/Lds/ConsoleLdsServer/Dockerfile -t opcua-lds-server .
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
RUN dotnet publish "samples/Lds/ConsoleLdsServer/ConsoleLdsServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -p:UseAppHost=false \
    -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .
# OPC UA Local Discovery Server endpoint port.
EXPOSE 4840
ENTRYPOINT ["dotnet", "ConsoleLdsServer.dll"]
