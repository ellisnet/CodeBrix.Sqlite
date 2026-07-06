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

namespace CodeBrix.Sqlite.EncryptedTables; //was previously: Portable.Data.Sqlite;

/// <summary>
/// The synchronization status of an <see cref="EncryptedTableItem"/> relative to its database table.
/// </summary>
public enum TableItemStatus
{
    /// <summary>The item is new and has not been written to the table yet.</summary>
    New = 0,

    /// <summary>The item matches the table record it was read from or written to.</summary>
    Unchanged = 1,

    /// <summary>The item has in-memory changes that have not been written to the table yet.</summary>
    Modified = 2,

    /// <summary>The item is marked for deletion from the table on the next write of changes.</summary>
    DeletePending = 3
}

/// <summary>
/// The base class for objects stored in an <see cref="EncryptedTable{T}"/>. Derived classes add
/// public read/write properties, optionally decorated with the encrypted-table attributes
/// (<see cref="NotEncryptedAttribute"/>, <see cref="SearchableAttribute"/>,
/// <see cref="BlindIndexedAttribute"/>, and related).
/// </summary>
public abstract class EncryptedTableItem
{
    /// <summary>
    /// The row id of the item in the table. Values less than 1 are temporary ids for items that
    /// have not been written to the table yet; the real id is assigned when changes are written.
    /// </summary>
    public long Id { get; set; } = -1;

    /// <summary>
    /// The synchronization status of the item relative to its database table.
    /// </summary>
    public TableItemStatus SyncStatus { get; set; } = TableItemStatus.New;
}
