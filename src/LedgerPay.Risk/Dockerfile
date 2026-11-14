FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY protos/ protos/
COPY src/LedgerPay.Risk/LedgerPay.Risk.csproj src/LedgerPay.Risk/
RUN dotnet restore src/LedgerPay.Risk/LedgerPay.Risk.csproj
COPY src/LedgerPay.Risk/ src/LedgerPay.Risk/
RUN dotnet publish src/LedgerPay.Risk/LedgerPay.Risk.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "LedgerPay.Risk.dll"]
