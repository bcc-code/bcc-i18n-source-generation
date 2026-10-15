using System;
using System.Linq;
using System.Text;
using System.Threading;
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
  public void Accepts_a_leading_byte_order_mark_and_keeps_columns_relative_to_the_text()
  {
    var result = Parse("\uFEFF{ \"a\": \"1\" }");
    Assert.Equal([("a", "1")], Entries(result));

    var broken = SingleDiagnostic(Parse("\uFEFF{ \"a\" 1 }"));
    Assert.Equal("I18N001", broken.Descriptor.Id);
    Assert.Contains("(line 1, column 7)", broken.Message, StringComparison.Ordinal);
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
