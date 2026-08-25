================================================================================
EXTRAS-README: CodeBrix.Sqlite
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

This repository ships no sample applications, demos, tools or optional test-data
sets. There are exactly two projects: the packable library
(src/CodeBrix.Sqlite) and its test project.

For runnable, compilable usage of the library, read the test project: the
"WORKING EXAMPLES ON GITHUB" section of AGENT-README.txt maps each feature area
to the test file that exercises it.


TEST PROJECT
============
    tests/CodeBrix.Sqlite.Tests/

The only non-package project in the solution. xUnit v3 with SilverAssertions;
run it with `dotnet test CodeBrix.Sqlite.slnx` from the repo root. It needs no
network access, environment variables or setup: each test creates its SQLite
files in a per-test temporary folder (TempFolder.cs) and clears the
Microsoft.Data.Sqlite connection pools on cleanup so those files can be
deleted.

Two files in it are worth knowing about even though they are not tests
themselves:

    tests/CodeBrix.Sqlite.Tests/SampleItems.cs
        The shared EncryptedTableItem fixtures - ContactItem, InventoryItem
        and the deliberately-broken items used for negative tests - plus the
        test doubles SpySerializer (an IObjectSerializer that counts calls) and
        PlainTextCryptEngine (an IObjectCryptEngine that does no encryption).
        ContactItem and InventoryItem between them exercise every attribute and
        column-type combination the library supports, so they are a good
        template when writing your own item type.

    tests/CodeBrix.Sqlite.Tests/TempFolder.cs
        The disposable per-test temporary folder helper.

The test project also references the CodeBrix.Compression package so that
BackupZipRoundTrip.cs can exercise the full
backup -> zip -> unzip -> restore -> read round trip. That reference exists for
the tests only and is not a dependency of the shipped package.


================================================================================
END OF EXTRAS-README
