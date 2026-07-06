namespace CodeBrix.Sqlite.Enumerations;

/// <summary>
/// Specifies how a decryption operation should behave when the database column value is NULL.
/// </summary>
public enum DbNullHandling
{
    /// <summary>Throw a <see cref="CodeBrix.Sqlite.Exceptions.DbNullValueException"/> when the column value is NULL.</summary>
    ThrowDbNullException = 0,

    /// <summary>Return the default value of the requested type when the column value is NULL.</summary>
    ReturnTypeDefaultValue = 1
}
