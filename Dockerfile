FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "src/FleetOps.Api/FleetOps.Api.csproj" -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
# Vercel only reached the container on port 80: a non-root image on 8080 returned FUNCTION_INVOCATION_FAILED.
ENV ASPNETCORE_HTTP_PORTS=80
ENV ASPNETCORE_ENVIRONMENT=Demo
COPY --from=build /app/publish .
COPY deploy/db/supabase-ca.crt /app/certs/supabase-ca.crt
ENTRYPOINT ["dotnet", "FleetOps.Api.dll"]
