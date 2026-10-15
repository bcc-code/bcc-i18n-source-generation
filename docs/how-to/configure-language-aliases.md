# How to map cultures to languages with aliases

Use an alias when users have a culture whose name does not match a translation file, for example Nynorsk (`nn`) users who should see the `no` file.

Set `I18NLanguageAliases` in the project file as `culture=language` pairs separated by `;`:

```xml
<PropertyGroup>
  <I18NLanguageAliases>nb=no;nn=no;pt-pt=pt</I18NLanguageAliases>
</PropertyGroup>
```

The left side is a culture name (or a parent culture name). The right side is the name of a translation file without `.json`. Case and surrounding whitespace do not matter.

Check the result:

```csharp
Console.WriteLine(LanguageStrings.ResolveLanguage(new CultureInfo("nn-NO"))); // "no"
```

## Built-in aliases

Without configuration, these aliases apply when their target file exists:

- `nb=no` and `nn=no`: Bokmål and Nynorsk users get `no.json`.
- `no=nb`: users with the culture `no` get `nb.json` when there is no `no.json`.

So with a `no.json` file, `nb-NO` users already get Norwegian without any configuration. An alias you configure replaces the built-in alias with the same left side.

## Rules to know

- A file always wins over an alias for the same name. With both `nb.json` and the alias `nb=no`, `nb-NO` users get `nb.json`.
- Aliases are checked at each step of the culture's parent chain, after file names. `nn-NO` is tried as `nn-NO` (file, then alias), then `nn` (file, then alias).
- An alias that points to a missing file, or a malformed entry, is reported as [I18N009](../reference/diagnostics.md#i18n009) and ignored.

[How culture matching works](../explanation/culture-matching-and-fallback.md) describes the full algorithm.
