using System;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Text;
using BccCode.I18N.SourceGen.Tests.Utils;
using Xunit;

namespace BccCode.I18N.SourceGen.Tests;

public class TranslationFileParserTests
{
  private const string Path = "./en.json";

  private static LanguageFileParseResult Parse(string json) =>
      TranslationFileParser.Parse(new TestAdditionalFile(Path, json), CancellationToken.None);

  private static (string Key, string Value)[] Entries(LanguageFileParseResult result) =>
      result.File.Entries.Select(static entry => (entry.KeyPath, entry.Value)).ToArray();

  private static GeneratorDiagnosticInfo SingleDiagnostic(LanguageFileParseResult result) =>
      Assert.Single(result.Diagnostics.Array);

  [Fact]
  public void Parses_nested_objects_into_sorted_dot_joined_keys()
  {
    var result = Parse("""
                       { "b": { "y": "2", "x": { "deep": "3" } }, "a": "1", "empty": {} }
                       """);

    Assert.True(result.HasFile);
    Assert.Empty(result.Diagnostics.Array);
    Assert.Equal([("a", "1"), ("b.x.deep", "3"), ("b.y", "2")], Entries(result));
  }

  [Fact]
  public void Decodes_all_escapes_and_unicode()
  {
    var result = Parse("""{ "k": "q\" b\\ s\/ \b\f\n\r\t \u00e9\u00C9 \u0041", "ø": "æå" }""");

    Assert.Equal(
        [("k", "q\" b\\ s/ \b\f\n\r\t \u00e9\u00c9 A"), ("ø", "æå")],
        Entries(result).OrderBy(static e => e.Key, StringComparer.Ordinal).ToArray());
  }

  [Fact]
  public void Decodes_surrogate_pairs()
  {
    var result = Parse("""{ "k": "x\uD83D\uDE00y", "raw": "😀" }""");

    Assert.Equal("x😀y", Entries(result).Single(static e => e.Key == "k").Value);
    Assert.Equal("😀", Entries(result).Single(static e => e.Key == "raw").Value);
  }

  [Fact]
  public void Escapes_in_property_names_are_decoded()
  {
    var result = Parse("""{ "a\u002eb": "x", "tab\there": "y" }""");

    Assert.Equal(["a.b", "tab\there"], Entries(result).Select(static e => e.Key).OrderBy(static k => k, StringComparer.Ordinal).ToArray());
  }

  [Fact]
  public void Preserves_duplicate_property_names()
  {
    var result = Parse("""{ "a": "first", "a": "second", "n": { "x": "1", "x": "2" } }""");

    Assert.Equal(
        [("a", "first"), ("a", "second"), ("n.x", "1"), ("n.x", "2")],
        Entries(result));
  }

  [Fact]
  public void Accepts_a_leading_byte_order_mark_and_keeps_positions_aligned_with_the_source_text()
  {
    var result = Parse("\uFEFF{ \"a\": \"1\" }");
    Assert.Equal([("a", "1")], Entries(result));

    var broken = SingleDiagnostic(Parse("\uFEFF{ \"a\" 1 }"));
    Assert.Equal("I18N001", broken.Descriptor.Id);
    // SourceText.From(string) keeps the BOM char as part of line 1, so the '1' is at column 8.
    Assert.Contains("(line 1, column 8)", broken.Message, StringComparison.Ordinal);
    var location = broken.Location!.Value.ToLocation();
    Assert.Equal(new TextSpan(7, 0), location.SourceSpan);
    Assert.Equal(7, location.GetLineSpan().StartLinePosition.Character);

    var unsupported = SingleDiagnostic(Parse("\uFEFF{ \"a\": 1 }"));
    Assert.Equal(new TextSpan(3, 3), unsupported.Location!.Value.ToLocation().SourceSpan);
  }

