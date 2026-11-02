FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN mkdir -p artifacts/packages && dotnet nuget add source /src/artifacts/packages --name candidates
RUN dotnet restore src/FIAP.TechChallenge.Billing.API
RUN dotnet publish src/FIAP.TechChallenge.Billing.API -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FIAP.TechChallenge.Billing.API.dll"]
