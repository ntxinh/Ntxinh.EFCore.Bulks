# How to push

```sh
cd src/Ntxinh.EFCore.Bulks
dotnet build
cd bin/Debug
dotnet nuget push Ntxinh.EFCore.Bulks.1.0.0.nupkg --api-key API_KEY --source https://api.nuget.org/v3/index.json
```