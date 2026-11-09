# Native AOT spike

Tried on 2026-10-26 with the .NET SDK `10.0.400` and the Phase 0 project `spikes/IacAgent.Spike`.

## Command

```text
dotnet publish -c Release -r win-x64 /p:PublishAot=true --self-contained true -o artifacts/aot
```

The project references `Microsoft.Extensions.AI` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0, and `OllamaSharp` 5.5.0. `PublishAot` is not set in the project file. The normal `dotnet build -c Release` succeeds with warnings treated as errors.

## Result

Publish failed before the IL compiler produced a binary. The Native AOT pack `Microsoft.DotNet.ILCompiler` 10.0.11 stopped with:

```text
Platform linker not found. Ensure you have all the required prerequisites documented at
https://aka.ms/nativeaot-prerequisites, in particular the Desktop Development for C++
workload in Visual Studio. For ARM64 development also install C++ ARM64 build tools.
```

This machine has no `cl.exe` linker, so the publish never reached trimming or compilation of those packages. Their AOT compatibility is still unknown.

## What this means for the product

Keep the normal build. Do not change the design to chase Native AOT. After the Desktop development with C++ workload is installed, rerun the same command in the spike project. If a package then fails trimming or compilation, record that here and leave the product on the normal build.
