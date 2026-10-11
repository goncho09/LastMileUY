FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/LastMileUY.Backoffice/LastMileUY.Backoffice.csproj", "src/LastMileUY.Backoffice/"]
COPY ["src/LastMileUY.Contracts/LastMileUY.Contracts.csproj", "src/LastMileUY.Contracts/"]
RUN dotnet restore "src/LastMileUY.Backoffice/LastMileUY.Backoffice.csproj"
COPY . .
WORKDIR "/src/src/LastMileUY.Backoffice"
RUN dotnet build "./LastMileUY.Backoffice.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LastMileUY.Backoffice.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LastMileUY.Backoffice.dll"]