  [Fact]
  public void Accepts_whitespace_and_all_line_break_styles_and_tracks_lines()
  {
    var result = Parse("\t{\r\n \"a\": \"1\",\r \"b\": 2\n}\n");

    var diagnostic = SingleDiagnostic(result);
    Assert.Equal("I18N003", diagnostic.Descriptor.Id);
    var span = diagnostic.Location!.Value.LineSpan!.Value;
    Assert.Equal(2, span.Start.Line); // zero-based: line 3 in an editor
    Assert.Equal(1, span.Start.Character);
  }

  [Fact]
  public void Non_string_values_report_I18N003_at_the_offending_key()
  {
    var json = "{\n" +
               "  \"ok\": \"fine\",\n" +
               "  \"num\": 12.5e+3,\n" +
               "  \"neg\": -0,\n" +
               "  \"yes\": true,\n" +
               "  \"no\": false,\n" +
               "  \"nothing\": null,\n" +
               "  \"list\": [1, \"two\", [3], {\"x\": \"y\"}, null],\n" +
               "  \"nested\": {\n" +
               "    \"bad\": 1\n" +
               "  }\n" +
               "}";

    var result = Parse(json);

    Assert.True(result.HasFile);
    Assert.Equal([("ok", "fine")], Entries(result));

    var diagnostics = result.Diagnostics.Array;
    Assert.All(diagnostics, static d => Assert.Equal("I18N003", d.Descriptor.Id));
    Assert.Equal(
        ["num", "neg", "yes", "no", "nothing", "list", "nested.bad"],
        diagnostics.Select(static d => d.Message.Split('\'')[1]).ToArray());
    Assert.Equal([2, 3, 4, 5, 6, 7, 9], diagnostics.Select(static d => d.Location!.Value.LineSpan!.Value.Start.Line).ToArray());

    var nested = diagnostics.Last().Location!.Value.LineSpan!.Value;
    Assert.Equal(4, nested.Start.Character);
    Assert.Equal(4 + "\"bad\"".Length, nested.End.Character);
  }

  [Fact]
  public void Reports_I18N002_for_non_object_roots()
  {
    foreach (var json in new[] { "[]", "\"text\"", "1", "true", "null" })
    {
      var result = Parse(json);
      Assert.False(result.HasFile);
      Assert.Equal("I18N002", SingleDiagnostic(result).Descriptor.Id);
    }
  }

