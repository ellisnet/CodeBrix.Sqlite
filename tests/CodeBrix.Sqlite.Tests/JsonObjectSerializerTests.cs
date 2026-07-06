using System;
using System.Collections.Generic;
using CodeBrix.Sqlite.Cryptography;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class JsonObjectSerializerTests
{
    public class FieldHolder
    {
        public string NameField;
        public int NumberField;
    }

    [Fact]
    public void Serialize_round_trips_a_dictionary()
    {
        //Arrange
        var serializer = new JsonObjectSerializer();
        var original = new Dictionary<string, string> { ["FullName"] = "Ada Lovelace", ["Email"] = null };

        //Act
        Dictionary<string, string> result =
            serializer.Deserialize<Dictionary<string, string>>(serializer.Serialize(original));

        //Assert
        result["FullName"].Should().Be("Ada Lovelace");
        result["Email"].Should().BeNull();
    }

    [Fact]
    public void Serialize_includes_public_fields_by_default()
    {
        //Arrange
        var serializer = new JsonObjectSerializer();
        var original = new FieldHolder { NameField = "field value", NumberField = 7 };

        //Act
        FieldHolder result = serializer.Deserialize<FieldHolder>(serializer.Serialize(original));

        //Assert
        result.NameField.Should().Be("field value");
        result.NumberField.Should().Be(7);
    }

    [Fact]
    public void Serialize_uses_runtime_type_of_the_value()
    {
        //Arrange
        var serializer = new JsonObjectSerializer();
        object boxed = new FieldHolder { NameField = "runtime", NumberField = 1 };

        //Act
        FieldHolder result = serializer.Deserialize<FieldHolder>(serializer.Serialize(boxed));

        //Assert
        result.NameField.Should().Be("runtime");
    }

    [Fact]
    public void Serialize_throws_on_null_value()
    {
        //Arrange
        Action act = () => new JsonObjectSerializer().Serialize(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Deserialize_throws_on_null_input()
    {
        //Arrange
        Action act = () => new JsonObjectSerializer().Deserialize<string>(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
