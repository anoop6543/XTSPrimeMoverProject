FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/shared/Contracts/ ./shared/Contracts/
COPY src/workforce/WorkforceService/ ./workforce/WorkforceService/
WORKDIR /src/workforce/WorkforceService
RUN dotnet restore && dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "WorkforceService.dll"]
