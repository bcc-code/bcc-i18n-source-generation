# How to set the fallback language

The fallback language is used when no translation file matches the user's culture, and for keys that a language file does not contain. Its file also defines which keys and members are generated.

Set `FallbackLanguage` in the project file to the name of one of your translation files, without `.json`:

```xml
<PropertyGroup>
  <FallbackLanguage>en</FallbackLanguage>
</PropertyGroup>
```

If you do not set it, the package uses `no`.

Check the result at run time:

```csharp
Console.WriteLine(LanguageStrings.FallbackLanguage); // "en"
```

## Choosing the fallback language

Pick the language that is most complete, because:

- Its keys become the generated members. Keys that exist only in other files are not generated ([I18N008](../reference/diagnostics.md#i18n008)).
- Its placeholders define the parameters of each member.
- Users whose culture matches no file see this language.

## If the build reports I18N004

The value does not match any translation file. Either add the file, or correct the value. Matching ignores case and surrounding whitespace. With a `ProjectReference` to the generator, the property is only read when the props file is imported; see [Reference the generator from source](reference-from-source.md).
