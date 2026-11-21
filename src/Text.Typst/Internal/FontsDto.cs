using System.Collections.Generic;

namespace M3scalinum.Text.Typst.Internal;

internal sealed class FontsDto
{
    public List<string> Directories { get; set; } = [];

    public bool System { get; set; }
}