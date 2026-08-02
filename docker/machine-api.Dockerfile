FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/shared/Contracts/ ./shared/Contracts/
COPY src/machines/MachineApi/ ./machines/MachineApi/
WORKDIR /src/machines/MachineApi
RUN dotnet restore && dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_URLS="http://+:8081"
EXPOSE 8081
ENTRYPOINT ["dotnet", "MachineApi.dll"]
