$ErrorActionPreference = 'Stop'
foreach ($name in 'VERCEL_TOKEN','VERCEL_SCOPE') { if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing required environment variable: $name" } }
npx --yes vercel@65.8.0 link --yes --project=fleetops-demo --token=$env:VERCEL_TOKEN --scope=$env:VERCEL_SCOPE
if ($LASTEXITCODE) { throw "Vercel project linking failed with exit code $LASTEXITCODE" }
npx --yes vercel@65.8.0 deploy --prod --token=$env:VERCEL_TOKEN --scope=$env:VERCEL_SCOPE --yes
if ($LASTEXITCODE) { throw "Vercel deployment failed with exit code $LASTEXITCODE" }
