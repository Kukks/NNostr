FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
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
ENV NNOSTR_USER_OVERRIDE=/datadir/user-override.json
COPY --from=publish /app/publish .

VOLUME /datadir
ENTRYPOINT ["dotnet", "Relay.dll"]
