using System.Text.Json;
using Lavalink4NET.Filters;
using Lavalink4NET.Protocol.Models.Filters;

namespace Assistant.Net.Utilities.Filters;

public sealed record WobbleFilterOptions(float Rate = 5.0f, float Depth = 2.0f) : IFilterOptions
{
    // Return true if you want Lavalink4NET to ignore this filter when it represents the default/off state.
    public bool IsDefault => Rate <= 0f || Depth <= 0f;

    public void Apply(ref PlayerFilterMapModel filterMap)
    {
        var additionalFilters = filterMap.AdditionalFilters is null
            ? new Dictionary<string, JsonElement>()
            : new Dictionary<string, JsonElement>(filterMap.AdditionalFilters);

        var pluginFilters = new Dictionary<string, JsonElement>();
        if (additionalFilters.TryGetValue("pluginFilters", out var existingPluginFilters))
        {
            var deserialized = existingPluginFilters.Deserialize<Dictionary<string, JsonElement>>();
            if (deserialized is not null) pluginFilters = deserialized;
        }
        pluginFilters["wobble"] = JsonSerializer.SerializeToElement(new
        {
            wobbleRate = Rate,
            wobbleDepth = Depth
        });
        additionalFilters["pluginFilters"] = JsonSerializer.SerializeToElement(pluginFilters);
        filterMap = filterMap with { AdditionalFilters = additionalFilters };
    }
}