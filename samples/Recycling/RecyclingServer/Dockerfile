# Build from the repository root. Runtime state and credentials are mounted, never embedded.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-azurelinux3.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/Recycling/RecyclingServer/RecyclingServer.csproj \
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 \
    -p:PublishAot=false -p:UseAppHost=false -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
USER $APP_UID
EXPOSE 62570
ENTRYPOINT ["dotnet", "RecyclingServer.dll"]
