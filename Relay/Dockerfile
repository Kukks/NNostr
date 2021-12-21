FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Relay/Relay.csproj", "Relay/"]
COPY ["NNostr.Client/NNostr.Client.csproj", "NNostr.Client/"]
RUN dotnet restore "Relay/Relay.csproj"
COPY . .
WORKDIR "/src/Relay"
RUN dotnet build "Relay.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Relay.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Relay.dll"]
