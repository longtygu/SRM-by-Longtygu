# SRM by Longtygu

software library, deployment and system-tools manager !

## Features
- Software library with version management
- Presets and one-click deployment of software sets
- Uninstall manager
- Built-in system tools: Disk Health, Hardware Info, Hash Calculator, RAM Test, Windows Health Check
- English and Vietnamese interface
- Automatic update checking via GitHub Releases

## Requirements
- Windows 10 or later ( can't run on 7 :X )
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Administrator rights may be required to read some hardware sensors

## Installation
1. Open the [Releases](https://github.com/longtygu/SRM-by-Longtygu/releases) page
2. Download the latest `.zip` and extract it
3. Run `SRM by Longtygu.exe`

## Build from source
```bash
git clone https://github.com/longtygu/SRM-by-Longtygu.git
cd SRM-by-Longtygu
dotnet build -c Release
```

## License
MIT License. See [LICENSE.txt](LICENSE.txt).
