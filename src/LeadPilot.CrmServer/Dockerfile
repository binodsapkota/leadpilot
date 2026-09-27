FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LeadPilot.Crm/LeadPilot.Crm.csproj src/LeadPilot.Crm/
COPY src/LeadPilot.CrmServer/LeadPilot.CrmServer.csproj src/LeadPilot.CrmServer/
RUN dotnet restore src/LeadPilot.CrmServer/LeadPilot.CrmServer.csproj
COPY src/LeadPilot.Crm/ src/LeadPilot.Crm/
COPY src/LeadPilot.CrmServer/ src/LeadPilot.CrmServer/
RUN dotnet publish src/LeadPilot.CrmServer/LeadPilot.CrmServer.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8081
EXPOSE 8081
USER $APP_UID
ENTRYPOINT ["dotnet", "LeadPilot.CrmServer.dll"]
