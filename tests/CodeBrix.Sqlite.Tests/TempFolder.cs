using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CodeBrix.Sqlite.Tests;

internal sealed class TempFolder : IDisposable
{
    public string FolderPath { get; }

    public TempFolder()
    {
        FolderPath = Path.Combine(Path.GetTempPath(), "CodeBrixSqliteTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FolderPath);
    }

    public string GetFilePath(string fileName) => Path.Combine(FolderPath, fileName);

    public void Dispose()
    {
        //NOTE: no SqliteConnection.ClearAllPools() here - it is process-wide and rips pooled
        //  native handles out from under OTHER test classes running in parallel. SqliteDatabase
        //  clears its own connection's pool on Dispose, and bare test connections use Pooling=False.
        try
        {
            Directory.Delete(FolderPath, recursive: true);
        }
        catch (IOException)
        {
            //A straggling file handle should not fail the test run - the OS temp
            //  folder cleanup will collect whatever is left behind.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
