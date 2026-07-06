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
/// Specifies how a <see cref="TableSearchItem"/> compares the indexed property value with the
/// search value.
/// </summary>
public enum SearchItemMatchType
{
    /// <summary>The property value must equal the search value.</summary>
    IsEqualTo = 0,

    /// <summary>The property value must not equal the search value.</summary>
    IsNotEqualTo = 1,

    /// <summary>The property value must contain the search value.</summary>
    Contains = 2,

    /// <summary>The property value must not contain the search value.</summary>
    DoesNotContain = 3,

    /// <summary>The property value must start with the search value.</summary>
    StartsWith = 4,

    /// <summary>The property value must end with the search value.</summary>
    EndsWith = 5
}

/// <summary>
/// One criterion of a <see cref="TableSearch"/>: the name of a searchable property, a value, and
/// how the two are compared.
/// </summary>
public class TableSearchItem
{
    /// <summary>
    /// The name of the item property to match; the property must be marked
    /// <see cref="SearchableAttribute"/> or <see cref="NotEncryptedAttribute"/>.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// The value to compare the property value with.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// How the property value is compared with <see cref="Value"/>.
    /// </summary>
    public SearchItemMatchType MatchType { get; }

    /// <summary>
    /// Creates a search criterion.
    /// </summary>
    /// <param name="propertyName">The name of the item property to match.</param>
    /// <param name="value">The value to compare the property value with.</param>
    /// <param name="matchType">How the values are compared; the default is <see cref="SearchItemMatchType.IsEqualTo"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="propertyName"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="propertyName"/> is empty or whitespace.</exception>
    public TableSearchItem(string propertyName, string value, SearchItemMatchType matchType = SearchItemMatchType.IsEqualTo)
    {
        if (propertyName == null) { throw new ArgumentNullException(nameof(propertyName)); }
        if (String.IsNullOrWhiteSpace(propertyName)) { throw new ArgumentException("The property name cannot be empty or whitespace.", nameof(propertyName)); }
        PropertyName = propertyName.Trim();
        Value = value ?? throw new ArgumentNullException(nameof(value));
        MatchType = matchType;
    }
}
