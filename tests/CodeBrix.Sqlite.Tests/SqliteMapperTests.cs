using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Sqlite.Tests;

public class SqliteMapperTests : IDisposable
{
    public enum PersonStatus
    {
        Unknown = 0,
        Active = 1,
        Retired = 2
    }

    public class Person
    {
        public long Id { get; set; }
        public string FullName { get; set; }
        public int Age { get; set; }
        public decimal Salary { get; set; }
        public bool IsActive { get; set; }
        public DateTime HireDate { get; set; }
        public Guid ExternalId { get; set; }
        public PersonStatus Status { get; set; }
        public int? OptionalNumber { get; set; }
        public string NotInTable { get; set; }
    }

    private static readonly Guid AdaGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly TempFolder _folder = new TempFolder();
    private readonly SqliteDatabase _database;
    private readonly SqliteConnection _connection;

    public SqliteMapperTests()
    {
        _database = new SqliteDatabase(_folder.GetFilePath("mapper.sqlite"));
        _connection = _database.Connection;
        _connection.Execute(
            "CREATE TABLE [People] (Id INTEGER PRIMARY KEY AUTOINCREMENT, full_name TEXT, Age INTEGER, " +
            "Salary TEXT, IsActive INTEGER, HireDate TEXT, ExternalId TEXT, Status INTEGER, OptionalNumber INTEGER);");
        _connection.Execute(
            "INSERT INTO [People] (full_name, Age, Salary, IsActive, HireDate, ExternalId, Status, OptionalNumber) " +
            "VALUES (@name, @age, @salary, @active, @hired, @guid, @status, @optional);",
            new
            {
                name = "Ada Lovelace",
                age = 36,
                salary = 1234.56m,
                active = true,
                hired = new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Unspecified),
                guid = AdaGuid,
                status = PersonStatus.Active,
                optional = (int?)null
            });
        _connection.Execute(
            "INSERT INTO [People] (full_name, Age, Salary, IsActive, HireDate, ExternalId, Status, OptionalNumber) " +
            "VALUES ('Grace Hopper', 85, '99.5', 0, '1943-06-01T00:00:00', @guid, 2, 7);",
            new { guid = Guid.NewGuid() });
    }

    public void Dispose()
    {
        _database.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void Execute_returns_the_number_of_rows_affected()
        => _connection.Execute("UPDATE [People] SET Age = Age + 0;").Should().Be(2);

    [Fact]
    public void Query_maps_poco_rows_including_type_conversions()
    {
        //Arrange + Act
        List<Person> people = _connection
            .Query<Person>("SELECT * FROM [People] ORDER BY [Id];")
            .ToList();

        //Assert
        people.Count.Should().Be(2);
        Person ada = people[0];
        ada.FullName.Should().Be("Ada Lovelace"); //full_name -> FullName (underscore-tolerant)
        ada.Age.Should().Be(36);
        ada.Salary.Should().Be(1234.56m);
        ada.IsActive.Should().BeTrue();
        ada.HireDate.Should().Be(new DateTime(2020, 1, 15));
        ada.ExternalId.Should().Be(AdaGuid);
        ada.Status.Should().Be(PersonStatus.Active);
        ada.OptionalNumber.Should().BeNull();
        ada.NotInTable.Should().BeNull(); //no matching column - stays default
        people[1].OptionalNumber.Should().Be(7);
        people[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public void Query_maps_primitive_result_columns()
    {
        //Arrange + Act
        List<string> names = _connection
            .Query<string>("SELECT [full_name] FROM [People] ORDER BY [Id];")
            .ToList();

        //Assert
        names.Count.Should().Be(2);
        names[0].Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Query_dynamic_exposes_columns_as_members()
    {
        //Arrange + Act
        var rows = _connection.Query("SELECT [Id], [full_name] FROM [People] ORDER BY [Id];").ToList();

        //Assert
        ((long)rows[0].Id).Should().Be(1L);
        ((string)rows[0].full_name).Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Query_filters_with_anonymous_object_parameters()
    {
        //Arrange + Act
        List<Person> people = _connection
            .Query<Person>("SELECT * FROM [People] WHERE [Age] > @minAge;", new { minAge = 50 })
            .ToList();

        //Assert
        people.Count.Should().Be(1);
        people[0].FullName.Should().Be("Grace Hopper");
    }

    [Fact]
    public void Query_accepts_dictionary_parameters()
    {
        //Arrange
        var param = new Dictionary<string, object> { ["@minAge"] = 50 };

        //Act
        List<Person> people = _connection
            .Query<Person>("SELECT * FROM [People] WHERE [Age] > @minAge;", param)
            .ToList();

        //Assert
        people.Count.Should().Be(1);
    }

    [Fact]
    public void Query_ignores_parameters_not_referenced_in_the_sql()
    {
        //Arrange + Act
        List<Person> people = _connection
            .Query<Person>("SELECT * FROM [People];", new { unusedParameter = "ignored" })
            .ToList();

        //Assert
        people.Count.Should().Be(2);
    }

    [Fact]
    public void Query_expands_list_parameters_for_in_clauses()
    {
        //Arrange + Act
        List<Person> people = _connection
            .Query<Person>("SELECT * FROM [People] WHERE [Id] IN @ids;", new { ids = new[] { 1L, 2L, 999L } })
            .ToList();

        //Assert
        people.Count.Should().Be(2);
    }

    [Fact]
    public void Query_expands_an_empty_list_to_match_nothing()
        => _connection.Query<Person>("SELECT * FROM [People] WHERE [Id] IN @ids;", new { ids = Array.Empty<long>() })
            .Count().Should().Be(0);

    [Fact]
    public void QueryFirst_returns_the_first_row()
        => _connection.QueryFirst<Person>("SELECT * FROM [People] ORDER BY [Id];").FullName.Should().Be("Ada Lovelace");

    [Fact]
    public void QueryFirst_throws_on_an_empty_result_set()
    {
        //Arrange
        Action act = () => _connection.QueryFirst<Person>("SELECT * FROM [People] WHERE [Id] = 999;");

        //Act + Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void QueryFirstOrDefault_returns_default_on_an_empty_result_set()
        => _connection.QueryFirstOrDefault<Person>("SELECT * FROM [People] WHERE [Id] = 999;").Should().BeNull();

    [Fact]
    public void QuerySingle_throws_when_more_than_one_row_matches()
    {
        //Arrange
        Action act = () => _connection.QuerySingle<Person>("SELECT * FROM [People];");

        //Act + Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void QuerySingleOrDefault_returns_default_on_an_empty_result_set()
        => _connection.QuerySingleOrDefault<Person>("SELECT * FROM [People] WHERE [Id] = 999;").Should().BeNull();

    [Fact]
    public void ExecuteScalar_converts_the_result_type()
        => _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM [People];").Should().Be(2);

    [Fact]
    public void ExecuteScalar_returns_default_for_a_null_result()
        => _connection.ExecuteScalar<string>("SELECT NULL;").Should().BeNull();

    [Fact]
    public void Execute_within_a_rolled_back_transaction_leaves_no_trace()
    {
        //Arrange - like Dapper, transactions require an already-open connection
        _database.SafeOpen();
        using (SqliteTransaction transaction = _connection.BeginTransaction())
        {
            //Act
            _connection.Execute(
                "INSERT INTO [People] (full_name, Age) VALUES ('Rollback Person', 1);", transaction: transaction);
            transaction.Rollback();
        }

        //Assert
        _connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM [People] WHERE [full_name] = 'Rollback Person';").Should().Be(0);
    }

    [Fact]
    public void ExecuteReader_returns_a_working_reader()
    {
        //Arrange + Act
        int rowCount = 0;
        using (SqliteDataReader reader = _connection.ExecuteReader("SELECT * FROM [People];"))
        {
            while (reader.Read()) { rowCount++; }
        }

        //Assert
        rowCount.Should().Be(2);
    }

    [Fact]
    public void QueryMultiple_reads_result_sets_in_order()
    {
        //Arrange + Act
        int count;
        List<string> names;
        using (SqliteGridReader grid = _connection.QueryMultiple(
            "SELECT COUNT(*) FROM [People]; SELECT [full_name] FROM [People] ORDER BY [Id];"))
        {
            count = grid.Read<int>().Single();
            names = grid.Read<string>().ToList();
            grid.IsConsumed.Should().BeTrue();
        }

        //Assert
        count.Should().Be(2);
        names[1].Should().Be("Grace Hopper");
    }

    [Fact]
    public void QueryMultiple_throws_when_reading_past_the_last_result_set()
    {
        //Arrange
        using SqliteGridReader grid = _connection.QueryMultiple("SELECT 1;");
        grid.Read<int>();

        //Act
        Action act = () => grid.Read<int>();

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Query_rejects_null_sql()
    {
        //Arrange
        Action act = () => _connection.Query<Person>(null);

        //Act + Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryAsync_maps_poco_rows()
    {
        //Arrange + Act
        List<Person> people = (await _connection.QueryAsync<Person>(
            "SELECT * FROM [People] ORDER BY [Id];",
            cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        people.Count.Should().Be(2);
        people[0].FullName.Should().Be("Ada Lovelace");
    }

    [Fact]
    public async Task ExecuteAsync_and_ExecuteScalarAsync_round_trip()
    {
        //Arrange
        int affected = await _connection.ExecuteAsync(
            "INSERT INTO [People] (full_name, Age) VALUES (@name, @age);",
            new { name = "Async Person", age = 30 },
            cancellationToken: TestContext.Current.CancellationToken);

        //Act
        int count = await _connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM [People] WHERE [full_name] = 'Async Person';",
            cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        affected.Should().Be(1);
        count.Should().Be(1);
    }

    [Fact]
    public async Task QueryFirstOrDefaultAsync_returns_default_on_empty()
        => (await _connection.QueryFirstOrDefaultAsync<Person>(
            "SELECT * FROM [People] WHERE [Id] = 999;",
            cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();

    [Fact]
    public async Task QuerySingleAsync_returns_the_single_row()
        => (await _connection.QuerySingleAsync<Person>(
            "SELECT * FROM [People] WHERE [Id] = 1;",
            cancellationToken: TestContext.Current.CancellationToken)).FullName.Should().Be("Ada Lovelace");

    [Fact]
    public async Task QueryMultipleAsync_reads_result_sets_in_order()
    {
        //Arrange + Act
        using SqliteGridReader grid = await _connection.QueryMultipleAsync(
            "SELECT 41; SELECT 42;", cancellationToken: TestContext.Current.CancellationToken);
        int first = grid.Read<int>().Single();
        int second = grid.Read<int>().Single();

        //Assert
        first.Should().Be(41);
        second.Should().Be(42);
    }

    [Fact]
    public async Task QueryAsync_dynamic_exposes_columns_as_members()
    {
        //Arrange + Act
        var rows = (await _connection.QueryAsync(
            "SELECT [Id], [full_name] FROM [People] ORDER BY [Id];",
            cancellationToken: TestContext.Current.CancellationToken)).ToList();

        //Assert
        ((long)rows[1].Id).Should().Be(2L);
        ((string)rows[1].full_name).Should().Be("Grace Hopper");
    }

    [Fact]
    public async Task QueryFirstAsync_returns_the_first_row()
        => (await _connection.QueryFirstAsync<Person>(
            "SELECT * FROM [People] ORDER BY [Id];",
            cancellationToken: TestContext.Current.CancellationToken)).FullName.Should().Be("Ada Lovelace");

    [Fact]
    public async Task QuerySingleOrDefaultAsync_returns_default_on_empty()
        => (await _connection.QuerySingleOrDefaultAsync<Person>(
            "SELECT * FROM [People] WHERE [Id] = 999;",
            cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();

    [Fact]
    public void Query_converts_datetimeoffset_timespan_and_char()
    {
        //Arrange + Act + Assert
        _connection.QuerySingle<DateTimeOffset>("SELECT '2026-07-05 10:00:00+02:00';")
            .Should().Be(new DateTimeOffset(2026, 7, 5, 10, 0, 0, TimeSpan.FromHours(2)));
        _connection.QuerySingle<TimeSpan>("SELECT '01:02:03';").Should().Be(new TimeSpan(1, 2, 3));
        _connection.QuerySingle<char>("SELECT 'A';").Should().Be('A');
    }

    [Fact]
    public void Query_converts_an_enum_from_its_string_name()
        => _connection.QuerySingle<PersonStatus>("SELECT 'Retired';").Should().Be(PersonStatus.Retired);

    [Fact]
    public void Query_converts_a_guid_from_a_blob()
    {
        //Arrange
        var guid = Guid.NewGuid();

        //Act
        Guid roundTripped = _connection.QuerySingle<Guid>("SELECT @blob;", new { blob = guid.ToByteArray() });

        //Assert
        roundTripped.Should().Be(guid);
    }

    [Fact]
    public void ExecuteReader_on_a_closed_connection_closes_it_on_disposal()
    {
        //Arrange - a bare, closed connection to the same database file
        using var bareConnection = new SqliteConnection($"Data Source={_database.DatabaseFilePath};Pooling=False");
        int rowCount = 0;

        //Act
        using (SqliteDataReader reader = bareConnection.ExecuteReader("SELECT * FROM [People];"))
        {
            while (reader.Read()) { rowCount++; }
        }

        //Assert - the mapper opened the connection, so disposing the reader closed it again
        rowCount.Should().Be(2);
        bareConnection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void QueryMultiple_on_a_closed_connection_closes_it_when_the_grid_is_disposed()
    {
        //Arrange
        using var bareConnection = new SqliteConnection($"Data Source={_database.DatabaseFilePath};Pooling=False");

        //Act
        using (SqliteGridReader grid = bareConnection.QueryMultiple("SELECT 1; SELECT 2;"))
        {
            grid.Read<int>().Single().Should().Be(1);
        }

        //Assert
        bareConnection.State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void SqliteGridReader_throws_after_disposal()
    {
        //Arrange
        SqliteGridReader grid = _connection.QueryMultiple("SELECT 1;");
        grid.Dispose();

        //Act
        Action act = () => grid.Read<int>();

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ExecuteReaderAsync_returns_a_working_reader()
    {
        //Arrange + Act
        int rowCount = 0;
        using (SqliteDataReader reader = await _connection.ExecuteReaderAsync(
            "SELECT * FROM [People];", cancellationToken: TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken)) { rowCount++; }
        }

        //Assert
        rowCount.Should().Be(2);
    }
}
