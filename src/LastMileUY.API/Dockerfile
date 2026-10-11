#Compilar API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet publish src/LastMileUY.API/LastMileUY.API.csproj \
    -c Release \
    -o /app/publish

# Ejecutar API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LastMileUY.API.dll"]