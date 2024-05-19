FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["vgt-saga-hotel.csproj", "./"]
RUN dotnet restore "vgt-saga-hotel.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "vgt-saga-hotel.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "vgt-saga-hotel.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "vgt-saga-hotel.dll"]