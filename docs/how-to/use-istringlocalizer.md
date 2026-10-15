# How to use the translations through IStringLocalizer

Use this when a library or framework feature expects `IStringLocalizer`, for example DataAnnotations validation messages in ASP.NET Core.

## 1. Reference the localization abstractions

Reference `Microsoft.Extensions.Localization` (or only `Microsoft.Extensions.Localization.Abstractions`). ASP.NET Core projects that use the shared framework already have it.

```bash
dotnet add package Microsoft.Extensions.Localization
```

After the next build, the generator adds `LanguageStringLocalizer` and `LanguageStringLocalizerFactory` to the generated namespace. Without the reference, these types are not generated.

## 2. Register the factory

There is no extension method for this. Register the types yourself:

```csharp
builder.Services.AddSingleton<IStringLocalizerFactory, LanguageStringLocalizerFactory>();
builder.Services.AddTransient(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
```

This works before or after `AddLocalization()`. `AddLocalization()` only adds its own factory when no `IStringLocalizerFactory` is registered yet, and when both are registered the last one wins.

The factory returns a `LanguageStringLocalizer` for every resource type, because the whole project uses one set of translation files. `IStringLocalizer<HomeController>` and `IStringLocalizer<SharedResource>` give the same texts.

## 3. Use keys as messages

```csharp
public class HomeController(IStringLocalizer<HomeController> localizer) : Controller
{
    public IActionResult Index() => Content(localizer["menu.save"]);
}
```

- `localizer["key"]` returns the stored text. For an unknown key, the value is the key and `ResourceNotFound` is `true`.
- `localizer["key", arg0, arg1]` formats the text. A numeric placeholder `{n}` takes the argument at index `n`. Named placeholders take the arguments in the order of the parameters of the matching `Language` member.

## 4. Localize validation messages

Configure DataAnnotations to use the factory:

```csharp
builder.Services.AddControllersWithViews()
    .AddDataAnnotationsLocalization();
```

Then use keys as error messages:

```json
{
  "validation": {
    "required": "{0} is required.",
    "length": "{0} must be between {2} and {1} characters."
  }
}
```

```csharp
public class SignUp
{
    [Required(ErrorMessage = "validation.required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "validation.length")]
    public string Name { get; set; } = "";
}
```

DataAnnotations passes the display name as `{0}`, and for `StringLength` the maximum as `{1}` and the minimum as `{2}`.

## Use a fixed culture

`new LanguageStringLocalizer()` reads the current UI culture on every call. `new LanguageStringLocalizer(culture)` always uses `culture`, for both the language and number formatting:

```csharp
IStringLocalizer norwegian = new LanguageStringLocalizer(new CultureInfo("nb-NO"));
string save = norwegian["menu.save"];
```

## List all strings

`localizer.GetAllStrings(includeParentCultures: true)` returns the strings of the current language plus the fallback strings it lacks. `false` returns only the language's own strings.

See the [reference](../reference/generated-api.md#languagestringlocalizer) for all members.
