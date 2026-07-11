# OPC UA ConsoleReferencePubSubClient sample image.
#
# The build context must be the repository ROOT so the project references resolve.
# Build directly:
#   docker build -f Applications/ConsoleReferencePubSubClient/Dockerfile -t opcua-pubsub-client .
FROM mcr.microsoft.com/dotnet/sdk AS build
WORKDIR /src
ENV DOTNET_EnableWriteXorExecute=0
COPY . .
RUN dotnet publish "Applications/ConsoleReferencePubSubClient/ConsoleReferencePubSubClient.csproj" \
    -c Release -f net10.0 -p:PublishAot=false -p:CustomTestTarget=net10.0 \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
# OPC UA endpoint and PubSub (UADP/MQTT) ports.
EXPOSE 4840 4841
ENTRYPOINT ["dotnet", "ConsoleReferencePubSubClient.dll"]
