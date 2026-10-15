# How to set the namespace of the generated code

Set `GeneratedNamespace` in the project file:

```xml
<PropertyGroup>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
</PropertyGroup>
```

All generated types (`Language`, `LanguageKeys`, `LanguageStrings`, `LocalizedLanguage` and, if generated, `LanguageStringLocalizer` and `LanguageStringLocalizerFactory`) are then in `MyApp.Localization`:

```csharp
using MyApp.Localization;

string text = Language.menu.save;
```

## If you do not set it

The types are in the project's `RootNamespace`. That is the project name unless the project file sets `RootNamespace`. Characters that are not valid in a C# namespace are replaced with `_`, so a project named `My-App` gets `My_App`. See [MSBuild reference](../reference/msbuild.md#generatednamespace-default) for the details.

Versions up to 1.1.0 did not use `RootNamespace` with the NuGet package and put the types in the global namespace. If you upgrade from such a version without setting `GeneratedNamespace`, add `using` directives for the new namespace.

## Several projects in one solution

Each project that uses the generator gets its own copy of the generated types, built from its own translation files. Give each project its own `GeneratedNamespace` so the types do not clash when one project references another.
