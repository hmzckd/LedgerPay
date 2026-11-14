FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LedgerPay.Notifications/LedgerPay.Notifications.csproj src/LedgerPay.Notifications/
RUN dotnet restore src/LedgerPay.Notifications/LedgerPay.Notifications.csproj
COPY src/LedgerPay.Notifications/ src/LedgerPay.Notifications/
RUN dotnet publish src/LedgerPay.Notifications/LedgerPay.Notifications.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "LedgerPay.Notifications.dll"]
