using System;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.EncryptedTables;

namespace CodeBrix.Sqlite.Tests;

public class ContactItem : EncryptedTableItem
{
    [NotEncrypted] public string Category { get; set; }

    [NotEncrypted, ColumnName("ContactAge")] public int Age { get; set; }

    [Searchable] public string FullName { get; set; }

    [Searchable, BlindIndexed] public string Email { get; set; }

    public string PrivateNotes { get; set; }
}

public class BadDualAttributeItem : EncryptedTableItem
{
    [NotEncrypted, BlindIndexed] public string Broken { get; set; }
}

public enum InventoryStatus
{
    Unknown = 0,
    InStock = 1,
    Discontinued = 2
}

//Exercises every attribute/type combination the ContactItem does not: NOT NULL + DEFAULT,
//  and REAL / BLOB / bool / enum / DateTime / decimal plaintext column mappings.
public class InventoryItem : EncryptedTableItem
{
    [NotEncrypted, NotNull, ColumnDefaultValue("general")] public string Category { get; set; } = "general";

    [NotEncrypted] public double Weight { get; set; }

    [NotEncrypted] public byte[] Thumbnail { get; set; }

    [NotEncrypted] public bool IsAvailable { get; set; }

    [NotEncrypted] public InventoryStatus Status { get; set; }

    [NotEncrypted] public DateTime AddedOn { get; set; }

    [NotEncrypted] public decimal Price { get; set; }

    [Searchable] public string Sku { get; set; }

    public string HiddenNotes { get; set; }
}

public class ReservedColumnItem : EncryptedTableItem
{
    [NotEncrypted, ColumnName("Encrypted_Object")] public string Clash { get; set; }
}

internal sealed class SpySerializer : IObjectSerializer
{
    private readonly JsonObjectSerializer _inner = new JsonObjectSerializer();

    public int SerializeCount { get; private set; }
    public int DeserializeCount { get; private set; }

    public string Serialize(object value)
    {
        SerializeCount++;
        return _inner.Serialize(value);
    }

    public T Deserialize<T>(string serialized)
    {
        DeserializeCount++;
        return _inner.Deserialize<T>(serialized);
    }
}

internal sealed class PlainTextCryptEngine : IObjectCryptEngine
{
    //TEST-ONLY 'crypt engine': base64-encodes the serialized object with no actual
    //  encryption; exists to exercise engines that do NOT implement IBlindIndexProvider.
    private readonly IObjectSerializer _serializer = new JsonObjectSerializer();

    public string EncryptObject(object valueToEncrypt)
        => valueToEncrypt == null
            ? null
            : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_serializer.Serialize(valueToEncrypt)));

    public T DecryptObject<T>(string encryptedValue)
    {
        if (encryptedValue == null) { throw new ArgumentNullException(nameof(encryptedValue)); }
        return _serializer.Deserialize<T>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encryptedValue)));
    }

    public void Dispose()
    {
    }
}
