# Product Description Generator - GenAI
Simple tool for creating SEO friendly product listing title and HTML description, built with Avalonia and .NET 10.

Supports Windows, macOS and Linux, both X64 and Arm64.

The following inference providers are supported:
- Google
- OpenAI
- VeniceAI
- OpenRouter

## Download
Check the [downloads](https://github.com/Tesyl-sro/product-description-generator/releases) page. Builds are made automatically for every OS and architecture.
Builds with `aot` use Native AOT compilation, which run faster.

## Publishing
Each platform has a publish profile in `Properties/PublishProfiles/`. It builds a self-contained, trimmed, single-file executable in `bin/publish/<profile>/`.

| Platform | Command |
|---|---|
| Windows x64 | `dotnet publish ProductDescGen.csproj -p:PublishProfile=win-x64` |
| Windows Arm64 | `dotnet publish ProductDescGen.csproj -p:PublishProfile=win-arm64` |
| macOS Intel | `dotnet publish ProductDescGen.csproj -p:PublishProfile=osx-x64` |
| macOS Apple Silicon | `dotnet publish ProductDescGen.csproj -p:PublishProfile=osx-arm64` |
| Linux x64 | `dotnet publish ProductDescGen.csproj -p:PublishProfile=linux-x64` |
| Linux Arm64 | `dotnet publish ProductDescGen.csproj -p:PublishProfile=linux-arm64` |

### Native AOT compilation
You can fully compile the app and remove the JIT runtime using the following command:
```sh
dotnet publish ProductDescGen.csproj -p:PublishProfile=<profile> -p:PublishAot=true -p:PublishSingleFile=false -p:PublishDir=bin/publish/<profile>-aot/
```
