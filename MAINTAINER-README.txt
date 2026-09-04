================================================================================
MAINTAINER-README: CodeBrix.Sqlite
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, stop reading and open AGENT-README.txt
instead. Everything below is about the repository itself: how it is laid out,
how it builds, how it is tested, how it is packaged, and the conventions the
source follows.


PURPOSE AND SCOPE
=================
This repository produces exactly one NuGet package:

    PackageId:  CodeBrix.Sqlite.ApacheLicenseForever
    Assembly:   CodeBrix.Sqlite
    Project:    src/CodeBrix.Sqlite/CodeBrix.Sqlite.csproj
    License:    Apache-2.0
    Consumer documentation: AGENT-README.txt (repo root)

The library is a SQLite convenience layer over Microsoft.Data.Sqlite:
selective column/object encryption with a pluggable crypt engine, the typed
EncryptedTable<T> abstraction with searchable encrypted data and HMAC
blind-index equality search, an encryption-aware Dapper-style mapper, safe
quiesce-and-backup orchestration, and user_version schema-version helpers.


REPOSITORY LAYOUT
=================
    src/CodeBrix.Sqlite/
      (root)               SqliteDatabase (entry point), SqliteDatabaseOptions,
                           SqliteMapper (+ .Async partial; the Dapper-style
                           CRUD extension methods), SqliteGridReader,
                           EncryptedValue, EncryptedColumnAttribute
      Cryptography/        IObjectCryptEngine, IBlindIndexProvider,
                           IObjectSerializer, JsonObjectSerializer,
                           AesGcmCryptEngine
      EncryptedTables/     EncryptedTable<T>, EncryptedTableItem (+
                           TableItemStatus), ItemPropertyAttributes (the
                           attribute set), TableColumn, TableIndex,
                           TableSearch (+ TableSearchType), TableSearchItem
                           (+ SearchItemMatchType)
      Extensions/          SqliteCommandExtensions,
                           SqliteDataReaderExtensions
      Enumerations/        DbNullHandling
      Exceptions/          CodeBrixSqliteException + the derived types
      InternalsVisibleTo.cs   grants internals access to CodeBrix.Sqlite.Tests

    tests/CodeBrix.Sqlite.Tests/
      the xUnit v3 test project, plus TempFolder.cs (per-test temporary
      folders) and SampleItems.cs (the shared EncryptedTableItem fixtures and
      test doubles: SpySerializer, PlainTextCryptEngine)

    global.json
      selects the Microsoft.Testing.Platform test runner. It does NOT pin an
      SDK version. See TESTING below.

    CodeBrix.Sqlite.slnx
      the solution; its Solution Items folder carries .gitignore,
      AGENT-README.txt, EXTRAS-README.txt, global.json, icon-codebrix-128.png,
      LICENSE, MAINTAINER-README.txt, README-INDEX.txt, README.md and
      THIRD-PARTY-NOTICES.txt, and its Tests folder carries the test project

Sub-folder names are namespace segments here (CodeBrix.Sqlite.Cryptography,
.EncryptedTables, .Extensions, .Enumerations, .Exceptions); the entry-point
types stay at the project root in the plain CodeBrix.Sqlite namespace. Keep
new types in the folder that matches their namespace.


BUILDING
========
    dotnet restore CodeBrix.Sqlite.slnx
    dotnet build   CodeBrix.Sqlite.slnx

Target framework: net10.0 only. GenerateDocumentationFile is ON, so every
public (and protected-on-unsealed) member must carry an XML doc comment; fix
CS1591 at the source and never add a project-wide <NoWarn>.

THE SQLITEPCLRAW PIN IS LOAD-BEARING
------------------------------------
The library project carries an EXPLICIT PackageReference to
SQLitePCLRaw.bundle_e_sqlite3 in addition to Microsoft.Data.Sqlite. That is
deliberate and must not be "simplified away".

Durable rule: keep an explicit reference to a 3.x SQLitePCLRaw bundle. The
reference began as a vulnerability fix - Microsoft.Data.Sqlite through 10.0.10
pinned bundle_e_sqlite3 2.1.11, whose lib.e_sqlite3 has a known high-severity
advisory (NU1903 / GHSA-2m69-gcr7-jv3q). As of Microsoft.Data.Sqlite 10.0.11
the transitive pin is 2.1.12, which is NOT flagged, so this reference no longer
avoids an advisory; it keeps the graph on the current 3.x bundle. Re-check
before removing it. With the explicit reference in place, a consuming project
needs no pin of its own and
`dotnet list package --vulnerable --include-transitive` reports the SQLite
graph clean. When bumping Microsoft.Data.Sqlite, re-check that the bundle
reference still wins the version resolution and stays on 3.x - and never write
the specific pinned version into AGENT-README.txt or README.md; the csproj
comment above the reference is the single source of truth for the rationale.


TESTING
=======
    dotnet test CodeBrix.Sqlite.slnx

global.json at the repository root is load-bearing for testing. It has no
"sdk" section and pins no SDK version - runner selection is the only thing it
is there for:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

The test project runs on Microsoft.Testing.Platform (xunit.v3), which no longer
supports the legacy VSTest bridge on the .NET 10 SDK. Do NOT delete global.json
- without it "dotnet test" fails outright with "Testing with VSTest target is
no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later".
Because the setting lives in global.json rather than in a csproj, it applies to
every "dotnet test" run anywhere in the repository, including CI. Keep the file
committed and keep it in the .slnx Solution Items folder.

There is no code-coverage collector in the test project; coverlet.collector is
not referenced.

