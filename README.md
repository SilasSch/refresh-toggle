# RefreshToggle

This repository now contains the rewrite baseline for RefreshToggle.

## Project layout

- `src/RefreshToggle.App`: fresh .NET 10 console application scaffold.
- `tests/RefreshToggle.Tests`: xUnit test project for the new baseline.

## Build and test

```powershell
dotnet restore RefreshToggle.sln --locked-mode
dotnet build RefreshToggle.sln -c Release --no-restore
dotnet test tests/RefreshToggle.Tests/RefreshToggle.Tests.csproj -c Release --no-build --no-restore
```