  [Theory]
  [InlineData("", 1, 1, "does not contain a JSON value")]
  [InlineData("  \n ", 2, 2, "does not contain a JSON value")]
  [InlineData("{ \"a\": \"abc", 1, 8, "not terminated")]
  [InlineData("{ \"a\": \"x\\qy\" }", 1, 10, "'\\q' is not a valid escape")]
  [InlineData("{ \"a\": \"x\\u12G4\" }", 1, 10, "four hex digits")]
  [InlineData("{ \"a\": \"x\\u12", 1, 10, "four hex digits")]
  [InlineData("{ \"a\": \"\\uD83D\" }", 1, 9, "unpaired high surrogate")]
  [InlineData("{ \"a\": \"\\uD83Dx\\uDE00\" }", 1, 9, "unpaired high surrogate")]
  [InlineData("{ \"a\": \"\\uDE00\" }", 1, 9, "unpaired low surrogate")]
  [InlineData("{ \"a\": \"line\nbreak\" }", 1, 13, "control character")]
  [InlineData("{ \"a\": \"tab\there\" }", 1, 12, "control character")]
  [InlineData("{ \"a\": \"1\", }", 1, 13, "trailing comma")]
  [InlineData("{ \"a\": [1, 2,] }", 1, 14, "trailing comma")]
  [InlineData("{ // note\n \"a\": \"1\" }", 1, 3, "comments are not allowed")]
  [InlineData("{ \"a\": /* c */ \"1\" }", 1, 8, "comments are not allowed")]
  [InlineData("{ \"a\" \"1\" }", 1, 7, "expected ':'")]
  [InlineData("{ \"a\" }", 1, 7, "expected ':'")]
  [InlineData("{ a: \"1\" }", 1, 3, "expected a property name")]
  [InlineData("{ 'a': \"1\" }", 1, 3, "expected a property name")]
  [InlineData("{ \"a\": \"1\" \"b\": \"2\" }", 1, 12, "expected ',' or '}'")]
  [InlineData("{ \"a\": \"1\"", 1, 11, "end of file")]
  [InlineData("{ \"a\": ", 1, 8, "end of file")]
  [InlineData("{ \"a\": [1 2] }", 1, 11, "expected ',' or ']'")]
  [InlineData("{ \"a\": [1, 2 }", 1, 14, "expected ',' or ']'")]
  [InlineData("{ \"a\": \"1\" } { \"b\": \"2\" }", 1, 14, "after the root value")]
  [InlineData("{ \"a\": \"1\" } x", 1, 14, "after the root value")]
  [InlineData("{ \"a\": tru }", 1, 8, "expected 'true'")]
  [InlineData("{ \"a\": nulL }", 1, 8, "expected 'null'")]
  [InlineData("{ \"a\": 01 }", 1, 9, "expected ',' or '}'")]
  [InlineData("{ \"a\": 1. }", 1, 10, "digit after the decimal point")]
  [InlineData("{ \"a\": -x }", 1, 9, "expected a digit")]
  [InlineData("{ \"a\": 1e }", 1, 10, "digit in the exponent")]
  [InlineData("{ \"a\": +1 }", 1, 8, "expected a value")]
  [InlineData("{\n  \"a\": \"1\",\n  \"b\": oops\n}", 3, 8, "expected a value")]
  [InlineData("\u00a0{ }", 1, 1, "unexpected")]
  public void Reports_I18N001_with_the_error_position(string json, int line, int column, string expectedDescription)
  {
    var result = Parse(json);

    Assert.False(result.HasFile);
    var diagnostic = SingleDiagnostic(result);
    Assert.Equal("I18N001", diagnostic.Descriptor.Id);
    Assert.StartsWith($"Invalid JSON in translation file '{Path}': ", diagnostic.Message, StringComparison.Ordinal);
    Assert.EndsWith($"(line {line}, column {column})", diagnostic.Message, StringComparison.Ordinal);
    Assert.Contains(expectedDescription, diagnostic.Message, StringComparison.Ordinal);

    var span = diagnostic.Location!.Value.LineSpan!.Value;
    Assert.Equal(line - 1, span.Start.Line);
    Assert.Equal(column - 1, span.Start.Character);

    var expectedOffset = SourceText.From(json).Lines.GetPosition(new LinePosition(line - 1, column - 1));
    var location = diagnostic.Location!.Value.ToLocation();
    Assert.Equal(new TextSpan(expectedOffset, 0), location.SourceSpan);
    Assert.Equal(span, location.GetLineSpan().Span);
  }

  // U+0085, U+2028 and U+2029 are legal inside JSON strings, but Roslyn's SourceText treats them as line breaks.
  [Theory]
  [InlineData("\u0085")]
  [InlineData("\u2028")]
  [InlineData("\u2029")]
  public void Positions_follow_SourceText_line_breaks_for_separators_in_strings_and_names(string separator)
  {
    var prefix = "{\n \"a" + separator + "b\": \"x" + separator + "y\",\n ";

    var unsupported = SingleDiagnostic(Parse(prefix + "\"n\": 1\n}"));
    Assert.Equal("I18N003", unsupported.Descriptor.Id);
    var location = unsupported.Location!.Value.ToLocation();
    Assert.Equal(new TextSpan(prefix.Length, 3), location.SourceSpan);
    Assert.Equal(new LinePosition(4, 1), location.GetLineSpan().StartLinePosition);
    Assert.Equal(new LinePosition(4, 4), location.GetLineSpan().EndLinePosition);

    var syntaxError = SingleDiagnostic(Parse(prefix + "\"n\" 1\n}"));
    Assert.Equal("I18N001", syntaxError.Descriptor.Id);
    Assert.EndsWith("(line 5, column 6)", syntaxError.Message, StringComparison.Ordinal);
    location = syntaxError.Location!.Value.ToLocation();
    Assert.Equal(new TextSpan(prefix.Length + 4, 0), location.SourceSpan);
    Assert.Equal(new LinePosition(4, 5), location.GetLineSpan().StartLinePosition);

    // The separator inside a key that is itself reported.
    var inName = SingleDiagnostic(Parse("{ \"k" + separator + "k\": 1 }"));
    location = inName.Location!.Value.ToLocation();
    Assert.Equal(new TextSpan(2, 5), location.SourceSpan);
    Assert.Equal(new LinePosition(0, 2), location.GetLineSpan().StartLinePosition);
    Assert.Equal(new LinePosition(1, 2), location.GetLineSpan().EndLinePosition);
  }

