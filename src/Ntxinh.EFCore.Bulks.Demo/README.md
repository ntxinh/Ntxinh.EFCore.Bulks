# Setup

```sh
dotnet tool install --global dotnet-ef
dotnet ef
dotnet-ef --version

# Test only
cd src/Ntxinh.EFCore.Bulks.Demo
dotnet ef migrations add Initial
dotnet ef database update
dotnet ef database drop
```
