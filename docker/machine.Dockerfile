# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY src/shared/Contracts/ ./shared/Contracts/
COPY src/machines/MachineService/ ./machines/MachineService/
COPY src/temporal/machine-worker/ ./temporal/machine-worker/

WORKDIR /app/machines/MachineService
RUN dotnet restore && dotnet publish -c Release -o /publish/machine-service

WORKDIR /app/temporal/machine-worker
RUN dotnet restore && dotnet publish -c Release -o /publish/machine-worker

# --- Runtime stage: machine service ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS machine-service
WORKDIR /app
COPY --from=build /publish/machine-service .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
EXPOSE 9090
ENTRYPOINT ["dotnet", "MachineService.dll"]

# --- Runtime stage: machine API ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS machine-api
WORKDIR /app
COPY src/shared/Contracts/ /build/shared/Contracts/
COPY src/machines/MachineApi/ /build/machines/MachineApi/
WORKDIR /build/machines/MachineApi
RUN dotnet restore && dotnet publish -c Release -o /publish
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "MachineApi.dll"]
