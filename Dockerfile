FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props CondertechGov.sln ./
COPY src/CondertechGov.SharedKernel/*.csproj src/CondertechGov.SharedKernel/
COPY src/CondertechGov.Identity/*.csproj src/CondertechGov.Identity/
COPY src/CondertechGov.Tenancy/*.csproj src/CondertechGov.Tenancy/
COPY src/CondertechGov.Commercial/*.csproj src/CondertechGov.Commercial/
COPY src/CondertechGov.Infrastructure/*.csproj src/CondertechGov.Infrastructure/
COPY src/CondertechGov.Api/*.csproj src/CondertechGov.Api/
COPY src/CondertechGov.Worker/*.csproj src/CondertechGov.Worker/
COPY tests/CondertechGov.Tests/*.csproj tests/CondertechGov.Tests/
COPY tests/CondertechGov.IntegrationTests/*.csproj tests/CondertechGov.IntegrationTests/
COPY tools/CondertechGov.PublicApiProbe/*.csproj tools/CondertechGov.PublicApiProbe/
RUN dotnet restore CondertechGov.sln
COPY src/ src/
ARG APP_PROJECT=Api
RUN dotnet publish src/CondertechGov.${APP_PROJECT}/CondertechGov.${APP_PROJECT}.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CondertechGov.Api.dll"]
