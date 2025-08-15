# Installation

You can install Perla manually by downloading the latest release from the [releases page](https://github.com/AngelMunoz/Perla/releases/latest) and adding it to your path.

If you prefer a more automated approach, you can use the provided install scripts, they're available on this repository for you to review the contents before running them.

For Linux or macOS, you can run the following command in your terminal:

```bash
curl -fsSL https://raw.githubusercontent.com/AngelMunoz/Perla/dev/install.sh | bash
```

For Windows (or Linux, MacOS if you've installed pwsh), you can use powershell to run the install script:

```powershell
iwr https://raw.githubusercontent.com/AngelMunoz/Perla/dev/install.ps1 -UseBasicParsing | iex
```

If you prefer to use the dotnet (global | local) tool, you can install it using the following command:

```bash
dotnet tool install --global Perla
```

> **Note**: From `v1.0.0-rc-002` onwards, the dotnet tool requires `dotnet 10 preview 7 or above` to be installed as we leverage the new multi-rid nuget packaging. This is due the extra weight playwright puts over us by bundling nodejs for each target platform (win, linux, osx, and arch x64 and arm64) on net8 our nuget package goes over 400mb which is over the allowed size on NuGetUploads (they don't have this issue though 😜).
>
> We'll try to address this issue by splitting the testing support in a separate tool in the future if feasible.
>
> The install scripts and manual download from the releases page still work unchanged.

Once you're done with the installation, you can run `perla --help` to verify that the installation was successful.
