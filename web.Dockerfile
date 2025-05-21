FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["NLQueryApp.Web/NLQueryApp.Web.csproj", "NLQueryApp.Web/"]
COPY ["NLQueryApp.Core/NLQueryApp.Core.csproj", "NLQueryApp.Core/"]
RUN dotnet restore "NLQueryApp.Web/NLQueryApp.Web.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/NLQueryApp.Web"
RUN dotnet build "NLQueryApp.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NLQueryApp.Web.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html
# Copy the entire publish output
COPY --from=publish /app/publish/wwwroot .
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
ENTRYPOINT ["nginx", "-g", "daemon off;"]