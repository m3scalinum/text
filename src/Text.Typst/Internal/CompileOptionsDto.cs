namespace M3scalinum.Text.Typst.Internal;

/// <summary>Payload sent to <c>tn_compile</c> (Spec 02 §3.4).</summary>
internal sealed class CompileOptionsDto
{
    public string Root { get; set; } = "main.typ";

    public string Document { get; set; } = "paged";
}