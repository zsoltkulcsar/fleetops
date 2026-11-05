FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY ["src/FleetOps.Api/FleetOps.Api.csproj", "src/FleetOps.Api/"]
COPY ["src/Kernel/Kernel.csproj", "src/Kernel/"]
RUN dotnet restore "src/FleetOps.Api/FleetOps.Api.csproj"
COPY . .
RUN dotnet publish "src/FleetOps.Api/FleetOps.Api.csproj" -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=80
COPY --from=build /app/publish .
COPY deploy/db/supabase-ca.crt /app/certs/supabase-ca.crt
ENTRYPOINT ["dotnet", "FleetOps.Api.dll"]
