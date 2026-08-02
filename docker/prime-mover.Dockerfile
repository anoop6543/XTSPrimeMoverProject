FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/shared/Contracts/ ./shared/Contracts/
COPY src/prime-mover/PrimeMoverService/ ./prime-mover/PrimeMoverService/
WORKDIR /src/prime-mover/PrimeMoverService
RUN dotnet restore && dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_URLS="http://+:8080"
EXPOSE 8080
EXPOSE 9090
ENTRYPOINT ["dotnet", "PrimeMoverService.dll"]
