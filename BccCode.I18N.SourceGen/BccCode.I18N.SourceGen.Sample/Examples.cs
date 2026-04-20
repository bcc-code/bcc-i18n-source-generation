using System;
using System.Globalization;
using BccCode.I18N.SourceGen.Generated;

namespace BccCode.I18N.SourceGen.Sample;

// This code will not compile until you build the project with the Source Generators

public class Examples
{

    public static void TestLanguage(string language)
    {
        Console.WriteLine($"Testing {language}");
        CultureInfo.CurrentUICulture = new CultureInfo(language);
        string[] text =
        [
            Language.message.hello1,
            Language.message.hello2("hello"),

            Language.plural.car(1),
            Language.plural.car(2),

            Language.plural.apple(0),
            Language.plural.apple(1),
            Language.plural.apple(2),

            Language.plural.banana(0),
            Language.plural.banana(1),
            Language.plural.banana(2),
            
            LanguageKeys.message.hello1,
            LanguageKeys.message.hello2,
            
            LanguageStrings.GetString("message.hello1"),
            LanguageStrings.GetString("message.hello2")
        ];
        
        foreach (var item in text)
        {
            Console.WriteLine(item);
        }
        Console.WriteLine("\n\n");
    }
    
    public static void Main()
    {
        TestLanguage("no");
        TestLanguage("en");
        TestLanguage("de");
    }
}

