# Prints a value compiled into a .NET assembly, read from its metadata without loading it: an
# AssemblyMetadata attribute's value by its key, or a string constant by Namespace.Type.Field. It
# prints nothing where the assembly has no such value.
param(
    [Parameter(Mandatory)] [string] $Assembly,
    [Parameter(Mandatory, ParameterSetName = 'Metadata')] [string] $Metadata,
    [Parameter(Mandatory, ParameterSetName = 'Constant')] [string] $Constant
)
$ErrorActionPreference = 'Stop'

if (-not ('PitboardAssembly' -as [type])) {
    Add-Type -TypeDefinition @'
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public static class PitboardAssembly
{
    public static string Metadata(string path, string key)
    {
        using (var stream = File.OpenRead(path))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference) continue;
                var type = reader.GetTypeReference((TypeReferenceHandle)parent);
                if (reader.GetString(type.Namespace) != "System.Reflection" || reader.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() == 1 && blob.ReadSerializedString() == key) return blob.ReadSerializedString();
            }
            return null;
        }
    }

    public static string Constant(string path, string space, string type, string field)
    {
        using (var stream = File.OpenRead(path))
        using (var pe = new PEReader(stream))
        {
            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                if (reader.GetString(definition.Namespace) != space || reader.GetString(definition.Name) != type) continue;
                foreach (var fieldHandle in definition.GetFields())
                {
                    var found = reader.GetFieldDefinition(fieldHandle);
                    if (reader.GetString(found.Name) != field || found.GetDefaultValue().IsNil) continue;
                    var constant = reader.GetConstant(found.GetDefaultValue());
                    if (constant.TypeCode != ConstantTypeCode.String) return null;
                    var blob = reader.GetBlobReader(constant.Value);
                    return blob.ReadUTF16(blob.Length);
                }
            }
            return null;
        }
    }
}
'@
}

$path = (Resolve-Path -LiteralPath $Assembly).Path
$value = if ($PSCmdlet.ParameterSetName -eq 'Metadata') {
    [PitboardAssembly]::Metadata($path, $Metadata)
} else {
    $parts = $Constant -split '\.'
    if ($parts.Count -lt 3) { throw "-Constant takes Namespace.Type.Field, not $Constant" }
    [PitboardAssembly]::Constant($path, ($parts[0..($parts.Count - 3)] -join '.'), $parts[-2], $parts[-1])
}
if ($null -ne $value) { $value }
