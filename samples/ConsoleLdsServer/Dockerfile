# OPC UA ConsoleLdsServer (Local Discovery Server) sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f Applications/ConsoleLdsServer/Dockerfile -t opcua-lds-server .
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/ConsoleLdsServer/ConsoleLdsServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
# OPC UA Local Discovery Server endpoint port.
EXPOSE 4840
ENTRYPOINT ["dotnet", "ConsoleLdsServer.dll"]
