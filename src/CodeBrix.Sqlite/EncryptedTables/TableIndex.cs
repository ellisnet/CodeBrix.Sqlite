/*
   Copyright 2014 Ellisnet - Jeremy Ellis (jeremy@ellisnet.com)

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;

namespace CodeBrix.Sqlite.EncryptedTables; //was previously: Portable.Data.Sqlite;

/// <summary>
/// An in-memory index of the items in an <see cref="EncryptedTable{T}"/>: for each item id, the
/// values of the properties marked <see cref="SearchableAttribute"/> or
/// <see cref="NotEncryptedAttribute"/> (as strings). The index is built by decrypting only the
/// small Encrypted_Searchable column of each row — never the full item objects — and expires
/// after <see cref="LifetimeSeconds"/>.
/// </summary>
public class TableIndex
{
    /// <summary>
    /// The UTC timestamp of when the index was built.
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The time-to-live of the index in seconds; once elapsed, <see cref="IsExpired"/> becomes true.
    /// A value of 0 means the index is immediately expired (it is rebuilt on every use).
    /// </summary>
    public int LifetimeSeconds { get; set; } = 600;

    /// <summary>
    /// The indexed items: item id → (property name → property value as string).
    /// </summary>
    public Dictionary<long, Dictionary<string, string>> Items { get; } = new Dictionary<long, Dictionary<string, string>>();

    /// <summary>
    /// True when the index has outlived its <see cref="LifetimeSeconds"/> and should be rebuilt.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= CreatedUtc.AddSeconds(LifetimeSeconds);

    /// <summary>
    /// Creates a deep copy of the index.
    /// </summary>
    /// <returns>The copied index.</returns>
    public TableIndex Clone()
    {
        var clone = new TableIndex { CreatedUtc = CreatedUtc, LifetimeSeconds = LifetimeSeconds };
        foreach (KeyValuePair<long, Dictionary<string, string>> item in Items)
        {
            clone.Items[item.Key] = new Dictionary<string, string>(item.Value);
        }
        return clone;
    }
}