  [Fact]
  public void Allows_nesting_up_to_the_maximum_depth_of_64()
  {
    var result = Parse(NestedObjects(64));

    Assert.True(result.HasFile);
    Assert.Equal([(string.Join(".", Enumerable.Repeat("a", 63)) + ".leaf", "x")], Entries(result));
  }

  [Fact]
  public void Rejects_nesting_deeper_than_64_in_objects_and_arrays()
  {
    var tooDeepObjects = SingleDiagnostic(Parse(NestedObjects(65)));
    Assert.Equal("I18N001", tooDeepObjects.Descriptor.Id);
    Assert.Contains("maximum nesting depth of 64", tooDeepObjects.Message, StringComparison.Ordinal);

    var allowed = Parse("{ \"a\": " + new string('[', 63) + new string(']', 63) + " }");
    Assert.Equal("I18N003", SingleDiagnostic(allowed).Descriptor.Id);

    var tooDeepArrays = SingleDiagnostic(Parse("{ \"a\": " + new string('[', 64) + new string(']', 64) + " }"));
    Assert.Equal("I18N001", tooDeepArrays.Descriptor.Id);
  }

  [Fact]
  public void Root_empty_object_gives_no_entries_and_no_diagnostics()
  {
    var result = Parse("{}");

    Assert.True(result.HasFile);
    Assert.Empty(result.File.Entries.Array);
    Assert.Empty(result.Diagnostics.Array);
  }

  // C# regular string literals and single-line comments end at these characters, and other control
  // characters are invisible in the generated source, so the generator must escape them.
  [Theory]
  [InlineData("0085")]
  [InlineData("2028")]
  [InlineData("2029")]
  [InlineData("0001")]
  [InlineData("001B")]
  [InlineData("007F")]
  [InlineData("009F")]
  public void Escaped_line_separators_and_control_characters_generate_valid_csharp(string hex)
  {
    var character = ((char)Convert.ToInt32(hex, 16)).ToString();
    var json = "{ \"a@u" + hex + "b\": \"x@u" + hex + "y\", \"p\": \"{n}@u" + hex + "z | {n} items\" }";
    json = json.Replace("@", "\\");

    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", json)],
        fallbackLanguage: "en");

