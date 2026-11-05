using System;
using System.Collections.Generic;

namespace BccCode.I18N.SourceGen;

/// <summary>
/// The index of every key path of all translation files (the union), in ordinal order. The generated typed
/// members and <c>LanguageStrings</c> address translations by this index, so both generators must use this class.
/// </summary>
internal sealed class TranslationKeyIndex
{
    private readonly Dictionary<string, int> _indexByKey;

    private TranslationKeyIndex(string[] keys)
    {
        Keys = keys;
        _indexByKey = new Dictionary<string, int>(keys.Length, StringComparer.Ordinal);
        for (var index = 0; index < keys.Length; index++)
        {
            _indexByKey.Add(keys[index], index);
        }
    }

    public string[] Keys { get; }

    public int Count => Keys.Length;

    public int GetIndex(string keyPath) => _indexByKey[keyPath];

    public static TranslationKeyIndex Create(EquatableArray<LanguageFileModel> files)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var entry in file.Entries)
            {
                seen.Add(entry.KeyPath);
            }
        }

        var keys = new string[seen.Count];
        seen.CopyTo(keys);
        Array.Sort(keys, StringComparer.Ordinal);
        return new TranslationKeyIndex(keys);
    }

    /// <summary>
    /// The value of each key of a file, in the order of the index (null when the file lacks the key). For duplicate
    /// key paths in a file the last one wins.
    /// </summary>
    public string?[] GetValues(LanguageFileModel file)
    {
        var values = new string?[Keys.Length];
        foreach (var entry in file.Entries)
        {
            values[_indexByKey[entry.KeyPath]] = entry.Value;
        }

        return values;
    }
}
