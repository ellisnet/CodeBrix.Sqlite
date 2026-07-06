using System;
using System.Security.Cryptography;
using System.Text;
using CodeBrix.Sqlite.Exceptions;

namespace CodeBrix.Sqlite.Cryptography;

/// <summary>
/// A ready-to-use <see cref="IObjectCryptEngine"/> based on AES-GCM authenticated encryption.
/// Each encrypted value uses a freshly generated random nonce, and the stored string is
/// Base64(nonce || tag || ciphertext). Keys are derived from a passphrase with PBKDF2 (SHA-256),
/// or supplied directly as a 32-byte key. The engine also implements
/// <see cref="IBlindIndexProvider"/> using HMAC-SHA256 with a separately derived key, enabling
/// blind-index equality searches over encrypted data.
/// </summary>
public sealed class AesGcmCryptEngine : IObjectCryptEngine, IBlindIndexProvider
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int Pbkdf2Iterations = 100_000;

    private static readonly byte[] DefaultSalt = Encoding.UTF8.GetBytes("CodeBrix.Sqlite.AesGcmCryptEngine.v1");
    private static readonly byte[] BlindIndexInfo = Encoding.UTF8.GetBytes("CodeBrix.Sqlite/blind-index/v1");

    private readonly IObjectSerializer _serializer;
    private byte[] _key;
    private byte[] _blindIndexKey;
    private bool _disposed;

    /// <summary>
    /// The serializer used to convert objects to and from strings around the encryption step.
    /// </summary>
    public IObjectSerializer Serializer => _serializer;

    /// <summary>
    /// Creates an instance of the crypt engine from a passphrase. The AES key is derived with
    /// PBKDF2 (SHA-256, 100,000 iterations) over the passphrase and salt.
    /// </summary>
    /// <param name="passphrase">The secret passphrase to derive the encryption key from.</param>
    /// <param name="salt">Optional salt for key derivation. When null, a fixed library-default salt
    /// is used; supply an application-specific salt for stronger isolation between applications
    /// that share a passphrase.</param>
    /// <param name="serializer">Optional object serializer; when null, a <see cref="JsonObjectSerializer"/> is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passphrase"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="passphrase"/> is empty or whitespace.</exception>
    public AesGcmCryptEngine(string passphrase, byte[] salt = null, IObjectSerializer serializer = null)
    {
        if (passphrase == null) { throw new ArgumentNullException(nameof(passphrase)); }
        if (String.IsNullOrWhiteSpace(passphrase)) { throw new ArgumentException("The passphrase cannot be empty or whitespace.", nameof(passphrase)); }
        _key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt ?? DefaultSalt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);
        _blindIndexKey = DeriveBlindIndexKey(_key);
        _serializer = serializer ?? new JsonObjectSerializer();
    }

    /// <summary>
    /// Creates an instance of the crypt engine from a raw 32-byte AES key.
    /// </summary>
    /// <param name="key">The 32-byte (256-bit) AES key. The bytes are copied; the caller retains ownership of the array.</param>
    /// <param name="serializer">Optional object serializer; when null, a <see cref="JsonObjectSerializer"/> is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not exactly 32 bytes.</exception>
    public AesGcmCryptEngine(byte[] key, IObjectSerializer serializer = null)
    {
        if (key == null) { throw new ArgumentNullException(nameof(key)); }
        if (key.Length != KeySizeBytes) { throw new ArgumentException($"The key must be exactly {KeySizeBytes} bytes.", nameof(key)); }
        _key = (byte[])key.Clone();
        _blindIndexKey = DeriveBlindIndexKey(_key);
        _serializer = serializer ?? new JsonObjectSerializer();
    }

    private static byte[] DeriveBlindIndexKey(byte[] masterKey)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, KeySizeBytes, salt: null, info: BlindIndexInfo);

    /// <inheritdoc />
    public string EncryptObject(object valueToEncrypt)
    {
        ThrowIfDisposed();
        if (valueToEncrypt == null) { return null; }
        try
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(_serializer.Serialize(valueToEncrypt));
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] tag = new byte[TagSizeBytes];
            byte[] ciphertext = new byte[plaintext.Length];
            using (var aesGcm = new AesGcm(_key, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }
            byte[] combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
            Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, combined, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
            return Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            throw new ObjectCryptographyException("Unable to encrypt the specified object.", ex);
        }
    }

    /// <inheritdoc />
    public T DecryptObject<T>(string encryptedValue)
    {
        ThrowIfDisposed();
        if (encryptedValue == null) { throw new ArgumentNullException(nameof(encryptedValue)); }
        try
        {
            byte[] combined = Convert.FromBase64String(encryptedValue);
            if (combined.Length < NonceSizeBytes + TagSizeBytes)
            {
                throw new ArgumentException("The encrypted value is too short to be valid.", nameof(encryptedValue));
            }
            byte[] nonce = new byte[NonceSizeBytes];
            byte[] tag = new byte[TagSizeBytes];
            byte[] ciphertext = new byte[combined.Length - NonceSizeBytes - TagSizeBytes];
            Buffer.BlockCopy(combined, 0, nonce, 0, NonceSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes, tag, 0, TagSizeBytes);
            Buffer.BlockCopy(combined, NonceSizeBytes + TagSizeBytes, ciphertext, 0, ciphertext.Length);
            byte[] plaintext = new byte[ciphertext.Length];
            using (var aesGcm = new AesGcm(_key, TagSizeBytes))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }
            return _serializer.Deserialize<T>(Encoding.UTF8.GetString(plaintext));
        }
        catch (Exception ex)
        {
            throw new ObjectCryptographyException(
                "Unable to decrypt the specified value - the key may be wrong, or the stored data may be corrupt.", ex);
        }
    }

    /// <inheritdoc />
    public string ComputeBlindIndex(string value)
    {
        ThrowIfDisposed();
        if (value == null) { return null; }
        using (var hmac = new HMACSHA256(_blindIndexKey))
        {
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// Zeroes and releases the key material held by the engine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_key != null) { CryptographicOperations.ZeroMemory(_key); }
        if (_blindIndexKey != null) { CryptographicOperations.ZeroMemory(_blindIndexKey); }
        _key = null;
        _blindIndexKey = null;
    }
}
