FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LeadPilot.Crm/LeadPilot.Crm.csproj src/LeadPilot.Crm/
COPY src/LeadPilot.Core/LeadPilot.Core.csproj src/LeadPilot.Core/
COPY src/LeadPilot.Api/LeadPilot.Api.csproj src/LeadPilot.Api/
RUN dotnet restore src/LeadPilot.Api/LeadPilot.Api.csproj
COPY src/ src/
RUN dotnet publish src/LeadPilot.Api/LeadPilot.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LeadPilot.Api.dll"]
