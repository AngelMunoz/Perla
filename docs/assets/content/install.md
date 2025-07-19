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

Once you're done with the installation, you can run `perla --help` to verify that the installation was successful.
