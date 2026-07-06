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
/// Specifies how multiple <see cref="TableSearchItem"/> criteria of a <see cref="TableSearch"/>
/// are combined.
/// </summary>
public enum TableSearchType
{
    /// <summary>An item matches only when ALL search criteria match (logical AND).</summary>
    MatchAll = 0,

    /// <summary>An item matches when ANY search criterion matches (logical OR).</summary>
    MatchAny = 1
}

/// <summary>
/// A search over the items of an <see cref="EncryptedTable{T}"/>, evaluated against the in-memory
/// <see cref="TableIndex"/> of searchable property values.
/// </summary>
public class TableSearch
{
    /// <summary>
    /// How multiple criteria are combined; the default is <see cref="TableSearchType.MatchAll"/>.
    /// A search with no criteria matches every item when combined with
    /// <see cref="TableSearchType.MatchAll"/>, and no items with <see cref="TableSearchType.MatchAny"/>.
    /// </summary>
    public TableSearchType SearchType { get; set; } = TableSearchType.MatchAll;

    /// <summary>
    /// The search criteria to evaluate.
    /// </summary>
    public List<TableSearchItem> MatchItems { get; } = new List<TableSearchItem>();

    /// <summary>
    /// True to compare values case-sensitively; the default is false (case-insensitive).
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// True (the default) to trim leading/trailing whitespace from both the indexed value and the
    /// search value before comparing.
    /// </summary>
    public bool TrimValues { get; set; } = true;

    /// <summary>
    /// Creates an empty search; add criteria via <see cref="MatchItems"/>.
    /// </summary>
    public TableSearch()
    {
    }

    /// <summary>
    /// Creates a search with the specified criteria.
    /// </summary>
    /// <param name="matchItems">The search criteria to evaluate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="matchItems"/> is null.</exception>
    public TableSearch(params TableSearchItem[] matchItems)
    {
        if (matchItems == null) { throw new ArgumentNullException(nameof(matchItems)); }
        MatchItems.AddRange(matchItems);
    }
}
