using System;

namespace CodeBrix.Sqlite;

/// <summary>
/// Wraps a parameter value so the Dapper-style <see cref="SqliteMapper"/> methods encrypt it with
/// the crypt engine before binding — for example
/// <c>connection.Execute(sql, new { secret = new EncryptedValue(myObject) })</c>. Any serializable
/// CLR object can be wrapped, mirroring the classic <c>AddEncryptedParameter()</c> behavior.
/// </summary>
public sealed class EncryptedValue
{
    /// <summary>
    /// The plaintext value to encrypt when the parameter is bound; may be null (bound as database NULL).
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Creates a wrapper around the specified value.
    /// </summary>
    /// <param name="value">The plaintext value to encrypt when the parameter is bound; may be null.</param>
    public EncryptedValue(object value)
    {
        Value = value;
    }
}
