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

The props file tries to default `GeneratedNamespace` to `$(RootNamespace)`, but it is evaluated before your project file sets `RootNamespace`. In practice:

- With the NuGet package, the types are in the global namespace.
- With a `ProjectReference` and the props imported at the top of the project file, the types are in a namespace named after the project.

Setting `GeneratedNamespace` explicitly avoids both cases. See [MSBuild reference](../reference/msbuild.md#generatednamespace-default) for the details.

## Several projects in one solution

Each project that uses the generator gets its own copy of the generated types, built from its own translation files. Give each project its own `GeneratedNamespace` so the types do not clash when one project references another.
