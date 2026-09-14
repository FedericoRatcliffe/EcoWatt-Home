# Build del publisher simulado desde la raiz del repo (context: .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EcoWattCasa.Domain/*.csproj EcoWattCasa.Domain/
COPY EcoWattCasa.Application/*.csproj EcoWattCasa.Application/
COPY EcoWattCasa.Infrastructure/*.csproj EcoWattCasa.Infrastructure/
COPY EcoWattCasa.MockDevices/*.csproj EcoWattCasa.MockDevices/
RUN dotnet restore EcoWattCasa.MockDevices/EcoWattCasa.MockDevices.csproj

COPY EcoWattCasa.Domain/ EcoWattCasa.Domain/
COPY EcoWattCasa.Application/ EcoWattCasa.Application/
COPY EcoWattCasa.Infrastructure/ EcoWattCasa.Infrastructure/
COPY EcoWattCasa.MockDevices/ EcoWattCasa.MockDevices/
RUN dotnet publish EcoWattCasa.MockDevices/EcoWattCasa.MockDevices.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "EcoWattCasa.MockDevices.dll"]