tests/CodeBrix.Sqlite.Tests/ is an xUnit v3 project using SilverAssertions
fluent assertions. It also references
CodeBrix.Compression so that it can exercise the full
backup -> zip -> unzip -> restore -> read round trip
(tests/CodeBrix.Sqlite.Tests/BackupZipRoundTrip.cs).

The library grants it internals access via
src/CodeBrix.Sqlite/InternalsVisibleTo.cs.

Tests create their SQLite database files in per-test temporary folders (see
the TempFolder helper) and clear the Microsoft.Data.Sqlite connection pools on
cleanup so the files can be deleted. No network access, environment variables
or special setup are required.


PACKAGING AND PUBLISHING
========================
GeneratePackageOnBuild is true, so every build of the library project emits a
fresh .nupkg.

Versioning is the CodeBrix date-stamped scheme, computed in the csproj from
System.DateTime.UtcNow as 1.<years-since-base>.<day-of-year>.<minute-of-day>.
It is monotonically increasing but is NOT SemVer, so major/minor say nothing
about API compatibility. TWO BUILDS IN THE SAME UTC MINUTE PRODUCE THE SAME
VERSION - never publish two packages from within one minute. To re-baseline
the minor number, change _VersionBaseYear in the csproj. Do not replace the
version block with a literal <Version>.

What ships inside the nupkg (declared as <None ... Pack="true"> in the
library csproj):
    icon-codebrix-128.png    (PackageIcon)
    README.md                (PackageReadmeFile)
    AGENT-README.txt         (the consumer guide, taken from the repo root)
    THIRD-PARTY-NOTICES.txt
MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt are repo-only
and are NOT packed.

PackageLicenseExpression is Apache-2.0 and PackageRequireLicenseAcceptance is
true. The Copyright property carries both the original Ellisnet copyright for
the ported material and the current project copyright.


PROVENANCE AND VENDORED SOURCES
===============================
Nothing is vendored as a source tree; the derived material is a modernized
port that lives in the normal source folders. THIRD-PARTY-NOTICES.txt is the
authoritative record. In summary:

  - Portable.Data.Sqlite (Ellisnet / Jeremy Ellis, Apache-2.0) - the
    EncryptedTable<T> subsystem in src/CodeBrix.Sqlite/EncryptedTables/ is a
    modernized derivative of that project's EncryptedTable folder, including
    the item attributes, TableColumn, TableIndex, TableSearch and
    TableSearchItem. The former IEncryptedTableItem interface was merged into
    the abstract EncryptedTableItem base class.
  - SimpleAdo.Sqlite (Ellisnet / Jeremy Ellis, Apache-2.0) - origin of the
    encrypted-column API and the maintenance-mode / backup concepts.
  - The Dapper-style mapper is modeled on the Dapper 2.1.79 API surface.

Ported files keep their upstream copyright header and carry a
"//was previously: <upstream-namespace>;" comment on the namespace line.
Preserve both when editing; never fabricate a header on a genuinely new file.
When a port's behavior is deliberately changed, say so in the file rather than
silently diverging from the notice.


CODING CONVENTIONS
==================
  - Nullable reference types are OFF: never use '?' on reference types
    (string?, MyClass?) and never use the null-forgiveness '!' operator.
    Value-type nullables (int?, DateOnly?, MyEnum?) are fine.
  - No <ImplicitUsings>, no global usings; every file lists its own using
    directives, System.* first, alphabetical within groups.
  - File-scoped namespaces only (namespace X;), never block-scoped.
  - Files ported from an upstream project keep the upstream copyright header
    and carry a "//was previously: <upstream-ns>;" comment on the namespace
    line. Never fabricate headers on new files.
  - <GenerateDocumentationFile> is ON: every public (and protected on
    unsealed types) member carries an XML doc comment. Fix CS1591 at the
    source; never suppress warnings project-wide (<NoWarn> forbidden).
  - Tests: xUnit v3 + SilverAssertions fluent asserts (x.Should().Be(y)).
    Test files are named <ClassUnderTest>Tests.cs (scenario suites are named
    <Area>Scenarios.cs); method names are
    MemberName_snake_case_description or pure snake_case; multi-statement
    tests carry //Arrange //Act //Assert comments; single-statement tests are
    expression-bodied. Tests pass TestContext.Current.CancellationToken to
    every cancellable call.
  - The library project carries the canonical date-stamped version block; do
    not replace it with a literal <Version>.


NOTES
=====
  - SqliteMapper is split into SqliteMapper.cs and SqliteMapper.Async.cs as
    one partial static class. Keep each synchronous method and its async twin
    in step: they share the same parameter shape
    (connection, sql, param, transaction, cryptEngine) with the async form
    appending a CancellationToken.
  - The maintenance-mode gate is enforced inside SqliteDatabase and inside the
    mapper's command creation, not by the raw SqliteConnection. Any new
    execution path has to opt into the gate explicitly.
  - EncryptedTable<T> holds its own _itemsLock around TempItems and the cached
    TableIndex; FullTableIndex deliberately returns a Clone() so callers
    cannot corrupt the cache. Preserve that in new members.
  - Reserved encrypted-table column names are Id, Encrypted_Searchable,
    Encrypted_Object and the BlindIndex_ prefix; the constructor throws
    EncryptedTableException on a [ColumnName] collision. Adding a new
    generated column means adding it to that reserved set too.
  - The AI-agent pointer stubs at the repo root (AGENTS.md, CLAUDE.md,
    .clinerules, .cursorrules, .cursor/rules/agent-readme.mdc, .windsurfrules,
    .github/copilot-instructions.md, .junie/guidelines.md) all point at
    README-INDEX.txt. Keep them in sync with the canonical family versions;
    they are not per-repo content.


================================================================================
END OF MAINTAINER-README
