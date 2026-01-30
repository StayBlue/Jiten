using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum) return;
        
        schema.Type = "integer";
        schema.Format = "int32";
        schema.Enum.Clear();
        
        var enumDescriptions = new List<string>();
        
        foreach (var value in Enum.GetValues(context.Type))
        {
            var intValue = Convert.ToInt32(value);
            var name = Enum.GetName(context.Type, value);
            
            schema.Enum.Add(new OpenApiInteger(intValue));
            enumDescriptions.Add($"{intValue} = {name}");
        }
        
        schema.Description = string.Join(", ", enumDescriptions);
    }
}