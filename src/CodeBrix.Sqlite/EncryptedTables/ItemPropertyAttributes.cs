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
using CodeBrix.Sqlite.Exceptions;

namespace CodeBrix.Sqlite.EncryptedTables; //was previously: Portable.Data.Sqlite;

/// <summary>
/// Specifies that the property should be stored in an unencrypted table column -
/// IMPORTANT: values of the marked property will NOT BE ENCRYPTED in the table.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotEncryptedAttribute : Attribute
{
}

/// <summary>
/// Specifies that the column used to store the property value in the table should be marked
/// NOT NULL - NOTE: only applies to NotEncrypted properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotNullAttribute : Attribute
{
}

/// <summary>
/// Specifies that the property WILL BE ENCRYPTED in the table, but will also be searchable and
/// contained in in-memory indexes of the table.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SearchableAttribute : Attribute
{
}

/// <summary>
/// Specifies that the property WILL BE ENCRYPTED in the table, and will additionally get a
/// deterministic HMAC 'blind index' column (with a real SQLite index on it), enabling exact-match
/// equality searches without decrypting the table. Requires a crypt engine that implements
/// <see cref="CodeBrix.Sqlite.Cryptography.IBlindIndexProvider"/>. This attribute is new in
/// CodeBrix.Sqlite and did not exist in the classic Portable.Data.Sqlite library.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class BlindIndexedAttribute : Attribute
{
}

/// <summary>
/// Specifies the SQLite default column value, if the property value is NULL -
/// NOTE: only applies to NotEncrypted properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnDefaultValueAttribute : Attribute
{
    /// <summary>
    /// The default value for the column, if a column value is not specified during record creation.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates an instance of the attribute with the specified value.
    /// </summary>
    /// <param name="value">The value to use as default.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> contains a single quote.</exception>
    public ColumnDefaultValueAttribute(string value)
    {
        if (value == null) { throw new ArgumentNullException(nameof(value), "Column default value cannot be null."); }
        if (value.Contains('\'')) { throw new ArgumentException("Column default values cannot contain single quotes.", nameof(value)); }
        Value = value;
    }
}

/// <summary>
/// Specifies a desired name for the SQLite table column, rather than using the default column name
/// derived from the property name - NOTE: only applies to NotEncrypted properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnNameAttribute : Attribute
{
    /// <summary>
    /// The desired name of the column.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates an instance of the attribute with the specified name.
    /// </summary>
    /// <param name="name">The desired column name.</param>
    /// <exception cref="EncryptedTableException">Thrown when the name is not a valid SQLite identifier.</exception>
    public ColumnNameAttribute(string name)
    {
        if (!TableColumn.IsValidIdentifier(name))
        {
            throw new EncryptedTableException(
                $"Problem with column name '{name}' - column names must start with a letter and may only contain letters, numbers and underscores.");
        }
        Name = name.Trim();
    }
}
