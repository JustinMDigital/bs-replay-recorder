using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: AssemblyMetadataDumper <assembly-path> [filter]");
    return 2;
}

var assemblyPath = args[0];
var filter = args.Length > 1 ? args[1] : "";

using var stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);

if (!peReader.HasMetadata)
{
    Console.Error.WriteLine("Assembly has no metadata: " + assemblyPath);
    return 1;
}

var reader = peReader.GetMetadataReader();
var assemblyDefinition = reader.GetAssemblyDefinition();
Console.WriteLine("Assembly: " + reader.GetString(assemblyDefinition.Name) + " " + assemblyDefinition.Version);

foreach (var typeHandle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(typeHandle);
    var typeName = reader.GetString(type.Name);
    var typeNamespace = reader.GetString(type.Namespace);
    var fullName = string.IsNullOrEmpty(typeNamespace) ? typeName : typeNamespace + "." + typeName;

    var typeMatches = Matches(fullName, filter);
    var matchingMembers = GetMatchingMembers(reader, type, filter, typeMatches).ToList();
    if (!typeMatches && matchingMembers.Count == 0)
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine("Type: " + fullName);
    Console.WriteLine("  Attributes: " + type.Attributes);

    foreach (var member in matchingMembers)
    {
        Console.WriteLine("  " + member);
    }
}

return 0;

static IEnumerable<string> GetMatchingMembers(MetadataReader reader, TypeDefinition type, string filter, bool includeAll)
{
    var provider = new TypeNameProvider();

    foreach (var methodHandle in type.GetMethods())
    {
        var method = reader.GetMethodDefinition(methodHandle);
        var name = reader.GetString(method.Name);
        if (includeAll || Matches(name, filter))
        {
            var signature = method.DecodeSignature(provider, genericContext: null);
            yield return "Method: " + FormatMethodAttributes(method.Attributes) + " " +
                         signature.ReturnType + " " + name + "(" +
                         string.Join(", ", signature.ParameterTypes) + ")";
        }
    }

    foreach (var fieldHandle in type.GetFields())
    {
        var field = reader.GetFieldDefinition(fieldHandle);
        var name = reader.GetString(field.Name);
        if (includeAll || Matches(name, filter))
        {
            yield return "Field: " + field.Attributes + " " + name;
        }
    }

    foreach (var propertyHandle in type.GetProperties())
    {
        var property = reader.GetPropertyDefinition(propertyHandle);
        var name = reader.GetString(property.Name);
        if (includeAll || Matches(name, filter))
        {
            yield return "Property: " + property.Attributes + " " + name;
        }
    }

    foreach (var eventHandle in type.GetEvents())
    {
        var eventDefinition = reader.GetEventDefinition(eventHandle);
        var name = reader.GetString(eventDefinition.Name);
        if (includeAll || Matches(name, filter))
        {
            yield return "Event: " + eventDefinition.Attributes + " " + name;
        }
    }
}

static bool Matches(string value, string filter)
{
    return string.IsNullOrWhiteSpace(filter) ||
           value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
}

static string FormatMethodAttributes(MethodAttributes attributes)
{
    var visibility = attributes & MethodAttributes.MemberAccessMask;
    var parts = new List<string> { visibility.ToString() };

    if ((attributes & MethodAttributes.Static) != 0)
    {
        parts.Add("static");
    }

    if ((attributes & MethodAttributes.Virtual) != 0)
    {
        parts.Add("virtual");
    }

    return string.Join(" ", parts);
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape)
    {
        return elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
    }

    public string GetByReferenceType(string elementType)
    {
        return elementType + "&";
    }

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        return "fnptr";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        return genericType + "<" + string.Join(", ", typeArguments) + ">";
    }

    public string GetGenericMethodParameter(object? genericContext, int index)
    {
        return "!!" + index;
    }

    public string GetGenericTypeParameter(object? genericContext, int index)
    {
        return "!" + index;
    }

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
    {
        return unmodifiedType;
    }

    public string GetPinnedType(string elementType)
    {
        return elementType;
    }

    public string GetPointerType(string elementType)
    {
        return elementType + "*";
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return typeCode.ToString();
    }

    public string GetSZArrayType(string elementType)
    {
        return elementType + "[]";
    }

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return FormatTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return FormatTypeName(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var specification = reader.GetTypeSpecification(handle);
        return specification.DecodeSignature(this, genericContext);
    }

    private static string FormatTypeName(string typeNamespace, string typeName)
    {
        return string.IsNullOrEmpty(typeNamespace) ? typeName : typeNamespace + "." + typeName;
    }
}
