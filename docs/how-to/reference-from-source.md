# How to reference the generator from source (ProjectReference)

Use this when you build the generator from this repository instead of using the NuGet package, for example to test a change.

## 1. Add the project reference as an analyzer

```xml
<ItemGroup>
  <ProjectReference Include="..\BccCode.I18N.SourceGen\BccCode.I18N.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

`OutputItemType="Analyzer"` loads the generator into the compiler. `ReferenceOutputAssembly="false"` keeps the generator out of your app's references.

## 2. Import the props file

The NuGet package imports `build/BccCode.I18N.SourceGen.props` for you. With a `ProjectReference`, import it yourself:

```xml
<Import Project="..\BccCode.I18N.SourceGen\build\BccCode.I18N.SourceGen.props" />
```

Without this import:

- `FallbackLanguage`, `GeneratedNamespace` and `I18NLanguageAliases` are not visible to the generator. The fallback language is then `en`, and the types are in the global namespace.
- The `I18NTranslation` metadata is not visible, so every `.json` file in `AdditionalFiles` is treated as a translation file.

## 3. Set the properties explicitly

The props file sets `GeneratedNamespace` from `RootNamespace` at the point of the import. Set `GeneratedNamespace` (and `FallbackLanguage`) in your project file so the result does not depend on where the import is:

```xml
<PropertyGroup>
  <FallbackLanguage>en</FallbackLanguage>
  <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
</PropertyGroup>
```

## Complete example

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Import Project="..\BccCode.I18N.SourceGen\build\BccCode.I18N.SourceGen.props" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <FallbackLanguage>en</FallbackLanguage>
    <GeneratedNamespace>MyApp.Localization</GeneratedNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\BccCode.I18N.SourceGen\BccCode.I18N.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="en.json" I18NTranslation="true" />
    <AdditionalFiles Include="no.json" I18NTranslation="true" />
  </ItemGroup>

</Project>
```

The `BccCode.I18N.SourceGen.Sample` and `BccCode.I18N.SourceGen.IntegrationTests` projects in this repository use this setup.

## After changing the generator

The IDE may keep an old version of the generator loaded. If the generated code does not change after a rebuild, restart the IDE, or build from the command line with `dotnet build`.
