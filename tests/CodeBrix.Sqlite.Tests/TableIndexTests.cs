using System;
using System.Collections.Generic;
using CodeBrix.Sqlite.EncryptedTables;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class TableIndexTests
{
    [Fact]
    public void IsExpired_is_false_within_the_lifetime()
    {
        //Arrange
        var index = new TableIndex { LifetimeSeconds = 600 };

        //Act + Assert
        index.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_is_true_with_zero_lifetime()
        => new TableIndex { LifetimeSeconds = 0 }.IsExpired.Should().BeTrue();

    [Fact]
    public void IsExpired_is_true_once_the_lifetime_has_passed()
    {
        //Arrange
        var index = new TableIndex
        {
            CreatedUtc = DateTime.UtcNow.AddSeconds(-700),
            LifetimeSeconds = 600
        };

        //Act + Assert
        index.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void Clone_copies_all_entries()
    {
        //Arrange
        var index = new TableIndex();
        index.Items[1] = new Dictionary<string, string> { ["Name"] = "Ada" };
        index.Items[2] = new Dictionary<string, string> { ["Name"] = "Grace" };

        //Act
        TableIndex clone = index.Clone();

        //Assert
        clone.Items.Count.Should().Be(2);
        clone.Items[1]["Name"].Should().Be("Ada");
        clone.LifetimeSeconds.Should().Be(index.LifetimeSeconds);
        clone.CreatedUtc.Should().Be(index.CreatedUtc);
    }

    [Fact]
    public void Clone_is_a_deep_copy()
    {
        //Arrange
        var index = new TableIndex();
        index.Items[1] = new Dictionary<string, string> { ["Name"] = "Ada" };
        TableIndex clone = index.Clone();

        //Act - mutating the clone must not touch the original
        clone.Items[1]["Name"] = "Changed";

        //Assert
        index.Items[1]["Name"].Should().Be("Ada");
    }
}
