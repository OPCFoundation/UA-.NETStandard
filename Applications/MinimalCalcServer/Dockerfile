# OPC UA MinimalCalcServer sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f Applications/MinimalCalcServer/Dockerfile -t opcua-minimal-calc-server .
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/MinimalCalcServer/MinimalCalcServer.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
# OPC UA server endpoint port.
EXPOSE 4841
ENTRYPOINT ["dotnet", "MinimalCalcServer.dll"]
