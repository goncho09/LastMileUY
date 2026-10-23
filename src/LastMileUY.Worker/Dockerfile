FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/LastMileUY.Worker/LastMileUY.Worker.csproj", "src/LastMileUY.Worker/"]
COPY ["src/LastMileUY.Contracts/LastMileUY.Contracts.csproj", "src/LastMileUY.Contracts/"]
RUN dotnet restore "src/LastMileUY.Worker/LastMileUY.Worker.csproj"
COPY . .
WORKDIR "/src/src/LastMileUY.Worker"
RUN dotnet build "./LastMileUY.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LastMileUY.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LastMileUY.Worker.dll"]
