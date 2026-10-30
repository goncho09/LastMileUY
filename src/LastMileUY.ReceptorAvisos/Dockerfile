FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

WORKDIR /app

# Preparar el directorio persistente para las claves HMAC.
# Un volumen Docker nuevo heredará estos permisos.
RUN mkdir -p /app/data \
    && chown -R $APP_UID:$APP_UID /app/data \
    && chmod 700 /app/data

# Ejecutar .NET sin privilegios de administrador.
USER $APP_UID

EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/LastMileUY.ReceptorAvisos/LastMileUY.ReceptorAvisos.csproj", "src/LastMileUY.ReceptorAvisos/"]
COPY ["src/LastMileUY.Contracts/LastMileUY.Contracts.csproj", "src/LastMileUY.Contracts/"]
RUN dotnet restore "src/LastMileUY.ReceptorAvisos/LastMileUY.ReceptorAvisos.csproj"
COPY . .
WORKDIR "/src/src/LastMileUY.ReceptorAvisos"
RUN dotnet build "./LastMileUY.ReceptorAvisos.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LastMileUY.ReceptorAvisos.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LastMileUY.ReceptorAvisos.dll"]
