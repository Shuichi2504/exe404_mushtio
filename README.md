# IoTAgriculture

Clean workspace layout:

```text
IoTAgriculture.sln
src/
  IoTAgriculture.API/
  IoTAgriculture.Application/
  IoTAgriculture.Infrastructure/
  IoTAgriculture.Domain/
tests/
  IoTAgriculture.Tests/
web/
  index.html
  app.js
  styles.css
frontend/
  Flutter mobile app
```

## API

```powershell
dotnet build IoTAgriculture.sln
dotnet run --project src\IoTAgriculture.API\IoTAgriculture.API.csproj
```

Health check:

```text
GET /api/health
```

Login is a POST endpoint, so opening it directly in a browser will return `405 Method Not Allowed`:

```text
POST /api/auth/login
```

## Web

Serve the `web/` folder with any static server or VS Code Live Server. The web app calls the backend API directly and lets you change the API base URL on the login screen.
