using System;
using System.Collections.Generic;
using CodeBrix.Sqlite.Cryptography;
using CodeBrix.Sqlite.Exceptions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class AesGcmCryptEngineTests
{
    private const string Passphrase = "unit-test passphrase";

    public class RoundTripPoco
    {
        public string Name { get; set; }
        public int Number { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, long> Counts { get; set; }
    }

    private static RoundTripPoco CreatePoco()
        => new RoundTripPoco
        {
            Name = "Holy Inanna of Uruk",
            Number = 42,
            Tags = new List<string> { "alpha", "beta" },
            Counts = new Dictionary<string, long> { ["one"] = 1, ["two"] = 2 }
        };

    [Fact]
    public void EncryptObject_round_trips_a_string()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);
        const string plaintext = "This string will be encrypted.";

        //Act
        string encrypted = engine.EncryptObject(plaintext);
        string decrypted = engine.DecryptObject<string>(encrypted);

        //Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EncryptObject_round_trips_a_complex_object()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);
        RoundTripPoco original = CreatePoco();

        //Act
        RoundTripPoco decrypted = engine.DecryptObject<RoundTripPoco>(engine.EncryptObject(original));

        //Assert
        decrypted.Name.Should().Be(original.Name);
        decrypted.Number.Should().Be(original.Number);
        decrypted.Tags.Count.Should().Be(2);
        decrypted.Tags[1].Should().Be("beta");
        decrypted.Counts["two"].Should().Be(2L);
    }

    [Fact]
    public void EncryptObject_round_trips_value_types()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act + Assert
        engine.DecryptObject<int>(engine.EncryptObject(12345)).Should().Be(12345);
        engine.DecryptObject<bool>(engine.EncryptObject(true)).Should().BeTrue();
        engine.DecryptObject<DateTime>(engine.EncryptObject(new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc)))
            .Should().Be(new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EncryptObject_returns_null_for_null_input()
        => new AesGcmCryptEngine(Passphrase).EncryptObject(null).Should().BeNull();

    [Fact]
    public void EncryptObject_produces_different_ciphertext_each_time()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);
        const string plaintext = "same value";

        //Act
        string first = engine.EncryptObject(plaintext);
        string second = engine.EncryptObject(plaintext);

        //Assert - random nonce means no two ciphertexts match, but both decrypt
        (first == second).Should().BeFalse();
        engine.DecryptObject<string>(first).Should().Be(plaintext);
        engine.DecryptObject<string>(second).Should().Be(plaintext);
    }

    [Fact]
    public void EncryptObject_output_does_not_contain_plaintext()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);
        const string secret = "TopSecretValue";

        //Act
        string encrypted = engine.EncryptObject(secret);

        //Assert
        encrypted.Contains(secret).Should().BeFalse();
    }

    [Fact]
    public void DecryptObject_throws_on_null_input()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act
        Action act = () => engine.DecryptObject<string>(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DecryptObject_with_wrong_passphrase_throws()
    {
        //Arrange
        using var rightEngine = new AesGcmCryptEngine(Passphrase);
        using var wrongEngine = new AesGcmCryptEngine("a completely different passphrase");
        string encrypted = rightEngine.EncryptObject("secret");

        //Act
        Action act = () => wrongEngine.DecryptObject<string>(encrypted);

        //Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void DecryptObject_with_corrupt_data_throws()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act
        Action act = () => engine.DecryptObject<string>("not-valid-encrypted-data");

        //Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void Constructor_with_different_salt_produces_incompatible_keys()
    {
        //Arrange
        using var saltedOne = new AesGcmCryptEngine(Passphrase, new byte[] { 1, 2, 3, 4 });
        using var saltedTwo = new AesGcmCryptEngine(Passphrase, new byte[] { 5, 6, 7, 8 });
        string encrypted = saltedOne.EncryptObject("secret");

        //Act
        Action act = () => saltedTwo.DecryptObject<string>(encrypted);

        //Assert
        act.Should().Throw<ObjectCryptographyException>();
    }

    [Fact]
    public void Constructor_accepts_raw_32_byte_key()
    {
        //Arrange
        byte[] key = new byte[32];
        for (int i = 0; i < key.Length; i++) { key[i] = (byte)i; }
        using var engine = new AesGcmCryptEngine(key);

        //Act
        string decrypted = engine.DecryptObject<string>(engine.EncryptObject("raw key value"));

        //Assert
        decrypted.Should().Be("raw key value");
    }

    [Fact]
    public void Constructor_rejects_wrong_size_raw_key()
    {
        //Arrange
        Action act = () => new AesGcmCryptEngine(new byte[16]);

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_null_key()
    {
        //Arrange
        Action act = () => new AesGcmCryptEngine((byte[])null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_passphrase()
    {
        //Arrange
        Action act = () => new AesGcmCryptEngine((string)null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_whitespace_passphrase()
    {
        //Arrange
        Action act = () => new AesGcmCryptEngine("   ");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeBlindIndex_is_deterministic()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act
        string first = engine.ComputeBlindIndex("ada@example.com");
        string second = engine.ComputeBlindIndex("ada@example.com");

        //Assert
        first.Should().Be(second);
    }

    [Fact]
    public void ComputeBlindIndex_differs_for_different_values()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act + Assert
        (engine.ComputeBlindIndex("one") == engine.ComputeBlindIndex("two")).Should().BeFalse();
    }

    [Fact]
    public void ComputeBlindIndex_differs_between_keys()
    {
        //Arrange
        using var engineOne = new AesGcmCryptEngine(Passphrase);
        using var engineTwo = new AesGcmCryptEngine("another passphrase");

        //Act + Assert
        (engineOne.ComputeBlindIndex("value") == engineTwo.ComputeBlindIndex("value")).Should().BeFalse();
    }

    [Fact]
    public void ComputeBlindIndex_does_not_reveal_plaintext()
    {
        //Arrange
        using var engine = new AesGcmCryptEngine(Passphrase);

        //Act + Assert
        engine.ComputeBlindIndex("SecretEmail").Contains("SecretEmail").Should().BeFalse();
    }

    [Fact]
    public void ComputeBlindIndex_returns_null_for_null_input()
        => new AesGcmCryptEngine(Passphrase).ComputeBlindIndex(null).Should().BeNull();

    [Fact]
    public void Dispose_blocks_further_use()
    {
        //Arrange
        var engine = new AesGcmCryptEngine(Passphrase);
        engine.Dispose();

        //Act
        Action act = () => engine.EncryptObject("anything");

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }
}
