# OPC UA PumpDeviceIntegrationServer sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f Applications/PumpDeviceIntegrationServer/Dockerfile -t opcua-pump-server .
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/PumpDeviceIntegrationServer/PumpDeviceIntegrationServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
# OPC UA server endpoint port.
EXPOSE 4841
ENTRYPOINT ["dotnet", "PumpDeviceIntegrationServer.dll"]
