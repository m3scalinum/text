namespace M3scalinum.Text.Documents;

/// <summary>Which track of a table a header cell labels, as PDF's <c>/Scope</c> states it.</summary>
public enum TypstHeaderScope
{
    /// <summary>The cell labels the column beneath it: the header row of a table.</summary>
    Column,

    /// <summary>The cell labels the row beside it: the header column of a table.</summary>
    Row,

    /// <summary>The cell labels both its column and its row: the corner of a table.</summary>
    Both,
}
