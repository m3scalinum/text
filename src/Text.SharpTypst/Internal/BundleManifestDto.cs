using System.Collections.Generic;

namespace M3scalinum.Text.SharpTypst.Internal;

/// <summary>Manifest returned by <c>tn_document_export</c> with format <c>bundle</c>.</summary>
internal sealed class BundleManifestDto
{
    public List<BundleAssetDto>? Assets { get; set; }
}