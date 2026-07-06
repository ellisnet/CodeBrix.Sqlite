using System;
using CodeBrix.Sqlite.EncryptedTables;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class TableSearchItemTests
{
    [Fact]
    public void Constructor_stores_the_criterion()
    {
        //Arrange + Act
        var item = new TableSearchItem("FullName", "Ada", SearchItemMatchType.StartsWith);

        //Assert
        item.PropertyName.Should().Be("FullName");
        item.Value.Should().Be("Ada");
        item.MatchType.Should().Be(SearchItemMatchType.StartsWith);
    }

    [Fact]
    public void Constructor_defaults_to_equality_matching()
        => new TableSearchItem("FullName", "Ada").MatchType.Should().Be(SearchItemMatchType.IsEqualTo);

    [Fact]
    public void Constructor_trims_the_property_name()
        => new TableSearchItem("  FullName  ", "Ada").PropertyName.Should().Be("FullName");

    [Fact]
    public void Constructor_rejects_null_property_name()
    {
        //Arrange
        Action act = () => new TableSearchItem(null, "Ada");

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_whitespace_property_name()
    {
        //Arrange
        Action act = () => new TableSearchItem("  ", "Ada");

        //Act + Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_null_value()
    {
        //Arrange
        Action act = () => new TableSearchItem("FullName", null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
