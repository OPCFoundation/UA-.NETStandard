FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY *.* .
COPY ["Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj", "Applications/ConsoleReferenceServer/"]
COPY ["Stack/Opc.Ua.Core/Opc.Ua.Core.csproj", "Stack/Opc.Ua.Core/"]
COPY ["Libraries/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj", "Libraries/Opc.Ua.Security.Certificates/"]
COPY ["Libraries/Opc.Ua.Configuration/Opc.Ua.Configuration.csproj", "Libraries/Opc.Ua.Configuration/"]
COPY ["Libraries/Opc.Ua.Server/Opc.Ua.Server.csproj", "Libraries/Opc.Ua.Server/"]
COPY ["Applications/Quickstarts.Servers/Quickstarts.Servers.csproj", "Applications/Quickstarts.Servers/"]
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet restore "Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj" --force -p:NoHttps=true -p:CustomTestTarget=net8.0
COPY . .

WORKDIR "/src/Applications/ConsoleReferenceServer"
ARG Version
ARG SimpleVersion
ARG InformationalVersion
ENV OPTIONS="-c Release -f net8.0 -p:NoHttps=true -p:Dockerbuild=true -p:Version=${Version} -p:AssemblyVersion=${SimpleVersion} -p:FileVersion=${Version} -p:InformationalVersion=${InformationalVersion} -p:CustomTestTarget=net8.0"

FROM build AS publish
ENV DOTNET_EnableWriteXorExecute=0
RUN dotnet publish "ConsoleReferenceServer.csproj" --no-restore ${OPTIONS} -o /app/publish 

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
