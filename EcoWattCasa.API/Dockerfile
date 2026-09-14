# Build de la API desde la raiz del repo (context: .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EcoWattCasa.Domain/*.csproj EcoWattCasa.Domain/
COPY EcoWattCasa.Application/*.csproj EcoWattCasa.Application/
COPY EcoWattCasa.Infrastructure/*.csproj EcoWattCasa.Infrastructure/
COPY EcoWattCasa.API/*.csproj EcoWattCasa.API/
RUN dotnet restore EcoWattCasa.API/EcoWattCasa.API.csproj

COPY EcoWattCasa.Domain/ EcoWattCasa.Domain/
COPY EcoWattCasa.Application/ EcoWattCasa.Application/
COPY EcoWattCasa.Infrastructure/ EcoWattCasa.Infrastructure/
COPY EcoWattCasa.API/ EcoWattCasa.API/
RUN dotnet publish EcoWattCasa.API/EcoWattCasa.API.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# tzdata: sin esto el contenedor no resuelve America/Argentina/Buenos_Aires.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "EcoWattCasa.API.dll"]
