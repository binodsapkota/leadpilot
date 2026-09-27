using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LeadPilot.Crm;

public static class CrmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
