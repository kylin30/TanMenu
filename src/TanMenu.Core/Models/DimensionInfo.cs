using System.Text.Json.Serialization;

namespace TanMenu.Core.Models;

public class DimensionInfo
{
    [JsonPropertyName("width")]
    public int NaturalWidth { get; set; }

    [JsonPropertyName("height")]
    public int NaturalHeight { get; set; }
}

public class DpiInfo
{
    public float SystemDpi { get; set; }
    public float SystemScale { get; set; }
    public int WindowDpi { get; set; }
    public float WindowScale { get; set; }
    public bool IsHighDpi => SystemDpi > 96;
}
