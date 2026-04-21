#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Jobly.TestWorker/Jobly.TestWorker.csproj", "Jobly.TestWorker/"]
RUN dotnet restore "Jobly.TestWorker/Jobly.TestWorker.csproj"
COPY . .
WORKDIR "/src/Jobly.TestWorker"
RUN dotnet build "Jobly.TestWorker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Jobly.TestWorker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Jobly.TestWorker.dll"]