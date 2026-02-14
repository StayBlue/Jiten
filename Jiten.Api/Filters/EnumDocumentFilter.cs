using System.ComponentModel;
using System.Reflection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public class EnumDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var discoveredEnums = new HashSet<Type>();

        var apiTypes = context.ApiDescriptions
                              .SelectMany(api => api.ParameterDescriptions)
                              .Select(param => param.Type)
                              .Where(type => type != null)
                              .Union(
                                     context.ApiDescriptions
                                            .SelectMany(api => api.SupportedResponseTypes)
                                            .Select(response => response.Type)
                                            .Where(type => type != null)
                                    );

        foreach (var type in apiTypes)
        {
            var enumTypes = GetEnumTypesRecursively(type!);
            foreach (var enumType in enumTypes)
            {
                discoveredEnums.Add(enumType);
            }
        }

        // Create schemas for all discovered enums
        foreach (var enumType in discoveredEnums)
        {
            var enumSchema = CreateEnumSchema(enumType);
            var schemaName = enumType.Name;

            // Add to components/schemas if not already present
            if (swaggerDoc.Components.Schemas.ContainsKey(schemaName))
                continue;

            swaggerDoc.Components.Schemas.Add(schemaName, enumSchema);
        }
    }

    private static IEnumerable<Type> GetEnumTypesRecursively(Type type)
    {
        if (type == null) yield break;

        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(type);

        while (queue.Count > 0)
        {
            var currentType = queue.Dequeue();

            if (visited.Contains(currentType))
                continue;

            visited.Add(currentType);

            if (currentType.IsEnum)
            {
                yield return currentType;
                continue;
            }

            // Handle generic types
            if (currentType.IsGenericType)
            {
                foreach (var genericArg in currentType.GetGenericArguments())
                {
                    if (!visited.Contains(genericArg))
                        queue.Enqueue(genericArg);
                }
            }

            // Handle arrays and collections
            if (currentType.IsArray)
            {
                var elementType = currentType.GetElementType();
                if (elementType != null && !visited.Contains(elementType))
                    queue.Enqueue(elementType);
            }

            // Handle properties for classes and structs
            if (!currentType.IsClass && (!currentType.IsValueType || currentType.IsPrimitive)) continue;
            try
            {
                foreach (var property in currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!visited.Contains(property.PropertyType))
                        queue.Enqueue(property.PropertyType);
                }

                foreach (var field in currentType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!visited.Contains(field.FieldType))
                        queue.Enqueue(field.FieldType);
                }
            }
            catch (Exception)
            {
                // Ignore types that can't be reflected
            }
        }
    }

    private static OpenApiSchema CreateEnumSchema(Type enumType)
    {
        var schema = new OpenApiSchema { Type = "integer", Format = "int32", Enum = new List<IOpenApiAny>() };

        var descriptions = new List<string>();
        var enumNames = new List<string>();

        foreach (var value in Enum.GetValues(enumType))
        {
            var name = Enum.GetName(enumType, value);
            var intValue = Convert.ToInt32(value);

            schema.Enum.Add(new OpenApiInteger(intValue));
            enumNames.Add(name!);

            var description = GetEnumDescription(enumType, name!) ?? name;
            descriptions.Add($"{name} ({intValue}){(description != name ? $": {description}" : "")}");
        }

        schema.Description = $"Enum values:<br />{string.Join("<br />", descriptions)}";

        // Add enum names as extension
        var enumNamesArray = new OpenApiArray();
        enumNamesArray.AddRange(enumNames.Select(name => new OpenApiString(name)));
        schema.Extensions.Add("x-enum-varnames", enumNamesArray);

        return schema;
    }

    private static string? GetEnumDescription(Type enumType, string enumName)
    {
        var field = enumType.GetField(enumName);
        var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description;
    }
}