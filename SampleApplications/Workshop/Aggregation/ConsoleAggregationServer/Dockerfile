#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["ConsoleAggregationServer/ConsoleAggregationServer.csproj", "ConsoleAggregationServer/"]
COPY ["../../SDK/Opc.Ua.Server/Opc.Ua.Server.csproj", "../../SDK/Opc.Ua.Server/"]
COPY ["../../../Stack/Opc.Ua.Core/Opc.Ua.Core.csproj", "../../../Stack/Opc.Ua.Core/"]
COPY ["../../SDK/Opc.Ua.Configuration/Opc.Ua.Configuration.csproj", "../../SDK/Opc.Ua.Configuration/"]
COPY ["../../SDK/Opc.Ua.Client/Opc.Ua.Client.csproj", "../../SDK/Opc.Ua.Client/"]
RUN dotnet restore "ConsoleAggregationServer/ConsoleAggregationServer.csproj"
COPY . .
WORKDIR "/src/ConsoleAggregationServer"
RUN dotnet build "ConsoleAggregationServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ConsoleAggregationServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 4840
ENTRYPOINT ["dotnet", "ConsoleAggregationServer.dll"]
