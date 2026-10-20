# Build from the repository root. Keep credentials, private scenarios and state outside the context.
# Images CI publishes portable IL once natively, then selects the prebuilt stage for both architectures.
ARG PUBLISH_SOURCE=build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402-azurelinux3.0 AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish samples/Recycling/RecyclingOperatorConsole/RecyclingOperatorConsole.csproj \
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 \
    -p:PublishAot=false -p:UseAppHost=false -p:SelfContained=false -o /app/publish

FROM scratch AS prebuilt
ARG PUBLISH_DIR=artifacts/docker/publish
COPY ${PUBLISH_DIR}/ /app/publish/

FROM ${PUBLISH_SOURCE} AS publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0 AS runtime
WORKDIR /app
COPY --from=publish --chown=app:app /app/publish/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "RecyclingOperatorConsole.dll"]
