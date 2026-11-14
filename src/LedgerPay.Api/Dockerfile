FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY protos/ protos/
COPY src/LedgerPay.Api/LedgerPay.Api.csproj src/LedgerPay.Api/
RUN dotnet restore src/LedgerPay.Api/LedgerPay.Api.csproj
COPY src/LedgerPay.Api/ src/LedgerPay.Api/
RUN dotnet publish src/LedgerPay.Api/LedgerPay.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "LedgerPay.Api.dll"]
