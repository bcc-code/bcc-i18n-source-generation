# Why there is no runtime dependency

## What the package contains

`BccCode.I18N.SourceGen` is a source generator. Its package contains:

- the generator assembly, under `analyzers/dotnet/cs`, which the compiler loads during a build;
- `build/BccCode.I18N.SourceGen.props` (and the same file under `buildTransitive/`), which makes the MSBuild properties visible to the generator.

There is no `lib/` folder, so your project does not reference any assembly from the package. The package is marked as a development dependency, so when you add it, NuGet sets `PrivateAssets="all"` and the reference does not flow to projects that depend on yours.

## What ends up in your app

Only the generated C# code, compiled into your own assembly. It uses types from the base class library:

- `System.Globalization.CultureInfo`
- `string.Format` and `IFormatProvider`
- `System.Collections.Generic` (`IReadOnlyList<T>`, `KeyValuePair<TKey, TValue>`, `List<T>`, `HashSet<T>`)
- `System.Text.StringBuilder`, `System.Math`

The translations themselves are string constants in that code. Nothing reads the JSON files at run time, so they do not need to be copied to the output folder or embedded as resources.

The one optional exception is `LanguageStringLocalizer`. It implements `Microsoft.Extensions.Localization.IStringLocalizer` and is generated only when your project already references that package. The generator never adds the reference itself.

## Why this design

- **Fewer things to deploy and update.** There is no runtime library whose version has to match the generator.
- **No loading or parsing at startup.** Texts are compiled constants. There is no file I/O, no JSON parsing and no reflection at run time.
- **Trimming and AOT.** The generated code uses no reflection or dynamic loading, so it suits trimmed and native AOT apps. (The repository's tests do not include a native AOT build.)
- **Errors at build time.** Problems in the files are reported as build diagnostics, not exceptions at run time.

## The trade-off

Translations are fixed at build time. Changing a text means rebuilding the app (or applying it with [hot reload](hot-reload.md) during development). The generator is not suitable for texts that editors change in production without a deployment; load those from a database or a translation service instead.

## Language version

The generated code is C#, so the consuming project must be able to compile it. It needs C# 8 or later, which is the default for .NET Core 3.0 and later. Projects targeting .NET Framework or .NET Standard 2.0 need `<LangVersion>8.0</LangVersion>` or higher.
