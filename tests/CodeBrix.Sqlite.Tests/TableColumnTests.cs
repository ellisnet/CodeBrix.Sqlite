using CodeBrix.Sqlite.EncryptedTables;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class TableColumnTests
{
    [Fact]
    public void IsValidIdentifier_accepts_simple_names()
    {
        //Arrange + Act + Assert
        TableColumn.IsValidIdentifier("MyColumn").Should().BeTrue();
        TableColumn.IsValidIdentifier("a").Should().BeTrue();
        TableColumn.IsValidIdentifier("Column_2").Should().BeTrue();
    }

    [Fact]
    public void IsValidIdentifier_rejects_leading_digits()
        => TableColumn.IsValidIdentifier("1Column").Should().BeFalse();

    [Fact]
    public void IsValidIdentifier_rejects_special_characters()
    {
        //Arrange + Act + Assert
        TableColumn.IsValidIdentifier("My Column").Should().BeFalse();
        TableColumn.IsValidIdentifier("My-Column").Should().BeFalse();
        TableColumn.IsValidIdentifier("My;Column").Should().BeFalse();
    }

    [Fact]
    public void IsValidIdentifier_rejects_null_and_whitespace()
    {
        //Arrange + Act + Assert
        TableColumn.IsValidIdentifier(null).Should().BeFalse();
        TableColumn.IsValidIdentifier("").Should().BeFalse();
        TableColumn.IsValidIdentifier("   ").Should().BeFalse();
    }

    [Fact]
    public void IsValidIdentifier_ignores_surrounding_whitespace()
        => TableColumn.IsValidIdentifier("  Trimmed  ").Should().BeTrue();
}
