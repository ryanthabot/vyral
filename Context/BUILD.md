# Build and Setup Instructions

## Prerequisites

- **.NET 9 SDK** or later ([download](https://dotnet.microsoft.com/download/dotnet/9.0))
- **OBS Studio** with obs-websocket enabled ([download](https://obsproject.com/))
- **Visual Studio 2022** or **VS Code** with C# Dev Kit extension

## Building the Solution

### 1. Restore NuGet Packages

```powershell
dotnet restore
```

### 2. Build All Projects

```powershell
dotnet build --configuration Release
```

### 3. Run Tests

```powershell
dotnet test
```

### 4. Run the Application

```powershell
cd src\App.UI
dotnet run
```

## Configuration

### OBS WebSocket Setup

1. **Open OBS Studio**
2. Navigate to **Tools → obs-websocket Settings**
3. Ensure WebSocket server is **enabled**
4. Note the **port** (default: 4455) and **password** (if set)
5. Update `src/App.UI/Program.cs` if using a non-default configuration:

```csharp
services.AddSingleton<OBSConfig>(sp => new OBSConfig
{
    Url = "ws://127.0.0.1:4455",
    Password = "your-password-here", // Or null
    AutoReconnect = true
});
```

## Building Plugins

### Build Sample Plugin

```powershell
cd plugins\SamplePlugin
dotnet build -c Release
```

The compiled plugin will be in `bin\Release\net9.0\`. Copy the entire output to the runtime plugin directory:

```powershell
# From plugins/SamplePlugin directory
xcopy bin\Release\net9.0\*.* ..\..\bin\plugins\SamplePlugin\ /E /I /Y
```

### Plugin Directory Structure

```
bin/
└── plugins/
    └── SamplePlugin/
        ├── plugin.json          # Required manifest
        ├── SamplePlugin.dll     # Plugin assembly
        └── data/                # Auto-created for plugin storage
```

## Development Workflow

### Using Visual Studio 2022

1. Open `VyralBot.sln`
2. Set `App.UI` as the startup project
3. Press F5 to build and run with debugging

### Using VS Code

1. Open the workspace folder
2. Install recommended extensions (C# Dev Kit)
3. Use `Run → Start Debugging` (F5)

### Recommended Extensions (VS Code)

- **C# Dev Kit** (Microsoft)
- **Avalonia for VS Code**
- **.NET Core Test Explorer**

## Troubleshooting

### OBS Connection Fails

- Verify OBS WebSocket server is enabled and running
- Check firewall isn't blocking port 4455
- Ensure URL matches: `ws://127.0.0.1:4455`
- Check OBS logs for authentication errors

### Plugin Not Loading

- Ensure `plugin.json` exists in plugin directory
- Verify `entryAssembly` and `entryType` match compiled DLL
- Check logs for `PluginLoader` errors
- Ensure plugin targets `net9.0`

### Build Errors

- Clean solution: `dotnet clean`
- Restore packages: `dotnet restore`
- Ensure .NET 9 SDK is installed: `dotnet --version`

## Publishing

### Single-File Executable

```powershell
dotnet publish src\App.UI\App.UI.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish\
```

### Cross-Platform Builds

```powershell
# Windows
dotnet publish -r win-x64 -c Release --self-contained true -o publish\win-x64

# Linux
dotnet publish -r linux-x64 -c Release --self-contained true -o publish\linux-x64

# macOS
dotnet publish -r osx-x64 -c Release --self-contained true -o publish\osx-x64
```

## Next Steps

- Read [CONTRIBUTING.md](CONTRIBUTING.md) for development guidelines
- See [docs/PLUGIN_DEV_GUIDE.md](docs/PLUGIN_DEV_GUIDE.md) for plugin development
- Check [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for system design
