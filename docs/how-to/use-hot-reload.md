# How to edit translations while the app runs

Edits to the text of existing keys can be applied with .NET hot reload, without restarting the app.

## With `dotnet watch`

```bash
dotnet watch run
```

Change a text in a translation file and save it. `dotnet watch` re-runs the generator and applies the change. The output shows a line like:

```text
dotnet watch 🔥 C# and Razor changes applied in 1115ms.
```

The next call to `Language`, `LocalizedLanguage` or `LanguageStrings` returns the new text.

## In an IDE

Start the app with the debugger and use the IDE's hot reload command after saving the JSON file. The same rules apply.

## What can be applied

- **Changing the text of an existing key** changes only method bodies in the generated code. This can be applied.
- **Adding, removing or renaming keys, or changing a key's placeholders** adds, removes or changes generated members. Whether such an edit can be applied without a restart depends on the hot reload support of your .NET version and IDE. If it cannot, `dotnet watch` asks to restart (or restarts, depending on its settings).

## If edits are not picked up

- Check that the file is an `AdditionalFiles` item. Files that are only `None` or `Content` items do not reach the generator.
- If you cache values from `LanguageStrings.Keys`, `GetAllStrings` or a `LocalizedLanguage` result in your own static fields, those caches keep the old values until they are rebuilt.

[Why the generated code is shaped for hot reload](../explanation/hot-reload.md) explains how the generated code makes this possible.
