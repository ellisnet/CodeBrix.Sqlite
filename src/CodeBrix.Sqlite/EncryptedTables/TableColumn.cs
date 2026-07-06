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

namespace CodeBrix.Sqlite.EncryptedTables; //was previously: Portable.Data.Sqlite;

/// <summary>
/// Describes one SQLite column of the table behind an <see cref="EncryptedTable{T}"/>.
/// </summary>
public class TableColumn
{
    /// <summary>
    /// The name of the SQLite column.
    /// </summary>
    public string ColumnName { get; set; }

    /// <summary>
    /// The name of the item property the column stores, or null for the columns the library
    /// manages itself (Id, Encrypted_Searchable, Encrypted_Object).
    /// </summary>
    public string PropertyName { get; set; }

    /// <summary>
    /// The SQLite datatype of the column (INTEGER, REAL, TEXT or BLOB).
    /// </summary>
    public string DataType { get; set; }

    /// <summary>
    /// True when the column is declared NOT NULL.
    /// </summary>
    public bool IsNotNull { get; set; }

    /// <summary>
    /// The declared DEFAULT value of the column, or null when the column has no default.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Determines whether the specified name is a valid SQLite identifier for use as a table or
    /// column name: it must start with a letter and contain only letters, digits and underscores.
    /// </summary>
    /// <param name="name">The candidate identifier (leading/trailing whitespace is ignored).</param>
    /// <returns>True when the name is a valid identifier.</returns>
    public static bool IsValidIdentifier(string name)
    {
        if (String.IsNullOrWhiteSpace(name)) { return false; }
        string trimmed = name.Trim();
        if (!Char.IsAsciiLetter(trimmed[0])) { return false; }
        foreach (char letter in trimmed)
        {
            if (!(Char.IsAsciiLetterOrDigit(letter) || letter == '_')) { return false; }
        }
        return true;
    }
}
