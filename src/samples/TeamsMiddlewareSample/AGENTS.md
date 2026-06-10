# AGENTS.md — TeamsMiddlewareSample

## App Registration

- **App/Client ID:** `c38307fd-7423-48a8-84f9-dd8a7c9d291f`
- **Tenant ID:** `3f3d1cea-7a18-41af-872b-cfbbd5140984`
- **Dev Tunnel:** `dmjgmbp2-3978.usw2.devtunnels.ms`
- **Endpoint:** `https://dmjgmbp2-3978.usw2.devtunnels.ms/api/messages`
- **Install Link:** `https://teams.microsoft.com/l/app/c38307fd-7423-48a8-84f9-dd8a7c9d291f?installAppPackage=true&appTenantId=3f3d1cea-7a18-41af-872b-cfbbd5140984`

## Run

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:3978 dotnet run --project src/samples/TeamsMiddlewareSample/TeamsMiddlewareSample.csproj
```

## Manifest

- **Template:** `appManifest/manifest.json` (uses `${{AAD_APP_CLIENT_ID}}` placeholders)
- **Upload:** `teams app manifest upload <resolved-manifest.json> c38307fd-7423-48a8-84f9-dd8a7c9d291f`
- **Download:** `teams app manifest download c38307fd-7423-48a8-84f9-dd8a7c9d291f`

## Key Files

| File | Role |
|------|------|
| `MyTeamsBot.cs` | Teams SDK handlers (Extension path) |
| `MyAgent.cs` | Agent SDK handlers using Teams SDK services (Host path) |
| `TeamsExtensionMiddleware.cs` | Routes msteams activities between the two SDKs |
| `TeamsSdkExtensions.cs` | DI registration bridging Agent SDK auth into Teams SDK |
| `AspNetExtensions.cs` | JWT auth setup |
| `appsettings.Development.json` | Dev credentials (not committed) |
| `COMMANDS.md` | Full command reference with status |
| `CHANGELOG.md` | Change history |
