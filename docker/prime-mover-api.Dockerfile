FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/shared/Contracts/ ./shared/Contracts/
COPY src/prime-mover/PrimeMoverApi/ ./prime-mover/PrimeMoverApi/
WORKDIR /src/prime-mover/PrimeMoverApi
RUN dotnet restore && dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_URLS="http://+:8082"
EXPOSE 8082
ENTRYPOINT ["dotnet", "PrimeMoverApi.dll"]
