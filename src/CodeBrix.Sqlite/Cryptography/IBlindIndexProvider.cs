namespace CodeBrix.Sqlite.Cryptography;

/// <summary>
/// Defines the ability to compute a deterministic keyed 'blind index' value for a plaintext string.
/// A blind index allows equality searches over encrypted data: the same plaintext and key always
/// produce the same index value, but the index value does not reveal the plaintext. Crypt engines
/// that implement this interface can be used with
/// <see cref="CodeBrix.Sqlite.EncryptedTables.BlindIndexedAttribute"/> table item properties.
/// </summary>
public interface IBlindIndexProvider
{
    /// <summary>
    /// Computes the deterministic blind-index value for the specified plaintext. Matching is exact
    /// and case-sensitive: callers that want case-insensitive matching should normalize the value
    /// (for example with <c>ToLowerInvariant()</c>) before storing and before searching.
    /// </summary>
    /// <param name="value">The plaintext value to compute the blind index for; a null value produces a null result.</param>
    /// <returns>The blind-index value as a string, or null when <paramref name="value"/> is null.</returns>
    string ComputeBlindIndex(string value);
}
