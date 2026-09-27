FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/SignalDesk.Core/SignalDesk.Core.csproj src/SignalDesk.Core/
COPY src/SignalDesk.Api/SignalDesk.Api.csproj src/SignalDesk.Api/
RUN dotnet restore src/SignalDesk.Api/SignalDesk.Api.csproj
COPY src/ src/
RUN dotnet publish src/SignalDesk.Api/SignalDesk.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SignalDesk.Api.dll"]
