# How to change the severity of a diagnostic

Use this when, for example, missing translations ([I18N007](../reference/diagnostics.md#i18n007)) should not show up as warnings while a language is in progress, or should fail the build in CI.

The I18N diagnostics are reported on the JSON files, or without a file. Sections in `.editorconfig` such as `[*.cs]` or `[*.json]` therefore do not affect them. Use one of the options below.

## Option 1: a `.globalconfig` file

Create a file named `.globalconfig` in the project folder:

```ini
is_global = true

# Missing translations are fine for now
dotnet_diagnostic.I18N007.severity = none

# Keys that only exist in other languages are a mistake
dotnet_diagnostic.I18N008.severity = error
```

The .NET SDK picks up `.globalconfig` files automatically. Valid severities are `none`, `silent`, `suggestion`, `warning` and `error`.

## Option 2: the project file

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);I18N007</NoWarn>
  <WarningsAsErrors>$(WarningsAsErrors);I18N008</WarningsAsErrors>
</PropertyGroup>
```

`NoWarn` hides a warning. `WarningsAsErrors` turns a warning into an error. With `TreatWarningsAsErrors` set to `true`, all I18N warnings fail the build.

## Only in CI

Pass the property on the command line:

```bash
dotnet build -p:WarningsAsErrors=I18N007%3BI18N008
```

`%3B` is an escaped `;`, which separates the codes.

## Which diagnostics to adjust

- I18N007 (missing keys) and I18N008 (extra keys) are the usual candidates.
- I18N006, I18N009 and I18N010 point to texts that are not used as written. Keep them as warnings or raise them to errors.
- I18N001 to I18N005 are errors because the generated code would be missing or incomplete.
