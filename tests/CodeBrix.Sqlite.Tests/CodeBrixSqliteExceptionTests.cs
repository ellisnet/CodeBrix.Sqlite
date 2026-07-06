using System;
using CodeBrix.Sqlite.Exceptions;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class CodeBrixSqliteExceptionTests
{
    [Fact]
    public void all_library_exceptions_derive_from_the_base_exception()
    {
        //Arrange + Act + Assert
        typeof(CodeBrixSqliteException).IsAssignableFrom(typeof(DatabaseMaintenanceException)).Should().BeTrue();
        typeof(CodeBrixSqliteException).IsAssignableFrom(typeof(ObjectCryptographyException)).Should().BeTrue();
        typeof(CodeBrixSqliteException).IsAssignableFrom(typeof(EncryptedTableException)).Should().BeTrue();
        typeof(CodeBrixSqliteException).IsAssignableFrom(typeof(DbNullValueException)).Should().BeTrue();
    }

    [Fact]
    public void base_exception_derives_from_system_exception()
        => typeof(Exception).IsAssignableFrom(typeof(CodeBrixSqliteException)).Should().BeTrue();

    [Fact]
    public void message_is_preserved()
        => new EncryptedTableException("the message").Message.Should().Be("the message");

    [Fact]
    public void inner_exception_is_preserved()
    {
        //Arrange
        var inner = new InvalidOperationException("inner");

        //Act
        var outer = new ObjectCryptographyException("outer", inner);

        //Assert
        outer.InnerException.Should().Be(inner);
    }

    [Fact]
    public void every_exception_type_supports_an_inner_exception()
    {
        //Arrange
        var inner = new InvalidOperationException("inner");

        //Act + Assert
        new CodeBrixSqliteException("m", inner).InnerException.Should().Be(inner);
        new DatabaseMaintenanceException("m", inner).InnerException.Should().Be(inner);
        new EncryptedTableException("m", inner).InnerException.Should().Be(inner);
        new DbNullValueException("m", inner).InnerException.Should().Be(inner);
    }
}
