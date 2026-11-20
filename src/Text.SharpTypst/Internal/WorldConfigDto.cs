namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>Payload sent to <c>tn_world_create</c> (Spec 02 §3.3).</summary>
internal sealed class WorldConfigDto
{
    public ClockDto Clock { get; set; } = new();

    public LimitsDto Limits { get; set; } = new();

    public PackagesDto Packages { get; set; } = new();

    public FontsDto Fonts { get; set; } = new();
}