namespace CDorst.HttpApiGenerator;

internal static class StringExtensions
{
    public static string ToSqlDataReaderType(this string type)
        => type switch
        {
            "string?" => "String",
            "char?" => "Char",
            "bool?" => "Boolean",
            "int?" => "Int32",
            "short?" => "Int16",
            "long?" => "Int64",
            "DateTime?" => "DateTime",
            "DateTimeOffset?" => "DateTimeOffset",
            "decimal?" => "Decimal",
            "double?" => "Double",
            "Guid?" => "Guid",
            _ => type,
        };

    public static string ToSystemType(this string type)
    {
        var typeUpper = type.ToUpper();
        if (typeUpper.Contains("VARCHAR")) return "string";
        return typeUpper switch
        {
            "DATETIMEOFFSET" => "DateTimeOffset",
            "DATETIME" => "DateTime",
            "DATE" => "DateTime",
            "BIGINT" => "long",
            "INT" => "int",
            "SMALLINT" => "short",
            "SHORT" => "short",
            "TINYINT" => "byte",
            "BIT" => "byte",
            "BYTE" => "byte",
            "DECIMAL" => "decimal",
            "DOUBLE" => "double",
            "GUID" => "Guid",
            "CHAR" => "char",
            "BOOL" => "bool",
            "BOOLEAN" => "bool",
            _ => type,
        };
    }

    public static string ToSystemTypeNullable(this string type)
        => type.ToSystemType() + '?';
}
