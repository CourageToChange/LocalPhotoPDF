# Third-Party Notices

LocalPhotoPDF is distributed under the MIT License. It uses or redistributes the components below. The links point to the upstream projects and licence texts; those upstream projects do not endorse LocalPhotoPDF.

## Included in release builds

| Component | Use | Licence |
| --- | --- | --- |
| [.NET Runtime and Windows Desktop Runtime](https://github.com/dotnet/runtime) | Self-contained application runtime | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [upstream notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) |
| [PDFsharp-WPF 6.2.4](https://www.nuget.org/packages/PDFsharp-WPF/6.2.4) | PDF creation | [MIT](https://github.com/empira/PDFsharp/blob/master/LICENSE) |
| [Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/8.0.2) | Transitive PDFsharp dependency | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Microsoft.Extensions.Logging.Abstractions 8.0.3](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/8.0.3) | Transitive PDFsharp dependency | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [NSIS](https://nsis.sourceforge.io/) | Windows installer container | [zlib/libpng licence](https://nsis.sourceforge.io/License) |

The portable ZIP and installed application include the full applicable licence and notice texts under `licenses/`. The release build copies .NET, Windows Desktop, and Microsoft.Extensions notices from the exact resolved packages, alongside PDFsharp's MIT licence and NSIS's copying terms. The generated CycloneDX SBOM is scoped to the distributed application, records its exact bundled .NET runtime packs and runtime NuGet packages, and hashes the published executable.

## Development and test dependencies

These packages are used to build or test the project and are not intended to become application runtime features.

| Component | Licence |
| --- | --- |
| [Microsoft.NET.Test.Sdk 18.8.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1) | [MIT](https://github.com/microsoft/vstest/blob/main/LICENSE) |
| [xunit.v3 3.2.2](https://www.nuget.org/packages/xunit.v3/3.2.2) | [Apache-2.0](https://github.com/xunit/xunit/blob/main/LICENSE) |
| [xunit.runner.visualstudio 3.1.5](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | [Apache-2.0](https://github.com/xunit/visualstudio.xunit/blob/main/LICENSE) |
| [coverlet.collector 10.0.1](https://www.nuget.org/packages/coverlet.collector/10.0.1) | [MIT](https://github.com/coverlet-coverage/coverlet/blob/master/LICENSE) |

GitHub Actions used by this repository are build infrastructure and are not bundled with the application. See the pinned workflow files under `.github/workflows/` for their exact revisions.