    Assert.DoesNotContain(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    var generated = string.Concat(outputCompilation.SyntaxTrees
        .Where(static tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
        .Select(static tree => tree.ToString()));
    Assert.DoesNotContain(character, generated, StringComparison.Ordinal);
  }

  [Fact]
  public void Accepts_escaped_nul_in_values_and_names_and_generates_valid_csharp()
  {
    var json = "{ \"a@u0000b\": \"x@u0000y\" }".Replace("@", "\\");
    var result = Parse(json);

    Assert.Equal([("a\0b", "x\0y")], Entries(result));
    Assert.Empty(result.Diagnostics.Array);

    var (_, runResult, outputCompilation) = GeneratorTestHelpers.RunGenerator(
        new I18NSourceGenerator(),
        [new TestAdditionalFile("./en.json", json)],
        fallbackLanguage: "en");

    Assert.DoesNotContain(GeneratorTestHelpers.GetGeneratorDiagnostics(runResult), static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    Assert.DoesNotContain(outputCompilation.GetDiagnostics(), static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    var generated = string.Concat(outputCompilation.SyntaxTrees
        .Where(static tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
        .Select(static tree => tree.ToString()));
    Assert.Contains("x\\0y", generated, StringComparison.Ordinal);
    // The string literals escape NUL. (Doc comments still carry the raw character, which is harmless in a comment.)
    Assert.DoesNotContain(
        generated.Split('\n'),
        static line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && line.IndexOf('\0') >= 0);
  }

  [Fact]
  public void Bad_escape_in_a_property_name_points_at_the_escape()
  {
    var diagnostic = SingleDiagnostic(Parse("{ \"a@qb\": 1 }".Replace("@", "\\")));

    Assert.Equal("I18N001", diagnostic.Descriptor.Id);
    Assert.Contains("is not a valid escape", diagnostic.Message, StringComparison.Ordinal);
    Assert.EndsWith("(line 1, column 5)", diagnostic.Message, StringComparison.Ordinal);
    Assert.Equal(new TextSpan(4, 0), diagnostic.Location!.Value.ToLocation().SourceSpan);
  }

  [Theory]
  [InlineData("1.5e-3")]
  [InlineData("-0.0E+2")]
  [InlineData("0e0")]
  [InlineData("-1E-10")]
  public void Numbers_with_exponents_are_unsupported_values_not_syntax_errors(string number)
  {
    var diagnostic = SingleDiagnostic(Parse("{ \"n\": " + number + " }"));

    Assert.Equal("I18N003", diagnostic.Descriptor.Id);
  }

  [Theory]
  [InlineData("\"a\": \"@uD83D\"", "high", 8)]
  [InlineData("\"a\": \"@uD83Dx\"", "high", 8)]
  [InlineData("\"a\": \"x@uDE00\"", "low", 9)]
  [InlineData("\"a\": \"@uDE00@uD83D\"", "low", 8)]
  [InlineData("\"@uD83D\": 1", "high", 3)]
  [InlineData("\"a\": \"ok😀@uDE00\"", "low", 12)]
  public void Rejects_raw_lone_surrogates(string members, string kind, int expectedOffset)
  {
    // "@uXXXX" marks a raw surrogate char in the text; it is not a JSON escape.
    var raw = System.Text.RegularExpressions.Regex.Replace(
        members, "@u([0-9A-F]{4})", static m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    var json = "{ " + raw + " }";

    var diagnostic = SingleDiagnostic(Parse(json));

    Assert.Equal("I18N001", diagnostic.Descriptor.Id);
    Assert.Contains($"unpaired {kind} surrogate character", diagnostic.Message, StringComparison.Ordinal);
    Assert.Equal(new TextSpan(expectedOffset, 0), diagnostic.Location!.Value.ToLocation().SourceSpan);
    Assert.EndsWith($"(line 1, column {expectedOffset + 1})", diagnostic.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Does_not_overflow_the_stack_on_very_deep_input()
  {
    var diagnostic = SingleDiagnostic(Parse(new string('[', 200_000)));

    Assert.Equal("I18N001", diagnostic.Descriptor.Id);
  }

  [Fact]
  public void Results_are_equatable_for_incremental_caching()
  {
    var first = Parse("{ \"a\": 1, \"b\": \"x\" }");
    var second = Parse("{ \"a\": 1, \"b\": \"x\" }");
    var moved = Parse("{\n \"a\": 1, \"b\": \"x\" }");

    Assert.Equal(first, second);
    Assert.NotEqual(first, moved);
  }

  private static string NestedObjects(int depth)
  {
    var builder = new StringBuilder();
    for (var i = 1; i < depth; i++)
    {
      builder.Append("{\"a\":");
    }

    builder.Append("{\"leaf\":\"x\"}");
    builder.Append('}', depth - 1);
    return builder.ToString();
  }
}
