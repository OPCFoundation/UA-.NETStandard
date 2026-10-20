# Build from the repository root. Runtime state and credentials are mounted, never embedded.
# Images CI selects prebuilt and supplies its native, portable IL publish directory.
ARG PUBLISH_SOURCE=build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-azurelinux3.0 AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish samples/Recycling/RecyclingServer/RecyclingServer.csproj \
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 \
    -p:PublishAot=false -p:UseAppHost=false -p:SelfContained=false -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0 AS runtime
WORKDIR /app
COPY --from=publish --chown=app:app /app/publish/ ./
USER $APP_UID
EXPOSE 62570
ENTRYPOINT ["dotnet", "RecyclingServer.dll"]
