# How to add translation files

Use this when you add the generator to a project, or add a language.

1. Create one JSON file per language. Name it after the language: `en.json`, `no.json`, `pt-BR.json`. The file name without `.json` is the language, compared case-insensitively.

2. Add the files to `AdditionalFiles` in the project file and mark them with `I18NTranslation="true"`:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="Translations\en.json" I18NTranslation="true" />
     <AdditionalFiles Include="Translations\no.json" I18NTranslation="true" />
   </ItemGroup>
   ```

   A wildcard works too:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="Translations\*.json" I18NTranslation="true" />
   </ItemGroup>
   ```

3. Make sure one of the files is the fallback language. The package default is `no`. To use another language, see [Set the fallback language](set-fallback-language.md).

4. Build. The generated types appear after the first build.

## Why mark the files

When at least one `AdditionalFiles` item has `I18NTranslation="true"`, only marked `.json` files are read as translations. Other JSON files in `AdditionalFiles`, for example configuration for another analyzer, are ignored.

When no file is marked, every `.json` file in `AdditionalFiles` is treated as a translation file. This is supported for older projects, but marking is recommended.

## Add a language

Add a new file (for example `de.json`) with the same keys as the fallback file and mark it as above. Keys you have not translated yet use the fallback text, and the build reports [I18N007](../reference/diagnostics.md#i18n007) listing them.

## Avoid duplicate languages

Each language may have only one file. Two files with the same name in different folders cause [I18N005](../reference/diagnostics.md#i18n005).

## Troubleshooting

- **The generated types do not exist:** check the build output for [I18N004](../reference/diagnostics.md#i18n004). If the fallback file is missing, nothing is generated.
- **A JSON file that is not a translation causes errors:** mark your translation files with `I18NTranslation="true"`, so unmarked files are ignored.
- **With a `ProjectReference` to the generator, marking has no effect:** the props file is not imported. See [Reference the generator from source](reference-from-source.md).
