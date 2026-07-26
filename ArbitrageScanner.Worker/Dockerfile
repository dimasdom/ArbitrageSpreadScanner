# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["ArbitrageScanner.Worker/ArbitrageScanner.Worker.csproj", "ArbitrageScanner.Worker/"]
COPY ["ArbitrageScanner.Domain/ArbitrageScanner.Domain.csproj", "ArbitrageScanner.Domain/"]
COPY ["ArbitrageScanner.Infrastructure/ArbitrageScanner.Infrastructure.csproj", "ArbitrageScanner.Infrastructure/"]
COPY ["ArbitrageScanner.Futures/ArbitrageScanner.Futures.csproj", "ArbitrageScanner.Futures/"]
COPY ["ArbitrageScanner.Funding/ArbitrageScanner.Funding.csproj", "ArbitrageScanner.Funding/"]
COPY ["ArbitrageScanner.Spot/ArbitrageScanner.Spot.csproj", "ArbitrageScanner.Spot/"]
RUN dotnet restore "./ArbitrageScanner.Worker/ArbitrageScanner.Worker.csproj"
COPY . .
WORKDIR "/src/ArbitrageScanner.Worker"
RUN dotnet build "./ArbitrageScanner.Worker.csproj" -c "$BUILD_CONFIGURATION" -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ArbitrageScanner.Worker.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ArbitrageScanner.Worker.dll"]