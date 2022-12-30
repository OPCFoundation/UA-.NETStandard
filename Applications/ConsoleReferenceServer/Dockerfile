FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# copy csproj and restore as distinct layers
COPY *.* .
COPY ["Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj", "Applications/ConsoleReferenceServer/"]
COPY ["Stack/Opc.Ua.Core/Opc.Ua.Core.csproj", "Stack/Opc.Ua.Core/"]
COPY ["Libraries/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj", "Libraries/Opc.Ua.Security.Certificates/"]
COPY ["Libraries/Opc.Ua.Configuration/Opc.Ua.Configuration.csproj", "Libraries/Opc.Ua.Configuration/"]
COPY ["Libraries/Opc.Ua.Server/Opc.Ua.Server.csproj", "Libraries/Opc.Ua.Server/"]
COPY ["Applications/Quickstarts.Servers/Quickstarts.Servers.csproj", "Applications/Quickstarts.Servers/"]
RUN dotnet restore "Applications/ConsoleReferenceServer/ConsoleReferenceServer.csproj" -p:NoHttps=true 
COPY . .

WORKDIR "/src/Applications/ConsoleReferenceServer"
ARG Version
ARG SimpleVersion
ARG AssemblyFileVersion
ENV OPTIONS="-c Release -f net6.0 -p:NoHttps=true -p:Version=$(Version) -p:AssemblyVersion=$(SimpleVersion) -p:FileVersion=$(AssemblyFileVersion) "
RUN dotnet build "ConsoleReferenceServer.csproj" --no-restore ${OPTIONS} -o /app/build

FROM build AS publish
RUN dotnet publish "ConsoleReferenceServer.csproj" ${OPTIONS} -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 62541
ENTRYPOINT ["dotnet", "ConsoleReferenceServer.dll"]
