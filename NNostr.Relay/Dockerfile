FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["NNostr.Relay/NNostr.Relay.csproj", "NNostr.Relay/"]
RUN dotnet restore "NNostr.Relay/NNostr.Relay.csproj"
COPY . .
WORKDIR "/src/NNostr.Relay"
RUN dotnet build "NNostr.Relay.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NNostr.Relay.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NNostr.Relay.dll"]
