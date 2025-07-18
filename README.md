# Shimmer-MachineLearning-Interface
A simple .NET console app that reads real‑time data from a Shimmer3 GSR+ biosensor.

## Prerequisites

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download) or higher  
- Windows (for Shimmer USB/serial driver)  

## Installation

```bash
# 1. Clone the repo
git clone https://github.com/LienoPC/Shimmer-MachineLearning-Interface.git
cd Shimmer-MachineLearning-Interface

# 2. Restore dependencies
dotnet restore

# 3. Build
dotnet build
```

## Usage

### From the CLI
```bash
# Run the Shimmer interface app
dotnet run --project ShimmerInterface/ShimmerInterface.csproj
```
-The program will attempt to connect to your Shimmer sensor and begin streaming data.
-Press any key in the console to stop and exit.

### From Visual Studio
1. Open ShimmerInterface.sln
2. Set **ShimmerInterface** as the startup project
3. Run (F5)

## Contact
- Alberto Cagnazzo <s327678@studenti.polito.it>
Project Link: https://github.com/LienoPC/ShimmerBioTransmit-MachineLearning-Interface
