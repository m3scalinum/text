namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>Payload sent to <c>tn_compile</c> (Spec 02 §3.4).</summary>
internal sealed class CompileOptionsDto
{
    public string Root { get; set; } = "main.typ";

    public string Document { get; set; } = "paged";
}